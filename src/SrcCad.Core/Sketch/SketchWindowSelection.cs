using SrcCad.Core.Geometry;
namespace SrcCad.Core.Sketch;
public sealed record SketchWindowResult(IReadOnlyList<int> Entities,IReadOnlyList<int> Points,IReadOnlyList<int> Dimensions);
public sealed partial class SketchModel
{
    // The four projected viewport corners form a convex quadrilateral in sketch
    // space, so window selection also works after camera rotation.
    public SketchWindowResult SelectWindow(IReadOnlyList<Point2d> polygon,bool crossing)
    {
        if(polygon.Count!=4)throw new ArgumentException("Four projected corners required");
        var edges=Enumerable.Range(0,4).Select(i=>(A:polygon[i],B:polygon[(i+1)%4])).ToArray();
        double Cross(Point2d a,Point2d b,Point2d p)=>(b.X-a.X)*(p.Y-a.Y)-(b.Y-a.Y)*(p.X-a.X);
        bool Inside(Point2d p)
        {var signs=edges.Select(e=>Cross(e.A,e.B,p)).ToArray();return signs.All(x=>x>=-1e-8)||signs.All(x=>x<=1e-8);}
        bool Intersects(Point2d a,Point2d b,Point2d c,Point2d d)
        {
            if(Math.Max(a.X,b.X)<Math.Min(c.X,d.X)-1e-8||Math.Max(c.X,d.X)<Math.Min(a.X,b.X)-1e-8||Math.Max(a.Y,b.Y)<Math.Min(c.Y,d.Y)-1e-8||Math.Max(c.Y,d.Y)<Math.Min(a.Y,b.Y)-1e-8)return false;
            return Cross(a,b,c)*Cross(a,b,d)<=1e-8&&Cross(c,d,a)*Cross(c,d,b)<=1e-8;
        }
        bool EntityInside(SketchEntity entity)
        {
            if(entity is SketchLine l)
            {
                var a=Point(l.StartPointId).Position;var b=Point(l.EndPointId).Position;
                return crossing ? Inside(a)||Inside(b)||edges.Any(e=>Intersects(a,b,e.A,e.B)) : Inside(a)&&Inside(b);
            }
            if(entity is SketchCircle circle)
            {
                var center=Point(circle.CenterPointId).Position;
                var full=Inside(center)&&edges.All(e=>DistancePointToSegment(center,e.A,e.B)>=circle.Radius-1e-8);
                if(!crossing)return full;
                return full||edges.Any(e=>DistancePointToSegment(center,e.A,e.B)<=circle.Radius+1e-8&&Math.Max(Point2d.Distance(center,e.A),Point2d.Distance(center,e.B))>=circle.Radius-1e-8);
            }
            return false;
        }
        return new(Entities.Where(EntityInside).Select(e=>e.Id).ToArray(),Points.Values.Where(p=>Inside(p.Position)).Select(p=>p.Id).ToArray(),Dimensions.Where(d=>Inside(d.LabelPosition)).Select(d=>d.Id).ToArray());
    }
}
