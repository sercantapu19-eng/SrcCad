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
    PlaneFrame? MirrorPlane=null,
    Vector3d? ExplodeOffset=null);

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


public static class AssemblyReferenceCatalog
{
    public const string Origin="ORIGIN";
    public const string FrontPlane="PLANE:FRONT";
    public const string TopPlane="PLANE:TOP";
    public const string RightPlane="PLANE:RIGHT";
    public const string XAxis="AXIS:X";
    public const string YAxis="AXIS:Y";
    public const string ZAxis="AXIS:Z";

    public static readonly (string Name,TopologyKind Kind,string Semantic)[] Standard =
    {
        ("Orijin",TopologyKind.Vertex,Origin),
        ("Ön Düzlem",TopologyKind.Face,FrontPlane),
        ("Üst Düzlem",TopologyKind.Face,TopPlane),
        ("Sağ Düzlem",TopologyKind.Face,RightPlane),
        ("X Ekseni",TopologyKind.Edge,XAxis),
        ("Y Ekseni",TopologyKind.Edge,YAxis),
        ("Z Ekseni",TopologyKind.Edge,ZAxis)
    };

    public static bool TryDirection(AssemblyReference reference,out Vector3d direction)
    {
        direction=reference.SemanticName switch
        {
            FrontPlane or YAxis=>Vector3d.UnitY,
            TopPlane or ZAxis=>Vector3d.UnitZ,
            RightPlane or XAxis=>Vector3d.UnitX,
            _=>Vector3d.Zero
        };
        return direction.Length>1e-12;
    }
}

public static class AssemblyMateSolver
{
    private static double Rad(double degrees)=>degrees*Math.PI/180.0;
    private static double Deg(double radians)=>radians*180.0/Math.PI;

    private static double[,] Multiply(double[,] a,double[,] b)
    {
        var r=new double[3,3];
        for(var i=0;i<3;i++)for(var j=0;j<3;j++)
            r[i,j]=a[i,0]*b[0,j]+a[i,1]*b[1,j]+a[i,2]*b[2,j];
        return r;
    }

    private static Vector3d Multiply(double[,] m,Vector3d v)
        => new(
            m[0,0]*v.X+m[0,1]*v.Y+m[0,2]*v.Z,
            m[1,0]*v.X+m[1,1]*v.Y+m[1,2]*v.Z,
            m[2,0]*v.X+m[2,1]*v.Y+m[2,2]*v.Z);

    // Matches the OCCT application order: X, then Y, then Z.
    private static double[,] RotationMatrix(Vector3d degrees)
    {
        var x=Rad(degrees.X);var y=Rad(degrees.Y);var z=Rad(degrees.Z);
        var cx=Math.Cos(x);var sx=Math.Sin(x);
        var cy=Math.Cos(y);var sy=Math.Sin(y);
        var cz=Math.Cos(z);var sz=Math.Sin(z);
        var rx=new[,]{{1d,0,0},{0,cx,-sx},{0,sx,cx}};
        var ry=new[,]{{cy,0,sy},{0,1d,0},{-sy,0,cy}};
        var rz=new[,]{{cz,-sz,0},{sz,cz,0},{0,0,1d}};
        return Multiply(rz,Multiply(ry,rx));
    }

    private static Vector3d EulerDegrees(double[,] r)
    {
        var sy=Math.Clamp(-r[2,0],-1.0,1.0);
        var y=Math.Asin(sy);
        double x,z;
        if(Math.Abs(Math.Cos(y))>1e-9)
        {
            x=Math.Atan2(r[2,1],r[2,2]);
            z=Math.Atan2(r[1,0],r[0,0]);
        }
        else
        {
            x=Math.Atan2(-r[1,2],r[1,1]);
            z=0;
        }
        return new Vector3d(Deg(x),Deg(y),Deg(z));
    }

    private static double[,] Align(Vector3d from,Vector3d to)
    {
        var a=from.Normalized();var b=to.Normalized();
        var v=Vector3d.Cross(a,b);var c=Math.Clamp(Vector3d.Dot(a,b),-1.0,1.0);
        var s=v.Length;
        if(s<1e-10)
        {
            if(c>0)return new[,]{{1d,0,0},{0,1d,0},{0,0,1d}};
            var helper=Math.Abs(a.X)<0.8?Vector3d.UnitX:Vector3d.UnitY;
            var axis=Vector3d.Cross(a,helper).Normalized();
            var x=axis.X;var y=axis.Y;var z=axis.Z;
            return new[,]
            {
                {2*x*x-1,2*x*y,2*x*z},
                {2*x*y,2*y*y-1,2*y*z},
                {2*x*z,2*y*z,2*z*z-1}
            };
        }
        var k=new[,]{{0d,-v.Z,v.Y},{v.Z,0d,-v.X},{-v.Y,v.X,0d}};
        var k2=Multiply(k,k);var f=(1-c)/(s*s);
        var r=new double[3,3];
        for(var i=0;i<3;i++)for(var j=0;j<3;j++)
            r[i,j]=(i==j?1d:0d)+k[i,j]+k2[i,j]*f;
        return r;
    }

    private static Vector3d WorldDirection(AssemblyComponent component,Vector3d localDirection)
        => Multiply(RotationMatrix(component.Transform.RotationDegrees),localDirection).Normalized();

    private static Vector3d WorldPoint(AssemblyComponent component,Vector3d localPoint)
        => Multiply(RotationMatrix(component.Transform.RotationDegrees),localPoint)+component.Transform.Translation;

    private static Vector3d LocalDirectionOrZero(AssemblyReference reference)
        => AssemblyReferenceCatalog.TryDirection(reference,out var local)?local:Vector3d.Zero;

    private static Vector3d PerpendicularTarget(Vector3d a,Vector3d current)
    {
        var projected=current-a*Vector3d.Dot(current,a);
        if(projected.Length>1e-9)return projected.Normalized();
        var helper=Math.Abs(a.X)<0.8?Vector3d.UnitX:Vector3d.UnitY;
        return Vector3d.Cross(a,helper).Normalized();
    }

    private static Vector3d RotateAround(Vector3d vector,Vector3d axis,double angleDegrees)
    {
        var a=axis.Normalized();var t=Rad(angleDegrees);
        var c=Math.Cos(t);var s=Math.Sin(t);
        return vector*c+Vector3d.Cross(a,vector)*s+a*(Vector3d.Dot(a,vector)*(1-c));
    }

    public static AssemblyDocument Apply(AssemblyDocument document,AssemblyMate mate)
    {
        var aLocalDirection=LocalDirectionOrZero(mate.A);
        var bLocalDirection=LocalDirectionOrZero(mate.B);
        return ApplyResolved(
            document,mate,
            Vector3d.Zero,aLocalDirection.Length>1e-12?aLocalDirection:null,
            Vector3d.Zero,bLocalDirection.Length>1e-12?bLocalDirection:null);
    }

    public static AssemblyDocument ApplyResolved(
        AssemblyDocument document,
        AssemblyMate mate,
        Vector3d localOriginA,
        Vector3d? localDirectionA,
        Vector3d localOriginB,
        Vector3d? localDirectionB)
    {
        AssemblyDocumentFile.Validate(document);
        if(mate.Suppressed)return document;
        var a=document.Component(mate.A.ComponentId);
        var b=document.Component(mate.B.ComponentId);
        if(b.Fixed)throw new InvalidOperationException("Sabit bileşen montaj ilişkisiyle taşınamaz.");

        static bool Finite(Vector3d value)
            => double.IsFinite(value.X)&&double.IsFinite(value.Y)&&double.IsFinite(value.Z);
        if(!Finite(localOriginA)||!Finite(localOriginB)||
           (localDirectionA is Vector3d da&&(!Finite(da)||da.Length<=1e-12))||
           (localDirectionB is Vector3d db&&(!Finite(db)||db.Length<=1e-12)))
            throw new InvalidOperationException("Montaj referans geometrisi geçersiz.");

        var transform=b.Transform;
        var aHasDirection=localDirectionA is Vector3d;
        var bHasDirection=localDirectionB is Vector3d;

        if(aHasDirection&&bHasDirection)
        {
            var aDir=WorldDirection(a,localDirectionA!.Value);
            var bDir=WorldDirection(b,localDirectionB!.Value);
            Vector3d desired=bDir;
            switch(mate.Type)
            {
                case MateType.Coincident:
                case MateType.Parallel:
                case MateType.Concentric:
                case MateType.Distance:
                    desired=aDir;
                    break;
                case MateType.Perpendicular:
                    desired=PerpendicularTarget(aDir,bDir);
                    break;
                case MateType.Angle:
                {
                    var axis=Vector3d.Cross(aDir,bDir);
                    if(axis.Length<1e-9)
                    {
                        var helper=Math.Abs(aDir.X)<0.8?Vector3d.UnitX:Vector3d.UnitY;
                        axis=Vector3d.Cross(aDir,helper);
                    }
                    desired=RotateAround(aDir,axis,mate.Value);
                    break;
                }
            }
            var delta=Align(bDir,desired);
            transform=transform with
            {
                RotationDegrees=EulerDegrees(Multiply(delta,RotationMatrix(transform.RotationDegrees)))
            };
        }

        var aOrigin=WorldPoint(a,localOriginA);
        var rotatedBOrigin=Multiply(RotationMatrix(transform.RotationDegrees),localOriginB);
        var currentBOrigin=rotatedBOrigin+transform.Translation;
        switch(mate.Type)
        {
            case MateType.Coincident:
                transform=transform with{Translation=aOrigin-rotatedBOrigin};
                break;
            case MateType.Distance:
            {
                var normal=aHasDirection?WorldDirection(a,localDirectionA!.Value):Vector3d.UnitX;
                transform=transform with{Translation=(aOrigin+normal*mate.Value)-rotatedBOrigin};
                break;
            }
            case MateType.Concentric when aHasDirection:
            {
                var axis=WorldDirection(a,localDirectionA!.Value);
                var along=Vector3d.Dot(currentBOrigin-aOrigin,axis);
                var desiredOrigin=aOrigin+axis*along;
                transform=transform with{Translation=desiredOrigin-rotatedBOrigin};
                break;
            }
        }

        var updated=b with{Transform=transform.Validate()};
        return document with{Components=document.Components.Select(c=>c.Id==b.Id?updated:c).ToArray()};
    }}

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
            if(component.ExplodeOffset is Vector3d explode&&
               (!double.IsFinite(explode.X)||!double.IsFinite(explode.Y)||!double.IsFinite(explode.Z)))
                throw new InvalidDataException("Invalid assembly explode offset.");
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
