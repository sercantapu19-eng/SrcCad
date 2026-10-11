using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
namespace SrcCad.Commands;
public sealed class TrimSketchCommand : SketchCommandBase
{
    public double Tolerance {get;set;}
    public TrimSketchCommand(SketchModel sketch,double tolerance):base(sketch){Tolerance=tolerance;}
    public override string Id=>"sketch.trim.line";
    public override string Title=>"Çizgi Kırp";
    public override bool HasPendingStep=>false;
    public override void CancelStep() { }
    public override void Pick(SketchPick pick)
    {
        if(Sketch.TrimLineAt(pick.Point,pick.SnapTolerance))Changed("Çizgi kırpıldı; kaldırılan çizginin bağımlı ölçü/ilişkileri temizlendi. Ctrl+Z ile geri alınabilir.");
        else Context.SetStatus?.Invoke("Kırp: iki kesişim arasındaki çizgi parçasını seçin. Daire/yay kırpma henüz desteklenmiyor.");
    }
    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        var plan=Sketch.PreviewLineTrim(cursor,Tolerance);
        return plan is null?Array.Empty<SketchPreviewPrimitive>():new SketchPreviewPrimitive[]{new SketchPreviewLine(plan.Start,plan.End)};
    }
}
