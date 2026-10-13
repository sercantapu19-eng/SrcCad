using SrcCad.Core.Drawing;
using SrcCad.Core.Geometry;

internal static class DrawingDocumentTests
{
    private static int _checks;
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);_checks++;}

    public static void Run()
    {
        var sheet=new DrawingSheet(Guid.NewGuid(),"A3",420,297,DrawingProjectionStandard.FirstAngle);
        var front=new DrawingView(Guid.NewGuid(),"Ön","C:\\Models\\Body.scpart",DrawingViewType.Front,sheet.Id,new Point2d(120,150),1.0);
        var top=new DrawingView(Guid.NewGuid(),"Üst","C:\\Models\\Body.scpart",DrawingViewType.Projected,sheet.Id,new Point2d(120,70),1.0,front.Id);
        var note=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,front.Id,new Point2d(20,20),"GENEL: 40 × 30 mm",
            DrawingAnnotationKind.OverallSize,"projection");
        var doc=new DrawingDocument(Guid.NewGuid(),new[]{sheet},new[]{front,top},new[]{note});
        var clone=DrawingDocumentFile.Clone(doc);

        Check(clone.Sheets.Length==1&&clone.Sheets[0].Width==420,"drawing sheet roundtrip");
        Check(clone.Views.Length==2&&clone.View(top.Id).ParentViewId==front.Id,"projected drawing view roundtrip");
        Check(clone.Annotations.Single().Text=="GENEL: 40 × 30 mm","drawing annotation roundtrip");
        Check(clone.Annotations.Single().Kind==DrawingAnnotationKind.OverallSize&&clone.Annotations.Single().Data=="projection",
            "associative annotation kind/data roundtrip");

        var linear=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,front.Id,new Point2d(50,50),"40 mm",
            DrawingAnnotationKind.LinearDimension,"L;0;0;40;0");
        var diameter=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,front.Id,new Point2d(80,80),"Ø10",
            DrawingAnnotationKind.DiameterDimension,"D;20;15;5");
        var dimensions=DrawingDocumentFile.Clone(doc with{Annotations=new[]{linear,diameter}});
        Check(dimensions.Annotations.Any(a=>a.Kind==DrawingAnnotationKind.LinearDimension&&a.Data=="L;0;0;40;0"),
            "associative projected-edge dimension roundtrip");
        Check(dimensions.Annotations.Any(a=>a.Kind==DrawingAnnotationKind.DiameterDimension&&a.Data=="D;20;15;5"),
            "associative diameter dimension roundtrip");

        var removedView=clone.RemoveView(front.Id,true);
        Check(removedView.Views.Length==0&&removedView.Annotations.Length==0,
            "removing parent view cascades projected views and attached annotations");

        var removedAnnotation=clone.RemoveAnnotation(note.Id);
        Check(removedAnnotation.Annotations.Length==0&&removedAnnotation.Views.Length==2,
            "annotation removal preserves drawing views");

        try
        {
            DrawingDocumentFile.Serialize(doc with{Views=new[]{top with{ParentViewId=Guid.NewGuid()}}});
            throw new InvalidOperationException("drawing accepted missing parent view");
        }
        catch(InvalidDataException){_checks++;}

        Console.WriteLine($"Drawing Document Tests: {_checks} PASS");
    }
}
