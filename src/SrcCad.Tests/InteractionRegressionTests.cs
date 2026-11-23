using SrcCad.Core.Geometry;
using SrcCad.Core.History;
using SrcCad.Core.Sketch;

internal static class InteractionRegressionTests
{
    public static void Run()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Interaction: " + message); count++; }
        var wheel = new ExtrusionDepthWheel();
        Check(wheel.Apply(20, 120) == 30, "wheel +10");
        Check(wheel.Apply(20, -120) == 10, "wheel -10");
        Check(wheel.Apply(-20, -240) == -40, "negative depth/multiple detents");
        Check(wheel.Apply(20, 60) == 20 && wheel.Apply(20, 60) == 30, "high resolution accumulation");
        Check(wheel.Apply(10, -120) == 0 && wheel.Apply(0, -120) == -10, "zero must invalidate, not clamp or hide reversal");
        foreach (var plane in new[] { StandardPlanes.Front, StandardPlanes.Top, StandardPlanes.Right })
        foreach (var reverse in new[] { false, true })
        {
            var s = new SketchModel(plane);
            var first = s.CreatePointExact(new(20, 30));
            var second = s.CreatePointExact(new(60, 80));
            var c1 = s.AddCircle(first, 5); var c2 = s.AddCircle(second, 8);
            int a = reverse ? second : first, b = reverse ? first : second;
            var ht = SmartDimensionPlacement.BetweenPoints(s.Point(a).Position, s.Point(b).Position, new(40, 110));
            var vt = SmartDimensionPlacement.BetweenPoints(s.Point(a).Position, s.Point(b).Position, new(110, 55));
            Check(ht == SketchDimensionType.HorizontalDistance && vt == SketchDimensionType.VerticalDistance, "placement chooses axes");
            var h = s.AddDimension(ht, new(40,110), pointA:a, pointB:b);
            var v = s.AddDimension(vt, new(110,55), pointA:a, pointB:b);
            Check(s.SetDimensionValue(h.Id, 100) && s.SetDimensionValue(v.Id, 120), "two center driving dimensions");
            Check(Math.Abs(s.Point(a).Position.X-s.Point(b).Position.X)==100 && Math.Abs(s.Point(a).Position.Y-s.Point(b).Position.Y)==120 && s.Circles().All(c=>c.Radius==(c.Id==c1.Id?5:8)), "centers move and radii survive");
            var origin = new SketchModel(plane);
            origin.AddCornerRectangle(new(20,30), new(60,80), 0.001);
            var corner = origin.FindPointNear(new(20,30),0.001);
            Check(origin.FindPointNear(new(0,0),0.001)==0, "origin selectable");
            a=reverse?corner:0; b=reverse?0:corner;
            h=origin.AddDimension(SketchDimensionType.HorizontalDistance,new(10,-20),pointA:a,pointB:b);
            v=origin.AddDimension(SketchDimensionType.VerticalDistance,new(-20,15),pointA:a,pointB:b);
            Check(origin.SetDimensionValue(h.Id, 40) && origin.SetDimensionValue(v.Id, 50), "origin to rectangle driving dimensions");
            Check(origin.Point(0).Position==new Point2d(0,0) && origin.Point(0).Fixed && origin.Point(corner).Position==new Point2d(40,50), "fixed origin preserved and rectangle corner driven");
            Check(origin.TryGetExtrusionProfile(out _,out _,out _), "driven rectangle remains closed extrudable profile");
        }
        // More than the old six solver passes; reversed relation order must not matter.
        var chain = new SketchModel(StandardPlanes.Front);
        var points = Enumerable.Range(0, 25).Select(i => chain.CreatePointExact(new(10, i * 10))).ToArray();
        for (int i = points.Length - 2; i >= 0; i--)
        {
            var line = chain.AddLine(points[i], points[i+1]);
            chain.AddRelation(SketchRelationType.Vertical, entityA:line.Id);
        }
        var drive = chain.AddDimension(SketchDimensionType.HorizontalDistance, new(0,-20), pointA:0, pointB:points[0]);
        Check(chain.SetDimensionValue(drive.Id, 50) && points.All(id=>chain.Point(id).Position.X==50), "24-relation axis chain");
        chain.SetPointFixed(points[^1], true);
        var snapshot = chain.CaptureSnapshot();
        Check(!chain.SetDimensionValue(drive.Id, 60) && points.All(id=>chain.Point(id).Position==snapshot.Points[id].Position), "fixed remote endpoint rejects atomically");
        Check(SmartDimensionPlacement.BetweenPoints(new(20,30),new(60,80),new(40,55))==SketchDimensionType.PointDistance, "aligned placement retained");

        // Direct SolidWorks-style grab/drag: body drag translates unconstrained
        // sketch entities while preserving relations and rolls back if fixed.
        var drag = new SketchModel(StandardPlanes.Front);
        var la=drag.CreatePointExact(new(10,10));var lb=drag.CreatePointExact(new(40,10));
        var dragLine=drag.AddLine(la,lb);drag.AddRelation(SketchRelationType.Horizontal,entityA:dragLine.Id);
        Check(drag.TryTranslateEntity(dragLine.Id,new Point2d(15,20)),"line body drag accepted");
        Check(drag.Point(la).Position==new Point2d(25,30)&&drag.Point(lb).Position==new Point2d(55,30),
            "line body drag translates both endpoints and keeps horizontal relation");
        drag.SetPointFixed(la,true);
        var dragBefore=drag.CaptureSnapshot();
        Check(!drag.TryTranslateEntity(dragLine.Id,new Point2d(5,5))&&
              drag.Point(la).Position==dragBefore.Points[la].Position&&drag.Point(lb).Position==dragBefore.Points[lb].Position,
            "fixed endpoint rejects whole line drag atomically");

        var circleDrag=new SketchModel(StandardPlanes.Front);
        var center=circleDrag.CreatePointExact(new(20,20));var circle=circleDrag.AddCircle(center,8);
        Check(circleDrag.TryTranslateEntity(circle.Id,new Point2d(-5,12))&&circleDrag.Point(center).Position==new Point2d(15,32),
            "circle body drag translates center without changing radius");
        Check(Math.Abs(((SketchCircle)circleDrag.Entity(circle.Id)!).Radius-8)<1e-12,"circle drag preserves radius");

        var curveDrag=new SketchModel(StandardPlanes.Front);
        var ac=curveDrag.CreatePointExact(new(0,0));var ast=curveDrag.CreatePointExact(new(10,0));var aen=curveDrag.CreatePointExact(new(0,10));
        var arc=curveDrag.AddArc(ac,ast,aen);
        Check(curveDrag.TryTranslateEntity(arc.Id,new Point2d(7,-3))&&
              curveDrag.Point(ac).Position==new Point2d(7,-3)&&curveDrag.Point(ast).Position==new Point2d(17,-3)&&curveDrag.Point(aen).Position==new Point2d(7,7),
            "arc body drag translates center and endpoints");
        var ec=curveDrag.CreatePointExact(new(30,20));var ellipse=curveDrag.AddEllipse(ec,12,6,0.4);
        Check(curveDrag.TryTranslateEntity(ellipse.Id,new Point2d(4,9))&&curveDrag.Point(ec).Position==new Point2d(34,29),
            "ellipse body drag translates center");

        Console.WriteLine($"INTERACTION REGRESSION: {count} PASS (geometry; Windows event routing pending)");
    }
}
