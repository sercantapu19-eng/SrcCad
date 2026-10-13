using System.Text.Json;
using SrcCad.Core.Geometry;

namespace SrcCad.Core.Drawing;

public enum DrawingViewType { Front, Top, Right, Isometric, Projected, Section, Detail, Broken }
public enum DrawingProjectionStandard { FirstAngle, ThirdAngle }

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
    string Text);

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
}

public static class DrawingDocumentFile
{
    private sealed record Envelope(int SchemaVersion,string Units,DrawingDocument Document);
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,MaxDepth=64};

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
        foreach(var view in document.Views)
        {
            if(view.Id==Guid.Empty||string.IsNullOrWhiteSpace(view.Name)||string.IsNullOrWhiteSpace(view.ModelPath)||
               !Enum.IsDefined(view.Type)||!sheetIds.Contains(view.SheetId)||!double.IsFinite(view.Position.X)||!double.IsFinite(view.Position.Y)||
               !double.IsFinite(view.Scale)||view.Scale<=1e-9||
               (view.ParentViewId is Guid parent&&(!viewIds.Contains(parent)||parent==view.Id)))
                throw new InvalidDataException("Invalid drawing view.");
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

        if(document.Annotations.Select(a=>a.Id).Distinct().Count()!=document.Annotations.Length)
            throw new InvalidDataException("Duplicate drawing annotation id.");
        foreach(var annotation in document.Annotations)
        {
            if(annotation.Id==Guid.Empty||!sheetIds.Contains(annotation.SheetId)||
               (annotation.ViewId is Guid viewId&&!viewIds.Contains(viewId))||
               !double.IsFinite(annotation.Position.X)||!double.IsFinite(annotation.Position.Y)||
               string.IsNullOrWhiteSpace(annotation.Text))
                throw new InvalidDataException("Invalid drawing annotation.");
        }
    }
}
