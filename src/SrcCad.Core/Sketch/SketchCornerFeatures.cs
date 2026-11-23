using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

public sealed partial class SketchModel
{
    private sealed record CornerLineInfo(SketchLine Line,int CommonPointId,int OuterPointId,bool CommonIsStart);

    private (Point2d Corner,CornerLineInfo First,CornerLineInfo Second,Point2d U1,Point2d U2,double L1,double L2)
        ResolveLineCorner(int firstLineId,int secondLineId)
    {
        if(firstLineId==secondLineId||Entity(firstLineId) is not SketchLine first||Entity(secondLineId) is not SketchLine second)
            throw new InvalidOperationException("İki farklı çizgi seçin.");

        var shared=new[]{first.StartPointId,first.EndPointId}.Intersect(new[]{second.StartPointId,second.EndPointId}).Distinct().ToArray();
        if(shared.Length!=1)throw new InvalidOperationException("Radyus/Pah için ortak köşesi olan iki çizgi seçin.");
        var common=shared[0];
        var firstOuter=first.StartPointId==common?first.EndPointId:first.StartPointId;
        var secondOuter=second.StartPointId==common?second.EndPointId:second.StartPointId;
        var corner=Point(common).Position;
        var p1=Point(firstOuter).Position;var p2=Point(secondOuter).Position;
        var v1=new Point2d(p1.X-corner.X,p1.Y-corner.Y);
        var v2=new Point2d(p2.X-corner.X,p2.Y-corner.Y);
        var l1=Math.Sqrt(v1.X*v1.X+v1.Y*v1.Y);var l2=Math.Sqrt(v2.X*v2.X+v2.Y*v2.Y);
        if(l1<=1e-9||l2<=1e-9)throw new InvalidOperationException("Sıfır uzunluklu köşe çizgisi.");
        var u1=new Point2d(v1.X/l1,v1.Y/l1);var u2=new Point2d(v2.X/l2,v2.Y/l2);
        var dot=Math.Clamp(u1.X*u2.X+u1.Y*u2.Y,-1.0,1.0);
        var angle=Math.Acos(dot);
        if(angle<=1e-6||Math.PI-angle<=1e-6)throw new InvalidOperationException("Paralel/doğrusal çizgilerde köşe özelliği oluşturulamaz.");
        return (corner,
            new CornerLineInfo(first,common,firstOuter,first.StartPointId==common),
            new CornerLineInfo(second,common,secondOuter,second.StartPointId==common),
            u1,u2,l1,l2);
    }

    private void ReplaceCornerEndpoint(CornerLineInfo info,int newPointId)
    {
        var index=Entities.FindIndex(e=>e.Id==info.Line.Id);
        if(index<0)throw new InvalidOperationException("Çizgi bulunamadı.");
        Entities[index]=info.CommonIsStart
            ?info.Line with{StartPointId=newPointId}
            :info.Line with{EndPointId=newPointId};
    }

    public int AddSketchChamfer(int firstLineId,int secondLineId,double distance)
    {
        if(!double.IsFinite(distance)||distance<=1e-9)throw new ArgumentException("Pah mesafesi pozitif olmalı.");
        var before=CaptureSnapshot();
        try
        {
            var c=ResolveLineCorner(firstLineId,secondLineId);
            if(distance>=c.L1-1e-9||distance>=c.L2-1e-9)
                throw new InvalidOperationException("Pah mesafesi çizgi uzunluğundan küçük olmalı.");
            var q1=new Point2d(c.Corner.X+c.U1.X*distance,c.Corner.Y+c.U1.Y*distance);
            var q2=new Point2d(c.Corner.X+c.U2.X*distance,c.Corner.Y+c.U2.Y*distance);
            var p1=CreatePointExact(q1);var p2=CreatePointExact(q2);
            ReplaceCornerEndpoint(c.First,p1);ReplaceCornerEndpoint(c.Second,p2);
            var connector=AddLine(p1,p2);
            RemoveUnusedPoints();
            Revision++;
            return connector.Id;
        }
        catch
        {
            RestoreSnapshot(before);throw;
        }
    }

    public int AddSketchFillet(int firstLineId,int secondLineId,double radius)
    {
        if(!double.IsFinite(radius)||radius<=1e-9)throw new ArgumentException("Radyus pozitif olmalı.");
        var before=CaptureSnapshot();
        try
        {
            var c=ResolveLineCorner(firstLineId,secondLineId);
            var dot=Math.Clamp(c.U1.X*c.U2.X+c.U1.Y*c.U2.Y,-1.0,1.0);
            var angle=Math.Acos(dot);
            var tangent=radius/Math.Tan(angle*0.5);
            if(!double.IsFinite(tangent)||tangent<=1e-9||tangent>=c.L1-1e-9||tangent>=c.L2-1e-9)
                throw new InvalidOperationException("Radyus bu köşe için fazla büyük.");

            var q1=new Point2d(c.Corner.X+c.U1.X*tangent,c.Corner.Y+c.U1.Y*tangent);
            var q2=new Point2d(c.Corner.X+c.U2.X*tangent,c.Corner.Y+c.U2.Y*tangent);
            var bisector=new Point2d(c.U1.X+c.U2.X,c.U1.Y+c.U2.Y);
            var bisectorLength=Math.Sqrt(bisector.X*bisector.X+bisector.Y*bisector.Y);
            if(bisectorLength<=1e-9)throw new InvalidOperationException("Radyus açıortayı oluşturulamadı.");
            var centerDistance=radius/Math.Sin(angle*0.5);
            var center=new Point2d(c.Corner.X+bisector.X/bisectorLength*centerDistance,
                                   c.Corner.Y+bisector.Y/bisectorLength*centerDistance);

            var p1=CreatePointExact(q1);var p2=CreatePointExact(q2);var pc=CreatePointExact(center);
            ReplaceCornerEndpoint(c.First,p1);ReplaceCornerEndpoint(c.Second,p2);

            var r1=new Point2d(q1.X-center.X,q1.Y-center.Y);
            var r2=new Point2d(q2.X-center.X,q2.Y-center.Y);
            var cross=r1.X*r2.Y-r1.Y*r2.X;
            var arc=AddArc(pc,p1,p2,cross<0);
            RemoveUnusedPoints();
            Revision++;
            return arc.Id;
        }
        catch
        {
            RestoreSnapshot(before);throw;
        }
    }
}
