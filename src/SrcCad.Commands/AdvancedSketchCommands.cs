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
