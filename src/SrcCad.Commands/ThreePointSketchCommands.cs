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
