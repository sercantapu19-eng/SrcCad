using System.Text.Json;
using SrcCad.Core.Geometry;

namespace SrcCad.Core.Drawing;

public enum DrawingViewType { Front, Top, Right, Isometric, Projected, Section, Detail, Broken }
public enum DrawingProjectionStandard { FirstAngle, ThirdAngle }
public enum DrawingAnnotationKind { Text, OverallSize, LinearDimension, DiameterDimension, RadiusDimension, AngularDimension, CenterMark, HoleCallout, Balloon, BillOfMaterials }

public sealed record DrawingSheet(
    Guid Id,
    string Name,
    double Width,
    double Height,
    DrawingProjectionStandard Projection = DrawingProjectionStandard.FirstAngle);

public sealed record DrawingView(
    Guid Id,
    string Name,
    string ModelPath,
    DrawingViewType Type,
    Guid SheetId,
    Point2d Position,
    double Scale = 1.0,
    Guid? ParentViewId = null,
    string? Configuration = null,
    Vector3d? SectionOrigin = null,
    Vector3d? SectionNormal = null,
    double? BreakStart = null,
    double? BreakEnd = null);

public sealed record DrawingAnnotation(
    Guid Id,
    Guid SheetId,
    Guid? ViewId,
    Point2d Position,
    string Text,
    DrawingAnnotationKind Kind = DrawingAnnotationKind.Text,
    string? Data = null);

public sealed record DrawingDocument(
    Guid Id,
    DrawingSheet[] Sheets,
    DrawingView[] Views,
    DrawingAnnotation[] Annotations)
{
    public DrawingSheet Sheet(Guid id)=>Sheets.Single(s=>s.Id==id);
    public DrawingView View(Guid id)=>Views.Single(v=>v.Id==id);

    public DrawingDocument RemoveView(Guid id,bool cascade=true)
    {
        _=View(id);
        var remove=new HashSet<Guid>{id};
        if(cascade)
        {
            bool changed;
            do
            {
                changed=false;
                foreach(var view in Views)
                    if(view.ParentViewId is Guid parent&&remove.Contains(parent)&&remove.Add(view.Id))
                        changed=true;
            }while(changed);
        }
        else if(Views.Any(v=>v.ParentViewId==id))
            throw new InvalidOperationException("Dependent projected/detail/section views must be removed explicitly.");

        return this with
        {
            Views=Views.Where(v=>!remove.Contains(v.Id)).ToArray(),
            Annotations=Annotations.Where(a=>a.ViewId is not Guid viewId||!remove.Contains(viewId)).ToArray()
        };
    }

    public DrawingDocument RemoveAnnotation(Guid id)
    {
        if(!Annotations.Any(a=>a.Id==id))throw new InvalidOperationException("Drawing annotation not found.");
        return this with{Annotations=Annotations.Where(a=>a.Id!=id).ToArray()};
    }

    public DrawingDocument RemoveSheet(Guid id)
    {
        if(!Sheets.Any(s=>s.Id==id))throw new InvalidOperationException("Drawing sheet not found.");
        if(Sheets.Length<=1)throw new InvalidOperationException("Drawing must keep at least one sheet.");
        var viewIds=Views.Where(v=>v.SheetId==id).Select(v=>v.Id).ToHashSet();
        return this with
        {
            Sheets=Sheets.Where(s=>s.Id!=id).ToArray(),
            Views=Views.Where(v=>v.SheetId!=id).ToArray(),
            Annotations=Annotations.Where(a=>a.SheetId!=id&&
                (a.ViewId is not Guid viewId||!viewIds.Contains(viewId))).ToArray()
        };
    }
}

public static class DrawingDocumentFile
{
    private sealed record Envelope(int SchemaVersion,string Units,DrawingDocument Document);
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,MaxDepth=64};

    public static string ResolveModelPath(string drawingPath,string modelPath)
    {
        if(string.IsNullOrWhiteSpace(drawingPath))throw new ArgumentException("Drawing path is required.",nameof(drawingPath));
        if(string.IsNullOrWhiteSpace(modelPath))throw new ArgumentException("Drawing model path is required.",nameof(modelPath));
        if(Path.IsPathRooted(modelPath))return Path.GetFullPath(modelPath);
        var directory=Path.GetDirectoryName(Path.GetFullPath(drawingPath))??Environment.CurrentDirectory;
        return Path.GetFullPath(Path.Combine(directory,modelPath));
    }

    private static bool TryAnnotationData(string? data,char type,int count,out double[] values)
    {
        values=Array.Empty<double>();
        if(string.IsNullOrWhiteSpace(data))return false;
        var fields=data.Split(';');
        if(fields.Length!=count+1||fields[0].Length!=1||fields[0][0]!=type)return false;
        values=new double[count];
        for(var i=0;i<count;i++)
            if(!double.TryParse(fields[i+1],System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out values[i])||!double.IsFinite(values[i]))
                return false;
        return true;
    }

    private static bool ValidAssociativeAnnotation(DrawingAnnotation annotation)
    {
        if(annotation.Kind is DrawingAnnotationKind.LinearDimension)
        {
            if(annotation.ViewId is null||!TryAnnotationData(annotation.Data,'L',4,out var v))return false;
            return Math.Sqrt(Math.Pow(v[2]-v[0],2)+Math.Pow(v[3]-v[1],2))>1e-9;
        }
        if(annotation.Kind is DrawingAnnotationKind.DiameterDimension)
            return annotation.ViewId is not null&&TryAnnotationData(annotation.Data,'D',3,out var v)&&v[2]>1e-9;
        if(annotation.Kind is DrawingAnnotationKind.RadiusDimension or DrawingAnnotationKind.CenterMark)
            return annotation.ViewId is not null&&TryAnnotationData(annotation.Data,'R',3,out var v)&&v[2]>1e-9;
        if(annotation.Kind is DrawingAnnotationKind.AngularDimension)
        {
            if(annotation.ViewId is null||!TryAnnotationData(annotation.Data,'A',8,out var v))return false;
            var a=Math.Sqrt(Math.Pow(v[2]-v[0],2)+Math.Pow(v[3]-v[1],2));
            var b=Math.Sqrt(Math.Pow(v[6]-v[4],2)+Math.Pow(v[7]-v[5],2));
            return a>1e-9&&b>1e-9;
        }
        if(annotation.Kind is DrawingAnnotationKind.HoleCallout)
        {
            if(annotation.ViewId is null)return false;
            if(TryAnnotationData(annotation.Data,'D',3,out var v))return v[2]>1e-9;
            return int.TryParse(annotation.Data,out var legacyIndex)&&legacyIndex>=0;
        }
        return true;
    }

    public static string Serialize(DrawingDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(new Envelope(1,"mm",document),Options);
    }

    public static DrawingDocument Deserialize(string json)
    {
        if(json.Length>32*1024*1024)throw new InvalidDataException("Drawing file exceeds size limit.");
        var envelope=JsonSerializer.Deserialize<Envelope>(json,Options)??throw new InvalidDataException("Empty drawing document.");
        if(envelope.SchemaVersion!=1||envelope.Units!="mm")throw new InvalidDataException("Unsupported drawing schema/units.");
        Validate(envelope.Document);
        return envelope.Document;
    }

    public static DrawingDocument Clone(DrawingDocument document)=>Deserialize(Serialize(document));

    public static void Save(string path,DrawingDocument document)
    {
        var full=Path.GetFullPath(path);
        var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,Serialize(document));
            File.Move(temp,full,true);
        }
        finally
        {
            if(File.Exists(temp))File.Delete(temp);
        }
    }

    public static DrawingDocument Load(string path)
    {
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Drawing file exceeds size limit.");
        return Deserialize(File.ReadAllText(path));
    }

    public static void Validate(DrawingDocument document)
    {
        if(document is null||document.Id==Guid.Empty||document.Sheets is null||document.Views is null||document.Annotations is null)
            throw new InvalidDataException("Invalid drawing document.");
        if(document.Sheets.Length==0)throw new InvalidDataException("Drawing requires at least one sheet.");
        if(document.Sheets.Select(s=>s.Id).Distinct().Count()!=document.Sheets.Length)
            throw new InvalidDataException("Duplicate drawing sheet id.");
        foreach(var sheet in document.Sheets)
        {
            if(sheet.Id==Guid.Empty||string.IsNullOrWhiteSpace(sheet.Name)||!double.IsFinite(sheet.Width)||!double.IsFinite(sheet.Height)||
               sheet.Width<=1e-9||sheet.Height<=1e-9||!Enum.IsDefined(sheet.Projection))
                throw new InvalidDataException("Invalid drawing sheet.");
        }

        var sheetIds=document.Sheets.Select(s=>s.Id).ToHashSet();
        if(document.Views.Select(v=>v.Id).Distinct().Count()!=document.Views.Length)
            throw new InvalidDataException("Duplicate drawing view id.");
        var viewIds=document.Views.Select(v=>v.Id).ToHashSet();
        var viewsById=document.Views.ToDictionary(v=>v.Id);
        foreach(var view in document.Views)
        {
            if(view.Id==Guid.Empty||string.IsNullOrWhiteSpace(view.Name)||string.IsNullOrWhiteSpace(view.ModelPath)||
               !Enum.IsDefined(view.Type)||!sheetIds.Contains(view.SheetId)||!double.IsFinite(view.Position.X)||!double.IsFinite(view.Position.Y)||
               !double.IsFinite(view.Scale)||view.Scale<=1e-9||
               (view.ParentViewId is Guid parent&&(!viewIds.Contains(parent)||parent==view.Id)))
                throw new InvalidDataException("Invalid drawing view.");
            if(view.ParentViewId is Guid parentId)
            {
                var parentView=viewsById[parentId];
                if(parentView.SheetId!=view.SheetId)
                    throw new InvalidDataException("Dependent drawing views must stay on the same sheet.");
                if(!string.Equals(parentView.ModelPath,view.ModelPath,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Dependent drawing views must reference the same model.");
            }
            if(view.Type is DrawingViewType.Projected or DrawingViewType.Section or DrawingViewType.Detail or DrawingViewType.Broken)
            {
                if(view.ParentViewId is null)
                    throw new InvalidDataException("Dependent drawing view requires a parent view.");
            }
            if(view.Type==DrawingViewType.Section)
            {
                if(view.SectionOrigin is not Vector3d origin||view.SectionNormal is not Vector3d normal||
                   !double.IsFinite(origin.X)||!double.IsFinite(origin.Y)||!double.IsFinite(origin.Z)||
                   !double.IsFinite(normal.X)||!double.IsFinite(normal.Y)||!double.IsFinite(normal.Z)||normal.Length<=1e-9)
                    throw new InvalidDataException("Section view requires a valid section plane.");
            }
            if(view.Type==DrawingViewType.Broken)
            {
                if(view.BreakStart is not double start||view.BreakEnd is not double end||
                   !double.IsFinite(start)||!double.IsFinite(end)||end-start<=1e-9||view.ParentViewId is null)
                    throw new InvalidDataException("Broken view requires a valid break interval and parent view.");
            }
        }

        foreach(var view in document.Views)
        {
            var seen=new HashSet<Guid>{view.Id};
            var current=view;
            while(current.ParentViewId is Guid parentId)
            {
                if(!seen.Add(parentId))
                    throw new InvalidDataException("Drawing view dependency cycle detected.");
                current=viewsById[parentId];
            }
        }

        if(document.Annotations.Select(a=>a.Id).Distinct().Count()!=document.Annotations.Length)
            throw new InvalidDataException("Duplicate drawing annotation id.");
        foreach(var annotation in document.Annotations)
        {
            if(annotation.Id==Guid.Empty||!sheetIds.Contains(annotation.SheetId)||
               (annotation.ViewId is Guid viewId&&!viewIds.Contains(viewId))||
               !double.IsFinite(annotation.Position.X)||!double.IsFinite(annotation.Position.Y)||
               string.IsNullOrWhiteSpace(annotation.Text)||!Enum.IsDefined(annotation.Kind))
                throw new InvalidDataException("Invalid drawing annotation.");
            if(annotation.ViewId is Guid attachedViewId&&viewsById[attachedViewId].SheetId!=annotation.SheetId)
                throw new InvalidDataException("Drawing annotation and attached view must be on the same sheet.");
            if(!ValidAssociativeAnnotation(annotation))
                throw new InvalidDataException("Invalid associative drawing annotation.");
        }
    }
}
