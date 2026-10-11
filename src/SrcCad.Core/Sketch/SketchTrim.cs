using SrcCad.Core.Geometry;
namespace SrcCad.Core.Sketch;

public sealed record SketchTrimSpan(int EntityId,Point2d Start,Point2d End,double From,double To);
public sealed partial class SketchModel
{
    // Finite line/circle intersections; the hover plan is recalculated on commit.
    // Circle -> arc trimming is deliberately not represented as a fake line.
    public SketchTrimSpan? PreviewLineTrim(Point2d cursor,double tolerance)
    {
        var id=FindEntityNear(cursor,tolerance);
        if(Entity(id) is not SketchLine line)return null;
        var a=Point(line.StartPointId).Position;var b=Point(line.EndPointId).Position;
        var dx=b.X-a.X;var dy=b.Y-a.Y;var length2=dx*dx+dy*dy;
        if(length2<1e-18)return null;
        var at=((cursor.X-a.X)*dx+(cursor.Y-a.Y)*dy)/length2;
        var cuts=new List<double>{0,1};
        void Add(double t){if(t>1e-9&&t<1-1e-9&&cuts.All(v=>Math.Abs(v-t)>1e-9))cuts.Add(t);}
        foreach(var other in Lines().Where(l=>l.Id!=id))
        {
            var p=Point(other.StartPointId).Position;var q=Point(other.EndPointId).Position;
            var ex=q.X-p.X;var ey=q.Y-p.Y;var cross=dx*ey-dy*ex;
            if(Math.Abs(cross)<=1e-10*Math.Sqrt(length2*(ex*ex+ey*ey)))continue;
            var t=((p.X-a.X)*ey-(p.Y-a.Y)*ex)/cross;
            var u=((p.X-a.X)*dy-(p.Y-a.Y)*dx)/cross;
            if(u>=-1e-9&&u<=1+1e-9)Add(t);
        }
        foreach(var circle in Circles())
        {
            var c=Point(circle.CenterPointId).Position;var ox=a.X-c.X;var oy=a.Y-c.Y;
            var beta=2*(ox*dx+oy*dy);var gamma=ox*ox+oy*oy-circle.Radius*circle.Radius;
            var disc=beta*beta-4*length2*gamma;
            if(disc<0)continue;
            var root=Math.Sqrt(disc);Add((-beta-root)/(2*length2));Add((-beta+root)/(2*length2));
        }
        cuts.Sort();at=Math.Clamp(at,0,1);
        // At an intersection there is no unique segment under the cursor.
        if(cuts.Skip(1).SkipLast(1).Any(v=>Math.Abs(v-at)*Math.Sqrt(length2)<1e-7))return null;
        for(int i=0;i<cuts.Count-1;i++)
            if(at>=cuts[i]-1e-9&&at<=cuts[i+1]+1e-9)
                return new(id,new(a.X+cuts[i]*dx,a.Y+cuts[i]*dy),new(a.X+cuts[i+1]*dx,a.Y+cuts[i+1]*dy),cuts[i],cuts[i+1]);
        return null;
    }
    public bool TrimLineAt(Point2d cursor,double tolerance)
    {
        var plan=PreviewLineTrim(cursor,tolerance);
        if(plan is null||Entity(plan.EntityId) is not SketchLine line)return false;
        var before=CaptureSnapshot();
        try
        {
            var a=Point(line.StartPointId).Position;var b=Point(line.EndPointId).Position;
            DeleteEntity(line.Id); // Consumers of the removed entity are cleaned explicitly.
            void Keep(Point2d p,Point2d q)
            {
                var first=CreateOrSnapPoint(p,1e-8);var last=CreateOrSnapPoint(q,1e-8);
                AddLine(first,last,line.Construction);
            }
            if(plan.From>1e-9)Keep(a,plan.Start);
            if(plan.To<1-1e-9)Keep(plan.End,b);
            if(!ConstraintsSatisfied()){RestoreSnapshot(before);return false;}
            return true;
        }
        catch {RestoreSnapshot(before);throw;}
    }
}
