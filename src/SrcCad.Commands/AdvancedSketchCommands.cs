using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;

namespace SrcCad.Commands;

public sealed class PolygonSketchCommand : SketchCommandBase
{
    private readonly int sides;
    private Point2d? center;

    public PolygonSketchCommand(SketchModel sketch,int sides):base(sketch)
    {
        if(sides<3||sides>100)throw new ArgumentOutOfRangeException(nameof(sides));
        this.sides=sides;
    }

    public override string Id=>"sketch.polygon";
    public override string Title=>"Çokgen";
    public override bool HasPendingStep=>center.HasValue;

    public override void Begin(CommandContext context)
    {
        base.Begin(context);
        Context.SetStatus?.Invoke($"{sides} kenarlı çokgen: merkez noktasını seçin.");
    }

    private Point2d[] Vertices(Point2d edgePoint)
    {
        var c=center!.Value;
        var radius=Point2d.Distance(c,edgePoint);
        if(radius<=1e-9)return Array.Empty<Point2d>();
        var start=Math.Atan2(edgePoint.Y-c.Y,edgePoint.X-c.X);
        return Enumerable.Range(0,sides)
            .Select(i=>new Point2d(c.X+Math.Cos(start+Math.PI*2*i/sides)*radius,
                                  c.Y+Math.Sin(start+Math.PI*2*i/sides)*radius))
            .ToArray();
    }

    public override void Pick(SketchPick pick)
    {
        if(!center.HasValue)
        {
            center=pick.Point;
            Context.SetStatus?.Invoke("Çokgen merkezi seçildi; çevre noktasını seçin.");
            return;
        }

        var vertices=Vertices(pick.Point);
        if(vertices.Length!=sides){Context.SetStatus?.Invoke("Çokgen yarıçapı sıfır olamaz.");return;}
        var ids=vertices.Select(p=>Sketch.CreateOrSnapPoint(p,pick.SnapTolerance)).ToArray();
        for(var i=0;i<sides;i++)
        {
            var a=ids[i];var b=ids[(i+1)%sides];
            if(a==b)throw new InvalidOperationException("Çokgen köşeleri çakıştı.");
            Sketch.AddLine(a,b);
        }
        center=null;
        Changed($"{sides} kenarlı kapalı çokgen oluşturuldu.");
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if(!center.HasValue)return Array.Empty<SketchPreviewPrimitive>();
        var vertices=Vertices(cursor);
        if(vertices.Length!=sides)return Array.Empty<SketchPreviewPrimitive>();
        return Enumerable.Range(0,sides)
            .Select(i=>(SketchPreviewPrimitive)new SketchPreviewLine(vertices[i],vertices[(i+1)%sides]))
            .ToArray();
    }

    public override void CancelStep()=>center=null;
}


public sealed class CenterArcSketchCommand : SketchCommandBase
{
    private Point2d? center;
    private Point2d? start;

    public CenterArcSketchCommand(SketchModel sketch):base(sketch){}
    public override string Id=>"sketch.arc.center";
    public override string Title=>"Merkez-Yay";
    public override bool HasPendingStep=>center.HasValue;

    public override void Begin(CommandContext context)
    {
        base.Begin(context);
        Context.SetStatus?.Invoke("Yay: merkez noktasını seçin.");
    }

    public override void Pick(SketchPick pick)
    {
        if(!center.HasValue)
        {
            center=pick.Point;Context.SetStatus?.Invoke("Yay merkezi seçildi; başlangıç noktasını seçin.");return;
        }
        if(!start.HasValue)
        {
            if(Point2d.Distance(center.Value,pick.Point)<=1e-9){Context.SetStatus?.Invoke("Yay yarıçapı sıfır olamaz.");return;}
            start=pick.Point;Context.SetStatus?.Invoke("Yay başlangıcı seçildi; bitiş yönünü seçin.");return;
        }

        var radius=Point2d.Distance(center.Value,start.Value);
        var dx=pick.Point.X-center.Value.X;var dy=pick.Point.Y-center.Value.Y;
        var length=Math.Sqrt(dx*dx+dy*dy);
        if(length<=1e-9){Context.SetStatus?.Invoke("Yay bitiş yönü geçersiz.");return;}
        var end=new Point2d(center.Value.X+dx/length*radius,center.Value.Y+dy/length*radius);
        var cId=Sketch.CreateOrSnapPoint(center.Value,pick.SnapTolerance);
        var sId=Sketch.CreateOrSnapPoint(start.Value,pick.SnapTolerance);
        var eId=Sketch.CreateOrSnapPoint(end,pick.SnapTolerance);
        if(sId==eId){Context.SetStatus?.Invoke("Tam daire için Daire komutunu kullanın.");return;}
        Sketch.AddArc(cId,sId,eId,false);
        center=null;start=null;Changed("Parametrik merkez-yay oluşturuldu.");
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if(!center.HasValue)return Array.Empty<SketchPreviewPrimitive>();
        if(!start.HasValue)return new SketchPreviewPrimitive[]{new SketchPreviewLine(center.Value,cursor)};
        var radius=Point2d.Distance(center.Value,start.Value);
        var dx=cursor.X-center.Value.X;var dy=cursor.Y-center.Value.Y;var length=Math.Sqrt(dx*dx+dy*dy);
        if(length<=1e-9)return Array.Empty<SketchPreviewPrimitive>();
        var end=new Point2d(center.Value.X+dx/length*radius,center.Value.Y+dy/length*radius);
        var a0=Math.Atan2(start.Value.Y-center.Value.Y,start.Value.X-center.Value.X);
        var a1=Math.Atan2(end.Y-center.Value.Y,end.X-center.Value.X);
        while(a1<a0)a1+=Math.PI*2;
        const int n=24;
        var points=Enumerable.Range(0,n+1).Select(i=>new Point2d(
            center.Value.X+Math.Cos(a0+(a1-a0)*i/n)*radius,
            center.Value.Y+Math.Sin(a0+(a1-a0)*i/n)*radius)).ToArray();
        return Enumerable.Range(0,n).Select(i=>(SketchPreviewPrimitive)new SketchPreviewLine(points[i],points[i+1])).ToArray();
    }

    public override void CancelStep(){center=null;start=null;}
}

public sealed class StraightSlotSketchCommand : SketchCommandBase
{
    private Point2d? a;
    private Point2d? b;

    public StraightSlotSketchCommand(SketchModel sketch):base(sketch){}
    public override string Id=>"sketch.slot.straight";
    public override string Title=>"Düz Slot";
    public override bool HasPendingStep=>a.HasValue;

    public override void Begin(CommandContext context)
    {
        base.Begin(context);Context.SetStatus?.Invoke("Slot: merkez çizgisinin ilk noktasını seçin.");
    }

    private static bool Geometry(Point2d a,Point2d b,Point2d widthPoint,out Point2d aL,out Point2d bL,out Point2d bR,out Point2d aR,out double radius)
    {
        aL=bL=bR=aR=default;radius=0;
        var dx=b.X-a.X;var dy=b.Y-a.Y;var length=Math.Sqrt(dx*dx+dy*dy);
        if(length<=1e-9)return false;
        var nx=-dy/length;var ny=dx/length;
        var signed=(widthPoint.X-a.X)*nx+(widthPoint.Y-a.Y)*ny;
        radius=Math.Abs(signed);
        if(radius<=1e-9)return false;
        var sign=signed>=0?1.0:-1.0;nx*=sign;ny*=sign;
        aL=new Point2d(a.X+nx*radius,a.Y+ny*radius);
        bL=new Point2d(b.X+nx*radius,b.Y+ny*radius);
        bR=new Point2d(b.X-nx*radius,b.Y-ny*radius);
        aR=new Point2d(a.X-nx*radius,a.Y-ny*radius);
        return true;
    }

    public override void Pick(SketchPick pick)
    {
        if(!a.HasValue){a=pick.Point;Context.SetStatus?.Invoke("Slot: merkez çizgisinin ikinci noktasını seçin.");return;}
        if(!b.HasValue)
        {
            if(Point2d.Distance(a.Value,pick.Point)<=1e-9){Context.SetStatus?.Invoke("Slot uzunluğu sıfır olamaz.");return;}
            b=pick.Point;Context.SetStatus?.Invoke("Slot genişliğini belirlemek için üçüncü noktayı seçin.");return;
        }
        if(!Geometry(a.Value,b.Value,pick.Point,out var aL,out var bL,out var bR,out var aR,out _))
        {Context.SetStatus?.Invoke("Slot genişliği sıfır olamaz.");return;}

        var ca=Sketch.CreateOrSnapPoint(a.Value,pick.SnapTolerance);
        var cb=Sketch.CreateOrSnapPoint(b.Value,pick.SnapTolerance);
        var pAL=Sketch.CreateOrSnapPoint(aL,pick.SnapTolerance);
        var pBL=Sketch.CreateOrSnapPoint(bL,pick.SnapTolerance);
        var pBR=Sketch.CreateOrSnapPoint(bR,pick.SnapTolerance);
        var pAR=Sketch.CreateOrSnapPoint(aR,pick.SnapTolerance);
        Sketch.AddLine(pAL,pBL);
        Sketch.AddArc(cb,pBL,pBR,true);
        Sketch.AddLine(pBR,pAR);
        Sketch.AddArc(ca,pAR,pAL,true);
        a=null;b=null;Changed("Kapalı parametrik düz slot oluşturuldu.");
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if(!a.HasValue)return Array.Empty<SketchPreviewPrimitive>();
        if(!b.HasValue)return new SketchPreviewPrimitive[]{new SketchPreviewLine(a.Value,cursor)};
        if(!Geometry(a.Value,b.Value,cursor,out var aL,out var bL,out var bR,out var aR,out var r))
            return Array.Empty<SketchPreviewPrimitive>();
        var list=new List<SketchPreviewPrimitive>{new SketchPreviewLine(aL,bL),new SketchPreviewLine(bR,aR)};
        void Arc(Point2d c,Point2d p0,Point2d p1,bool clockwise)
        {
            var s=Math.Atan2(p0.Y-c.Y,p0.X-c.X);var e=Math.Atan2(p1.Y-c.Y,p1.X-c.X);
            double span;
            if(clockwise){while(e>s)e-=Math.PI*2;span=e-s;}
            else{while(e<s)e+=Math.PI*2;span=e-s;}
            Point2d Prev(double t)=>new(c.X+Math.Cos(t)*r,c.Y+Math.Sin(t)*r);
            var prev=Prev(s);
            for(var i=1;i<=16;i++){var cur=Prev(s+span*i/16);list.Add(new SketchPreviewLine(prev,cur));prev=cur;}
        }
        Arc(b.Value,bL,bR,true);Arc(a.Value,aR,aL,true);
        return list;
    }

    public override void CancelStep(){a=null;b=null;}
}


public sealed class EllipseSketchCommand : SketchCommandBase
{
    private Point2d? center;
    private Point2d? majorPoint;

    public EllipseSketchCommand(SketchModel sketch):base(sketch){}
    public override string Id=>"sketch.ellipse";
    public override string Title=>"Elips";
    public override bool HasPendingStep=>center.HasValue;

    public override void Begin(CommandContext context)
    {
        base.Begin(context);Context.SetStatus?.Invoke("Elips: merkez noktasını seçin.");
    }

    public override void Pick(SketchPick pick)
    {
        if(!center.HasValue){center=pick.Point;Context.SetStatus?.Invoke("Elips: ana eksen ucunu seçin.");return;}
        if(!majorPoint.HasValue)
        {
            if(Point2d.Distance(center.Value,pick.Point)<=1e-9){Context.SetStatus?.Invoke("Ana yarı eksen sıfır olamaz.");return;}
            majorPoint=pick.Point;Context.SetStatus?.Invoke("Elips: ikinci yarı ekseni belirleyin.");return;
        }

        var dx=majorPoint.Value.X-center.Value.X;var dy=majorPoint.Value.Y-center.Value.Y;
        var rx=Math.Sqrt(dx*dx+dy*dy);var rotation=Math.Atan2(dy,dx);
        var nx=-Math.Sin(rotation);var ny=Math.Cos(rotation);
        var ry=Math.Abs((pick.Point.X-center.Value.X)*nx+(pick.Point.Y-center.Value.Y)*ny);
        if(ry<=1e-9){Context.SetStatus?.Invoke("İkinci yarı eksen sıfır olamaz.");return;}
        var cId=Sketch.CreateOrSnapPoint(center.Value,pick.SnapTolerance);
        Sketch.AddEllipse(cId,rx,ry,rotation);
        center=null;majorPoint=null;Changed("Parametrik elips oluşturuldu.");
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if(!center.HasValue)return Array.Empty<SketchPreviewPrimitive>();
        if(!majorPoint.HasValue)return new SketchPreviewPrimitive[]{new SketchPreviewLine(center.Value,cursor)};
        var dx=majorPoint.Value.X-center.Value.X;var dy=majorPoint.Value.Y-center.Value.Y;
        var rx=Math.Sqrt(dx*dx+dy*dy);if(rx<=1e-9)return Array.Empty<SketchPreviewPrimitive>();
        var rotation=Math.Atan2(dy,dx);var nx=-Math.Sin(rotation);var ny=Math.Cos(rotation);
        var ry=Math.Abs((cursor.X-center.Value.X)*nx+(cursor.Y-center.Value.Y)*ny);
        if(ry<=1e-9)return Array.Empty<SketchPreviewPrimitive>();
        const int n=48;
        var cos=Math.Cos(rotation);var sin=Math.Sin(rotation);
        var pts=Enumerable.Range(0,n).Select(i=>
        {
            var a=Math.PI*2*i/n;var x=Math.Cos(a)*rx;var y=Math.Sin(a)*ry;
            return new Point2d(center.Value.X+x*cos-y*sin,center.Value.Y+x*sin+y*cos);
        }).ToArray();
        return Enumerable.Range(0,n).Select(i=>(SketchPreviewPrimitive)new SketchPreviewLine(pts[i],pts[(i+1)%n])).ToArray();
    }

    public override void CancelStep(){center=null;majorPoint=null;}
}
