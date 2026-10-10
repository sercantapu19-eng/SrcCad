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


// Moving a dimension label must never change the driven geometry or its value.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var p0 = sketch.CreatePointExact(new Point2d(0, 0));
    var p1 = sketch.CreatePointExact(new Point2d(75, 0));
    var line = sketch.AddLine(p0, p1);
    var dim = sketch.AddDimension(
        SketchDimensionType.LineLength,
        new Point2d(30, 10),
        entityA: line.Id);
    Check(sketch.SetDimensionValue(dim.Id, 150), "Dimension setup failed.");
    sketch.SetDimensionLabel(dim.Id, new Point2d(400, 250));
    Check(Near(sketch.Measure(sketch.Dimension(dim.Id)!), 150),
        "Moving a dimension label changed the driven geometry.");
    Check(sketch.Dimension(dim.Id)!.LabelPosition == new Point2d(400, 250),
        "Dimension label position was not updated.");
}


// Acceptance case from the real workflow: 250x200 plate + 3xØ15 must be
// interpreted as one outer region and three EMPTY circular holes.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    sketch.AddCornerRectangle(new Point2d(-125, -100), new Point2d(125, 100), 0.01);
    foreach (var centerPos in new[]
    {
        new Point2d(-80, 0),
        new Point2d(0, 0),
        new Point2d(80, 0)
    })
    {
        var centerId = sketch.CreatePointExact(centerPos);
        sketch.AddCircle(centerId, 7.5);
    }

    Check(sketch.TryGetExtrusionProfile(out var outer, out var holes),
        "250x200 + 3xØ15 extrusion profile was rejected.");
    Check(outer.Count == 4, "250x200 plate must have one 4-edge outer loop.");
    Check(holes.Count == 3 && holes.All(h => Near(h.Radius * 2.0, 15)),
        "Three Ø15 circles must be recognized as three empty holes.");
}

// Invalid inner contours must be rejected before OCCT preview.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    sketch.AddCornerRectangle(new Point2d(-50, -50), new Point2d(50, 50), 0.01);
    var c0 = sketch.CreatePointExact(new Point2d(45, 0));
    sketch.AddCircle(c0, 10); // crosses the outer boundary
    Check(!sketch.TryGetExtrusionProfile(out _, out _),
        "A hole crossing the outer boundary was accepted.");
}

{
    var sketch = new SketchModel(StandardPlanes.Front);
    sketch.AddCornerRectangle(new Point2d(-50, -50), new Point2d(50, 50), 0.01);
    var c0 = sketch.CreatePointExact(new Point2d(-5, 0));
    var c1 = sketch.CreatePointExact(new Point2d(5, 0));
    sketch.AddCircle(c0, 10);
    sketch.AddCircle(c1, 10); // overlaps first hole
    Check(!sketch.TryGetExtrusionProfile(out _, out _),
        "Overlapping circular holes were accepted.");
}


// Snapshot restore is the foundation of sketch Undo/Redo. IDs, geometry,
// relations and dimensions must return exactly to the captured state.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    sketch.AddCornerRectangle(new Point2d(0, 0), new Point2d(100, 60), 0.01);
    var snapshot = sketch.CaptureSnapshot();
    var originalEntityCount = sketch.Entities.Count;
    var originalRelationCount = sketch.Relations.Count;

    var center = sketch.CreatePointExact(new Point2d(50, 30));
    sketch.AddCircle(center, 10);
    Check(sketch.Entities.Count == originalEntityCount + 1, "Snapshot mutation setup failed.");

    sketch.RestoreSnapshot(snapshot);
    Check(sketch.Entities.Count == originalEntityCount, "Undo snapshot did not restore entities.");
    Check(sketch.Relations.Count == originalRelationCount, "Undo snapshot did not restore relations.");
    Check(!sketch.Circles(false).Any(), "Undo snapshot left newly-created geometry behind.");

    var nextPoint = sketch.CreatePointExact(new Point2d(10, 10));
    Check(nextPoint == snapshot.NextPointId, "Snapshot restore did not restore deterministic point IDs.");
}


// Collinear must project the second line onto the first line without changing topology.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var a = sketch.CreatePointExact(new Point2d(0, 0));
    var b = sketch.CreatePointExact(new Point2d(100, 0));
    var c = sketch.CreatePointExact(new Point2d(20, 30));
    var d = sketch.CreatePointExact(new Point2d(80, 45));
    var first = sketch.AddLine(a, b);
    var second = sketch.AddLine(c, d);
    Check(sketch.ApplyRelationToSelection(SketchRelationType.Collinear, new[] { first.Id, second.Id }, Array.Empty<int>()), "Collinear relation rejected valid lines.");
    Check(Math.Abs(sketch.Point(c).Position.Y) < 1e-8 && Math.Abs(sketch.Point(d).Position.Y) < 1e-8, "Collinear relation did not project second line.");
}

// Concentric must drive two circle centers together while preserving both radii.
{
    var sketch = new SketchModel(StandardPlanes.Front);
    var c1 = sketch.CreatePointExact(new Point2d(10, 20));
    var c2 = sketch.CreatePointExact(new Point2d(80, 90));
    var a = sketch.AddCircle(c1, 15);
    var b = sketch.AddCircle(c2, 30);
    Check(sketch.ApplyRelationToSelection(SketchRelationType.Concentric, new[] { a.Id, b.Id }, Array.Empty<int>()), "Concentric relation rejected valid circles.");
    Check(Point2d.Distance(sketch.Point(c1).Position, sketch.Point(c2).Position) < 1e-8, "Concentric relation did not merge circle centers.");
    Check(Math.Abs(((SketchCircle)sketch.Entity(a.Id)!).Radius - 15) < 1e-8 && Math.Abs(((SketchCircle)sketch.Entity(b.Id)!).Radius - 30) < 1e-8, "Concentric relation changed circle radii.");
}

Console.WriteLine("SrcCad CAD core smoke tests: PASS");


AcceptanceTests.Run();

#if NATIVE_ACCEPTANCE
NativeAcceptanceTests.Run();
#endif
