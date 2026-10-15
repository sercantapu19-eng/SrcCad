using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

public sealed partial class SketchModel
{
    public static (Point2d Center,double Radius) CircleThrough(Point2d a,Point2d b,Point2d c)
    {
        ValidatePoint(a);ValidatePoint(b);ValidatePoint(c);
        // Translate to a before squaring to avoid cancellation at large offsets.
        var bx=b.X-a.X;var by=b.Y-a.Y;var cx=c.X-a.X;var cy=c.Y-a.Y;
        var cross=bx*cy-by*cx;
        var scale=Math.Max(bx*bx+by*by,cx*cx+cy*cy);
        if(scale<1e-18 || Math.Abs(cross)<=1e-10*scale)throw new InvalidOperationException("Üç nokta farklı ve doğrusal olmayan konumlarda olmalı.");
        var b2=bx*bx+by*by;var c2=cx*cx+cy*cy;
        var center=new Point2d(a.X+(b2*cy-c2*by)/(2*cross),a.Y+(bx*c2-cx*b2)/(2*cross));
        var radius=Point2d.Distance(a,center);
        ValidatePoint(center);
        if(!double.IsFinite(radius)||radius<=1e-9)throw new InvalidOperationException("Geçersiz daire.");
        return(center,radius);
    }
    public IReadOnlyList<int> AddThreePointRectangle(Point2d a,Point2d b,Point2d heightPoint,double tolerance)
    {
        ValidatePoint(a);ValidatePoint(b);ValidatePoint(heightPoint);
        var dx=b.X-a.X;var dy=b.Y-a.Y;var length=Point2d.Distance(a,b);
        if(length<=1e-9)throw new InvalidOperationException("Dikdörtgen tabanı sıfır olamaz.");
        var height=(dx*(heightPoint.Y-a.Y)-dy*(heightPoint.X-a.X))/length;
        if(Math.Abs(height)<=1e-9)throw new InvalidOperationException("Dikdörtgen yüksekliği sıfır olamaz.");
        var vx=-dy/length*height;var vy=dx/length*height;
        var before=CaptureSnapshot();
        try
        {
            var ids=new[]{a,b,new Point2d(b.X+vx,b.Y+vy),new Point2d(a.X+vx,a.Y+vy)}.Select(p=>CreateOrSnapPoint(p,tolerance)).ToArray();
            var lines=Enumerable.Range(0,4).Select(i=>AddLine(ids[i],ids[(i+1)%4])).ToArray();
            Relations.Add(new(_nextRelationId++,SketchRelationType.Parallel,lines[0].Id,lines[2].Id));
            Relations.Add(new(_nextRelationId++,SketchRelationType.Parallel,lines[1].Id,lines[3].Id));
            Relations.Add(new(_nextRelationId++,SketchRelationType.Perpendicular,lines[0].Id,lines[1].Id));
            if(!SolveNumerically()||!ConstraintsSatisfied())throw new InvalidOperationException("Dikdörtgen mevcut kısıtlarla çakışıyor.");
            return lines.Select(l=>l.Id).ToArray();
        }
        catch {RestoreSnapshot(before);throw;}
    }
}
