using System.Text.Json;
using SrcCad.Core.History;
using SrcCad.Core.Sketch;

namespace SrcCad.Core.Persistence;

// Schema 1 represents the currently supported sketch + optional extrusion model.
// It does not pretend to contain a multi-feature dependency graph.
public sealed record PartFile(int SchemaVersion, string Units, ExtrusionState Model)
{
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,MaxDepth=64};
    public static string Serialize(ExtrusionState state)
    {
        Validate(state);
        return JsonSerializer.Serialize(new PartFile(1,"mm",state),Options);
    }
    public static ExtrusionState Deserialize(string json)
    {
        if(json.Length>16*1024*1024)throw new InvalidDataException("Parça dosyası boyut sınırını aşıyor.");
        var file=JsonSerializer.Deserialize<PartFile>(json,Options)??throw new InvalidDataException("Boş parça dosyası.");
        if(file.SchemaVersion!=1 || file.Units!="mm")throw new InvalidDataException("Desteklenmeyen parça sürümü veya birim.");
        Validate(file.Model);return file.Model;
    }
    public static void Save(string path,ExtrusionState state)
    {
        var content=Serialize(state);var full=Path.GetFullPath(path);
        var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,content);File.Move(temp,full,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public static ExtrusionState Load(string path)
    {
        if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Parça dosyası boyut sınırını aşıyor.");
        return Deserialize(File.ReadAllText(path));
    }
    private static void Validate(ExtrusionState state)
    {
        if(state?.Plane is null || state.Sketch is null)throw new InvalidDataException("Parça modeli eksik.");
        var frame=state.Plane;
        foreach(var v in new[]{frame.Origin,frame.U,frame.V,frame.Normal})
            if(!double.IsFinite(v.X)||!double.IsFinite(v.Y)||!double.IsFinite(v.Z))throw new InvalidDataException("Geçersiz düzlem koordinatı.");
        if(new[]{frame.U,frame.V,frame.Normal}.Any(v=>Math.Abs(v.Length-1)>1e-9))throw new InvalidDataException("Düzlem eksenleri birim olmalı.");
        frame.Validate();
        if(state.Depth is double depth && (!double.IsFinite(depth)||Math.Abs(depth)<1e-9))throw new InvalidDataException("Geçersiz feature derinliği.");
        var s=state.Sketch;
        if(s.Points is null || s.Entities is null || s.Relations is null || s.Dimensions is null || s.CenterRectangles is null)
            throw new InvalidDataException("Sketch koleksiyonları eksik.");
        if(!s.Points.TryGetValue(0,out var origin)||origin is null||!origin.Fixed||origin.Position!=new SrcCad.Core.Geometry.Point2d(0,0))
            throw new InvalidDataException("Origin sabit (0,0) olmalı.");
        if(s.Points.Values.Any(p=>p is null)||s.Entities.Any(e=>e is null)||s.Relations.Any(r=>r is null)||s.Dimensions.Any(d=>d is null)||s.CenterRectangles.Any(c=>c is null))
            throw new InvalidDataException("Boş model öğesi.");
        if(s.Points.Any(p=>p.Key!=p.Value.Id || p.Key<0 || !double.IsFinite(p.Value.Position.X)||!double.IsFinite(p.Value.Position.Y)))
            throw new InvalidDataException("Geçersiz nokta.");
        void Ids(IEnumerable<int> ids,int next)
        {var a=ids.ToArray();if(a.Any(i=>i<0)||a.Distinct().Count()!=a.Length||next<=a.DefaultIfEmpty(0).Max())throw new InvalidDataException("Geçersiz kimlik/counter.");}
        Ids(s.Points.Keys,s.NextPointId);Ids(s.Entities.Select(e=>e.Id),s.NextEntityId);Ids(s.Relations.Select(r=>r.Id),s.NextRelationId);Ids(s.Dimensions.Select(d=>d.Id),s.NextDimensionId);
        foreach(var e in s.Entities)
            switch(e)
            {
                case SketchLine l when s.Points.ContainsKey(l.StartPointId)&&s.Points.ContainsKey(l.EndPointId):break;
                case SketchCircle c when s.Points.ContainsKey(c.CenterPointId)&&double.IsFinite(c.Radius)&&c.Radius>1e-9:break;
                case SketchArc a when s.Points.ContainsKey(a.CenterPointId)&&s.Points.ContainsKey(a.StartPointId)&&s.Points.ContainsKey(a.EndPointId):
                {
                    if(a.StartPointId==a.EndPointId)throw new InvalidDataException("Yay başlangıç ve bitiş noktaları farklı olmalı.");
                    var center=s.Points[a.CenterPointId].Position;
                    var start=s.Points[a.StartPointId].Position;
                    var end=s.Points[a.EndPointId].Position;
                    var ra=SrcCad.Core.Geometry.Point2d.Distance(center,start);
                    var rb=SrcCad.Core.Geometry.Point2d.Distance(center,end);
                    if(ra<=1e-9||!double.IsFinite(ra)||!double.IsFinite(rb)||Math.Abs(ra-rb)>Math.Max(1e-6,ra*1e-5))
                        throw new InvalidDataException("Geçersiz yay geometrisi.");
                    break;
                }
                case SketchEllipse ellipse when s.Points.ContainsKey(ellipse.CenterPointId)&&
                    double.IsFinite(ellipse.RadiusX)&&double.IsFinite(ellipse.RadiusY)&&double.IsFinite(ellipse.RotationRadians)&&
                    ellipse.RadiusX>1e-9&&ellipse.RadiusY>1e-9:break;
                default:throw new InvalidDataException("Geçersiz geometri veya nokta referansı.");
            }
        var model=new SketchModel(frame);model.RestoreSnapshot(s);
        void Ref(GeometryReference? r)
        {
            if(r is null)return;
            if(!Enum.IsDefined(r.Kind)||r.Kind==GeometryReferenceKind.Origin&&r.Id!=0||r.Kind==GeometryReferenceKind.Endpoint&&r.Slot is not 0 and not 1)
                throw new InvalidDataException("Geçersiz semantik referans.");
            if(r.IsPointLike)model.ResolvePoint(r);
            else if(r.IsLine?model.Entity(r.Id) is not SketchLine:model.Entity(r.Id) is not SketchCircle)throw new InvalidDataException("Referans türü uyuşmuyor.");
        }
        try
        {
            foreach(var r in s.Relations) {if(!Enum.IsDefined(r.Type))throw new InvalidDataException("Geçersiz ilişki türü.");Ref(r.ReferenceA);Ref(r.ReferenceB);Ref(r.ReferenceC);}
            foreach(var d in s.Dimensions)
            {
                if(!Enum.IsDefined(d.Type)||!double.IsFinite(d.Value)||d.Value<0||!double.IsFinite(d.LabelPosition.X)||!double.IsFinite(d.LabelPosition.Y)||d.Sense is not 1 and not -1)
                    throw new InvalidDataException("Geçersiz ölçü.");
                Ref(d.ReferenceA);Ref(d.ReferenceB);
                if(d.Type==SketchDimensionType.ParallelLineDistance && (d.ReferenceA?.IsLine!=true || d.ReferenceB?.IsLine!=true))
                    throw new InvalidDataException("Paralel mesafe iki çizgi referansı gerektirir.");
            }
            if(model.DefinitionInfo().IsConflicting)throw new InvalidDataException("Sketch kısıtları tutarsız.");
            if(state.Depth is not null&&!model.TryGetExtrusionProfile(out _,out _,out _)&&!model.TryGetSingleCircle(out _))
                throw new InvalidDataException("Ekstrüzyon profili geçersiz.");
        }
        catch(KeyNotFoundException ex){throw new InvalidDataException("Kırık model referansı.",ex);}
        catch(NullReferenceException ex){throw new InvalidDataException("Eksik model referansı.",ex);}
        catch(InvalidCastException ex){throw new InvalidDataException("Model referans türü uyuşmuyor.",ex);}
    }
}
