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

        var perpendicular=new AssemblyMate(Guid.NewGuid(),"Dik1",MateType.Perpendicular,
            new AssemblyReference(a.Id,TopologyKind.Face,AssemblyReferenceCatalog.FrontPlane),
            new AssemblyReference(movable.Id,TopologyKind.Face,AssemblyReferenceCatalog.FrontPlane));
        var perpendicularSolved=AssemblyMateSolver.Apply(solveDoc,perpendicular);
        var perpendicularRotation=perpendicularSolved.Component(movable.Id).Transform.RotationDegrees;
        Check(Math.Abs(Math.Abs(perpendicularRotation.X)-90)<1e-6||Math.Abs(Math.Abs(perpendicularRotation.Z)-90)<1e-6,
            "perpendicular mate rotates reference directions by ninety degrees");

        var angleMate=new AssemblyMate(Guid.NewGuid(),"Açı1",MateType.Angle,
            new AssemblyReference(a.Id,TopologyKind.Face,AssemblyReferenceCatalog.FrontPlane),
            new AssemblyReference(movable.Id,TopologyKind.Face,AssemblyReferenceCatalog.FrontPlane),30);
        var angleSolved=AssemblyMateSolver.Apply(solveDoc,angleMate);
        var angleRotation=angleSolved.Component(movable.Id).Transform.RotationDegrees;
        Check(Math.Abs(angleRotation.X)+Math.Abs(angleRotation.Y)+Math.Abs(angleRotation.Z)>1e-6,
            "angle mate produces a non-zero controlled rotation");

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

        var pointPlaneComponent=movable with{Transform=new RigidTransform(new Vector3d(25,40,30),Vector3d.Zero)};
        var pointPlaneDoc=new AssemblyDocument(Guid.NewGuid(),new[]{a,pointPlaneComponent},Array.Empty<AssemblyMate>());
        var pointToPlane=new AssemblyMate(Guid.NewGuid(),"NoktaDüzlem",MateType.Coincident,
            new AssemblyReference(a.Id,TopologyKind.Face,AssemblyReferenceCatalog.TopPlane),
            new AssemblyReference(pointPlaneComponent.Id,TopologyKind.Vertex,AssemblyReferenceCatalog.Origin));
        var pointPlaneSolved=AssemblyMateSolver.ApplyResolved(
            pointPlaneDoc,pointToPlane,
            Vector3d.Zero,Vector3d.UnitZ,
            Vector3d.Zero,null);
        var pointPlaneTranslation=pointPlaneSolved.Component(pointPlaneComponent.Id).Transform.Translation;
        Check(Math.Abs(pointPlaneTranslation.X-25)<1e-8&&Math.Abs(pointPlaneTranslation.Y-40)<1e-8&&Math.Abs(pointPlaneTranslation.Z)<1e-8,
            "point-plane coincident preserves in-plane translation and removes normal offset");

        var pointPlaneDistance=pointToPlane with{Id=Guid.NewGuid(),Name="NoktaDüzlemMesafe",Type=MateType.Distance,Value=7};
        var pointPlaneDistanceSolved=AssemblyMateSolver.ApplyResolved(
            pointPlaneDoc,pointPlaneDistance,
            Vector3d.Zero,Vector3d.UnitZ,
            Vector3d.Zero,null);
        var pointPlaneDistanceTranslation=pointPlaneDistanceSolved.Component(pointPlaneComponent.Id).Transform.Translation;
        Check(Math.Abs(pointPlaneDistanceTranslation.X-25)<1e-8&&Math.Abs(pointPlaneDistanceTranslation.Y-40)<1e-8&&Math.Abs(pointPlaneDistanceTranslation.Z-7)<1e-8,
            "point-plane distance preserves in-plane translation and sets signed normal offset");

        var tangentComponent=movable with{Transform=new RigidTransform(new Vector3d(0,0,20),Vector3d.Zero)};
        var tangentDoc=new AssemblyDocument(Guid.NewGuid(),new[]{a,tangentComponent},Array.Empty<AssemblyMate>());
        var tangent=new AssemblyMate(Guid.NewGuid(),"Teğet1",MateType.Tangent,
            new AssemblyReference(a.Id,TopologyKind.Face,"F;plane"),
            new AssemblyReference(tangentComponent.Id,TopologyKind.Face,"C;cylinder"));
        var tangentSolved=AssemblyMateSolver.ApplyResolved(
            tangentDoc,tangent,
            Vector3d.Zero,Vector3d.UnitZ,
            Vector3d.Zero,Vector3d.UnitX,
            false,0,true,5);
        Check(Math.Abs(tangentSolved.Component(tangentComponent.Id).Transform.Translation.Z-5)<1e-8,
            "plane-cylinder tangent mate offsets cylinder axis by its radius");

        var cylinderComponent=movable with{Transform=new RigidTransform(new Vector3d(0,10,0),Vector3d.Zero)};
        var cylinderDoc=new AssemblyDocument(Guid.NewGuid(),new[]{a,cylinderComponent},Array.Empty<AssemblyMate>());
        var cylinderTangent=new AssemblyMate(Guid.NewGuid(),"Teğet2",MateType.Tangent,
            new AssemblyReference(a.Id,TopologyKind.Face,"C;fixed"),
            new AssemblyReference(cylinderComponent.Id,TopologyKind.Face,"C;moving"));
        var cylinderSolved=AssemblyMateSolver.ApplyResolved(
            cylinderDoc,cylinderTangent,
            Vector3d.Zero,Vector3d.UnitX,
            Vector3d.Zero,Vector3d.UnitX,
            true,5,true,3);
        Check(Math.Abs(cylinderSolved.Component(cylinderComponent.Id).Transform.Translation.Y-8)<1e-8,
            "cylinder-cylinder tangent mate sets external center distance to radius sum");


        try
        {
            AssemblyDocumentFile.Serialize(doc with{Components=new[]{a,a}});
            throw new InvalidOperationException("duplicate assembly component accepted");
        }
        catch(InvalidDataException){_checks++;}

        var validPlanarSemantic="F;100;0;0;0;0;0;1";
        var validCylinderSemantic="C;100;0;0;0;1;0;0;12.5";
        Check(AssemblyReferenceCatalog.IsWellFormed(new AssemblyReference(a.Id,TopologyKind.Face,validPlanarSemantic)),
            "valid planar persistent face reference rejected");
        Check(AssemblyReferenceCatalog.IsWellFormed(new AssemblyReference(a.Id,TopologyKind.Face,validCylinderSemantic)),
            "valid cylindrical persistent face reference rejected");
        Check(!AssemblyReferenceCatalog.IsWellFormed(new AssemblyReference(a.Id,TopologyKind.Edge,validPlanarSemantic)),
            "face semantic accepted with edge topology kind");
        Check(!AssemblyReferenceCatalog.IsWellFormed(new AssemblyReference(a.Id,TopologyKind.Face,"C;100;0;0;0;1;0;0;-2")),
            "negative cylinder radius accepted");
        Check(!AssemblyReferenceCatalog.IsWellFormed(new AssemblyReference(a.Id,TopologyKind.Face,"F;100;0;0;0;0;0")),
            "truncated planar face semantic accepted");

        try
        {
            var brokenMate=mate with
            {
                Id=Guid.NewGuid(),
                Name="KırıkRef",
                A=new AssemblyReference(a.Id,TopologyKind.Face,"F;100;0;0;0;0;0")
            };
            AssemblyDocumentFile.Serialize(doc with{Mates=new[]{brokenMate}});
            throw new InvalidOperationException("assembly accepted broken persistent mate reference");
        }
        catch(InvalidDataException){_checks++;}

        var planarA=new AssemblyReference(a.Id,TopologyKind.Face,validPlanarSemantic);
        var planarB=new AssemblyReference(b.Id,TopologyKind.Face,"F;90;0;0;5;0;1;0");
        var cylinderB=new AssemblyReference(b.Id,TopologyKind.Face,validCylinderSemantic);
        Check(AssemblyReferenceCatalog.IsMateCompatible(new AssemblyMate(Guid.NewGuid(),"TeğetUyumlu",MateType.Tangent,planarA,cylinderB)),
            "plane-cylinder tangent compatibility rejected");
        Check(!AssemblyReferenceCatalog.IsMateCompatible(new AssemblyMate(Guid.NewGuid(),"TeğetHatalı",MateType.Tangent,planarA,planarB)),
            "plane-plane tangent compatibility accepted");
        Check(!AssemblyReferenceCatalog.IsMateCompatible(new AssemblyMate(Guid.NewGuid(),"AçıHatalı",MateType.Angle,
            new AssemblyReference(a.Id,TopologyKind.Vertex,AssemblyReferenceCatalog.Origin),planarB,30)),
            "angle mate accepted origin without direction");

        try
        {
            var invalidTangent=new AssemblyMate(Guid.NewGuid(),"GeçersizTeğet",MateType.Tangent,planarA,planarB);
            AssemblyDocumentFile.Serialize(new AssemblyDocument(Guid.NewGuid(),new[]{a,b},new[]{invalidTangent}));
            throw new InvalidOperationException("assembly accepted incompatible tangent mate");
        }
        catch(InvalidDataException){_checks++;}

        var assemblyPath=Path.Combine(Path.GetTempPath(),"SrcCadTests","Assembly","Main.scasm");
        var relativeResolved=AssemblyDocumentFile.ResolveSourcePath(assemblyPath,Path.Combine("Parts","Body.scpart"));
        Check(relativeResolved==Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assemblyPath)!,"Parts","Body.scpart")),
            "relative component source resolves against assembly directory");
        var absoluteSource=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"SrcCadTests","Absolute.scpart"));
        Check(AssemblyDocumentFile.ResolveSourcePath(assemblyPath,absoluteSource)==absoluteSource,
            "absolute component source path remains absolute");

        Console.WriteLine($"Assembly Document Tests: {_checks} PASS");
    }
}
