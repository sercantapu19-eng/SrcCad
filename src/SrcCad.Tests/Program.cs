using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static bool Near(double a, double b, double eps = 1e-6)
    => Math.Abs(a - b) <= eps;

// Plane frame tests.
foreach (var plane in StandardPlanes.All)
{
    plane.Validate();
    var cross = Vector3d.Cross(plane.U.Normalized(), plane.V.Normalized()).Normalized();
    Check((cross - plane.Normal.Normalized()).Length < 1e-9,
        $"{plane.Name}: UxV normal değil.");
}

// Horizontal relation must really move geometry.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var p0 = sketch.CreatePointExact(new Point2d(0, 0));
    var p1 = sketch.CreatePointExact(new Point2d(100, 8));
    var line = sketch.AddLine(p0, p1);
    sketch.AddRelation(SketchRelationType.Horizontal, line.Id);
    sketch.SolveRelations();
    Check(Near(sketch.Point(p0).Position.Y, sketch.Point(p1).Position.Y),
        "Horizontal relation did not drive geometry.");
}

// Driving line dimension must really resize.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var p0 = sketch.CreatePointExact(new Point2d(0, 0));
    var p1 = sketch.CreatePointExact(new Point2d(50, 0));
    var line = sketch.AddLine(p0, p1);
    sketch.AddRelation(SketchRelationType.Horizontal, line.Id);
    var dim = sketch.AddDimension(
        SketchDimensionType.LineLength,
        new Point2d(25, 10),
        entityA: line.Id);
    Check(sketch.SetDimensionValue(dim.Id, 125), "Driving line dimension could not be applied.");
    Check(Near(sketch.Measure(sketch.Dimension(dim.Id)!), 125),
        "Driving line dimension changed label but not geometry.");
}

// Center rectangle must preserve center when width changes.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var center = new Point2d(10, 20);
    var ids = sketch.AddCenterRectangle(center, new Point2d(60, 45));
    var topLineId = ids[2];
    var dim = sketch.AddDimension(
        SketchDimensionType.LineLength,
        new Point2d(10, 60),
        entityA: topLineId);

    Check(sketch.SetDimensionValue(dim.Id, 200), "Center rectangle width dimension failed.");
    var def = sketch.CenterRectangles.Single();
    var p0 = sketch.Point(def.P0).Position;
    var p2 = sketch.Point(def.P2).Position;
    Check(Near((p0.X + p2.X) / 2.0, center.X) &&
          Near((p0.Y + p2.Y) / 2.0, center.Y),
        "Center rectangle lost its center while dimension changed.");
    Check(Near(Math.Abs(p2.X - p0.X), 200),
        "Center rectangle width did not follow driving dimension.");
}

// Circle diameter must drive radius.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var center = sketch.CreatePointExact(new Point2d(0, 0));
    var circle = sketch.AddCircle(center, 10);
    var dim = sketch.AddDimension(
        SketchDimensionType.CircleDiameter,
        new Point2d(20, 20),
        entityA: circle.Id);
    Check(sketch.SetDimensionValue(dim.Id, 80), "Circle diameter dimension failed.");
    Check(Near(((SketchCircle)sketch.Entity(circle.Id)!).Radius, 40),
        "Circle diameter did not drive radius.");
}


// Outer rectangle + inner circle must be a valid extrusion profile with one hole.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    sketch.AddCornerRectangle(new Point2d(-100, -80), new Point2d(100, 80), 0.01);
    var center = sketch.CreatePointExact(new Point2d(0, 0));
    sketch.AddCircle(center, 25);

    Check(sketch.TryGetExtrusionProfile(out var outer, out var holes),
        "Outer loop + circular hole profile was rejected.");
    Check(outer.Count == 4, "Outer rectangle loop should contain 4 vertices.");
    Check(holes.Count == 1 && Near(holes[0].Radius, 25),
        "Circular hole was not recognized.");
}



// Two connected lines: angle dimension must rotate real geometry around the shared vertex.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var pivot = sketch.CreatePointExact(new Point2d(0, 0));
    var p1 = sketch.CreatePointExact(new Point2d(100, 0));
    var p2 = sketch.CreatePointExact(new Point2d(0, 80));
    var first = sketch.AddLine(pivot, p1);
    var second = sketch.AddLine(pivot, p2);
    var dim = sketch.AddDimension(
        SketchDimensionType.Angle,
        new Point2d(30, 30),
        entityA: first.Id,
        entityB: second.Id);

    Check(sketch.SetDimensionValue(dim.Id, 30), "Driving angle dimension failed.");
    Check(Near(sketch.Measure(sketch.Dimension(dim.Id)!), 30),
        "Driving angle changed the label but not the geometry.");
    Check(Near(Point2d.Distance(sketch.Point(pivot).Position, sketch.Point(p2).Position), 80),
        "Angle edit changed the driven line length.");
}

// Equal relation must really make the second line the same length.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var a0 = sketch.CreatePointExact(new Point2d(0, 0));
    var a1 = sketch.CreatePointExact(new Point2d(120, 0));
    var b0 = sketch.CreatePointExact(new Point2d(0, 50));
    var b1 = sketch.CreatePointExact(new Point2d(40, 50));
    var first = sketch.AddLine(a0, a1);
    var second = sketch.AddLine(b0, b1);
    Check(sketch.ApplyRelationToSelection(
        SketchRelationType.Equal,
        new[] { first.Id, second.Id },
        Array.Empty<int>()), "Equal relation could not be applied.");
    Check(Near(
        Point2d.Distance(sketch.Point(second.StartPointId).Position, sketch.Point(second.EndPointId).Position),
        120), "Equal relation did not drive geometry.");
}

Console.WriteLine("SrcCad CAD core smoke tests: PASS");
