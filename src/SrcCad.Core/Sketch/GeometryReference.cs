using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

// Identity is model-space and semantic; no pixel coordinates are persisted.
public enum GeometryReferenceKind { Point, Origin, Endpoint, CenterPoint, Midpoint, Line, ConstructionLine, Circle, Axis }
public sealed record GeometryReference(GeometryReferenceKind Kind, int Id, int Slot = 0)
{
    public bool IsPointLike => Kind is GeometryReferenceKind.Point or GeometryReferenceKind.Origin or GeometryReferenceKind.Endpoint or GeometryReferenceKind.CenterPoint or GeometryReferenceKind.Midpoint;
    public bool IsLine => Kind is GeometryReferenceKind.Line or GeometryReferenceKind.ConstructionLine or GeometryReferenceKind.Axis;
}

public sealed partial class SketchModel
{
    private static bool ReferenceUsesEntity(GeometryReference? r,int id)
        => r is not null && r.Kind is not GeometryReferenceKind.Point and not GeometryReferenceKind.Origin && r.Id==id;
    private bool ReferenceUsesPoint(GeometryReference? r,int id)
        => r is not null && r.IsPointLike && ReferencePointId(r)==id;
    public GeometryReference ReferenceForPoint(int id)
    {
        if (id == 0) return new(GeometryReferenceKind.Origin, 0);
        var circle = Circles().FirstOrDefault(c => c.CenterPointId == id);
        if (circle is not null) return new(GeometryReferenceKind.CenterPoint, circle.Id);
        var line = Lines().FirstOrDefault(l => l.StartPointId == id || l.EndPointId == id);
        return line is null ? new(GeometryReferenceKind.Point, id) : new(GeometryReferenceKind.Endpoint, line.Id, line.StartPointId == id ? 0 : 1);
    }
    public GeometryReference AxisReference(int lineId)
        => Entity(lineId) is SketchLine ? new(GeometryReferenceKind.Axis,lineId) : throw new ArgumentException("Eksen için çizgi referansı gerekli.");
    public GeometryReference ReferenceForEntity(int id) => Entity(id) switch
    {
        SketchLine l => new(l.Construction ? GeometryReferenceKind.ConstructionLine : GeometryReferenceKind.Line, id),
        SketchCircle => new(GeometryReferenceKind.Circle, id),
        _ => throw new ArgumentException("Geometri bulunamadı.")
    };
    public int ReferencePointId(GeometryReference r) => r.Kind switch
    {
        GeometryReferenceKind.Origin => 0,
        GeometryReferenceKind.Point => r.Id,
        GeometryReferenceKind.CenterPoint when Entity(r.Id) is SketchCircle c => c.CenterPointId,
        GeometryReferenceKind.Endpoint when Entity(r.Id) is SketchLine l => r.Slot == 0 ? l.StartPointId : l.EndPointId,
        _ => -1
    };
    public Point2d ResolvePoint(GeometryReference r)
    {
        if (r.Kind == GeometryReferenceKind.Midpoint && Entity(r.Id) is SketchLine l)
        {
            var a=Point(l.StartPointId).Position; var b=Point(l.EndPointId).Position;
            return new((a.X+b.X)/2,(a.Y+b.Y)/2);
        }
        return Point(ReferencePointId(r)).Position;
    }
    public GeometryReference? HitReference(Point2d p, double tolerance, bool midpoints = false)
    {
        // Origin wins exact overlapping hits; bodies never consume a center hit.
        if (Point2d.Distance(p,Point(0).Position)<=tolerance) return ReferenceForPoint(0);
        var id=FindPointNear(p,tolerance);
        if(id>=0) return ReferenceForPoint(id);
        if(midpoints)
            foreach(var l in Lines())
            {
                var r=new GeometryReference(GeometryReferenceKind.Midpoint,l.Id);
                if(Point2d.Distance(p,ResolvePoint(r))<=tolerance) return r;
            }
        id=FindEntityNear(p,tolerance);
        return id>=0?ReferenceForEntity(id):null;
    }
    public bool ApplyReferenceRelation(SketchRelationType type, IReadOnlyList<GeometryReference> selection)
    {
        if(selection.Count==2 && selection[0]!=selection[1])
        {
            var a=selection[0];var b=selection[1];
            if(a.IsLine && b.IsPointLike || a.Kind==GeometryReferenceKind.Circle && b.IsLine) (a,b)=(b,a);
            bool pair=a.IsPointLike&&b.IsPointLike && type is SketchRelationType.Horizontal or SketchRelationType.Vertical or SketchRelationType.Coincident;
            bool pointLine=a.IsPointLike&&b.IsLine && type is SketchRelationType.Coincident or SketchRelationType.Midpoint;
            if(a.Kind==GeometryReferenceKind.Circle && b.IsPointLike)(a,b)=(b,a);
            bool pointCircle=a.IsPointLike&&b.Kind==GeometryReferenceKind.Circle&&type==SketchRelationType.Coincident;
            bool tangent=type==SketchRelationType.Tangent && (a.IsLine&&b.Kind==GeometryReferenceKind.Circle || a.Kind==GeometryReferenceKind.Circle&&b.Kind==GeometryReferenceKind.Circle);
            if(pair||pointLine||pointCircle||tangent)
            {
                var before=CaptureSnapshot();
                try
                {
                    foreach(var r in new[]{a,b})
                    {
                        if(r.IsPointLike) ResolvePoint(r);
                        else if(r.IsLine?Entity(r.Id) is not SketchLine:Entity(r.Id) is not SketchCircle) return false;
                    }
                    if(Relations.Any(r=>r.Type==type&&((r.ReferenceA==a&&r.ReferenceB==b)||(r.ReferenceA==b&&r.ReferenceB==a)))) return true;
                    double sense=1;bool inner=false;
                    if(tangent && a.IsLine)
                    {
                        var l=(SketchLine)Entity(a.Id)!;var c=(SketchCircle)Entity(b.Id)!;
                        sense=SignedLineDistance(Point(c.CenterPointId).Position,Point(l.StartPointId).Position,Point(l.EndPointId).Position)<0?-1:1;
                    }
                    if(tangent && !a.IsLine)
                    {
                        var c=(SketchCircle)Entity(a.Id)!;var d=(SketchCircle)Entity(b.Id)!;
                        var distance=Point2d.Distance(Point(c.CenterPointId).Position,Point(d.CenterPointId).Position);
                        inner=Math.Abs(distance-Math.Abs(c.Radius-d.Radius))<Math.Abs(distance-c.Radius-d.Radius);
                    }
                    Relations.Add(new SketchRelation(_nextRelationId++,type,ReferenceA:a,ReferenceB:b,Sense:sense,InternalTangency:inner));
                    if(SolveNumerically()&&ConstraintsSatisfied())return true;
                }
                catch(ArgumentException) { }
                catch(KeyNotFoundException) { }
                RestoreSnapshot(before);return false;
            }
        }
        return ApplyRelationToSelection(type,selection.Where(r=>!r.IsPointLike).Select(r=>r.Id).ToArray(),selection.Where(r=>r.IsPointLike).Select(ReferencePointId).ToArray());
    }
    public SketchDimension? InferDimension(IReadOnlyList<GeometryReference> refs, Point2d label)
    {
        if(refs.Count==1 && !refs[0].IsPointLike)
        {
            var type=refs[0].IsLine?SketchDimensionType.LineLength:SketchDimensionType.CircleDiameter;
            return new(-1,type,Measure(type,refs[0].Id,-1,-1,-1),label,EntityA:refs[0].Id);
        }
        if(refs.Count!=2 || refs[0]==refs[1]) return null;
        var a=refs[0];var b=refs[1];
        if(a.IsPointLike && b.IsPointLike)
        {
            var type=SmartDimensionPlacement.BetweenPoints(ResolvePoint(a),ResolvePoint(b),label);
            var d=new SketchDimension(-1,type,0,label,PointA:ReferencePointId(a),PointB:ReferencePointId(b),ReferenceA:a,ReferenceB:b);
            return d with { Value=Measure(d), Sense=ReferenceDimensionSense(d) };
        }
        if(a.IsLine && b.IsPointLike) (a,b)=(b,a);
        if(a.IsPointLike && b.IsLine)
        {
            var d=new SketchDimension(-1,SketchDimensionType.PointLineDistance,0,label,EntityA:b.Id,PointA:ReferencePointId(a),ReferenceA:a,ReferenceB:b);
            return d with {Value=Measure(d),Sense=ReferenceDimensionSense(d)};
        }
        if(a.IsLine && b.IsLine)
        {
            var first=(SketchLine)Entity(a.Id)!;var second=(SketchLine)Entity(b.Id)!;
            var p=Point(first.StartPointId).Position;var q=Point(first.EndPointId).Position;
            var u=Point(second.StartPointId).Position;var v=Point(second.EndPointId).Position;
            var length=Point2d.Distance(u,v);
            if(length>1e-9 && Math.Abs(SignedLineDistance(v,p,q)-SignedLineDistance(u,p,q))/length<1e-8)
            {
                var d=new SketchDimension(-1,SketchDimensionType.ParallelLineDistance,0,label,EntityA:a.Id,EntityB:b.Id,ReferenceA:a,ReferenceB:b);
                return d with {Value=Measure(d),Sense=ReferenceDimensionSense(d)};
            }
            return new(-1,SketchDimensionType.Angle,Measure(SketchDimensionType.Angle,a.Id,b.Id,-1,-1),label,EntityA:a.Id,EntityB:b.Id);
        }
        return null;
    }
    private double SignedReferenceMeasure(SketchDimension d)
    {
        var (a,b)=DimensionReferencePoints(d);
        if(d.Type==SketchDimensionType.HorizontalDistance)return b.X-a.X;
        if(d.Type==SketchDimensionType.VerticalDistance)return b.Y-a.Y;
        if(d.Type is SketchDimensionType.PointLineDistance or SketchDimensionType.ParallelLineDistance)
        {
            var l=(SketchLine)Entity(d.ReferenceB!.Id)!;
            return SignedLineDistance(a,Point(l.StartPointId).Position,Point(l.EndPointId).Position);
        }
        return Point2d.Distance(a,b);
    }
    private double ReferenceDimensionSense(SketchDimension d) => SignedReferenceMeasure(d)<0?-1:1;

    public SketchDimension AddInferredDimension(SketchDimension candidate)
    {
        var d=candidate with {Id=_nextDimensionId++};Dimensions.Add(d);return d;
    }
    public (Point2d A,Point2d B) DimensionReferencePoints(SketchDimension d)
    {
        // A line distance uses a live endpoint on the first carrier and its
        // projection onto the second. Both carrier endpoints enter the solver.
        var a=d.Type==SketchDimensionType.ParallelLineDistance
            ? Point(((SketchLine)Entity(d.ReferenceA!.Id)!).StartPointId).Position
            : ResolvePoint(d.ReferenceA!);
        if(d.ReferenceB!.IsPointLike) return (a,ResolvePoint(d.ReferenceB));
        var l=(SketchLine)Entity(d.ReferenceB.Id)!;
        var p=Point(l.StartPointId).Position;var q=Point(l.EndPointId).Position;
        var dx=q.X-p.X;var dy=q.Y-p.Y;
        var t=((a.X-p.X)*dx+(a.Y-p.Y)*dy)/(dx*dx+dy*dy);
        return (a,new(p.X+t*dx,p.Y+t*dy));
    }
}
