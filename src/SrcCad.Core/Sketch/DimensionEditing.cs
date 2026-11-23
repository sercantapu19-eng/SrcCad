using System.Globalization;
namespace SrcCad.Core.Sketch;

public static class DimensionNumbers
{
    // Accept either decimal separator, never thousands grouping. Input is finite
    // and validated by dimension kind before the model is touched.
    public static bool TryParseScalar(string text,out double value)
    {
        value=0;text=text.Trim();
        if(text.Contains(',')&&text.Contains('.'))return false;
        return double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&double.IsFinite(value);
    }
    public static bool TryParse(string text,SketchDimensionType type,out double value,out string error)
    {
        value=0;error="Geçerli, sonlu bir sayı girin (45,5 veya 45.5).";
        text=text.Trim();
        if(type==SketchDimensionType.Angle&&text.EndsWith("°"))text=text[..^1].Trim();
        if(!TryParseScalar(text,out value))return false;
        var zeroAllowed=type is SketchDimensionType.HorizontalDistance or SketchDimensionType.VerticalDistance or SketchDimensionType.PointLineDistance or SketchDimensionType.ParallelLineDistance;
        if(value<0||(!zeroAllowed&&value<=1e-9)){error=zeroAllowed?"Ölçü negatif olamaz.":"Ölçü sıfırdan büyük olmalıdır.";return false;}
        if(type==SketchDimensionType.Angle&&value>=180){error="Açı 0° ile 180° arasında olmalıdır (uçlar hariç).";return false;}
        error="";return true;
    }
}

public sealed class DimensionEditSession
{
    private readonly SketchModel model;
    private readonly int id;
    public SketchSnapshot Before {get;}
    public bool Committed {get;private set;}
    public DimensionEditSession(SketchModel model,int dimensionId)
    {this.model=model;id=dimensionId;Before=model.CaptureSnapshot();}
    public bool TryCommit(string text,out string error,Func<SketchModel,bool>? validateDependentModel=null)
    {
        error="Ölçü artık mevcut değil veya referans ölçüsü.";
        if(Committed||model.Dimension(id) is not {Driving:true} dimension)return false;
        if(!DimensionNumbers.TryParse(text,dimension.Type,out var value,out error))return false;
        if(value==dimension.Value){Committed=true;error="";return true;}
        // Solve an isolated candidate: parse/solver/dependency rejection leaves
        // the live model, revision and history untouched.
        var candidate=new SketchModel(model.Plane);candidate.RestoreSnapshot(Before);
        if(!candidate.SetDimensionValue(id,value)||candidate.DefinitionInfo().IsConflicting)
        {error="Ölçü mevcut ilişkilerle çakışıyor; önceki geometri korundu.";return false;}
        if(validateDependentModel is not null&&!validateDependentModel(candidate))
        {error="Bağımlı feature yeniden oluşturulamadı; değişiklik uygulanmadı.";return false;}
        model.RestoreSnapshot(candidate.CaptureSnapshot(),notifyChange:true);
        Committed=true;error="";return true;
    }
    // No live state changes occur until a valid commit. Closing/Esc is a no-op.
}
