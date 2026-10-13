using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

public sealed partial class SketchModel
{
    public IReadOnlyList<int> OffsetEntities(IEnumerable<int> entityIds,double distance)
    {
        if(!double.IsFinite(distance)||Math.Abs(distance)<=1e-9)throw new ArgumentException("Ofset mesafesi sıfır olamaz.");
        var result=new List<int>();
        foreach(var id in entityIds.Distinct().ToArray())
        {
            switch(Entity(id))
            {
                case SketchLine line:
                {
                    var a=Point(line.StartPointId).Position;var b=Point(line.EndPointId).Position;
                    var dx=b.X-a.X;var dy=b.Y-a.Y;var length=Math.Sqrt(dx*dx+dy*dy);
                    if(length<=1e-9)continue;
                    var ox=-dy/length*distance;var oy=dx/length*distance;
                    var p0=CreatePointExact(new Point2d(a.X+ox,a.Y+oy));
                    var p1=CreatePointExact(new Point2d(b.X+ox,b.Y+oy));
                    result.Add(AddLine(p0,p1,line.Construction).Id);
                    break;
                }
                case SketchCircle circle:
                {
                    var radius=circle.Radius+distance;
                    if(radius<=1e-9)throw new InvalidOperationException("Ofset daire yarıçapını sıfır/negatif yapıyor.");
                    var center=Point(circle.CenterPointId).Position;
                    var centerId=CreatePointExact(center);
                    result.Add(AddCircle(centerId,radius,circle.Construction).Id);
                    break;
                }
            }
        }
        if(result.Count==0)throw new InvalidOperationException("Ofset için çizgi veya daire seçin.");
        return result;
    }

    private static Point2d MirrorPoint(Point2d p,Point2d a,Point2d b)
    {
        var dx=b.X-a.X;var dy=b.Y-a.Y;var len2=dx*dx+dy*dy;
        if(len2<=1e-18)throw new InvalidOperationException("Ayna ekseni sıfır uzunlukta.");
        var t=((p.X-a.X)*dx+(p.Y-a.Y)*dy)/len2;
        var q=new Point2d(a.X+t*dx,a.Y+t*dy);
        return new Point2d(2*q.X-p.X,2*q.Y-p.Y);
    }

    public IReadOnlyList<int> MirrorEntities(IEnumerable<int> entityIds,int axisLineId)
    {
        if(Entity(axisLineId) is not SketchLine axis)throw new ArgumentException("Ayna ekseni çizgi olmalı.");
        var a=Point(axis.StartPointId).Position;var b=Point(axis.EndPointId).Position;
        var result=new List<int>();
        foreach(var id in entityIds.Distinct().Where(id=>id!=axisLineId).ToArray())
        {
            switch(Entity(id))
            {
                case SketchLine line:
                {
                    var p0=CreatePointExact(MirrorPoint(Point(line.StartPointId).Position,a,b));
                    var p1=CreatePointExact(MirrorPoint(Point(line.EndPointId).Position,a,b));
                    result.Add(AddLine(p0,p1,line.Construction).Id);break;
                }
                case SketchCircle circle:
                {
                    var center=CreatePointExact(MirrorPoint(Point(circle.CenterPointId).Position,a,b));
                    result.Add(AddCircle(center,circle.Radius,circle.Construction).Id);break;
                }
            }
        }
        if(result.Count==0)throw new InvalidOperationException("Aynalanacak çizgi/daire seçin.");
        return result;
    }

    private int CopyEntityTranslated(SketchEntity entity,double dx,double dy)
    {
        return entity switch
        {
            SketchLine line=>AddLine(
                CreatePointExact(new Point2d(Point(line.StartPointId).Position.X+dx,Point(line.StartPointId).Position.Y+dy)),
                CreatePointExact(new Point2d(Point(line.EndPointId).Position.X+dx,Point(line.EndPointId).Position.Y+dy)),
                line.Construction).Id,
            SketchCircle circle=>AddCircle(
                CreatePointExact(new Point2d(Point(circle.CenterPointId).Position.X+dx,Point(circle.CenterPointId).Position.Y+dy)),
                circle.Radius,circle.Construction).Id,
            _=>throw new NotSupportedException("Bu çizim öğesi henüz dizilim kopyasına uygun değil.")
        };
    }

    public IReadOnlyList<int> LinearPatternEntities(IEnumerable<int> entityIds,double directionX,double directionY,double spacing,int count)
    {
        if(!double.IsFinite(spacing)||spacing<=1e-9||count<2||count>500)throw new ArgumentException("Geçersiz doğrusal dizilim.");
        var length=Math.Sqrt(directionX*directionX+directionY*directionY);
        if(!double.IsFinite(length)||length<=1e-12)throw new ArgumentException("Dizilim yönü sıfır olamaz.");
        var ux=directionX/length;var uy=directionY/length;
        var source=entityIds.Distinct().Select(Entity).Where(e=>e is SketchLine or SketchCircle).Cast<SketchEntity>().ToArray();
        if(source.Length==0)throw new InvalidOperationException("Dizilim için çizgi/daire seçin.");
        var result=new List<int>();
        for(var i=1;i<count;i++)
            foreach(var entity in source)result.Add(CopyEntityTranslated(entity,ux*spacing*i,uy*spacing*i));
        return result;
    }

    private int CopyEntityRotated(SketchEntity entity,Point2d center,double radians)
    {
        Point2d Rotate(Point2d p)
        {
            var x=p.X-center.X;var y=p.Y-center.Y;var c=Math.Cos(radians);var s=Math.Sin(radians);
            return new Point2d(center.X+x*c-y*s,center.Y+x*s+y*c);
        }
        return entity switch
        {
            SketchLine line=>AddLine(
                CreatePointExact(Rotate(Point(line.StartPointId).Position)),
                CreatePointExact(Rotate(Point(line.EndPointId).Position)),
                line.Construction).Id,
            SketchCircle circle=>AddCircle(CreatePointExact(Rotate(Point(circle.CenterPointId).Position)),circle.Radius,circle.Construction).Id,
            _=>throw new NotSupportedException("Bu çizim öğesi henüz dairesel dizilime uygun değil.")
        };
    }

    public IReadOnlyList<int> CircularPatternEntities(IEnumerable<int> entityIds,Point2d center,double angleDegrees,int count)
    {
        if(!double.IsFinite(angleDegrees)||angleDegrees<=1e-9||angleDegrees>360||count<2||count>500)
            throw new ArgumentException("Geçersiz dairesel dizilim.");
        var source=entityIds.Distinct().Select(Entity).Where(e=>e is SketchLine or SketchCircle).Cast<SketchEntity>().ToArray();
        if(source.Length==0)throw new InvalidOperationException("Dizilim için çizgi/daire seçin.");
        var total=angleDegrees*Math.PI/180.0;
        var step=Math.Abs(angleDegrees-360)<1e-9?total/count:total/(count-1);
        var result=new List<int>();
        for(var i=1;i<count;i++)
            foreach(var entity in source)result.Add(CopyEntityRotated(entity,center,step*i));
        return result;
    }
}
