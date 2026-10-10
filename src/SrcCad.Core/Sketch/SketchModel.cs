using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

public sealed record SketchPoint(int Id, Point2d Position, bool Fixed = false);

public abstract record SketchEntity(int Id, bool Construction);
public sealed record SketchLine(int Id, int StartPointId, int EndPointId, bool Construction = false)
    : SketchEntity(Id, Construction);
public sealed record SketchCircle(int Id, int CenterPointId, double Radius, bool Construction = false)
    : SketchEntity(Id, Construction);

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
    Fixed
}

public sealed record SketchRelation(
    int Id,
    SketchRelationType Type,
    int EntityA = -1,
    int EntityB = -1,
    int PointA = -1,
    int PointB = -1);

public enum SketchDimensionType
{
    LineLength,
    CircleDiameter,
    PointDistance,
    HorizontalDistance,
    VerticalDistance,
    Angle
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
    bool Driving = true);

public sealed record CenterRectangleDefinition(
    int CenterPointId,
    int P0, int P1, int P2, int P3,
    int BottomLineId, int RightLineId, int TopLineId, int LeftLineId);

public sealed record SketchDefinitionInfo(
    int DegreesOfFreedom,
    bool IsFullyDefined,
    bool IsConflicting,
    string Text);

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


public sealed class SketchModel
{
    private int _nextPointId = 1;
    private int _nextEntityId = 1;
    private int _nextRelationId = 1;
    private int _nextDimensionId = 1;

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

    public void RestoreSnapshot(SketchSnapshot snapshot)
    {
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
        if (!Points.TryGetValue(id, out var point)) return;
        if (point.Fixed && !ignoreFixed) return;
        Points[id] = point with { Position = position };
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

        foreach (var entity in Entities.Where(x => !x.Construction))
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

    public int CreatePointExact(Point2d p)
    {
        var id = _nextPointId++;
        Points[id] = new SketchPoint(id, p);
        return id;
    }

    public SketchLine AddLine(int startPointId, int endPointId, bool construction = false)
    {
        if (startPointId == endPointId)
            throw new InvalidOperationException("Sıfır uzunluklu çizgi oluşturulamaz.");
        var line = new SketchLine(_nextEntityId++, startPointId, endPointId, construction);
        Entities.Add(line);
        return line;
    }

    public SketchCircle AddCircle(int centerPointId, double radius, bool construction = false)
    {
        if (radius <= 1e-9) throw new InvalidOperationException("Daire yarıçapı sıfır olamaz.");
        var circle = new SketchCircle(_nextEntityId++, centerPointId, radius, construction);
        Entities.Add(circle);
        return circle;
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
        if (value <= 1e-9) return false;
        var index = Dimensions.FindIndex(x => x.Id == dimensionId);
        if (index < 0) return false;

        var dim = Dimensions[index];
        if (!dim.Driving)
        {
            Dimensions[index] = dim with { Value = Measure(dim) };
            return false;
        }

        Dimensions[index] = dim with { Value = value };
        var ok = EnforceDimension(Dimensions[index]);
        if (!ok)
        {
            Dimensions[index] = dim;
            return false;
        }

        SolveRelations();
        RefreshDimensionValues();
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
            _ => false
        }).Select(e => e.Id).ToList();

        foreach (var entityId in incident) DeleteEntity(entityId);
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
            }
        }
        foreach (var center in CenterRectangles)
            used.Add(center.CenterPointId);

        foreach (var pointId in Points.Keys.Where(id => !used.Contains(id)).ToList())
            Points.Remove(pointId);
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

    public bool ApplyRelationToSelection(
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
                if (selectedPoints.Count == 0) return false;
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
                if (selectedEntities.Count != 2) return false;
                AddRelation(type, selectedEntities[0], selectedEntities[1]);
                return true;

            case SketchRelationType.Coincident:
                if (selectedPoints.Count != 2) return false;
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

        RefreshDimensionValues();
    }

    private bool EnforceDimension(SketchDimension dim)
    {
        return dim.Type switch
        {
            SketchDimensionType.LineLength => SetLineLength(dim.EntityA, dim.Value),
            SketchDimensionType.CircleDiameter => SetCircleDiameter(dim.EntityA, dim.Value),
            SketchDimensionType.PointDistance => SetPointDistance(dim.PointA, dim.PointB, dim.Value),
            SketchDimensionType.HorizontalDistance => SetHorizontalDistance(dim.PointA, dim.PointB, dim.Value),
            SketchDimensionType.VerticalDistance => SetVerticalDistance(dim.PointA, dim.PointB, dim.Value),
            SketchDimensionType.Angle => SetAngle(dim.EntityA, dim.EntityB, dim.Value),
            _ => false
        };
    }

    private void ApplyRelation(SketchRelation relation)
    {
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

    private bool SetHorizontalDistance(int pointA, int pointB, double value)
    {
        if (!Points.ContainsKey(pointA) || !Points.ContainsKey(pointB)) return false;
        var a = Point(pointA).Position;
        var b = Point(pointB).Position;
        var sign = b.X >= a.X ? 1.0 : -1.0;
        if (!Point(pointB).Fixed)
        {
            SetPointPosition(pointB, new Point2d(a.X + sign * value, b.Y));
            return true;
        }
        if (!Point(pointA).Fixed)
        {
            SetPointPosition(pointA, new Point2d(b.X - sign * value, a.Y));
            return true;
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
            SetPointPosition(pointB, new Point2d(b.X, a.Y + sign * value));
            return true;
        }
        if (!Point(pointA).Fixed)
        {
            SetPointPosition(pointA, new Point2d(a.X, b.Y - sign * value));
            return true;
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
        SetPointPosition(def.P0, new Point2d(center.X - halfWidth, center.Y - halfHeight), true);
        SetPointPosition(def.P1, new Point2d(center.X + halfWidth, center.Y - halfHeight), true);
        SetPointPosition(def.P2, new Point2d(center.X + halfWidth, center.Y + halfHeight), true);
        SetPointPosition(def.P3, new Point2d(center.X - halfWidth, center.Y + halfHeight), true);
    }

    public double Measure(SketchDimension dimension)
        => Measure(
            dimension.Type,
            dimension.EntityA,
            dimension.EntityB,
            dimension.PointA,
            dimension.PointB);

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
            Dimensions[i] = Dimensions[i] with { Value = Measure(Dimensions[i]) };
    }

    public SketchDefinitionInfo DefinitionInfo()
    {
        // Alpha-stage DOF accounting. It is intentionally conservative:
        // points=2 variables, circle radius=1; explicit constraints/dimensions remove DOF.
        var dof = Points.Values.Where(p => p.Id != 0 && !p.Fixed).Count() * 2
                  + Circles(false).Count();

        foreach (var relation in Relations)
        {
            dof -= relation.Type switch
            {
                SketchRelationType.Coincident => 2,
                SketchRelationType.Fixed => 2,
                _ => 1
            };
        }

        dof -= Dimensions.Count(d => d.Driving);

        // Center rectangle has four independent parameters: centerX, centerY, halfW, halfH.
        // Its construction reduces the raw four-corner DOF by four.
        dof -= CenterRectangles.Count * 4;

        dof = Math.Max(0, dof);
        return dof == 0
            ? new SketchDefinitionInfo(0, true, false, "Tam Tanımlı")
            : new SketchDefinitionInfo(dof, false, false, $"Eksik Tanımlı ({dof} DOF)");
    }

    public IEnumerable<SketchLine> Lines(bool includeConstruction = true)
        => Entities.OfType<SketchLine>().Where(x => includeConstruction || !x.Construction);

    public IEnumerable<SketchCircle> Circles(bool includeConstruction = true)
        => Entities.OfType<SketchCircle>().Where(x => includeConstruction || !x.Construction);

    public bool TryGetSingleCircle(out SketchCircle circle)
    {
        var circles = Circles(false).ToList();
        circle = circles.FirstOrDefault()!;
        return circles.Count == 1 && !Lines(false).Any();
    }

    private bool TryGetClosedLineLoops(out List<List<Point2d>> loops)
    {
        loops = new List<List<Point2d>>();
        var lines = Lines(false).ToList();
        if (lines.Count == 0) return false;

        var adjacency = new Dictionary<int, List<SketchLine>>();
        foreach (var line in lines)
        {
            if (!adjacency.TryGetValue(line.StartPointId, out var a)) adjacency[line.StartPointId] = a = new();
            if (!adjacency.TryGetValue(line.EndPointId, out var b)) adjacency[line.EndPointId] = b = new();
            a.Add(line);
            b.Add(line);
        }
        if (adjacency.Values.Any(v => v.Count != 2)) return false;

        var unused = new HashSet<int>(lines.Select(l => l.Id));
        while (unused.Count > 0)
        {
            var first = lines.First(l => unused.Contains(l.Id));
            var startPoint = first.StartPointId;
            var currentPoint = startPoint;
            SketchLine? previous = null;
            var loop = new List<Point2d>();

            while (true)
            {
                loop.Add(Point(currentPoint).Position);
                var next = adjacency[currentPoint]
                    .FirstOrDefault(l => unused.Contains(l.Id) && (previous is null || l.Id != previous.Id));
                if (next is null) return false;
                unused.Remove(next.Id);
                var nextPoint = next.StartPointId == currentPoint ? next.EndPointId : next.StartPointId;
                previous = next;
                currentPoint = nextPoint;
                if (currentPoint == startPoint) break;
                if (loop.Count > lines.Count) return false;
            }

            if (loop.Count < 3) return false;
            loops.Add(loop);
        }
        return loops.Count > 0;
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
        if (!TryGetClosedLineLoops(out var loops)) return false;

        outerLoop = loops.OrderByDescending(loop => Math.Abs(SignedArea(loop))).First();
        foreach (var loop in loops)
        {
            if (ReferenceEquals(loop, outerLoop)) continue;
            if (!loop.All(p => PointInPolygon(p, outerLoop))) return false;
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
            if (polygonHoles.Any(hole => PointInPolygon(center, hole)))
                return false;
            circularHoles.Add(circle);
        }
        return true;
    }

    public bool TryGetExtrusionProfile(
        out List<Point2d> outerLoop,
        out List<SketchCircle> circularHoles)
        => TryGetExtrusionProfile(out outerLoop, out _, out circularHoles);

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

    private static double DistancePointToSegment(Point2d p, Point2d a, Point2d b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length2 = dx * dx + dy * dy;
        if (length2 <= 1e-24) return Point2d.Distance(p, a);

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length2;
        t = Math.Max(0.0, Math.Min(1.0, t));
        return Point2d.Distance(p, new Point2d(a.X + t * dx, a.Y + t * dy));
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
