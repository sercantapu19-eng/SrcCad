using System.Text.Json;
using System.Text.Json.Serialization;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.History;
using SrcCad.Core.Persistence;

namespace SrcCad.Core.Features;

public enum FeatureKind { Sketch, Extrude, ExtrudedCut }
public enum RebuildState { Clean, Dirty, Suppressed, Blocked, Failed }
public enum ExtrusionTermination { Blind, ThroughAll }
public enum TopologyKind { Body, Face, Edge, Vertex }
// Producer provenance + a semantic name; never a transient OCCT selection index.
public sealed record FeatureReference(Guid ProducerId, TopologyKind Kind, string SemanticName);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SketchFeatureParameters), "sketch")]
[JsonDerivedType(typeof(ExtrudeFeatureParameters), "extrude")]
[JsonDerivedType(typeof(CutFeatureParameters), "extruded-cut")]
public abstract record FeatureParameters;
public sealed record SketchFeatureParameters(PlaneFrame Plane, SketchSnapshot Sketch) : FeatureParameters;
public sealed record ExtrudeFeatureParameters(Guid SketchId, double Depth) : FeatureParameters;
public sealed record CutFeatureParameters(Guid BaseFeatureId, Guid SketchId, double Depth,
    ExtrusionTermination Termination = ExtrusionTermination.Blind, bool Reverse = false) : FeatureParameters;
public sealed record FeatureNode(Guid Id, string Name, FeatureParameters Parameters,
    bool Suppressed = false, FeatureReference[]? References = null)
{
    [JsonIgnore] public FeatureKind Kind => Parameters switch
    { SketchFeatureParameters => FeatureKind.Sketch, ExtrudeFeatureParameters => FeatureKind.Extrude,
      CutFeatureParameters => FeatureKind.ExtrudedCut, _ => throw new InvalidDataException("Unsupported feature") };
    [JsonIgnore] public Guid[] Dependencies => (Parameters switch
    {
        ExtrudeFeatureParameters e => new[] { e.SketchId },
        CutFeatureParameters c => new[] { c.BaseFeatureId, c.SketchId },
        _ => Array.Empty<Guid>()
    }).Concat((References ?? Array.Empty<FeatureReference>()).Select(r=>r.ProducerId)).Distinct().ToArray();
}
public sealed record FeatureDocument(Guid Id, FeatureNode[] Features)
{
    public FeatureNode Node(Guid id) => Features.Single(f=>f.Id==id);
    public Guid[] Children(Guid id) => Features.Where(f=>f.Dependencies.Contains(id)).Select(f=>f.Id).ToArray();
    public HashSet<Guid> Descendants(Guid id)
    {
        var result=new HashSet<Guid>();var queue=new Queue<Guid>();queue.Enqueue(id);
        while(queue.TryDequeue(out var parent))foreach(var child in Children(parent))if(result.Add(child))queue.Enqueue(child);
        return result;
    }
    public FeatureDocument Delete(Guid id, bool cascade = false)
    {
        _=Node(id);var children=Descendants(id);
        if(children.Count>0&&!cascade)throw new InvalidOperationException("Dependent features must be included explicitly.");
        children.Add(id);return this with {Features=Features.Where(f=>!children.Contains(f.Id)).ToArray()};
    }
    public FeatureNode[] Ordered()
    {
        if(Id==Guid.Empty||Features is null||Features.Any(n=>n is null||n.Id==Guid.Empty)||Features.Select(n=>n.Id).Distinct().Count()!=Features.Length)
            throw new InvalidDataException("Invalid document/feature identity.");
        var nodes=Features.ToDictionary(n=>n.Id);var state=new Dictionary<Guid,int>();var ordered=new List<FeatureNode>();
        void Visit(FeatureNode node)
        {
            if(state.TryGetValue(node.Id,out var mark)){if(mark==1)throw new InvalidDataException("Feature dependency cycle.");return;}
            state[node.Id]=1;
            foreach(var parent in node.Dependencies)
            {if(!nodes.TryGetValue(parent,out var dependency))throw new InvalidDataException("Missing feature dependency.");Visit(dependency);}
            state[node.Id]=2;ordered.Add(node);
        }
        foreach(var node in Features)Visit(node);
        return ordered.ToArray();
    }
}

public static class FeatureDocumentFile
{
    private sealed record Envelope(int SchemaVersion,string Units,FeatureDocument Document);
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,MaxDepth=64};
    public static string Serialize(FeatureDocument document)
    {Validate(document);return JsonSerializer.Serialize(new Envelope(2,"mm",document),Options);}
    public static FeatureDocument Deserialize(string json)
    {
        if(json.Length>16*1024*1024)throw new InvalidDataException("Part file exceeds size limit.");
        using var parsed=JsonDocument.Parse(json);
        if(parsed.RootElement.GetProperty("SchemaVersion").GetInt32()==1)return FromLegacy(PartFile.Deserialize(json));
        var envelope=JsonSerializer.Deserialize<Envelope>(json,Options)??throw new InvalidDataException("Empty document.");
        if(envelope.SchemaVersion!=2||envelope.Units!="mm")throw new InvalidDataException("Unsupported schema/units.");
        Validate(envelope.Document);return envelope.Document;
    }
    public static FeatureDocument Clone(FeatureDocument document)=>Deserialize(Serialize(document));
    public static FeatureDocument FromLegacy(ExtrusionState model)
    {
        // Reuse legacy validation, preserve exact sketch IDs and parameter intent.
        var safe=PartFile.Deserialize(PartFile.Serialize(model));
        var sketch=new FeatureNode(Guid.NewGuid(),"Çizim1",new SketchFeatureParameters(safe.Plane,safe.Sketch));
        var nodes=new List<FeatureNode>{sketch};
        if(safe.Depth is double depth)nodes.Add(new FeatureNode(Guid.NewGuid(),"Ekstrüzyon1",new ExtrudeFeatureParameters(sketch.Id,depth)));
        return new FeatureDocument(Guid.NewGuid(),nodes.ToArray());
    }
    public static void Save(string path,FeatureDocument document)
    {
        var text=Serialize(document);var full=Path.GetFullPath(path);var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,text);File.Move(temp,full,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static FeatureDocument Load(string path)
    {
        if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Part file exceeds size limit.");
        return Deserialize(File.ReadAllText(path));
    }
    public static string Fingerprint(FeatureNode node)=>JsonSerializer.Serialize(node,Options);
    public static void Validate(FeatureDocument document)
    {
        if(document is null)throw new InvalidDataException("Missing document.");
        foreach(var node in document.Ordered())
        {
            if(string.IsNullOrWhiteSpace(node.Name))throw new InvalidDataException("Feature name required.");
            foreach(var reference in node.References??Array.Empty<FeatureReference>())
                if(reference is null||!Enum.IsDefined(reference.Kind)||string.IsNullOrWhiteSpace(reference.SemanticName))throw new InvalidDataException("Invalid semantic reference.");
            switch(node.Parameters)
            {
                case SketchFeatureParameters s: _=PartFile.Serialize(new(s.Plane,s.Sketch,null));break;
                case ExtrudeFeatureParameters e:
                    if(!double.IsFinite(e.Depth)||Math.Abs(e.Depth)<1e-9||document.Node(e.SketchId).Kind!=FeatureKind.Sketch)throw new InvalidDataException("Invalid extrusion.");break;
                case CutFeatureParameters c:
                    if(!Enum.IsDefined(c.Termination)||!double.IsFinite(c.Depth)||c.Depth<=1e-9||document.Node(c.SketchId).Kind!=FeatureKind.Sketch||document.Node(c.BaseFeatureId).Kind==FeatureKind.Sketch)throw new InvalidDataException("Invalid cut.");break;
                default:throw new InvalidDataException("Unsupported feature parameters.");
            }
        }
    }
}
