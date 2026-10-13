using System.Text.Json;
using System.Text.Json.Serialization;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.History;
using SrcCad.Core.Persistence;

namespace SrcCad.Core.Features;

public enum FeatureKind { Sketch, SpatialSketch, Extrude, Boundary, BoundaryCut, ExtrudedCut, Fillet, Chamfer, Hole, Revolve, RevolvedCut, Sweep, SweptCut, Loft, LoftedCut, Shell, Draft, Rib, Mirror, LinearPattern, CircularPattern, SketchPattern, BodyBoolean, MoveCopyBody, SplitBody, Dome, HoleWizard, ReferencePlane, ReferenceAxis, ReferencePoint, SheetMetalBase, SheetMetalCut, SheetMetalEdgeFlange, SheetMetalMiterFlange, SheetMetalConvert, SheetMetalHem, SheetMetalJog, SheetMetalRip, SheetMetalCornerRelief, SheetMetalBend, SheetMetalFlatten, StructuralMember, StructuralTrim, EndCap, WeldBead }
public enum RebuildState { Clean, Dirty, Suppressed, Blocked, Failed }
public enum ExtrusionTermination { Blind, ThroughAll }
public enum BodyBooleanOperation { Add, Subtract, Common }
public enum HoleWizardType { Simple, Counterbore, Countersink }
public enum TopologyKind { Body, Face, Edge, Vertex }
// Producer provenance + a semantic name; never a transient OCCT selection index.
public sealed record FeatureReference(Guid ProducerId, TopologyKind Kind, string SemanticName);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SketchFeatureParameters), "sketch")]
[JsonDerivedType(typeof(SpatialSketchFeatureParameters), "spatial-sketch")]
[JsonDerivedType(typeof(ExtrudeFeatureParameters), "extrude")]
[JsonDerivedType(typeof(BoundaryFeatureParameters), "boundary")]
[JsonDerivedType(typeof(BoundaryCutFeatureParameters), "boundary-cut")]
[JsonDerivedType(typeof(CutFeatureParameters), "extruded-cut")]
[JsonDerivedType(typeof(FilletFeatureParameters), "fillet")]
[JsonDerivedType(typeof(ChamferFeatureParameters), "chamfer")]
[JsonDerivedType(typeof(HoleFeatureParameters), "hole")]
[JsonDerivedType(typeof(RevolveFeatureParameters), "revolve")]
[JsonDerivedType(typeof(RevolveCutFeatureParameters), "revolved-cut")]
[JsonDerivedType(typeof(SweepFeatureParameters), "sweep")]
[JsonDerivedType(typeof(SweepCutFeatureParameters), "swept-cut")]
[JsonDerivedType(typeof(LoftFeatureParameters), "loft")]
[JsonDerivedType(typeof(LoftCutFeatureParameters), "lofted-cut")]
[JsonDerivedType(typeof(ShellFeatureParameters), "shell")]
[JsonDerivedType(typeof(DraftFeatureParameters), "draft")]
[JsonDerivedType(typeof(RibFeatureParameters), "rib")]
[JsonDerivedType(typeof(MirrorFeatureParameters), "mirror")]
[JsonDerivedType(typeof(LinearPatternFeatureParameters), "linear-pattern")]
[JsonDerivedType(typeof(CircularPatternFeatureParameters), "circular-pattern")]
[JsonDerivedType(typeof(SketchPatternFeatureParameters), "sketch-pattern")]
[JsonDerivedType(typeof(BodyBooleanFeatureParameters), "body-boolean")]
[JsonDerivedType(typeof(MoveCopyBodyFeatureParameters), "move-copy-body")]
[JsonDerivedType(typeof(SplitBodyFeatureParameters), "split-body")]
[JsonDerivedType(typeof(DomeFeatureParameters), "dome")]
[JsonDerivedType(typeof(HoleWizardFeatureParameters), "hole-wizard")]
[JsonDerivedType(typeof(ReferencePlaneFeatureParameters), "reference-plane")]
[JsonDerivedType(typeof(ReferenceAxisFeatureParameters), "reference-axis")]
[JsonDerivedType(typeof(ReferencePointFeatureParameters), "reference-point")]
[JsonDerivedType(typeof(SheetMetalBaseFeatureParameters), "sheet-metal-base")]
[JsonDerivedType(typeof(SheetMetalCutFeatureParameters), "sheet-metal-cut")]
[JsonDerivedType(typeof(SheetMetalEdgeFlangeFeatureParameters), "sheet-metal-edge-flange")]
[JsonDerivedType(typeof(SheetMetalMiterFlangeFeatureParameters), "sheet-metal-miter-flange")]
[JsonDerivedType(typeof(SheetMetalConvertFeatureParameters), "sheet-metal-convert")]
[JsonDerivedType(typeof(SheetMetalHemFeatureParameters), "sheet-metal-hem")]
[JsonDerivedType(typeof(SheetMetalJogFeatureParameters), "sheet-metal-jog")]
[JsonDerivedType(typeof(SheetMetalRipFeatureParameters), "sheet-metal-rip")]
[JsonDerivedType(typeof(SheetMetalCornerReliefFeatureParameters), "sheet-metal-corner-relief")]
[JsonDerivedType(typeof(SheetMetalBendFeatureParameters), "sheet-metal-bend")]
[JsonDerivedType(typeof(SheetMetalFlattenFeatureParameters), "sheet-metal-flatten")]
[JsonDerivedType(typeof(StructuralMemberFeatureParameters), "structural-member")]
[JsonDerivedType(typeof(StructuralTrimFeatureParameters), "structural-trim")]
[JsonDerivedType(typeof(EndCapFeatureParameters), "end-cap")]
[JsonDerivedType(typeof(WeldBeadFeatureParameters), "weld-bead")]
public abstract record FeatureParameters;
public sealed record SketchFeatureParameters(PlaneFrame Plane, SketchSnapshot Sketch) : FeatureParameters;
public sealed record SpatialSketchFeatureParameters(Vector3d[] Points,bool Closed=false) : FeatureParameters;
public sealed record ExtrudeFeatureParameters(Guid SketchId, double Depth) : FeatureParameters;
public sealed record BoundaryFeatureParameters(Guid[] ProfileSketchIds,Guid? BaseFeatureId=null) : FeatureParameters;
public sealed record BoundaryCutFeatureParameters(Guid BaseFeatureId,Guid[] ProfileSketchIds) : FeatureParameters;
public sealed record CutFeatureParameters(Guid BaseFeatureId, Guid SketchId, double Depth,
    ExtrusionTermination Termination = ExtrusionTermination.Blind, bool Reverse = false) : FeatureParameters;
public sealed record FilletFeatureParameters(Guid BaseFeatureId, double Radius) : FeatureParameters;
public sealed record ChamferFeatureParameters(Guid BaseFeatureId, double Distance) : FeatureParameters;
public sealed record HoleFeatureParameters(Guid BaseFeatureId, Vector3d Center, double Diameter, double Depth,
    ExtrusionTermination Termination = ExtrusionTermination.Blind, bool Reverse = true) : FeatureParameters;
public sealed record RevolveFeatureParameters(Guid SketchId, int AxisEntityId, double AngleDegrees = 360, bool Reverse = false, Guid? BaseFeatureId = null) : FeatureParameters;
public sealed record RevolveCutFeatureParameters(Guid BaseFeatureId, Guid SketchId, int AxisEntityId, double AngleDegrees = 360, bool Reverse = false) : FeatureParameters;
public sealed record SweepFeatureParameters(Guid ProfileSketchId, Guid PathSketchId, Guid? BaseFeatureId = null) : FeatureParameters;
public sealed record SweepCutFeatureParameters(Guid BaseFeatureId, Guid ProfileSketchId, Guid PathSketchId) : FeatureParameters;
public sealed record LoftFeatureParameters(Guid[] ProfileSketchIds, Guid? BaseFeatureId = null) : FeatureParameters;
public sealed record LoftCutFeatureParameters(Guid BaseFeatureId, Guid[] ProfileSketchIds) : FeatureParameters;
public sealed record ShellFeatureParameters(Guid BaseFeatureId, double Thickness, bool Outward = false) : FeatureParameters;
public sealed record DraftFeatureParameters(Guid BaseFeatureId, PlaneFrame NeutralPlane, double AngleDegrees, bool Reverse = false) : FeatureParameters;
public sealed record RibFeatureParameters(Guid BaseFeatureId, Guid SketchId, double Thickness, double Depth, bool Reverse = false) : FeatureParameters;
public sealed record MirrorFeatureParameters(Guid BaseFeatureId, PlaneFrame Plane, bool KeepOriginal = true) : FeatureParameters;
public sealed record LinearPatternFeatureParameters(Guid BaseFeatureId, Vector3d Direction, double Spacing, int Count) : FeatureParameters;
public sealed record CircularPatternFeatureParameters(Guid BaseFeatureId, Vector3d AxisOrigin, Vector3d AxisDirection, double AngleDegrees, int Count) : FeatureParameters;
public sealed record SketchPatternFeatureParameters(Guid BaseFeatureId,Guid SketchId) : FeatureParameters;
public sealed record BodyBooleanFeatureParameters(Guid BodyAId,Guid BodyBId,BodyBooleanOperation Operation) : FeatureParameters;
public sealed record MoveCopyBodyFeatureParameters(Guid BaseFeatureId,Vector3d Translation,Vector3d RotationDegrees,bool KeepOriginal=false) : FeatureParameters;
public sealed record SplitBodyFeatureParameters(Guid BaseFeatureId,PlaneFrame Plane) : FeatureParameters;
public sealed record DomeFeatureParameters(Guid BaseFeatureId,double Height) : FeatureParameters;
public sealed record HoleWizardFeatureParameters(Guid BaseFeatureId,Vector3d Center,double Diameter,double Depth,
    HoleWizardType Type=HoleWizardType.Simple,double CounterDiameter=0,double CounterDepth=0,bool Reverse=true) : FeatureParameters;
public sealed record ReferencePlaneFeatureParameters(PlaneFrame BasePlane, double Offset) : FeatureParameters;
public sealed record ReferenceAxisFeatureParameters(Vector3d Origin, Vector3d Direction) : FeatureParameters;
public sealed record ReferencePointFeatureParameters(Vector3d Position) : FeatureParameters;
public sealed record SheetMetalBaseFeatureParameters(Guid SketchId, double Thickness, double BendRadius, double KFactor, bool Reverse = false) : FeatureParameters;
public sealed record SheetMetalCutFeatureParameters(Guid BaseFeatureId, Guid SketchId, double Depth,
    ExtrusionTermination Termination = ExtrusionTermination.ThroughAll, bool Reverse = true) : FeatureParameters;
public sealed record SheetMetalEdgeFlangeFeatureParameters(Guid BaseFeatureId, double Length, double AngleDegrees,
    double Thickness, double BendRadius, bool Reverse = false) : FeatureParameters;
public sealed record SheetMetalMiterFlangeFeatureParameters(Guid BaseFeatureId, double Length, double AngleDegrees,
    double Thickness, double BendRadius, bool Reverse = false) : FeatureParameters;
public sealed record SheetMetalConvertFeatureParameters(Guid BaseFeatureId,double Thickness,double BendRadius,double KFactor) : FeatureParameters;
public sealed record SheetMetalHemFeatureParameters(Guid BaseFeatureId,double Width,double Thickness,double BendRadius,bool Reverse=false) : FeatureParameters;
public sealed record SheetMetalJogFeatureParameters(Guid BaseFeatureId,double Height,double Offset,double Thickness,bool Reverse=false) : FeatureParameters;
public sealed record SheetMetalRipFeatureParameters(Guid BaseFeatureId,double Gap) : FeatureParameters;
public sealed record SheetMetalCornerReliefFeatureParameters(Guid BaseFeatureId,double Radius) : FeatureParameters;
public sealed record SheetMetalBendFeatureParameters(Guid BaseFeatureId,Guid SketchId,double AngleDegrees,bool Reverse=false) : FeatureParameters;
public sealed record SheetMetalBendStep(Guid SketchId,double AngleDegrees,bool Reverse=false);
public sealed record SheetMetalFlattenFeatureParameters(Guid BaseFeatureId,SheetMetalBendStep[] Steps,bool Refold=false) : FeatureParameters;
public sealed record StructuralMemberFeatureParameters(Guid ProfileSketchId, Guid PathSketchId) : FeatureParameters;
public sealed record StructuralTrimFeatureParameters(Guid BaseFeatureId,PlaneFrame Plane,bool KeepPositive=true) : FeatureParameters;
public sealed record EndCapFeatureParameters(Guid BaseFeatureId,double Thickness,bool Reverse=false) : FeatureParameters;
public sealed record WeldBeadFeatureParameters(Guid BaseFeatureId,double Radius) : FeatureParameters;
public sealed record FeatureNode(Guid Id, string Name, FeatureParameters Parameters,
    bool Suppressed = false, FeatureReference[]? References = null)
{
    [JsonIgnore] public FeatureKind Kind => Parameters switch
    { SketchFeatureParameters => FeatureKind.Sketch, SpatialSketchFeatureParameters => FeatureKind.SpatialSketch,
      ExtrudeFeatureParameters => FeatureKind.Extrude, BoundaryFeatureParameters => FeatureKind.Boundary,
      BoundaryCutFeatureParameters => FeatureKind.BoundaryCut,
      CutFeatureParameters => FeatureKind.ExtrudedCut, FilletFeatureParameters => FeatureKind.Fillet,
      ChamferFeatureParameters => FeatureKind.Chamfer, HoleFeatureParameters => FeatureKind.Hole,
      RevolveFeatureParameters => FeatureKind.Revolve, RevolveCutFeatureParameters => FeatureKind.RevolvedCut,
      SweepFeatureParameters => FeatureKind.Sweep, SweepCutFeatureParameters => FeatureKind.SweptCut,
      LoftFeatureParameters => FeatureKind.Loft, LoftCutFeatureParameters => FeatureKind.LoftedCut,
      ShellFeatureParameters => FeatureKind.Shell, DraftFeatureParameters => FeatureKind.Draft,
      RibFeatureParameters => FeatureKind.Rib, MirrorFeatureParameters => FeatureKind.Mirror,
      LinearPatternFeatureParameters => FeatureKind.LinearPattern, CircularPatternFeatureParameters => FeatureKind.CircularPattern,
      SketchPatternFeatureParameters => FeatureKind.SketchPattern, BodyBooleanFeatureParameters => FeatureKind.BodyBoolean,
      MoveCopyBodyFeatureParameters => FeatureKind.MoveCopyBody, SplitBodyFeatureParameters => FeatureKind.SplitBody,
      DomeFeatureParameters => FeatureKind.Dome, HoleWizardFeatureParameters => FeatureKind.HoleWizard,
      ReferencePlaneFeatureParameters => FeatureKind.ReferencePlane, ReferenceAxisFeatureParameters => FeatureKind.ReferenceAxis,
      ReferencePointFeatureParameters => FeatureKind.ReferencePoint, SheetMetalBaseFeatureParameters => FeatureKind.SheetMetalBase,
      SheetMetalCutFeatureParameters => FeatureKind.SheetMetalCut, SheetMetalEdgeFlangeFeatureParameters => FeatureKind.SheetMetalEdgeFlange,
      SheetMetalMiterFlangeFeatureParameters => FeatureKind.SheetMetalMiterFlange,
      SheetMetalConvertFeatureParameters => FeatureKind.SheetMetalConvert,
      SheetMetalHemFeatureParameters => FeatureKind.SheetMetalHem,
      SheetMetalJogFeatureParameters => FeatureKind.SheetMetalJog,
      SheetMetalRipFeatureParameters => FeatureKind.SheetMetalRip,
      SheetMetalCornerReliefFeatureParameters => FeatureKind.SheetMetalCornerRelief,
      SheetMetalBendFeatureParameters => FeatureKind.SheetMetalBend,
      SheetMetalFlattenFeatureParameters => FeatureKind.SheetMetalFlatten,
      StructuralMemberFeatureParameters => FeatureKind.StructuralMember, StructuralTrimFeatureParameters => FeatureKind.StructuralTrim,
      EndCapFeatureParameters => FeatureKind.EndCap,
      WeldBeadFeatureParameters => FeatureKind.WeldBead,
      _ => throw new InvalidDataException("Unsupported feature") };
    [JsonIgnore] public Guid[] Dependencies => (Parameters switch
    {
        SpatialSketchFeatureParameters => Array.Empty<Guid>(),
        ExtrudeFeatureParameters e => new[] { e.SketchId },
        BoundaryFeatureParameters b => (b.BaseFeatureId is Guid baseId?new[]{baseId}.Concat(b.ProfileSketchIds):b.ProfileSketchIds).ToArray(),
        BoundaryCutFeatureParameters b => new[]{b.BaseFeatureId}.Concat(b.ProfileSketchIds).ToArray(),
        CutFeatureParameters c => new[] { c.BaseFeatureId, c.SketchId },
        FilletFeatureParameters f => new[] { f.BaseFeatureId },
        ChamferFeatureParameters c => new[] { c.BaseFeatureId },
        HoleFeatureParameters h => new[] { h.BaseFeatureId },
        RevolveFeatureParameters r => r.BaseFeatureId is Guid rb ? new[] { rb,r.SketchId } : new[] { r.SketchId },
        RevolveCutFeatureParameters r => new[] { r.BaseFeatureId,r.SketchId },
        SweepFeatureParameters s => s.BaseFeatureId is Guid sb ? new[] { sb,s.ProfileSketchId,s.PathSketchId } : new[] { s.ProfileSketchId,s.PathSketchId },
        SweepCutFeatureParameters s => new[] { s.BaseFeatureId,s.ProfileSketchId,s.PathSketchId },
        LoftFeatureParameters l => (l.BaseFeatureId is Guid lb ? new[] { lb }.Concat(l.ProfileSketchIds) : l.ProfileSketchIds).ToArray(),
        LoftCutFeatureParameters l => new[] { l.BaseFeatureId }.Concat(l.ProfileSketchIds).ToArray(),
        ShellFeatureParameters s => new[] { s.BaseFeatureId },
        DraftFeatureParameters d => new[] { d.BaseFeatureId },
        RibFeatureParameters r => new[] { r.BaseFeatureId,r.SketchId },
        MirrorFeatureParameters m => new[] { m.BaseFeatureId },
        LinearPatternFeatureParameters p => new[] { p.BaseFeatureId },
        CircularPatternFeatureParameters p => new[] { p.BaseFeatureId },
        SketchPatternFeatureParameters p => new[] { p.BaseFeatureId,p.SketchId },
        BodyBooleanFeatureParameters b => new[] { b.BodyAId,b.BodyBId },
        MoveCopyBodyFeatureParameters m => new[] { m.BaseFeatureId },
        SplitBodyFeatureParameters s => new[] { s.BaseFeatureId },
        DomeFeatureParameters d => new[] { d.BaseFeatureId },
        HoleWizardFeatureParameters h => new[] { h.BaseFeatureId },
        ReferencePlaneFeatureParameters => Array.Empty<Guid>(),
        ReferenceAxisFeatureParameters => Array.Empty<Guid>(),
        ReferencePointFeatureParameters => Array.Empty<Guid>(),
        SheetMetalBaseFeatureParameters s => new[] { s.SketchId },
        SheetMetalCutFeatureParameters s => new[] { s.BaseFeatureId,s.SketchId },
        SheetMetalEdgeFlangeFeatureParameters s => new[] { s.BaseFeatureId },
        SheetMetalMiterFlangeFeatureParameters s => new[] { s.BaseFeatureId },
        SheetMetalConvertFeatureParameters s => new[] { s.BaseFeatureId },
        SheetMetalHemFeatureParameters s => new[] { s.BaseFeatureId },
        SheetMetalJogFeatureParameters s => new[] { s.BaseFeatureId },
        SheetMetalRipFeatureParameters s => new[] { s.BaseFeatureId },
        SheetMetalCornerReliefFeatureParameters s => new[] { s.BaseFeatureId },
        SheetMetalBendFeatureParameters s => new[] { s.BaseFeatureId,s.SketchId },
        SheetMetalFlattenFeatureParameters s => new[] { s.BaseFeatureId }.Concat((s.Steps??Array.Empty<SheetMetalBendStep>()).Select(step=>step.SketchId)).ToArray(),
        StructuralMemberFeatureParameters s => new[] { s.ProfileSketchId,s.PathSketchId },
        StructuralTrimFeatureParameters s => new[] { s.BaseFeatureId },
        EndCapFeatureParameters e => new[] { e.BaseFeatureId },
        WeldBeadFeatureParameters w => new[] { w.BaseFeatureId },
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
    public static FeatureDocument FromLegacy(ExtrusionState model, Guid? documentId=null, Guid? sketchId=null, Guid? bossId=null)
    {
        // Reuse legacy validation, preserve exact sketch IDs and parameter intent.
        var safe=PartFile.Deserialize(PartFile.Serialize(model));
        var sketch=new FeatureNode(sketchId??Guid.NewGuid(),"Çizim1",new SketchFeatureParameters(safe.Plane,safe.Sketch));
        var nodes=new List<FeatureNode>{sketch};
        if(safe.Depth is double depth)nodes.Add(new FeatureNode(bossId??Guid.NewGuid(),"Ekstrüzyon1",new ExtrudeFeatureParameters(sketch.Id,depth)));
        return new FeatureDocument(documentId??Guid.NewGuid(),nodes.ToArray());
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
            {
                if(reference is null||!Enum.IsDefined(reference.Kind)||string.IsNullOrWhiteSpace(reference.SemanticName))
                    throw new InvalidDataException("Invalid semantic reference.");
                if(!document.Features.Any(f=>f.Id==reference.ProducerId))
                    throw new InvalidDataException("Semantic reference producer is missing.");
            }
            switch(node.Parameters)
            {
                case SketchFeatureParameters s: _=PartFile.Serialize(new(s.Plane,s.Sketch,null));break;
                case SpatialSketchFeatureParameters s:
                    if(s.Points is null||s.Points.Length<2||(s.Closed&&s.Points.Length<3)||
                       s.Points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z))||
                       s.Points.Zip(s.Points.Skip(1),(a,b)=>(b-a).Length).Any(length=>length<=1e-9))
                        throw new InvalidDataException("Invalid 3D sketch.");
                    break;
                case ExtrudeFeatureParameters e:
                    if(!double.IsFinite(e.Depth)||Math.Abs(e.Depth)<1e-9||document.Node(e.SketchId).Kind!=FeatureKind.Sketch)throw new InvalidDataException("Invalid extrusion.");break;
                case BoundaryFeatureParameters b:
                    if(b.ProfileSketchIds is null||b.ProfileSketchIds.Length<2||b.ProfileSketchIds.Distinct().Count()!=b.ProfileSketchIds.Length||
                       b.ProfileSketchIds.Any(id=>document.Node(id).Kind!=FeatureKind.Sketch)||
                       (b.BaseFeatureId is Guid bb&&document.Node(bb).Kind==FeatureKind.Sketch))
                        throw new InvalidDataException("Invalid boundary boss.");
                    break;
                case BoundaryCutFeatureParameters b:
                    if(document.Node(b.BaseFeatureId).Kind==FeatureKind.Sketch||b.ProfileSketchIds is null||b.ProfileSketchIds.Length<2||
                       b.ProfileSketchIds.Distinct().Count()!=b.ProfileSketchIds.Length||b.ProfileSketchIds.Any(id=>document.Node(id).Kind!=FeatureKind.Sketch))
                        throw new InvalidDataException("Invalid boundary cut.");
                    break;
                case CutFeatureParameters c:
                    if(!Enum.IsDefined(c.Termination)||!double.IsFinite(c.Depth)||c.Depth<=1e-9||document.Node(c.SketchId).Kind!=FeatureKind.Sketch||document.Node(c.BaseFeatureId).Kind==FeatureKind.Sketch)throw new InvalidDataException("Invalid cut.");break;
                case FilletFeatureParameters f:
                    if(!double.IsFinite(f.Radius)||f.Radius<=1e-9||document.Node(f.BaseFeatureId).Kind==FeatureKind.Sketch||
                       node.References is null||node.References.Length==0||node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=f.BaseFeatureId))
                        throw new InvalidDataException("Invalid fillet.");
                    break;
                case ChamferFeatureParameters c:
                    if(!double.IsFinite(c.Distance)||c.Distance<=1e-9||document.Node(c.BaseFeatureId).Kind==FeatureKind.Sketch||
                       node.References is null||node.References.Length==0||node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=c.BaseFeatureId))
                        throw new InvalidDataException("Invalid chamfer.");
                    break;
                case HoleFeatureParameters h:
                    if(!double.IsFinite(h.Center.X)||!double.IsFinite(h.Center.Y)||!double.IsFinite(h.Center.Z)||
                       !double.IsFinite(h.Diameter)||h.Diameter<=1e-9||!double.IsFinite(h.Depth)||h.Depth<=1e-9||
                       !Enum.IsDefined(h.Termination)||document.Node(h.BaseFeatureId).Kind==FeatureKind.Sketch||
                       node.References is null||node.References.Length!=1||node.References[0].Kind!=TopologyKind.Face||
                       node.References[0].ProducerId!=h.BaseFeatureId)
                        throw new InvalidDataException("Invalid hole.");
                    break;
                case RevolveFeatureParameters r:
                    if(!double.IsFinite(r.AngleDegrees)||r.AngleDegrees<=1e-9||r.AngleDegrees>360.0||
                       document.Node(r.SketchId).Parameters is not SketchFeatureParameters rs||
                       !rs.Sketch.Entities.OfType<SketchLine>().Any(l=>l.Id==r.AxisEntityId&&l.Construction)||
                       (r.BaseFeatureId is Guid rb&&document.Node(rb).Kind==FeatureKind.Sketch))
                        throw new InvalidDataException("Invalid revolve.");
                    break;
                case RevolveCutFeatureParameters r:
                    if(!double.IsFinite(r.AngleDegrees)||r.AngleDegrees<=1e-9||r.AngleDegrees>360.0||
                       document.Node(r.BaseFeatureId).Kind==FeatureKind.Sketch||
                       document.Node(r.SketchId).Parameters is not SketchFeatureParameters rcs||
                       !rcs.Sketch.Entities.OfType<SketchLine>().Any(l=>l.Id==r.AxisEntityId&&l.Construction))
                        throw new InvalidDataException("Invalid revolved cut.");
                    break;
                case SweepFeatureParameters s:
                    if(document.Node(s.ProfileSketchId).Kind!=FeatureKind.Sketch||document.Node(s.PathSketchId).Kind is not (FeatureKind.Sketch or FeatureKind.SpatialSketch)||
                       s.ProfileSketchId==s.PathSketchId||(s.BaseFeatureId is Guid sb&&document.Node(sb).Kind==FeatureKind.Sketch))
                        throw new InvalidDataException("Invalid sweep.");
                    break;
                case SweepCutFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||document.Node(s.ProfileSketchId).Kind!=FeatureKind.Sketch||
                       document.Node(s.PathSketchId).Kind is not (FeatureKind.Sketch or FeatureKind.SpatialSketch)||s.ProfileSketchId==s.PathSketchId)
                        throw new InvalidDataException("Invalid swept cut.");
                    break;
                case LoftFeatureParameters l:
                    if(l.ProfileSketchIds is null||l.ProfileSketchIds.Length<2||l.ProfileSketchIds.Distinct().Count()!=l.ProfileSketchIds.Length||
                       l.ProfileSketchIds.Any(id=>document.Node(id).Kind!=FeatureKind.Sketch)||
                       (l.BaseFeatureId is Guid lb&&document.Node(lb).Kind==FeatureKind.Sketch))
                        throw new InvalidDataException("Invalid loft.");
                    break;
                case LoftCutFeatureParameters l:
                    if(document.Node(l.BaseFeatureId).Kind==FeatureKind.Sketch||l.ProfileSketchIds is null||l.ProfileSketchIds.Length<2||
                       l.ProfileSketchIds.Distinct().Count()!=l.ProfileSketchIds.Length||l.ProfileSketchIds.Any(id=>document.Node(id).Kind!=FeatureKind.Sketch))
                        throw new InvalidDataException("Invalid lofted cut.");
                    break;
                case ShellFeatureParameters s:
                    if(!double.IsFinite(s.Thickness)||s.Thickness<=1e-9||document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||
                       node.References is null||node.References.Length==0||
                       node.References.Any(r=>r.Kind!=TopologyKind.Face||r.ProducerId!=s.BaseFeatureId))
                        throw new InvalidDataException("Invalid shell.");
                    break;
                case DraftFeatureParameters d:
                    if(document.Node(d.BaseFeatureId).Kind==FeatureKind.Sketch||
                       !double.IsFinite(d.AngleDegrees)||d.AngleDegrees<=1e-9||d.AngleDegrees>=89.0||
                       node.References is null||node.References.Length==0||
                       node.References.Any(r=>r.Kind!=TopologyKind.Face||r.ProducerId!=d.BaseFeatureId))
                        throw new InvalidDataException("Invalid draft.");
                    _=d.NeutralPlane.Validate();
                    break;
                case RibFeatureParameters r:
                    if(document.Node(r.BaseFeatureId).Kind==FeatureKind.Sketch||
                       document.Node(r.SketchId).Parameters is not SketchFeatureParameters rs||
                       !rs.Sketch.Entities.OfType<SketchLine>().Any(line=>!line.Construction)||
                       !double.IsFinite(r.Thickness)||r.Thickness<=1e-9||
                       !double.IsFinite(r.Depth)||r.Depth<=1e-9)
                        throw new InvalidDataException("Invalid rib.");
                    break;
                case MirrorFeatureParameters m:
                    if(document.Node(m.BaseFeatureId).Kind==FeatureKind.Sketch)throw new InvalidDataException("Invalid mirror base.");
                    _=m.Plane.Validate();
                    break;
                case LinearPatternFeatureParameters p:
                    if(document.Node(p.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(p.Spacing)||p.Spacing<=1e-9||
                       p.Count<2||p.Count>500||!double.IsFinite(p.Direction.X)||!double.IsFinite(p.Direction.Y)||!double.IsFinite(p.Direction.Z)||
                       p.Direction.Length<=1e-9)throw new InvalidDataException("Invalid linear pattern.");
                    break;
                case CircularPatternFeatureParameters p:
                    if(document.Node(p.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(p.AngleDegrees)||p.AngleDegrees<=1e-9||p.AngleDegrees>360||
                       p.Count<2||p.Count>500||!double.IsFinite(p.AxisOrigin.X)||!double.IsFinite(p.AxisOrigin.Y)||!double.IsFinite(p.AxisOrigin.Z)||
                       !double.IsFinite(p.AxisDirection.X)||!double.IsFinite(p.AxisDirection.Y)||!double.IsFinite(p.AxisDirection.Z)||
                       p.AxisDirection.Length<=1e-9)throw new InvalidDataException("Invalid circular pattern.");
                    break;
                case SketchPatternFeatureParameters p:
                    if(document.Node(p.BaseFeatureId).Kind==FeatureKind.Sketch||
                       document.Node(p.SketchId).Parameters is not SketchFeatureParameters patternSketch||
                       patternSketch.Sketch.Points.Values.Count(point=>point.Id!=0)<1)
                        throw new InvalidDataException("Invalid sketch pattern.");
                    break;
                case BodyBooleanFeatureParameters b:
                    if(b.BodyAId==b.BodyBId||document.Node(b.BodyAId).Kind==FeatureKind.Sketch||document.Node(b.BodyBId).Kind==FeatureKind.Sketch||
                       !Enum.IsDefined(b.Operation))throw new InvalidDataException("Invalid body boolean.");
                    break;
                case MoveCopyBodyFeatureParameters m:
                    if(document.Node(m.BaseFeatureId).Kind==FeatureKind.Sketch||
                       !double.IsFinite(m.Translation.X)||!double.IsFinite(m.Translation.Y)||!double.IsFinite(m.Translation.Z)||
                       !double.IsFinite(m.RotationDegrees.X)||!double.IsFinite(m.RotationDegrees.Y)||!double.IsFinite(m.RotationDegrees.Z))
                        throw new InvalidDataException("Invalid move/copy body.");
                    break;
                case SplitBodyFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch)throw new InvalidDataException("Invalid split body.");
                    _=s.Plane.Validate();break;
                case DomeFeatureParameters d:
                    if(document.Node(d.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(d.Height)||Math.Abs(d.Height)<=1e-9||
                       node.References is null||node.References.Length!=1||node.References[0].Kind!=TopologyKind.Face||node.References[0].ProducerId!=d.BaseFeatureId)
                        throw new InvalidDataException("Invalid dome.");
                    break;
                case HoleWizardFeatureParameters h:
                    if(document.Node(h.BaseFeatureId).Kind==FeatureKind.Sketch||!Enum.IsDefined(h.Type)||
                       !double.IsFinite(h.Center.X)||!double.IsFinite(h.Center.Y)||!double.IsFinite(h.Center.Z)||
                       !double.IsFinite(h.Diameter)||h.Diameter<=1e-9||!double.IsFinite(h.Depth)||h.Depth<=1e-9||
                       !double.IsFinite(h.CounterDiameter)||!double.IsFinite(h.CounterDepth)||
                       (h.Type!=HoleWizardType.Simple&&(h.CounterDiameter<=h.Diameter||h.CounterDepth<=1e-9))||
                       node.References is null||node.References.Length!=1||node.References[0].Kind!=TopologyKind.Face||node.References[0].ProducerId!=h.BaseFeatureId)
                        throw new InvalidDataException("Invalid hole wizard.");
                    break;
                case ReferencePlaneFeatureParameters p:
                    _=p.BasePlane.Validate();
                    if(!double.IsFinite(p.Offset))throw new InvalidDataException("Invalid reference plane.");
                    break;
                case ReferenceAxisFeatureParameters a:
                    if(!double.IsFinite(a.Origin.X)||!double.IsFinite(a.Origin.Y)||!double.IsFinite(a.Origin.Z)||
                       !double.IsFinite(a.Direction.X)||!double.IsFinite(a.Direction.Y)||!double.IsFinite(a.Direction.Z)||
                       a.Direction.Length<=1e-9)throw new InvalidDataException("Invalid reference axis.");
                    break;
                case ReferencePointFeatureParameters p:
                    if(!double.IsFinite(p.Position.X)||!double.IsFinite(p.Position.Y)||!double.IsFinite(p.Position.Z))
                        throw new InvalidDataException("Invalid reference point.");
                    break;
                case SheetMetalBaseFeatureParameters s:
                    if(document.Node(s.SketchId).Kind!=FeatureKind.Sketch||
                       !double.IsFinite(s.Thickness)||s.Thickness<=1e-9||
                       !double.IsFinite(s.BendRadius)||s.BendRadius<0||
                       !double.IsFinite(s.KFactor)||s.KFactor<0||s.KFactor>1)
                        throw new InvalidDataException("Invalid sheet metal base flange.");
                    break;
                case SheetMetalCutFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||document.Node(s.SketchId).Kind!=FeatureKind.Sketch||
                       !Enum.IsDefined(s.Termination)||!double.IsFinite(s.Depth)||s.Depth<=1e-9)
                        throw new InvalidDataException("Invalid sheet metal cut.");
                    break;
                case SheetMetalEdgeFlangeFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||
                       !double.IsFinite(s.Length)||s.Length<=1e-9||
                       !double.IsFinite(s.AngleDegrees)||s.AngleDegrees<=1e-6||s.AngleDegrees>=179.999||
                       !double.IsFinite(s.Thickness)||s.Thickness<=1e-9||
                       !double.IsFinite(s.BendRadius)||s.BendRadius<0||
                       node.References is null||node.References.Length==0||
                       node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=s.BaseFeatureId))
                        throw new InvalidDataException("Invalid sheet metal edge flange.");
                    break;
                case SheetMetalMiterFlangeFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||
                       !double.IsFinite(s.Length)||s.Length<=1e-9||
                       !double.IsFinite(s.AngleDegrees)||s.AngleDegrees<=1e-6||s.AngleDegrees>=179.999||
                       !double.IsFinite(s.Thickness)||s.Thickness<=1e-9||
                       !double.IsFinite(s.BendRadius)||s.BendRadius<0||
                       node.References is null||node.References.Length<2||
                       node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=s.BaseFeatureId))
                        throw new InvalidDataException("Invalid sheet metal miter flange.");
                    break;
                case SheetMetalConvertFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||
                       !double.IsFinite(s.Thickness)||s.Thickness<=1e-9||
                       !double.IsFinite(s.BendRadius)||s.BendRadius<0||
                       !double.IsFinite(s.KFactor)||s.KFactor<0||s.KFactor>1)
                        throw new InvalidDataException("Invalid sheet metal conversion.");
                    break;
                case SheetMetalHemFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(s.Width)||s.Width<=1e-9||
                       !double.IsFinite(s.Thickness)||s.Thickness<=1e-9||!double.IsFinite(s.BendRadius)||s.BendRadius<0||
                       node.References is null||node.References.Length==0||node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=s.BaseFeatureId))
                        throw new InvalidDataException("Invalid sheet metal hem.");
                    break;
                case SheetMetalJogFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(s.Height)||s.Height<=1e-9||
                       !double.IsFinite(s.Offset)||s.Offset<=1e-9||!double.IsFinite(s.Thickness)||s.Thickness<=1e-9||
                       node.References is null||node.References.Length==0||node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=s.BaseFeatureId))
                        throw new InvalidDataException("Invalid sheet metal jog.");
                    break;
                case SheetMetalRipFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(s.Gap)||s.Gap<=1e-9||
                       node.References is null||node.References.Length==0||node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=s.BaseFeatureId))
                        throw new InvalidDataException("Invalid sheet metal rip.");
                    break;
                case SheetMetalCornerReliefFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(s.Radius)||s.Radius<=1e-9||
                       node.References is null||node.References.Length==0||node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=s.BaseFeatureId))
                        throw new InvalidDataException("Invalid sheet metal corner relief.");
                    break;
                case SheetMetalBendFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||
                       document.Node(s.SketchId).Parameters is not SketchFeatureParameters bendSketch||
                       bendSketch.Sketch.Entities.OfType<SketchLine>().Count(l=>!l.Construction)!=1||
                       !double.IsFinite(s.AngleDegrees)||Math.Abs(s.AngleDegrees)<=1e-6||Math.Abs(s.AngleDegrees)>=179.0)
                        throw new InvalidDataException("Invalid sheet metal bend.");
                    break;
                case SheetMetalFlattenFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch||s.Steps is null||s.Steps.Length==0||
                       s.Steps.Any(step=>document.Node(step.SketchId).Parameters is not SketchFeatureParameters bend||
                           bend.Sketch.Entities.OfType<SketchLine>().Count(l=>!l.Construction)!=1||
                           !double.IsFinite(step.AngleDegrees)||Math.Abs(step.AngleDegrees)<=1e-6||Math.Abs(step.AngleDegrees)>=179.0))
                        throw new InvalidDataException("Invalid sheet metal flatten/refold.");
                    break;
                case StructuralMemberFeatureParameters s:
                    if(s.ProfileSketchId==s.PathSketchId||
                       document.Node(s.ProfileSketchId).Kind!=FeatureKind.Sketch||
                       document.Node(s.PathSketchId).Kind is not (FeatureKind.Sketch or FeatureKind.SpatialSketch))
                        throw new InvalidDataException("Invalid structural member.");
                    break;
                case StructuralTrimFeatureParameters s:
                    if(document.Node(s.BaseFeatureId).Kind==FeatureKind.Sketch)throw new InvalidDataException("Invalid structural trim base.");
                    _=s.Plane.Validate();
                    break;
                case EndCapFeatureParameters e:
                    if(document.Node(e.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(e.Thickness)||e.Thickness<=1e-9||
                       node.References is null||node.References.Length!=1||node.References[0].Kind!=TopologyKind.Face||
                       node.References[0].ProducerId!=e.BaseFeatureId)
                        throw new InvalidDataException("Invalid end cap.");
                    break;
                case WeldBeadFeatureParameters w:
                    if(document.Node(w.BaseFeatureId).Kind==FeatureKind.Sketch||!double.IsFinite(w.Radius)||w.Radius<=1e-9||
                       node.References is null||node.References.Length==0||
                       node.References.Any(r=>r.Kind!=TopologyKind.Edge||r.ProducerId!=w.BaseFeatureId))
                        throw new InvalidDataException("Invalid weld bead.");
                    break;
                default:throw new InvalidDataException("Unsupported feature parameters.");
            }
        }
    }
}
