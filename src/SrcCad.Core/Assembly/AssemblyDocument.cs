using System.Text.Json;
using SrcCad.Core.Features;
using SrcCad.Core.Geometry;

namespace SrcCad.Core.Assembly;

public sealed record RigidTransform(Vector3d Translation, Vector3d RotationDegrees)
{
    public static RigidTransform Identity { get; } = new(Vector3d.Zero,Vector3d.Zero);
    public RigidTransform Validate()
    {
        static bool Finite(Vector3d v)=>double.IsFinite(v.X)&&double.IsFinite(v.Y)&&double.IsFinite(v.Z);
        if(!Finite(Translation)||!Finite(RotationDegrees))throw new InvalidDataException("Invalid component transform.");
        return this;
    }
}

public sealed record AssemblyComponent(
    Guid Id,
    string Name,
    string SourcePath,
    RigidTransform Transform,
    bool Fixed=false,
    bool Suppressed=false,
    PlaneFrame? MirrorPlane=null);

public enum MateType { Coincident, Parallel, Perpendicular, Concentric, Distance, Angle }

public sealed record AssemblyReference(Guid ComponentId,TopologyKind Kind,string SemanticName);

public sealed record AssemblyMate(
    Guid Id,
    string Name,
    MateType Type,
    AssemblyReference A,
    AssemblyReference B,
    double Value=0,
    bool Suppressed=false);

public sealed record AssemblyDocument(Guid Id,AssemblyComponent[] Components,AssemblyMate[] Mates)
{
    public AssemblyComponent Component(Guid id)=>Components.Single(c=>c.Id==id);

    public AssemblyDocument AddComponent(AssemblyComponent component)
        => this with{Components=Components.Append(component).ToArray()};

    public AssemblyDocument RemoveComponent(Guid componentId)
        => this with
        {
            Components=Components.Where(c=>c.Id!=componentId).ToArray(),
            Mates=Mates.Where(m=>m.A.ComponentId!=componentId&&m.B.ComponentId!=componentId).ToArray()
        };

    public AssemblyDocument AddMate(AssemblyMate mate)
        => this with{Mates=Mates.Append(mate).ToArray()};
}

public static class AssemblyDocumentFile
{
    private sealed record Envelope(int SchemaVersion,string Units,AssemblyDocument Document);
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,MaxDepth=64};

    public static string Serialize(AssemblyDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(new Envelope(1,"mm",document),Options);
    }

    public static AssemblyDocument Deserialize(string json)
    {
        if(json.Length>32*1024*1024)throw new InvalidDataException("Assembly file exceeds size limit.");
        var envelope=JsonSerializer.Deserialize<Envelope>(json,Options)??throw new InvalidDataException("Empty assembly document.");
        if(envelope.SchemaVersion!=1||envelope.Units!="mm")throw new InvalidDataException("Unsupported assembly schema/units.");
        Validate(envelope.Document);
        return envelope.Document;
    }

    public static AssemblyDocument Clone(AssemblyDocument document)=>Deserialize(Serialize(document));

    public static void Save(string path,AssemblyDocument document)
    {
        var full=Path.GetFullPath(path);
        var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,Serialize(document));
            File.Move(temp,full,true);
        }
        finally
        {
            if(File.Exists(temp))File.Delete(temp);
        }
    }

    public static AssemblyDocument Load(string path)
    {
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Assembly file exceeds size limit.");
        return Deserialize(File.ReadAllText(path));
    }

    public static void Validate(AssemblyDocument document)
    {
        if(document is null||document.Id==Guid.Empty||document.Components is null||document.Mates is null)
            throw new InvalidDataException("Invalid assembly document.");
        if(document.Components.Any(c=>c is null||c.Id==Guid.Empty||string.IsNullOrWhiteSpace(c.Name)||string.IsNullOrWhiteSpace(c.SourcePath)))
            throw new InvalidDataException("Invalid assembly component.");
        if(document.Components.Select(c=>c.Id).Distinct().Count()!=document.Components.Length)
            throw new InvalidDataException("Duplicate assembly component id.");
        foreach(var component in document.Components)
        {
            _=component.Transform.Validate();
            if(component.MirrorPlane is PlaneFrame mirror)_=mirror.Validate();
        }

        var ids=document.Components.Select(c=>c.Id).ToHashSet();
        if(document.Mates.Select(m=>m.Id).Distinct().Count()!=document.Mates.Length)
            throw new InvalidDataException("Duplicate assembly mate id.");
        foreach(var mate in document.Mates)
        {
            if(mate is null||mate.Id==Guid.Empty||string.IsNullOrWhiteSpace(mate.Name)||!Enum.IsDefined(mate.Type)||
               !ids.Contains(mate.A.ComponentId)||!ids.Contains(mate.B.ComponentId)||
               mate.A.ComponentId==mate.B.ComponentId||
               string.IsNullOrWhiteSpace(mate.A.SemanticName)||string.IsNullOrWhiteSpace(mate.B.SemanticName)||
               !Enum.IsDefined(mate.A.Kind)||!Enum.IsDefined(mate.B.Kind)||
               !double.IsFinite(mate.Value))
                throw new InvalidDataException("Invalid assembly mate.");
        }
    }
}
