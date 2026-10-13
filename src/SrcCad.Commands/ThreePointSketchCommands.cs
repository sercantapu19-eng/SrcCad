using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;

namespace SrcCad.Commands;

public sealed class ThreePointCircleCommand : SketchCommandBase
{
    private readonly List<SketchPick> picks=new();
    public ThreePointCircleCommand(SketchModel model):base(model){}
    public override string Id=>"sketch.circle.threepoint";
    public override string Title=>"3 Nokta Dairesi";
    public override bool HasPendingStep=>picks.Count>0;
    public override void CancelStep()=>picks.Clear();
    public override void Pick(SketchPick pick)
    {
        picks.Add(pick);
        if(picks.Count<3){Context.SetStatus?.Invoke($"Daire: {picks.Count+1}. noktayı seçin.");return;}
        var before=Sketch.CaptureSnapshot();
        try
        {
            var geometry=SketchModel.CircleThrough(picks[0].Point,picks[1].Point,picks[2].Point);
            var circle=Sketch.AddCircle(Sketch.CreatePointExact(geometry.Center),geometry.Radius);
            foreach(var p in picks.Where(p=>p.SnappedPointId>=0).DistinctBy(p=>p.SnappedPointId))
                if(!Sketch.ApplyReferenceRelation(SketchRelationType.Coincident,new[]{Sketch.ReferenceForPoint(p.SnappedPointId),Sketch.ReferenceForEntity(circle.Id)}))
                    throw new InvalidOperationException("Daire seçili referanslarla çakışıyor.");
            picks.Clear();Changed("3 nokta dairesi oluşturuldu; yakalanan nokta ilişkileri korundu.");
        }
        catch{Sketch.RestoreSnapshot(before);picks.Clear();throw;}
    }
    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if(picks.Count!=2)return Array.Empty<SketchPreviewPrimitive>();
        try{var c=SketchModel.CircleThrough(picks[0].Point,picks[1].Point,cursor);return new SketchPreviewPrimitive[]{new SketchPreviewCircle(c.Center,c.Radius)};}
        catch(InvalidOperationException){return Array.Empty<SketchPreviewPrimitive>();}
    }
}

public sealed class ThreePointArcCommand : SketchCommandBase
{
    private readonly List<SketchPick> picks=new();
    public ThreePointArcCommand(SketchModel model):base(model){}
    public override string Id=>"sketch.arc.threepoint";
    public override string Title=>"3 Nokta Yay";
    public override bool HasPendingStep=>picks.Count>0;
    public override void CancelStep()=>picks.Clear();

    private Point2d PickPoint(SketchPick pick)
        => pick.SnappedPointId>=0?Sketch.Point(pick.SnappedPointId).Position:pick.Point;

    private static double Positive(double angle)
    {
        while(angle<0)angle+=Math.PI*2;
        while(angle>=Math.PI*2)angle-=Math.PI*2;
        return angle;
    }

    private static bool Clockwise(Point2d center,Point2d start,Point2d through,Point2d end)
    {
        var a0=Math.Atan2(start.Y-center.Y,start.X-center.X);
        var am=Math.Atan2(through.Y-center.Y,through.X-center.X);
        var a1=Math.Atan2(end.Y-center.Y,end.X-center.X);
        var ccwSpan=Positive(a1-a0);
        var ccwMid=Positive(am-a0);
        return ccwMid>ccwSpan;
    }

    public override void Begin(CommandContext context)
    {
        base.Begin(context);
        Context.SetStatus?.Invoke("3 Nokta Yay: başlangıç noktasını seçin.");
    }

    public override void Pick(SketchPick pick)
    {
        picks.Add(pick);
        if(picks.Count==1){Context.SetStatus?.Invoke("Yayın üzerinden geçeceği ikinci noktayı seçin.");return;}
        if(picks.Count==2){Context.SetStatus?.Invoke("Yay bitiş noktasını seçin.");return;}

        var before=Sketch.CaptureSnapshot();
        try
        {
            var start=PickPoint(picks[0]);
            var through=PickPoint(picks[1]);
            var end=PickPoint(picks[2]);
            var circle=SketchModel.CircleThrough(start,through,end);
            var centerId=Sketch.CreatePointExact(circle.Center);
            var startId=picks[0].SnappedPointId>=0?picks[0].SnappedPointId:Sketch.CreateOrSnapPoint(start,pick.SnapTolerance);
            var endId=picks[2].SnappedPointId>=0?picks[2].SnappedPointId:Sketch.CreateOrSnapPoint(end,pick.SnapTolerance);
            if(startId==endId)throw new InvalidOperationException("Yay başlangıç ve bitiş noktaları farklı olmalı.");
            Sketch.AddArc(centerId,startId,endId,Clockwise(circle.Center,start,through,end));
            picks.Clear();
            Changed("3 nokta yayı oluşturuldu.");
        }
        catch
        {
            Sketch.RestoreSnapshot(before);
            picks.Clear();
            throw;
        }
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if(picks.Count==0)return Array.Empty<SketchPreviewPrimitive>();
        if(picks.Count==1)return new SketchPreviewPrimitive[]{new SketchPreviewLine(PickPoint(picks[0]),cursor)};
        try
        {
            var start=PickPoint(picks[0]);
            var through=PickPoint(picks[1]);
            var circle=SketchModel.CircleThrough(start,through,cursor);
            var clockwise=Clockwise(circle.Center,start,through,cursor);
            var a0=Math.Atan2(start.Y-circle.Center.Y,start.X-circle.Center.X);
            var a1=Math.Atan2(cursor.Y-circle.Center.Y,cursor.X-circle.Center.X);
            double span;
            if(clockwise){while(a1>a0)a1-=Math.PI*2;span=a1-a0;}
            else{while(a1<a0)a1+=Math.PI*2;span=a1-a0;}
            const int n=24;
            var points=Enumerable.Range(0,n+1).Select(i=>new Point2d(
                circle.Center.X+Math.Cos(a0+span*i/n)*circle.Radius,
                circle.Center.Y+Math.Sin(a0+span*i/n)*circle.Radius)).ToArray();
            return Enumerable.Range(0,n)
                .Select(i=>(SketchPreviewPrimitive)new SketchPreviewLine(points[i],points[i+1]))
                .ToArray();
        }
        catch(InvalidOperationException){return Array.Empty<SketchPreviewPrimitive>();}
    }
}

public sealed class ThreePointRectangleCommand : SketchCommandBase
{
    private readonly List<SketchPick> picks=new();
    public ThreePointRectangleCommand(SketchModel model):base(model){}
    public override string Id=>"sketch.rectangle.threepoint";
    public override string Title=>"3 Noktalı Köşe Dikdörtgen";
    public override bool HasPendingStep=>picks.Count>0;
    public override void CancelStep()=>picks.Clear();
    public override void Pick(SketchPick pick)
    {
        picks.Add(pick);
        if(picks.Count<3){Context.SetStatus?.Invoke($"Dikdörtgen: {picks.Count+1}. noktayı seçin.");return;}
        try{Sketch.AddThreePointRectangle(picks[0].Point,picks[1].Point,picks[2].Point,pick.SnapTolerance);Changed("3 noktalı dikdörtgen oluşturuldu; paralellik/diklik ilişkileri aktif.");}
        finally{picks.Clear();}
    }
    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if(picks.Count==0)return Array.Empty<SketchPreviewPrimitive>();
        if(picks.Count==1)return new SketchPreviewPrimitive[]{new SketchPreviewLine(picks[0].Point,cursor)};
        // Preview uses the same model construction algorithm in an isolated candidate.
        var candidate=new SketchModel(Sketch.Plane);
        try
        {
            candidate.AddThreePointRectangle(picks[0].Point,picks[1].Point,cursor,1e-9);
            return candidate.Lines().Select(l=>(SketchPreviewPrimitive)new SketchPreviewLine(candidate.Point(l.StartPointId).Position,candidate.Point(l.EndPointId).Position)).ToArray();
        }
        catch(InvalidOperationException){return Array.Empty<SketchPreviewPrimitive>();}
    }
}
