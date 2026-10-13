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
        var radius=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,front.Id,new Point2d(90,85),"R5",
            DrawingAnnotationKind.RadiusDimension,"R;20;15;5");
        var angular=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,front.Id,new Point2d(100,95),"45°",
            DrawingAnnotationKind.AngularDimension,"A;0;0;40;0;0;0;20;20");
        var centerMark=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,front.Id,new Point2d(20,15),"MERKEZ",
            DrawingAnnotationKind.CenterMark,"R;20;15;5");
        var holeCallout=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,front.Id,new Point2d(110,105),"Ø10",
            DrawingAnnotationKind.HoleCallout,"D;20;15;5");
        var dimensions=DrawingDocumentFile.Clone(doc with{Annotations=new[]{linear,diameter,radius,angular,centerMark,holeCallout}});
        Check(dimensions.Annotations.Any(a=>a.Kind==DrawingAnnotationKind.LinearDimension&&a.Data=="L;0;0;40;0"),
            "associative projected-edge dimension roundtrip");
        Check(dimensions.Annotations.Any(a=>a.Kind==DrawingAnnotationKind.DiameterDimension&&a.Data=="D;20;15;5"),
            "associative diameter dimension roundtrip");
        Check(dimensions.Annotations.Any(a=>a.Kind==DrawingAnnotationKind.RadiusDimension&&a.Data=="R;20;15;5"),
            "associative radius dimension roundtrip");
        Check(dimensions.Annotations.Any(a=>a.Kind==DrawingAnnotationKind.AngularDimension&&a.Data=="A;0;0;40;0;0;0;20;20"),
            "associative angular dimension roundtrip");
        Check(dimensions.Annotations.Any(a=>a.Kind==DrawingAnnotationKind.CenterMark&&a.Data=="R;20;15;5"),
            "associative center mark roundtrip");
        Check(dimensions.Annotations.Any(a=>a.Kind==DrawingAnnotationKind.HoleCallout&&a.Data=="D;20;15;5"),
            "persistent hole callout circle signature roundtrip");
        Check(DrawingDocumentFile.Clone(doc with{Annotations=new[]{holeCallout with{Data="0"}}}).Annotations.Single().Data=="0",
            "legacy index-based hole callout remains readable");

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

        var secondSheet=new DrawingSheet(Guid.NewGuid(),"A4",297,210,DrawingProjectionStandard.FirstAngle);
        try
        {
            DrawingDocumentFile.Serialize(new DrawingDocument(
                Guid.NewGuid(),
                new[]{sheet,secondSheet},
                new[]
                {
                    front,
                    top with{SheetId=secondSheet.Id,ParentViewId=front.Id}
                },
                Array.Empty<DrawingAnnotation>()));
            throw new InvalidOperationException("drawing accepted cross-sheet parent view");
        }
        catch(InvalidDataException){_checks++;}

        try
        {
            DrawingDocumentFile.Serialize(new DrawingDocument(
                Guid.NewGuid(),
                new[]{sheet,secondSheet},
                new[]{front},
                new[]
                {
                    note with{SheetId=secondSheet.Id,ViewId=front.Id}
                }));
            throw new InvalidOperationException("drawing accepted cross-sheet annotation reference");
        }
        catch(InvalidDataException){_checks++;}

        var cycleA=new DrawingView(Guid.NewGuid(),"A","C:\\Models\\Body.scpart",DrawingViewType.Projected,sheet.Id,new Point2d(10,10),1.0);
        var cycleB=new DrawingView(Guid.NewGuid(),"B","C:\\Models\\Body.scpart",DrawingViewType.Projected,sheet.Id,new Point2d(20,20),1.0,cycleA.Id);
        cycleA=cycleA with{ParentViewId=cycleB.Id};
        try
        {
            DrawingDocumentFile.Serialize(new DrawingDocument(
                Guid.NewGuid(),
                new[]{sheet},
                new[]{cycleA,cycleB},
                Array.Empty<DrawingAnnotation>()));
            throw new InvalidOperationException("drawing accepted cyclic view dependency");
        }
        catch(InvalidDataException){_checks++;}

        try
        {
            var brokenLinear=linear with{Data="L;0;0;40"};
            DrawingDocumentFile.Serialize(doc with{Annotations=new[]{brokenLinear}});
            throw new InvalidOperationException("drawing accepted malformed linear dimension payload");
        }
        catch(InvalidDataException){_checks++;}

        try
        {
            var detachedRadius=radius with{ViewId=null};
            DrawingDocumentFile.Serialize(doc with{Annotations=new[]{detachedRadius}});
            throw new InvalidOperationException("drawing accepted detached associative radius dimension");
        }
        catch(InvalidDataException){_checks++;}

        try
        {
            var zeroRadius=centerMark with{Data="R;20;15;0"};
            DrawingDocumentFile.Serialize(doc with{Annotations=new[]{zeroRadius}});
            throw new InvalidOperationException("drawing accepted zero-radius center mark payload");
        }
        catch(InvalidDataException){_checks++;}

        try
        {
            var brokenHole=holeCallout with{Data="D;20;15;0"};
            DrawingDocumentFile.Serialize(doc with{Annotations=new[]{brokenHole}});
            throw new InvalidOperationException("drawing accepted invalid hole callout circle signature");
        }
        catch(InvalidDataException){_checks++;}

        Console.WriteLine($"Drawing Document Tests: {_checks} PASS");
    }
}
