using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

public sealed record SketchPoint(int Id, Point2d Position, bool Fixed = false);

[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SketchLine), "line")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SketchCircle), "circle")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SketchArc), "arc")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SketchEllipse), "ellipse")]
public abstract record SketchEntity(int Id, bool Construction);
public sealed record SketchLine(int Id, int StartPointId, int EndPointId, bool Construction = false)
    : SketchEntity(Id, Construction);
public sealed record SketchCircle(int Id, int CenterPointId, double Radius, bool Construction = false)
    : SketchEntity(Id, Construction);
public sealed record SketchArc(int Id, int CenterPointId, int StartPointId, int EndPointId, bool Clockwise = false, bool Construction = false)
    : SketchEntity(Id, Construction);
public sealed record SketchEllipse(int Id,int CenterPointId,double RadiusX,double RadiusY,double RotationRadians=0,bool Construction=false)
    : SketchEntity(Id,Construction);

public enum SketchRelationType
{
    Horizontal,
    Vertical,
    Coincident,
    Parallel,
    Perpendicular,
    Equal,
    Collinear,
    Concentric,
    Fixed,
    Midpoint,
    Tangent,
    Symmetric
}

public sealed record SketchRelation(
    int Id,
    SketchRelationType Type,
    int EntityA = -1,
    int EntityB = -1,
    int PointA = -1,
    int PointB = -1,
    GeometryReference? ReferenceA = null,
    GeometryReference? ReferenceB = null,
    double Sense = 1,
    bool InternalTangency = false,
    GeometryReference? ReferenceC = null);

public enum SketchDimensionType
{
    LineLength,
    CircleDiameter,
    ArcRadius,
    EllipseAxis1Diameter,
    EllipseAxis2Diameter,
    PointDistance,
    HorizontalDistance,
    VerticalDistance,
    Angle,
    PointLineDistance,
    ParallelLineDistance
}

public sealed record SketchDimension(
    int Id,
    SketchDimensionType Type,
    double Value,
    Point2d LabelPosition,
    int EntityA = -1,
    int EntityB = -1,
    int PointA = -1,
    int PointB = -1,
    bool Driving = true,
    GeometryReference? ReferenceA = null,
    GeometryReference? ReferenceB = null,
    double Sense = 1);

public sealed record CenterRectangleDefinition(
    int CenterPointId,
    int P0, int P1, int P2, int P3,
    int BottomLineId, int RightLineId, int TopLineId, int LeftLineId);

public sealed record SketchDefinitionInfo(
    int DegreesOfFreedom,
    bool IsFullyDefined,
    bool IsConflicting,
    string Text,
    int RedundantEquationCount = 0);

public sealed record SketchExtrusionRegion(
    IReadOnlyList<Point2d>? OuterPolygon,
    SketchCircle? OuterCircle,
    IReadOnlyList<IReadOnlyList<Point2d>> PolygonHoles,
    IReadOnlyList<SketchCircle> CircularHoles);

public sealed record SketchSnapshot(
    int NextPointId,
    int NextEntityId,
    int NextRelationId,
    int NextDimensionId,
    IReadOnlyDictionary<int, SketchPoint> Points,
    IReadOnlyList<SketchEntity> Entities,
    IReadOnlyList<SketchRelation> Relations,
    IReadOnlyList<SketchDimension> Dimensions,
    IReadOnlyList<CenterRectangleDefinition> CenterRectangles);


public sealed partial class SketchModel
{
    private int _nextPointId = 1;
    private int _nextEntityId = 1;
    private int _nextRelationId = 1;
    private int _nextDimensionId = 1;

    public long Revision { get; private set; }
    public PlaneFrame Plane { get; }
    public Dictionary<int, SketchPoint> Points { get; } = new();
    public List<SketchEntity> Entities { get; } = new();
    public List<SketchRelation> Relations { get; } = new();
    public List<SketchDimension> Dimensions { get; } = new();
    public List<CenterRectangleDefinition> CenterRectangles { get; } = new();

    public SketchModel(PlaneFrame plane)
    {
        Plane = plane.Validate();
        Points[0] = new SketchPoint(0, new Point2d(0, 0), true); // Origin is fixed.
    }

    public SketchPoint Point(int id) => Points[id];
    public SketchEntity? Entity(int id) => Entities.FirstOrDefault(x => x.Id == id);
    public SketchDimension? Dimension(int id) => Dimensions.FirstOrDefault(x => x.Id == id);

    public SketchSnapshot CaptureSnapshot()
        => new(
            _nextPointId,
            _nextEntityId,
            _nextRelationId,
            _nextDimensionId,
            new Dictionary<int, SketchPoint>(Points),
            Entities.ToList(),
            Relations.ToList(),
            Dimensions.ToList(),
            CenterRectangles.ToList());

    public void RestoreSnapshot(SketchSnapshot snapshot, bool notifyChange = false)
    {
        if(notifyChange)Revision++;
        _nextPointId = snapshot.NextPointId;
        _nextEntityId = snapshot.NextEntityId;
        _nextRelationId = snapshot.NextRelationId;
        _nextDimensionId = snapshot.NextDimensionId;

        Points.Clear();
        foreach (var pair in snapshot.Points)
            Points[pair.Key] = pair.Value;

        Entities.Clear();
        Entities.AddRange(snapshot.Entities);
        Relations.Clear();
        Relations.AddRange(snapshot.Relations);
        Dimensions.Clear();
        Dimensions.AddRange(snapshot.Dimensions);
        CenterRectangles.Clear();
        CenterRectangles.AddRange(snapshot.CenterRectangles);
    }

    public void SetPointPosition(int id, Point2d position, bool ignoreFixed = false)
    {
        ValidatePoint(position);
        if (!Points.TryGetValue(id, out var point)) return;
        if (point.Fixed && !ignoreFixed) return;
        Points[id] = point with { Position = position };
    }

    public bool TryMovePoint(int id, Point2d position)
    {
        if (!Points.TryGetValue(id, out var point) || point.Fixed ||
            !double.IsFinite(position.X) || !double.IsFinite(position.Y)) return false;
        var before = CaptureSnapshot();
        SetPointPosition(id, position);
        SolveRelations();
        if (ConstraintsSatisfied()) return true;
        RestoreSnapshot(before);
        return false;
    }

    public void SetPointFixed(int id, bool isFixed)
    {
        if (id == 0) return;
        if (Points.TryGetValue(id, out var point))
            Points[id] = point with { Fixed = isFixed };
    }

    public int FindPointNear(Point2d p, double tolerance)
    {
        var bestId = -1;
        var best = tolerance;
        foreach (var item in Points.Values)
        {
            var d = Point2d.Distance(item.Position, p);
            if (d <= best)
            {
                best = d;
                bestId = item.Id;
            }
        }
        return bestId;
    }

    public int FindEntityNear(Point2d p, double tolerance)
    {
        var bestId = -1;
        var best = tolerance;

        foreach (var entity in Entities)
        {
            double d;
            switch (entity)
            {
                case SketchLine line:
                    d = DistancePointToSegment(
                        p,
                        Point(line.StartPointId).Position,
                        Point(line.EndPointId).Position);
                    break;
                case SketchCircle circle:
                    d = Math.Abs(Point2d.Distance(p, Point(circle.CenterPointId).Position) - circle.Radius);
                    break;
                case SketchArc arc:
                    d = DistanceToArc(p,arc);
                    break;
                case SketchEllipse ellipse:
                    d = DistanceToEllipse(p,ellipse);
                    break;
                default:
                    continue;
            }

            if (d <= best)
            {
                best = d;
                bestId = entity.Id;
            }
        }

        return bestId;
    }

    public int FindDimensionNear(Point2d p, double tolerance)
    {
        var bestId = -1;
        var best = tolerance * 1.8;
        foreach (var dimension in Dimensions)
        {
            var d = Point2d.Distance(p, dimension.LabelPosition);
            if (d <= best)
            {
                best = d;
                bestId = dimension.Id;
            }
        }
        return bestId;
    }

    private double DistanceToEllipse(Point2d p,SketchEllipse ellipse)
    {
        var sampled=SampleEllipse(ellipse,72);
        var best=double.PositiveInfinity;
        for(var i=0;i<sampled.Count;i++)
            best=Math.Min(best,DistancePointToSegment(p,sampled[i],sampled[(i+1)%sampled.Count]));
        return best;
    }

    private double DistanceToArc(Point2d p,SketchArc arc)
    {
        var c=Point(arc.CenterPointId).Position;
        var a=Point(arc.StartPointId).Position;
        var b=Point(arc.EndPointId).Position;
        var radius=Point2d.Distance(c,a);
        if(radius<=1e-12)return double.PositiveInfinity;
        var angle=Math.Atan2(p.Y-c.Y,p.X-c.X);
        var start=Math.Atan2(a.Y-c.Y,a.X-c.X);
        var end=Math.Atan2(b.Y-c.Y,b.X-c.X);
        static double Norm(double x){while(x<0)x+=Math.PI*2;while(x>=Math.PI*2)x-=Math.PI*2;return x;}
        start=Norm(start);end=Norm(end);angle=Norm(angle);
        bool within;
        if(!arc.Clockwise)
        {
            var span=Norm(end-start);var rel=Norm(angle-start);within=rel<=span+1e-9;
        }
        else
        {
            var span=Norm(start-end);var rel=Norm(start-angle);within=rel<=span+1e-9;
        }
        if(within)return Math.Abs(Point2d.Distance(p,c)-radius);
        return Math.Min(Point2d.Distance(p,a),Point2d.Distance(p,b));
    }

    private static double DistancePointToSegment(Point2d p, Point2d a, Point2d b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < 1e-18) return Point2d.Distance(p, a);
        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Clamp(t, 0.0, 1.0);
        return Point2d.Distance(p, new Point2d(a.X + dx * t, a.Y + dy * t));
    }

    public int CreateOrSnapPoint(Point2d p, double tolerance)
    {
        var existing = FindPointNear(p, tolerance);
        if (existing >= 0) return existing;
        return CreatePointExact(p);
    }

    private static void ValidatePoint(Point2d p)
    {
        if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            throw new ArgumentException("Nokta koordinatları sonlu olmalıdır.");
    }

    public int CreatePointExact(Point2d p)
    {
        ValidatePoint(p);
        var id = _nextPointId++;
        Points[id] = new SketchPoint(id, p);
        return id;
    }

    public SketchLine AddLine(int startPointId, int endPointId, bool construction = false)
    {
        if (!Points.ContainsKey(startPointId) || !Points.ContainsKey(endPointId))
            throw new ArgumentException("Çizgi noktası bulunamadı.");
        if (startPointId == endPointId || Point2d.Distance(Point(startPointId).Position, Point(endPointId).Position) <= 1e-9)
            throw new InvalidOperationException("Sıfır uzunluklu çizgi oluşturulamaz.");
        var line = new SketchLine(_nextEntityId++, startPointId, endPointId, construction);
        Entities.Add(line);
        return line;
    }

    public SketchCircle AddCircle(int centerPointId, double radius, bool construction = false)
    {
        if (!Points.ContainsKey(centerPointId)) throw new ArgumentException("Daire merkezi bulunamadı.");
        if (!double.IsFinite(radius) || radius <= 1e-9) throw new InvalidOperationException("Daire yarıçapı sıfır olamaz.");
        var circle = new SketchCircle(_nextEntityId++, centerPointId, radius, construction);
        Entities.Add(circle);
        return circle;
    }

    public SketchEllipse AddEllipse(int centerPointId,double radiusX,double radiusY,double rotationRadians=0,bool construction=false)
    {
        if(!Points.ContainsKey(centerPointId))throw new ArgumentException("Elips merkezi bulunamadı.");
        if(!double.IsFinite(radiusX)||!double.IsFinite(radiusY)||radiusX<=1e-9||radiusY<=1e-9||!double.IsFinite(rotationRadians))
            throw new InvalidOperationException("Elips yarı eksenleri pozitif ve sonlu olmalı.");
        var ellipse=new SketchEllipse(_nextEntityId++,centerPointId,radiusX,radiusY,rotationRadians,construction);
        Entities.Add(ellipse);return ellipse;
    }

    public SketchArc AddArc(int centerPointId,int startPointId,int endPointId,bool clockwise=false,bool construction=false)
    {
        if(!Points.ContainsKey(centerPointId)||!Points.ContainsKey(startPointId)||!Points.ContainsKey(endPointId))
            throw new ArgumentException("Yay noktası bulunamadı.");
        if(startPointId==endPointId)throw new InvalidOperationException("Yay başlangıç ve bitiş noktaları farklı olmalı.");
        var c=Point(centerPointId).Position;
        var a=Point(startPointId).Position;
        var b=Point(endPointId).Position;
        var ra=Point2d.Distance(c,a);var rb=Point2d.Distance(c,b);
        if(ra<=1e-9||Math.Abs(ra-rb)>Math.Max(1e-6,ra*1e-5))
            throw new InvalidOperationException("Yay başlangıç ve bitiş noktaları aynı yarıçapta olmalı.");
        var arc=new SketchArc(_nextEntityId++,centerPointId,startPointId,endPointId,clockwise,construction);
        Entities.Add(arc);
        return arc;
    }

    public SketchRelation AddRelation(
        SketchRelationType type,
        int entityA = -1,
        int entityB = -1,
        int pointA = -1,
        int pointB = -1)
    {
        var existing = Relations.FirstOrDefault(r =>
            r.Type == type &&
            r.EntityA == entityA && r.EntityB == entityB &&
            r.PointA == pointA && r.PointB == pointB);
        if (existing is not null) return existing;

        var relation = new SketchRelation(
            _nextRelationId++, type, entityA, entityB, pointA, pointB);
        Relations.Add(relation);
        SolveRelations();
        return relation;
    }

    public void RemoveRelation(int relationId)
    {
        var relation = Relations.FirstOrDefault(x => x.Id == relationId);
        if (relation is null) return;

        if (relation.Type == SketchRelationType.Fixed && relation.PointA > 0)
            SetPointFixed(relation.PointA, false);

        Relations.RemoveAll(x => x.Id == relationId);
    }

    public SketchDimension AddDimension(
        SketchDimensionType type,
        Point2d labelPosition,
        int entityA = -1,
        int entityB = -1,
        int pointA = -1,
        int pointB = -1,
        bool driving = true)
    {
        var value = Measure(type, entityA, entityB, pointA, pointB);
        var dimension = new SketchDimension(
            _nextDimensionId++, type, value, labelPosition,
            entityA, entityB, pointA, pointB, driving);
        Dimensions.Add(dimension);
        return dimension;
    }

    public void SetDimensionLabel(int dimensionId, Point2d label)
    {
        var index = Dimensions.FindIndex(x => x.Id == dimensionId);
        if (index < 0) return;
        Dimensions[index] = Dimensions[index] with { LabelPosition = label };
    }

    public bool SetDimensionValue(int dimensionId, double value)
    {
        if (!double.IsFinite(value) || value < 0) return false;
        var index = Dimensions.FindIndex(x => x.Id == dimensionId);
        if (index < 0) return false;

        var dim = Dimensions[index];
        if (value <= 1e-9 && dim.Type is not SketchDimensionType.HorizontalDistance and not SketchDimensionType.VerticalDistance and not SketchDimensionType.PointLineDistance and not SketchDimensionType.ParallelLineDistance) return false;
        if (dim.Type == SketchDimensionType.Angle && value >= 180) return false;
        var before = CaptureSnapshot();
        if (!dim.Driving)
        {
            Dimensions[index] = dim with { Value = Measure(dim) };
            return false;
        }

        Dimensions[index] = dim with { Value = value };
        var ok = dim.ReferenceA is not null || EnforceDimension(Dimensions[index]);
        // A direct geometric move can be blocked while the general solver can
        // still satisfy the full system by moving other free parameters.
        SolveRelations();
        if (!ConstraintsSatisfied())
        {
            RestoreSnapshot(before);
            return false;
        }
        RefreshDimensionValues();
        if(value!=dim.Value)Revision++;
        return true;
    }

    public void SetDimensionDriving(int dimensionId, bool driving)
    {
        var index = Dimensions.FindIndex(x => x.Id == dimensionId);
        if (index < 0) return;
        Dimensions[index] = Dimensions[index] with
        {
            Driving = driving,
            Value = Measure(Dimensions[index])
        };
    }

    public void DeleteDimension(int dimensionId)
        => Dimensions.RemoveAll(x => x.Id == dimensionId);

    public void DeleteEntity(int entityId)
    {
        var entity = Entity(entityId);
        if (entity is null) return;

        Relations.RemoveAll(r => ReferenceUsesEntity(r.ReferenceA,entityId) || ReferenceUsesEntity(r.ReferenceB,entityId) || ReferenceUsesEntity(r.ReferenceC,entityId));
        Dimensions.RemoveAll(d => ReferenceUsesEntity(d.ReferenceA,entityId) || ReferenceUsesEntity(d.ReferenceB,entityId));
        Entities.RemoveAll(x => x.Id == entityId);
        Relations.RemoveAll(r => r.EntityA == entityId || r.EntityB == entityId);
        Dimensions.RemoveAll(d => d.EntityA == entityId || d.EntityB == entityId);

        CenterRectangles.RemoveAll(c =>
            c.BottomLineId == entityId || c.RightLineId == entityId ||
            c.TopLineId == entityId || c.LeftLineId == entityId);

        RemoveUnusedPoints();
    }

    public void DeletePoint(int pointId)
    {
        if (pointId == 0) return;
        var incident = Entities.Where(e => e switch
        {
            SketchLine l => l.StartPointId == pointId || l.EndPointId == pointId,
            SketchCircle c => c.CenterPointId == pointId,
            SketchArc a => a.CenterPointId==pointId||a.StartPointId==pointId||a.EndPointId==pointId,
            SketchEllipse e => e.CenterPointId==pointId,
            _ => false
        }).Select(e => e.Id).ToList();

        foreach (var entityId in incident) DeleteEntity(entityId);
        Relations.RemoveAll(r => ReferenceUsesPoint(r.ReferenceA,pointId) || ReferenceUsesPoint(r.ReferenceB,pointId) || ReferenceUsesPoint(r.ReferenceC,pointId));
        Dimensions.RemoveAll(d => ReferenceUsesPoint(d.ReferenceA,pointId) || ReferenceUsesPoint(d.ReferenceB,pointId));
        Points.Remove(pointId);
        Relations.RemoveAll(r => r.PointA == pointId || r.PointB == pointId);
        Dimensions.RemoveAll(d => d.PointA == pointId || d.PointB == pointId);
    }

    private void RemoveUnusedPoints()
    {
        var used = new HashSet<int> { 0 };
        foreach (var entity in Entities)
        {
            switch (entity)
            {
                case SketchLine line:
                    used.Add(line.StartPointId);
                    used.Add(line.EndPointId);
                    break;
                case SketchCircle circle:
                    used.Add(circle.CenterPointId);
                    break;
                case SketchArc arc:
                    used.Add(arc.CenterPointId);used.Add(arc.StartPointId);used.Add(arc.EndPointId);
                    break;
                case SketchEllipse ellipse:
                    used.Add(ellipse.CenterPointId);
                    break;
            }
        }
        foreach (var center in CenterRectangles)
            used.Add(center.CenterPointId);

        // Standalone point references (e.g. snapped three-point circle inputs)
        // remain part of the sketch even when an unrelated entity is deleted.
        foreach (var reference in Relations.SelectMany(r => new[] { r.ReferenceA,r.ReferenceB,r.ReferenceC })
                     .Concat(Dimensions.SelectMany(d => new[] { d.ReferenceA,d.ReferenceB })))
            if (reference is { Kind: GeometryReferenceKind.Point }) used.Add(reference.Id);

        foreach (var pointId in Points.Keys.Where(id => !used.Contains(id)).ToList())
        {
            Points.Remove(pointId);
            Relations.RemoveAll(r => r.PointA == pointId || r.PointB == pointId);
            Dimensions.RemoveAll(d => d.PointA == pointId || d.PointB == pointId);
        }
    }

    public IReadOnlyList<int> AddCornerRectangle(Point2d a, Point2d b, double snapTolerance)
    {
        var width = Math.Abs(b.X - a.X);
        var height = Math.Abs(b.Y - a.Y);
        if (width < 1e-9 || height < 1e-9)
            throw new InvalidOperationException("Dikdörtgen boyutu sıfır olamaz.");

        var localTolerance = Math.Min(snapTolerance, Math.Min(width, height) * 0.20);
        var p0 = CreateOrSnapPoint(new Point2d(a.X, a.Y), localTolerance);
        var p1 = CreateOrSnapPoint(new Point2d(b.X, a.Y), localTolerance);
        var p2 = CreateOrSnapPoint(new Point2d(b.X, b.Y), localTolerance);
        var p3 = CreateOrSnapPoint(new Point2d(a.X, b.Y), localTolerance);

        var l0 = AddLine(p0, p1);
        var l1 = AddLine(p1, p2);
        var l2 = AddLine(p2, p3);
        var l3 = AddLine(p3, p0);
        AddRelation(SketchRelationType.Horizontal, l0.Id);
        AddRelation(SketchRelationType.Vertical, l1.Id);
        AddRelation(SketchRelationType.Horizontal, l2.Id);
        AddRelation(SketchRelationType.Vertical, l3.Id);
        return new[] { l0.Id, l1.Id, l2.Id, l3.Id };
    }

    public IReadOnlyList<int> AddCenterRectangle(Point2d center, Point2d corner)
    {
        var cx = center.X;
        var cy = center.Y;
        var dx = Math.Abs(corner.X - cx);
        var dy = Math.Abs(corner.Y - cy);
        if (dx < 1e-9 || dy < 1e-9)
            throw new InvalidOperationException("Merkez dikdörtgen boyutu sıfır olamaz.");

        var centerId = CreateOrSnapPoint(center, 1e-9);
        var p0 = CreatePointExact(new Point2d(cx - dx, cy - dy));
        var p1 = CreatePointExact(new Point2d(cx + dx, cy - dy));
        var p2 = CreatePointExact(new Point2d(cx + dx, cy + dy));
        var p3 = CreatePointExact(new Point2d(cx - dx, cy + dy));

        var l0 = AddLine(p0, p1);
        var l1 = AddLine(p1, p2);
        var l2 = AddLine(p2, p3);
        var l3 = AddLine(p3, p0);
        AddLine(p0, p2, true);
        AddLine(p1, p3, true);

        AddRelation(SketchRelationType.Horizontal, l0.Id);
        AddRelation(SketchRelationType.Vertical, l1.Id);
        AddRelation(SketchRelationType.Horizontal, l2.Id);
        AddRelation(SketchRelationType.Vertical, l3.Id);

        CenterRectangles.Add(new CenterRectangleDefinition(
            centerId, p0, p1, p2, p3,
            l0.Id, l1.Id, l2.Id, l3.Id));
        EnforceCenterRectangle(CenterRectangles[^1]);
        return new[] { l0.Id, l1.Id, l2.Id, l3.Id };
    }

    public void AddAutomaticLineRelation(int lineId, double ratioTolerance = 0.015)
    {
        if (Entity(lineId) is not SketchLine line) return;
        var a = Point(line.StartPointId).Position;
        var b = Point(line.EndPointId).Position;
        var dx = Math.Abs(b.X - a.X);
        var dy = Math.Abs(b.Y - a.Y);
        var length = Math.Max(1e-12, Math.Sqrt(dx * dx + dy * dy));

        if (dy / length <= ratioTolerance)
            AddRelation(SketchRelationType.Horizontal, lineId);
        else if (dx / length <= ratioTolerance)
            AddRelation(SketchRelationType.Vertical, lineId);
    }

    public bool ApplyRelationToSelection(SketchRelationType type,
        IReadOnlyList<int> selectedEntities, IReadOnlyList<int> selectedPoints)
    {
        var before = CaptureSnapshot();
        if (ApplyRelationToSelectionCore(type, selectedEntities, selectedPoints) && ConstraintsSatisfied())
            return true;
        RestoreSnapshot(before);
        return false;
    }

    private bool ConstraintsSatisfied()
    {
        const double eps = 1e-6;
        if (Points.Values.Any(p => !double.IsFinite(p.Position.X) || !double.IsFinite(p.Position.Y))) return false;
        if (Circles().Any(c => !double.IsFinite(c.Radius) || c.Radius<=1e-9)) return false;
        if (Ellipses().Any(e => !double.IsFinite(e.RadiusX)||!double.IsFinite(e.RadiusY)||!double.IsFinite(e.RotationRadians)||
            e.RadiusX<=1e-9||e.RadiusY<=1e-9)) return false;
        if (Arcs().Any(a =>
        {
            var center=Point(a.CenterPointId).Position;
            var ra=Point2d.Distance(center,Point(a.StartPointId).Position);
            var rb=Point2d.Distance(center,Point(a.EndPointId).Position);
            return !double.IsFinite(ra)||!double.IsFinite(rb)||ra<=1e-9||Math.Abs(ra-rb)>Math.Max(1e-6,ra*1e-5);
        })) return false;
        if (Lines().Any(l => Point2d.Distance(Point(l.StartPointId).Position, Point(l.EndPointId).Position) <= 1e-9)) return false;
        if (Dimensions.Any(d => d.Driving && (!double.IsFinite(Measure(d)) || Math.Abs(Measure(d)-d.Value)>eps))) return false;
        if (Dimensions.Any(d => d.Driving && d.ReferenceA is not null && ReferenceDimensionResiduals(d).Any(v => !double.IsFinite(v) || Math.Abs(v)>eps))) return false;
        foreach (var rectangle in CenterRectangles)
        {
            var c = Point(rectangle.CenterPointId).Position;
            var a = Point(rectangle.P0).Position;
            var b = Point(rectangle.P2).Position;
            if (Math.Abs((a.X+b.X)/2-c.X)>eps || Math.Abs((a.Y+b.Y)/2-c.Y)>eps) return false;
        }
        foreach (var r in Relations)
        {
            if (r.ReferenceA is not null)
            {
                if (ReferenceRelationResidual(r).Any(v => !double.IsFinite(v) || Math.Abs(v)>eps)) return false;
                continue;
            }
            var a = Entity(r.EntityA) as SketchLine;
            var b = Entity(r.EntityB) as SketchLine;
            var ca = Entity(r.EntityA) as SketchCircle;
            var cb = Entity(r.EntityB) as SketchCircle;
            Point2d A(SketchLine l) => Point(l.StartPointId).Position;
            Point2d B(SketchLine l) => Point(l.EndPointId).Position;
            double Angle(SketchLine l, SketchLine k) => MeasureAngle(l,k);
            bool valid = r.Type switch
            {
                SketchRelationType.Horizontal => a is not null && Math.Abs(A(a).Y-B(a).Y)<eps,
                SketchRelationType.Vertical => a is not null && Math.Abs(A(a).X-B(a).X)<eps,
                SketchRelationType.Coincident => Points.ContainsKey(r.PointA) && Points.ContainsKey(r.PointB) && Point2d.Distance(Point(r.PointA).Position,Point(r.PointB).Position)<eps,
                SketchRelationType.Fixed => Points.ContainsKey(r.PointA) && Point(r.PointA).Fixed,
                SketchRelationType.Parallel => a is not null && b is not null && Math.Min(Angle(a,b),180-Angle(a,b))<eps,
                SketchRelationType.Perpendicular => a is not null && b is not null && Math.Abs(Angle(a,b)-90)<eps,
                SketchRelationType.Equal => a is not null && b is not null ? Math.Abs(Point2d.Distance(A(a),B(a))-Point2d.Distance(A(b),B(b)))<eps : ca is not null && cb is not null && Math.Abs(ca.Radius-cb.Radius)<eps,
                SketchRelationType.Concentric => ca is not null && cb is not null && Point2d.Distance(Point(ca.CenterPointId).Position,Point(cb.CenterPointId).Position)<eps,
                SketchRelationType.Collinear => a is not null && b is not null && LineDistance(A(b),A(a),B(a))<eps && LineDistance(B(b),A(a),B(a))<eps,
                _ => false
            };
            if (!valid) return false;
        }
        return true;
    }

    private static double LineDistance(Point2d p, Point2d a, Point2d b)
        => Math.Abs((b.X-a.X)*(p.Y-a.Y)-(b.Y-a.Y)*(p.X-a.X))/Math.Max(1e-12,Point2d.Distance(a,b));

    private bool ApplyRelationToSelectionCore(
        SketchRelationType type,
        IReadOnlyList<int> selectedEntities,
        IReadOnlyList<int> selectedPoints)
    {
        switch (type)
        {
            case SketchRelationType.Horizontal:
            case SketchRelationType.Vertical:
                if (selectedEntities.Count != 1 || Entity(selectedEntities[0]) is not SketchLine)
                    return false;
                AddRelation(type, selectedEntities[0]);
                return true;

            case SketchRelationType.Fixed:
                if (selectedPoints.Count == 0 || selectedPoints.Any(id => !Points.ContainsKey(id))) return false;
                foreach (var pointId in selectedPoints)
                {
                    SetPointFixed(pointId, true);
                    AddRelation(SketchRelationType.Fixed, pointA: pointId);
                }
                SolveRelations();
                return true;

            case SketchRelationType.Parallel:
            case SketchRelationType.Perpendicular:
            case SketchRelationType.Equal:
            case SketchRelationType.Collinear:
            case SketchRelationType.Concentric:
                if (selectedEntities.Count != 2 || selectedEntities[0] == selectedEntities[1]) return false;
                var a = Entity(selectedEntities[0]);
                var b = Entity(selectedEntities[1]);
                var lines = a is SketchLine && b is SketchLine;
                var circles = a is SketchCircle && b is SketchCircle;
                if (type == SketchRelationType.Concentric ? !circles :
                    type == SketchRelationType.Equal ? !(lines || circles) : !lines) return false;
                AddRelation(type, selectedEntities[0], selectedEntities[1]);
                return true;

            case SketchRelationType.Coincident:
                if (selectedPoints.Count != 2 || selectedPoints[0] == selectedPoints[1] || selectedPoints.Any(id => !Points.ContainsKey(id))) return false;
                AddRelation(type, pointA: selectedPoints[0], pointB: selectedPoints[1]);
                return true;

            default:
                return false;
        }
    }

    public void SolveRelations(int iterations = 6)
    {
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            foreach (var relation in Relations)
                ApplyRelation(relation);

            foreach (var dimension in Dimensions.Where(d => d.Driving))
                EnforceDimension(dimension);

            foreach (var rectangle in CenterRectangles)
                EnforceCenterRectangle(rectangle);
        }

        if (!ConstraintsSatisfied()) SolveNumerically();
        RefreshDimensionValues();
    }

    private bool EnforceDimension(SketchDimension dim)
    {
        if (dim.ReferenceA is not null) return true;
        return dim.Type switch
        {
            SketchDimensionType.LineLength => SetLineLength(dim.EntityA, dim.Value),
            SketchDimensionType.CircleDiameter => SetCircleDiameter(dim.EntityA, dim.Value),
            SketchDimensionType.ArcRadius => SetArcRadius(dim.EntityA, dim.Value),
            SketchDimensionType.EllipseAxis1Diameter => SetEllipseAxisDiameter(dim.EntityA, dim.Value, true),
            SketchDimensionType.EllipseAxis2Diameter => SetEllipseAxisDiameter(dim.EntityA, dim.Value, false),
            SketchDimensionType.PointDistance => SetPointDistance(dim.PointA, dim.PointB, dim.Value),
            SketchDimensionType.HorizontalDistance => SetHorizontalDistance(dim.PointA, dim.PointB, dim.Value),
            SketchDimensionType.VerticalDistance => SetVerticalDistance(dim.PointA, dim.PointB, dim.Value),
            SketchDimensionType.Angle => SetAngle(dim.EntityA, dim.EntityB, dim.Value),
            _ => false
        };
    }

    private void ApplyRelation(SketchRelation relation)
    {
        if (relation.ReferenceA is not null) return;
        switch (relation.Type)
        {
            case SketchRelationType.Horizontal:
                if (Entity(relation.EntityA) is SketchLine h)
                {
                    var a = Point(h.StartPointId).Position;
                    var b = Point(h.EndPointId).Position;
                    if (!Point(h.EndPointId).Fixed)
                        SetPointPosition(h.EndPointId, new Point2d(b.X, a.Y));
                    else if (!Point(h.StartPointId).Fixed)
                        SetPointPosition(h.StartPointId, new Point2d(a.X, b.Y));
                }
                break;

            case SketchRelationType.Vertical:
                if (Entity(relation.EntityA) is SketchLine v)
                {
                    var a = Point(v.StartPointId).Position;
                    var b = Point(v.EndPointId).Position;
                    if (!Point(v.EndPointId).Fixed)
                        SetPointPosition(v.EndPointId, new Point2d(a.X, b.Y));
                    else if (!Point(v.StartPointId).Fixed)
                        SetPointPosition(v.StartPointId, new Point2d(b.X, a.Y));
                }
                break;

            case SketchRelationType.Coincident:
                if (Points.ContainsKey(relation.PointA) && Points.ContainsKey(relation.PointB))
                {
                    var a = Point(relation.PointA).Position;
                    if (!Point(relation.PointB).Fixed)
                        SetPointPosition(relation.PointB, a);
                    else if (!Point(relation.PointA).Fixed)
                        SetPointPosition(relation.PointA, Point(relation.PointB).Position);
                }
                break;

            case SketchRelationType.Parallel:
                AlignSecondLine(relation.EntityA, relation.EntityB, 0.0);
                break;

            case SketchRelationType.Perpendicular:
                AlignSecondLine(relation.EntityA, relation.EntityB, Math.PI / 2.0);
                break;

            case SketchRelationType.Equal:
                MakeEqual(relation.EntityA, relation.EntityB);
                break;

            case SketchRelationType.Collinear:
                MakeCollinear(relation.EntityA, relation.EntityB);
                break;

            case SketchRelationType.Concentric:
                MakeConcentric(relation.EntityA, relation.EntityB);
                break;

            case SketchRelationType.Fixed:
                if (relation.PointA >= 0) SetPointFixed(relation.PointA, true);
                break;
        }
    }

    private void AlignSecondLine(int firstId, int secondId, double offset)
    {
        if (Entity(firstId) is not SketchLine first || Entity(secondId) is not SketchLine second) return;
        var a0 = Point(first.StartPointId).Position;
        var a1 = Point(first.EndPointId).Position;
        var b0 = Point(second.StartPointId).Position;
        var b1 = Point(second.EndPointId).Position;
        var angle = Math.Atan2(a1.Y - a0.Y, a1.X - a0.X) + offset;
        if ((b1.X-b0.X)*Math.Cos(angle)+(b1.Y-b0.Y)*Math.Sin(angle)<0) angle+=Math.PI;
        var length = Point2d.Distance(b0, b1);
        if (length < 1e-9) return;

        var candidate = new Point2d(b0.X + Math.Cos(angle) * length, b0.Y + Math.Sin(angle) * length);
        if (!Point(second.EndPointId).Fixed)
            SetPointPosition(second.EndPointId, candidate);
    }

    private void MakeCollinear(int firstId, int secondId)
    {
        if (Entity(firstId) is not SketchLine first || Entity(secondId) is not SketchLine second) return;
        var a = Point(first.StartPointId).Position;
        var b = Point(first.EndPointId).Position;
        var c = Point(second.StartPointId).Position;
        var d = Point(second.EndPointId).Position;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-12) return;
        var ux = dx / len;
        var uy = dy / len;

        Point2d Project(Point2d p)
        {
            var t = (p.X - a.X) * ux + (p.Y - a.Y) * uy;
            return new Point2d(a.X + ux * t, a.Y + uy * t);
        }

        if (!Point(second.StartPointId).Fixed) SetPointPosition(second.StartPointId, Project(c));
        if (!Point(second.EndPointId).Fixed) SetPointPosition(second.EndPointId, Project(d));
    }

    private void MakeConcentric(int firstId, int secondId)
    {
        if (Entity(firstId) is not SketchCircle first || Entity(secondId) is not SketchCircle second) return;
        var firstCenter = Point(first.CenterPointId);
        var secondCenter = Point(second.CenterPointId);
        if (!secondCenter.Fixed)
            SetPointPosition(second.CenterPointId, firstCenter.Position);
        else if (!firstCenter.Fixed)
            SetPointPosition(first.CenterPointId, secondCenter.Position);
    }

    private void MakeEqual(int firstId, int secondId)
    {
        var a = Entity(firstId);
        var b = Entity(secondId);

        if (a is SketchLine la && b is SketchLine lb)
        {
            var target = Point2d.Distance(Point(la.StartPointId).Position, Point(la.EndPointId).Position);
            SetLineLength(lb.Id, target);
        }
        else if (a is SketchCircle ca && b is SketchCircle cb)
        {
            var index = Entities.FindIndex(x => x.Id == cb.Id);
            if (index >= 0) Entities[index] = cb with { Radius = ca.Radius };
        }
    }

    private bool SetLineLength(int lineId, double value)
    {
        if (TryResizeCenterRectangleForLine(lineId, value))
            return true;

        if (Entity(lineId) is not SketchLine line) return false;
        var a = Point(line.StartPointId).Position;
        var b = Point(line.EndPointId).Position;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var oldLength = Math.Sqrt(dx * dx + dy * dy);
        if (oldLength < 1e-12) return false;

        var ux = dx / oldLength;
        var uy = dy / oldLength;

        if (!Point(line.EndPointId).Fixed)
        {
            SetPointPosition(line.EndPointId, new Point2d(a.X + ux * value, a.Y + uy * value));
            return true;
        }

        if (!Point(line.StartPointId).Fixed)
        {
            SetPointPosition(line.StartPointId, new Point2d(b.X - ux * value, b.Y - uy * value));
            return true;
        }

        return false;
    }

    private bool SetCircleDiameter(int circleId, double value)
    {
        if (Entity(circleId) is not SketchCircle circle) return false;
        var index = Entities.FindIndex(x => x.Id == circleId);
        if (index < 0) return false;
        Entities[index] = circle with { Radius = value / 2.0 };
        return true;
    }

    private bool SetArcRadius(int arcId,double value)
    {
        if(Entity(arcId) is not SketchArc arc||value<=1e-9)return false;
        var center=Point(arc.CenterPointId).Position;
        var start=Point(arc.StartPointId).Position;
        var end=Point(arc.EndPointId).Position;
        var rs=Point2d.Distance(center,start);var re=Point2d.Distance(center,end);
        if(rs<=1e-12||re<=1e-12)return false;
        if(Point(arc.StartPointId).Fixed||Point(arc.EndPointId).Fixed)return false;
        SetPointPosition(arc.StartPointId,new Point2d(center.X+(start.X-center.X)/rs*value,center.Y+(start.Y-center.Y)/rs*value));
        SetPointPosition(arc.EndPointId,new Point2d(center.X+(end.X-center.X)/re*value,center.Y+(end.Y-center.Y)/re*value));
        return true;
    }

    private bool SetEllipseAxisDiameter(int ellipseId,double value,bool firstAxis)
    {
        if(Entity(ellipseId) is not SketchEllipse ellipse||value<=1e-9)return false;
        var index=Entities.FindIndex(x=>x.Id==ellipseId);
        if(index<0)return false;
        Entities[index]=firstAxis
            ?ellipse with{RadiusX=value/2.0}
            :ellipse with{RadiusY=value/2.0};
        return true;
    }

    private bool SetPointDistance(int pointA, int pointB, double value)
    {
        if (!Points.ContainsKey(pointA) || !Points.ContainsKey(pointB)) return false;
        var a = Point(pointA).Position;
        var b = Point(pointB).Position;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-12)
        {
            dx = 1.0;
            dy = 0.0;
            length = 1.0;
        }

        if (!Point(pointB).Fixed)
        {
            SetPointPosition(pointB, new Point2d(a.X + dx / length * value, a.Y + dy / length * value));
            return true;
        }

        if (!Point(pointA).Fixed)
        {
            SetPointPosition(pointA, new Point2d(b.X - dx / length * value, b.Y - dy / length * value));
            return true;
        }

        return false;
    }

    private bool SetAxisConnectedPosition(int pointId, double value, bool horizontal)
    {
        // Axis equality is transitive. Propagate a driving coordinate through
        // the entire connected component before the iterative relations pass,
        // so a rectangle's last edge cannot overwrite the edited first corner.
        var connected = new HashSet<int> { pointId };
        var pending = new Queue<int>();
        pending.Enqueue(pointId);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var relation in Relations)
            {
                int a, b;
                if (relation.Type == SketchRelationType.Coincident)
                { a = relation.PointA; b = relation.PointB; }
                else if (relation.Type == (horizontal ? SketchRelationType.Vertical : SketchRelationType.Horizontal)
                         && Entity(relation.EntityA) is SketchLine line)
                { a = line.StartPointId; b = line.EndPointId; }
                else continue;
                var other = a == current ? b : b == current ? a : -1;
                if (other >= 0 && Points.ContainsKey(other) && connected.Add(other)) pending.Enqueue(other);
            }
        }
        if (connected.Any(id => Point(id).Fixed && Math.Abs((horizontal ? Point(id).Position.X : Point(id).Position.Y) - value) > 1e-8))
            return false;
        foreach (var id in connected)
        {
            var p = Point(id).Position;
            SetPointPosition(id, horizontal ? new Point2d(value, p.Y) : new Point2d(p.X, value));
        }
        return true;
    }

    private bool SetHorizontalDistance(int pointA, int pointB, double value)
    {
        if (!Points.ContainsKey(pointA) || !Points.ContainsKey(pointB)) return false;
        var a = Point(pointA).Position;
        var b = Point(pointB).Position;
        var sign = b.X >= a.X ? 1.0 : -1.0;
        if (!Point(pointB).Fixed)
        {
            return SetAxisConnectedPosition(pointB, a.X + sign * value, horizontal: true);
        }
        if (!Point(pointA).Fixed)
        {
            return SetAxisConnectedPosition(pointA, b.X - sign * value, horizontal: true);
        }
        return false;
    }

    private bool SetVerticalDistance(int pointA, int pointB, double value)
    {
        if (!Points.ContainsKey(pointA) || !Points.ContainsKey(pointB)) return false;
        var a = Point(pointA).Position;
        var b = Point(pointB).Position;
        var sign = b.Y >= a.Y ? 1.0 : -1.0;
        if (!Point(pointB).Fixed)
        {
            return SetAxisConnectedPosition(pointB, a.Y + sign * value, horizontal: false);
        }
        if (!Point(pointA).Fixed)
        {
            return SetAxisConnectedPosition(pointA, b.Y - sign * value, horizontal: false);
        }
        return false;
    }

    private bool SetAngle(int firstId, int secondId, double degrees)
    {
        if (Entity(firstId) is not SketchLine first || Entity(secondId) is not SketchLine second)
            return false;

        var a0 = Point(first.StartPointId).Position;
        var a1 = Point(first.EndPointId).Position;
        var baseAngle = Math.Atan2(a1.Y - a0.Y, a1.X - a0.X);

        int pivotId;
        int movingId;
        if (second.StartPointId == first.StartPointId || second.StartPointId == first.EndPointId)
        {
            pivotId = second.StartPointId;
            movingId = second.EndPointId;
        }
        else if (second.EndPointId == first.StartPointId || second.EndPointId == first.EndPointId)
        {
            pivotId = second.EndPointId;
            movingId = second.StartPointId;
        }
        else
        {
            pivotId = second.StartPointId;
            movingId = second.EndPointId;
        }

        if (Point(movingId).Fixed) return false;
        var pivot = Point(pivotId).Position;
        var moving = Point(movingId).Position;
        var length = Point2d.Distance(pivot, moving);
        if (length < 1e-9) return false;

        // Measurement uses directed edges; moving the start reverses that edge.
        if (movingId == second.StartPointId) baseAngle += Math.PI;
        var current = Math.Atan2(moving.Y - pivot.Y, moving.X - pivot.X);
        var sign = NormalizeAngle(current - baseAngle) >= 0 ? 1.0 : -1.0;
        var target = baseAngle + sign * degrees * Math.PI / 180.0;
        SetPointPosition(movingId, new Point2d(
            pivot.X + Math.Cos(target) * length,
            pivot.Y + Math.Sin(target) * length));
        return true;
    }

    private static double NormalizeAngle(double radians)
    {
        while (radians > Math.PI) radians -= Math.PI * 2;
        while (radians < -Math.PI) radians += Math.PI * 2;
        return radians;
    }

    private bool TryResizeCenterRectangleForLine(int lineId, double value)
    {
        var def = CenterRectangles.FirstOrDefault(c =>
            c.BottomLineId == lineId || c.TopLineId == lineId ||
            c.LeftLineId == lineId || c.RightLineId == lineId);
        if (def is null) return false;

        var center = Point(def.CenterPointId).Position;
        var p0 = Point(def.P0).Position;
        var halfWidth = Math.Abs(p0.X - center.X);
        var halfHeight = Math.Abs(p0.Y - center.Y);

        if (lineId == def.BottomLineId || lineId == def.TopLineId)
            halfWidth = value / 2.0;
        else
            halfHeight = value / 2.0;

        SetCenterRectanglePoints(def, center, halfWidth, halfHeight);
        return true;
    }

    private void EnforceCenterRectangle(CenterRectangleDefinition def)
    {
        if (!Points.ContainsKey(def.CenterPointId)) return;
        var center = Point(def.CenterPointId).Position;
        var corners = new[] { def.P0, def.P1, def.P2, def.P3 }
            .Where(Points.ContainsKey)
            .Select(id => Point(id).Position)
            .ToList();
        if (corners.Count != 4) return;

        var halfWidth = corners.Average(p => Math.Abs(p.X - center.X));
        var halfHeight = corners.Average(p => Math.Abs(p.Y - center.Y));
        if (halfWidth < 1e-9 || halfHeight < 1e-9) return;
        SetCenterRectanglePoints(def, center, halfWidth, halfHeight);
    }

    private void SetCenterRectanglePoints(
        CenterRectangleDefinition def,
        Point2d center,
        double halfWidth,
        double halfHeight)
    {
        SetPointPosition(def.P0, new Point2d(center.X - halfWidth, center.Y - halfHeight));
        SetPointPosition(def.P1, new Point2d(center.X + halfWidth, center.Y - halfHeight));
        SetPointPosition(def.P2, new Point2d(center.X + halfWidth, center.Y + halfHeight));
        SetPointPosition(def.P3, new Point2d(center.X - halfWidth, center.Y + halfHeight));
    }

    public double Measure(SketchDimension dimension)
    {
        if (dimension.ReferenceA is null) return Measure(dimension.Type,dimension.EntityA,dimension.EntityB,dimension.PointA,dimension.PointB);
        var (a,b)=DimensionReferencePoints(dimension);
        return dimension.Type switch
        {
            SketchDimensionType.HorizontalDistance => Math.Abs(b.X-a.X),
            SketchDimensionType.VerticalDistance => Math.Abs(b.Y-a.Y),
            _ => Point2d.Distance(a,b)
        };
    }

    public double Measure(
        SketchDimensionType type,
        int entityA = -1,
        int entityB = -1,
        int pointA = -1,
        int pointB = -1)
    {
        return type switch
        {
            SketchDimensionType.LineLength when Entity(entityA) is SketchLine line =>
                Point2d.Distance(Point(line.StartPointId).Position, Point(line.EndPointId).Position),

            SketchDimensionType.CircleDiameter when Entity(entityA) is SketchCircle circle =>
                circle.Radius * 2.0,

            SketchDimensionType.ArcRadius when Entity(entityA) is SketchArc arc =>
                Point2d.Distance(Point(arc.CenterPointId).Position,Point(arc.StartPointId).Position),

            SketchDimensionType.EllipseAxis1Diameter when Entity(entityA) is SketchEllipse ellipse =>
                ellipse.RadiusX * 2.0,

            SketchDimensionType.EllipseAxis2Diameter when Entity(entityA) is SketchEllipse ellipse =>
                ellipse.RadiusY * 2.0,

            SketchDimensionType.PointDistance when Points.ContainsKey(pointA) && Points.ContainsKey(pointB) =>
                Point2d.Distance(Point(pointA).Position, Point(pointB).Position),

            SketchDimensionType.HorizontalDistance when Points.ContainsKey(pointA) && Points.ContainsKey(pointB) =>
                Math.Abs(Point(pointB).Position.X - Point(pointA).Position.X),

            SketchDimensionType.VerticalDistance when Points.ContainsKey(pointA) && Points.ContainsKey(pointB) =>
                Math.Abs(Point(pointB).Position.Y - Point(pointA).Position.Y),

            SketchDimensionType.Angle when Entity(entityA) is SketchLine a && Entity(entityB) is SketchLine b =>
                MeasureAngle(a, b),

            _ => 0.0
        };
    }

    private double MeasureAngle(SketchLine first, SketchLine second)
    {
        var a0 = Point(first.StartPointId).Position;
        var a1 = Point(first.EndPointId).Position;
        var b0 = Point(second.StartPointId).Position;
        var b1 = Point(second.EndPointId).Position;
        var ax = a1.X - a0.X;
        var ay = a1.Y - a0.Y;
        var bx = b1.X - b0.X;
        var by = b1.Y - b0.Y;
        var al = Math.Sqrt(ax * ax + ay * ay);
        var bl = Math.Sqrt(bx * bx + by * by);
        if (al < 1e-12 || bl < 1e-12) return 0;
        var dot = Math.Clamp((ax * bx + ay * by) / (al * bl), -1.0, 1.0);
        return Math.Acos(dot) * 180.0 / Math.PI;
    }

    private void RefreshDimensionValues()
    {
        for (var i = 0; i < Dimensions.Count; i++)
            if (!Dimensions[i].Driving)
                Dimensions[i] = Dimensions[i] with { Value = Measure(Dimensions[i]) };
    }

    public SketchDefinitionInfo DefinitionInfo() => SolverDefinition();

    public IEnumerable<SketchLine> Lines(bool includeConstruction = true)
        => Entities.OfType<SketchLine>().Where(x => includeConstruction || !x.Construction);

    public IEnumerable<SketchCircle> Circles(bool includeConstruction = true)
        => Entities.OfType<SketchCircle>().Where(x => includeConstruction || !x.Construction);

    public IEnumerable<SketchArc> Arcs(bool includeConstruction = true)
        => Entities.OfType<SketchArc>().Where(x => includeConstruction || !x.Construction);

    public IEnumerable<SketchEllipse> Ellipses(bool includeConstruction=true)
        => Entities.OfType<SketchEllipse>().Where(x=>includeConstruction||!x.Construction);

    public IReadOnlyList<Point2d> SampleEllipse(SketchEllipse ellipse,int segments=64)
    {
        segments=Math.Clamp(segments,12,512);
        var c=Point(ellipse.CenterPointId).Position;
        var cos=Math.Cos(ellipse.RotationRadians);var sin=Math.Sin(ellipse.RotationRadians);
        return Enumerable.Range(0,segments).Select(i=>
        {
            var a=Math.PI*2*i/segments;
            var x=Math.Cos(a)*ellipse.RadiusX;var y=Math.Sin(a)*ellipse.RadiusY;
            return new Point2d(c.X+x*cos-y*sin,c.Y+x*sin+y*cos);
        }).ToArray();
    }

    public IReadOnlyList<Point2d> SampleArc(SketchArc arc,int segments=24)
    {
        segments=Math.Clamp(segments,2,256);
        var c=Point(arc.CenterPointId).Position;
        var a=Point(arc.StartPointId).Position;
        var b=Point(arc.EndPointId).Position;
        var radius=Point2d.Distance(c,a);
        var start=Math.Atan2(a.Y-c.Y,a.X-c.X);
        var end=Math.Atan2(b.Y-c.Y,b.X-c.X);
        static double Positive(double x){while(x<0)x+=Math.PI*2;while(x>=Math.PI*2)x-=Math.PI*2;return x;}
        var span=!arc.Clockwise?Positive(end-start):-Positive(start-end);
        return Enumerable.Range(0,segments+1)
            .Select(i=>new Point2d(c.X+Math.Cos(start+span*i/segments)*radius,
                                  c.Y+Math.Sin(start+span*i/segments)*radius)).ToArray();
    }

    public bool TryGetSingleCircle(out SketchCircle circle)
    {
        var circles = Circles(false).ToList();
        circle = circles.FirstOrDefault()!;
        return circles.Count == 1 && !Lines(false).Any();
    }

    private bool TryGetClosedLineLoops(out List<List<Point2d>> loops)
    {
        loops=new List<List<Point2d>>();
        var entities=Entities.Where(e=>!e.Construction&&(e is SketchLine||e is SketchArc)).ToArray();
        if(entities.Length==0)return false;

        (int A,int B) Endpoints(SketchEntity entity)=>entity switch
        {
            SketchLine line=>(line.StartPointId,line.EndPointId),
            SketchArc arc=>(arc.StartPointId,arc.EndPointId),
            _=>throw new InvalidOperationException()
        };

        var adjacency=new Dictionary<int,List<SketchEntity>>();
        foreach(var entity in entities)
        {
            var (a,b)=Endpoints(entity);
            if(!adjacency.TryGetValue(a,out var aa))adjacency[a]=aa=new();
            if(!adjacency.TryGetValue(b,out var bb))adjacency[b]=bb=new();
            aa.Add(entity);bb.Add(entity);
        }
        if(adjacency.Values.Any(v=>v.Count!=2))return false;

        var unused=new HashSet<int>(entities.Select(e=>e.Id));
        while(unused.Count>0)
        {
            var first=entities.First(e=>unused.Contains(e.Id));
            var startPoint=Endpoints(first).A;
            var currentPoint=startPoint;
            SketchEntity? previous=null;
            var loop=new List<Point2d>();

            while(true)
            {
                var next=adjacency[currentPoint]
                    .FirstOrDefault(e=>unused.Contains(e.Id)&&(previous is null||e.Id!=previous.Id));
                if(next is null)return false;
                unused.Remove(next.Id);

                var (a,b)=Endpoints(next);
                var forward=a==currentPoint;
                var nextPoint=forward?b:a;

                if(next is SketchLine)
                {
                    loop.Add(Point(currentPoint).Position);
                }
                else if(next is SketchArc arc)
                {
                    var sampled=SampleArc(arc,24);
                    var ordered=forward?sampled:sampled.Reverse().ToArray();
                    // Include the current endpoint and interior samples, but leave the
                    // terminal endpoint for the next connected entity.
                    for(var i=0;i<ordered.Count-1;i++)loop.Add(ordered[i]);
                }

                previous=next;
                currentPoint=nextPoint;
                if(currentPoint==startPoint)break;
                if(loop.Count>entities.Length*64)return false;
            }

            if(loop.Count<3||!SimpleLoop(loop))return false;
            if(SignedArea(loop)<0)loop.Reverse();
            loops.Add(loop);
        }
        return loops.Count>0;
    }

    public bool TryGetClosedPolyline(out List<Point2d> orderedPoints)
    {
        orderedPoints = new List<Point2d>();
        if (!TryGetClosedLineLoops(out var loops) || loops.Count != 1) return false;
        orderedPoints = loops[0];
        return true;
    }

    public bool TryGetExtrusionProfile(
        out List<Point2d> outerLoop,
        out List<List<Point2d>> polygonHoles,
        out List<SketchCircle> circularHoles)
    {
        outerLoop = new List<Point2d>();
        polygonHoles = new List<List<Point2d>>();
        circularHoles = new List<SketchCircle>();

        List<List<Point2d>> loops;
        if(!TryGetClosedLineLoops(out loops))
        {
            var ellipses=Ellipses(false).ToArray();
            if(ellipses.Length!=1||Lines(false).Any()||Arcs(false).Any())return false;
            loops=new List<List<Point2d>>{SampleEllipse(ellipses[0],72).ToList()};
        }
        if(Ellipses(false).Any()&&!(loops.Count==1&&Lines(false).Count()==0&&Arcs(false).Count()==0))
            return false;

        outerLoop = loops.OrderByDescending(loop => Math.Abs(SignedArea(loop))).First();
        var boundary = outerLoop;
        foreach (var loop in loops)
        {
            if (ReferenceEquals(loop, outerLoop)) continue;
            if (!loop.All(p => PointInPolygon(p, boundary)) || LoopsTouch(loop, boundary)) return false;
            if (polygonHoles.Any(h => LoopsTouch(loop,h) || PointInPolygon(loop[0],h) || PointInPolygon(h[0],loop))) return false;
            polygonHoles.Add(loop);
        }

        foreach (var circle in Circles(false))
        {
            var center = Point(circle.CenterPointId).Position;
            if (!CircleInsidePolygon(center, circle.Radius, outerLoop))
                return false;
            foreach (var existing in circularHoles)
            {
                var otherCenter = Point(existing.CenterPointId).Position;
                if (Point2d.Distance(center, otherCenter) <= circle.Radius + existing.Radius + 1e-9)
                    return false;
            }
            if (polygonHoles.Any(hole => PointInPolygon(center, hole) ||
                hole.Any(p => Point2d.Distance(center,p) <= circle.Radius + 1e-9) ||
                hole.Where((p,i) => DistancePointToSegment(center,p,hole[(i+1)%hole.Count]) <= circle.Radius + 1e-9).Any()))
                return false;
            circularHoles.Add(circle);
        }
        return true;
    }

    public bool TryGetExtrusionProfile(
        out List<Point2d> outerLoop,
        out List<SketchCircle> circularHoles)
        => TryGetExtrusionProfile(out outerLoop, out _, out circularHoles);

    public bool TryGetExtrusionRegions(out List<SketchExtrusionRegion> regions)
    {
        regions=new List<SketchExtrusionRegion>();

        var boundaries=new List<(IReadOnlyList<Point2d> Polygon,SketchCircle? Circle)>();
        var hasLineOrArc=Lines(false).Any()||Arcs(false).Any();
        if(TryGetClosedLineLoops(out var loops))
            boundaries.AddRange(loops.Select(loop=>((IReadOnlyList<Point2d>)loop,(SketchCircle?)null)));
        else if(hasLineOrArc)
            return false;

        foreach(var ellipse in Ellipses(false))
            boundaries.Add((SampleEllipse(ellipse,96),null));

        foreach(var circle in Circles(false))
        {
            if(!double.IsFinite(circle.Radius)||circle.Radius<=1e-9)return false;
            var sampled=Enumerable.Range(0,96).Select(i=>
            {
                var center=Point(circle.CenterPointId).Position;
                var a=2*Math.PI*i/96.0;
                return new Point2d(center.X+Math.Cos(a)*circle.Radius,center.Y+Math.Sin(a)*circle.Radius);
            }).ToArray();
            boundaries.Add((sampled,circle));
        }

        if(boundaries.Count==0)return false;

        bool Contains((IReadOnlyList<Point2d> Polygon,SketchCircle? Circle) outer,
                      (IReadOnlyList<Point2d> Polygon,SketchCircle? Circle) inner)
        {
            const double eps=1e-9;
            if(outer.Circle is SketchCircle oc)
            {
                var center=Point(oc.CenterPointId).Position;
                if(inner.Circle is SketchCircle ic)
                {
                    var innerCenter=Point(ic.CenterPointId).Position;
                    return Point2d.Distance(center,innerCenter)+ic.Radius<oc.Radius-eps;
                }
                return inner.Polygon.All(p=>Point2d.Distance(center,p)<oc.Radius-eps);
            }

            if(inner.Circle is SketchCircle circle)
            {
                var center=Point(circle.CenterPointId).Position;
                return CircleInsidePolygon(center,circle.Radius,outer.Polygon);
            }

            return PointInPolygon(inner.Polygon[0],outer.Polygon)&&!LoopsTouch(inner.Polygon,outer.Polygon);
        }

        bool InvalidIntersection((IReadOnlyList<Point2d> Polygon,SketchCircle? Circle) a,
                                 (IReadOnlyList<Point2d> Polygon,SketchCircle? Circle) b)
        {
            const double eps=1e-9;
            if(Contains(a,b)||Contains(b,a))return false;

            if(a.Circle is SketchCircle ac&&b.Circle is SketchCircle bc)
            {
                var ca=Point(ac.CenterPointId).Position;var cb=Point(bc.CenterPointId).Position;
                return Point2d.Distance(ca,cb)<=ac.Radius+bc.Radius+eps;
            }

            if(a.Circle is SketchCircle circleA)
            {
                var center=Point(circleA.CenterPointId).Position;
                if(PointInPolygon(center,b.Polygon))return true;
                return Enumerable.Range(0,b.Polygon.Count).Any(i=>
                           DistancePointToSegment(center,b.Polygon[i],b.Polygon[(i+1)%b.Polygon.Count])<=circleA.Radius+eps);
            }

            if(b.Circle is SketchCircle circleB)
            {
                var center=Point(circleB.CenterPointId).Position;
                if(PointInPolygon(center,a.Polygon))return true;
                return Enumerable.Range(0,a.Polygon.Count).Any(i=>
                    DistancePointToSegment(center,a.Polygon[i],a.Polygon[(i+1)%a.Polygon.Count])<=circleB.Radius+eps);
            }

            return LoopsTouch(a.Polygon,b.Polygon);
        }

        for(var i=0;i<boundaries.Count;i++)
            for(var j=i+1;j<boundaries.Count;j++)
                if(InvalidIntersection(boundaries[i],boundaries[j]))return false;

        var parent=new int[boundaries.Count];
        Array.Fill(parent,-1);
        for(var i=0;i<boundaries.Count;i++)
        {
            var candidates=Enumerable.Range(0,boundaries.Count)
                .Where(j=>j!=i&&Contains(boundaries[j],boundaries[i]))
                .OrderBy(j=>Math.Abs(SignedArea(boundaries[j].Polygon)))
                .ToArray();
            if(candidates.Length>0)parent[i]=candidates[0];
        }

        int Depth(int index)
        {
            var depth=0;var seen=new HashSet<int>{index};var p=parent[index];
            while(p>=0)
            {
                if(!seen.Add(p))return int.MaxValue;
                depth++;p=parent[p];
            }
            return depth;
        }

        var depths=Enumerable.Range(0,boundaries.Count).Select(Depth).ToArray();
        if(depths.Any(d=>d==int.MaxValue))return false;

        for(var i=0;i<boundaries.Count;i++)
        {
            if(depths[i]%2!=0)continue;
            var holeIndexes=Enumerable.Range(0,boundaries.Count)
                .Where(j=>parent[j]==i&&depths[j]==depths[i]+1).ToArray();
            var polygonHoles=holeIndexes.Where(j=>boundaries[j].Circle is null)
                .Select(j=>boundaries[j].Polygon).ToArray();
            var circularHoles=holeIndexes.Where(j=>boundaries[j].Circle is not null)
                .Select(j=>boundaries[j].Circle!).ToArray();
            regions.Add(new SketchExtrusionRegion(
                boundaries[i].Circle is null?boundaries[i].Polygon:null,
                boundaries[i].Circle,
                polygonHoles,
                circularHoles));
        }

        return regions.Count>0;
    }

    private static bool SegmentsTouch(Point2d a, Point2d b, Point2d c, Point2d d)
    {
        const double eps = 1e-9;
        if (DistancePointToSegment(a,c,d)<=eps || DistancePointToSegment(b,c,d)<=eps ||
            DistancePointToSegment(c,a,b)<=eps || DistancePointToSegment(d,a,b)<=eps) return true;
        double Cross(Point2d p,Point2d q,Point2d r) => (q.X-p.X)*(r.Y-p.Y)-(q.Y-p.Y)*(r.X-p.X);
        return Math.Sign(Cross(a,b,c)) != Math.Sign(Cross(a,b,d)) && Math.Sign(Cross(c,d,a)) != Math.Sign(Cross(c,d,b));
    }

    private static bool LoopsTouch(IReadOnlyList<Point2d> a,IReadOnlyList<Point2d> b)
    {
        for (int i=0;i<a.Count;i++) for (int j=0;j<b.Count;j++)
            if (SegmentsTouch(a[i],a[(i+1)%a.Count],b[j],b[(j+1)%b.Count])) return true;
        return false;
    }

    private static bool SimpleLoop(IReadOnlyList<Point2d> p)
    {
        if (Math.Abs(SignedArea(p)) <= 1e-9) return false;
        for (int i=0;i<p.Count;i++)
        {
            if (Point2d.Distance(p[i],p[(i+1)%p.Count])<=1e-9) return false;
            for (int j=i+1;j<p.Count;j++)
            {
                if (j==i+1 || (i==0 && j==p.Count-1)) continue;
                if (SegmentsTouch(p[i],p[(i+1)%p.Count],p[j],p[(j+1)%p.Count])) return false;
            }
        }
        return true;
    }

    private static double SignedArea(IReadOnlyList<Point2d> loop)
    {
        double area = 0;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area * 0.5;
    }

    private static bool CircleInsidePolygon(
        Point2d center,
        double radius,
        IReadOnlyList<Point2d> polygon)
    {
        if (radius <= 0 || polygon.Count < 3 || !PointInPolygon(center, polygon))
            return false;

        // Exact boundary clearance is stronger than sampling a few points on
        // the circumference and also works for concave line profiles.
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if (DistancePointToSegment(center, a, b) <= radius + 1e-9)
                return false;
        }

        return true;
    }

    private static bool PointInPolygon(Point2d p, IReadOnlyList<Point2d> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = (i + polygon.Count - 1) % polygon.Count;
            var pi = polygon[i];
            var pj = polygon[j];

            var intersects =
                ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                (p.X < (pj.X - pi.X) * (p.Y - pi.Y) /
                    ((pj.Y - pi.Y) + 1e-30) + pi.X);

            if (intersects) inside = !inside;
        }
        return inside;
    }


}

