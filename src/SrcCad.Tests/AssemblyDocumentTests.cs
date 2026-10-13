using SrcCad.Core.Assembly;
using SrcCad.Core.Features;
using SrcCad.Core.Geometry;

internal static class AssemblyDocumentTests
{
    private static int _checks;
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);_checks++;}

    public static void Run()
    {
        var a=new AssemblyComponent(Guid.NewGuid(),"Gövde","C:\\Models\\Body.scpart",RigidTransform.Identity,Fixed:true);
        var b=new AssemblyComponent(Guid.NewGuid(),"Kapak","C:\\Models\\Cover.scpart",
            new RigidTransform(new Vector3d(100,20,30),new Vector3d(0,0,90)));
        var mate=new AssemblyMate(Guid.NewGuid(),"Eşmerkez1",MateType.Concentric,
            new AssemblyReference(a.Id,TopologyKind.Face,"F;100;0;0;0;0;0;1"),
            new AssemblyReference(b.Id,TopologyKind.Face,"F;100;0;0;0;0;0;1"));
        var doc=new AssemblyDocument(Guid.NewGuid(),new[]{a,b},new[]{mate});
        var clone=AssemblyDocumentFile.Clone(doc);

        Check(clone.Components.Length==2,"assembly components roundtrip");
        Check(clone.Mates.Length==1&&clone.Mates[0].Type==MateType.Concentric,"assembly mate roundtrip");
        Check(clone.Component(b.Id).Transform.Translation==new Vector3d(100,20,30),"assembly transform roundtrip");
        var exploded=b with{ExplodeOffset=new Vector3d(40,0,0)};
        var explodedClone=AssemblyDocumentFile.Clone(new AssemblyDocument(Guid.NewGuid(),new[]{a,exploded},Array.Empty<AssemblyMate>()));
        Check(explodedClone.Component(exploded.Id).ExplodeOffset==new Vector3d(40,0,0),
            "assembly exploded-view offset roundtrip");

        var removed=clone.RemoveComponent(b.Id);
        Check(removed.Components.Length==1&&removed.Mates.Length==0,"component removal cascades mates");

        var movable=b with{Transform=new RigidTransform(new Vector3d(10,20,30),Vector3d.Zero)};
        var solveDoc=new AssemblyDocument(Guid.NewGuid(),new[]{a,movable},Array.Empty<AssemblyMate>());
        var coincident=new AssemblyMate(Guid.NewGuid(),"Çakışık1",MateType.Coincident,
            new AssemblyReference(a.Id,TopologyKind.Face,AssemblyReferenceCatalog.TopPlane),
            new AssemblyReference(movable.Id,TopologyKind.Face,AssemblyReferenceCatalog.TopPlane));
        var coincidentSolved=AssemblyMateSolver.Apply(solveDoc,coincident);
        Check(coincidentSolved.Component(movable.Id).Transform.Translation==Vector3d.Zero,
            "standard plane coincident mate aligns component origins");

        var distance=new AssemblyMate(Guid.NewGuid(),"Mesafe1",MateType.Distance,
            new AssemblyReference(a.Id,TopologyKind.Face,AssemblyReferenceCatalog.RightPlane),
            new AssemblyReference(movable.Id,TopologyKind.Face,AssemblyReferenceCatalog.RightPlane),25);
        var distanceSolved=AssemblyMateSolver.Apply(solveDoc,distance);
        Check((distanceSolved.Component(movable.Id).Transform.Translation-new Vector3d(25,0,0)).Length<1e-8,
            "standard plane distance mate offsets along reference normal");

        var parallel=new AssemblyMate(Guid.NewGuid(),"Paralel1",MateType.Parallel,
            new AssemblyReference(a.Id,TopologyKind.Face,AssemblyReferenceCatalog.FrontPlane),
            new AssemblyReference(movable.Id,TopologyKind.Face,AssemblyReferenceCatalog.TopPlane));
        var parallelSolved=AssemblyMateSolver.Apply(solveDoc,parallel);
        Check(Math.Abs(Math.Abs(parallelSolved.Component(movable.Id).Transform.RotationDegrees.X)-90)<1e-6,
            "parallel mate rotates different standard planes into alignment");

        var concentric=new AssemblyMate(Guid.NewGuid(),"Eşmerkez2",MateType.Concentric,
            new AssemblyReference(a.Id,TopologyKind.Edge,AssemblyReferenceCatalog.XAxis),
            new AssemblyReference(movable.Id,TopologyKind.Edge,AssemblyReferenceCatalog.XAxis));
        var concentricSolved=AssemblyMateSolver.Apply(solveDoc,concentric);
        Check((concentricSolved.Component(movable.Id).Transform.Translation-new Vector3d(10,0,0)).Length<1e-8,
            "concentric standard axes remove perpendicular offset");

        var faceA=new AssemblyReference(a.Id,TopologyKind.Face,"F;A");
        var faceB=new AssemblyReference(movable.Id,TopologyKind.Face,"F;B");
        var faceMate=new AssemblyMate(Guid.NewGuid(),"YüzÇakışık",MateType.Coincident,faceA,faceB);
        var faceSolved=AssemblyMateSolver.ApplyResolved(
            solveDoc,faceMate,
            new Vector3d(0,0,5),Vector3d.UnitZ,
            new Vector3d(0,0,-5),Vector3d.UnitZ);
        Check((faceSolved.Component(movable.Id).Transform.Translation-new Vector3d(0,0,10)).Length<1e-8,
            "resolved planar face mate aligns local face origins");

        var faceDistance=faceMate with{Id=Guid.NewGuid(),Name="YüzMesafe",Type=MateType.Distance,Value=2};
        var faceDistanceSolved=AssemblyMateSolver.ApplyResolved(
            solveDoc,faceDistance,
            new Vector3d(0,0,5),Vector3d.UnitZ,
            new Vector3d(0,0,-5),Vector3d.UnitZ);
        Check((faceDistanceSolved.Component(movable.Id).Transform.Translation-new Vector3d(0,0,12)).Length<1e-8,
            "resolved planar face distance mate honors offset");


        try
        {
            AssemblyDocumentFile.Serialize(doc with{Components=new[]{a,a}});
            throw new InvalidOperationException("duplicate assembly component accepted");
        }
        catch(InvalidDataException){_checks++;}

        Console.WriteLine($"Assembly Document Tests: {_checks} PASS");
    }
}
