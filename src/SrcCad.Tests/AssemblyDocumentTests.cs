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

        var removed=clone.RemoveComponent(b.Id);
        Check(removed.Components.Length==1&&removed.Mates.Length==0,"component removal cascades mates");

        try
        {
            AssemblyDocumentFile.Serialize(doc with{Components=new[]{a,a}});
            throw new InvalidOperationException("duplicate assembly component accepted");
        }
        catch(InvalidDataException){_checks++;}

        Console.WriteLine($"Assembly Document Tests: {_checks} PASS");
    }
}
