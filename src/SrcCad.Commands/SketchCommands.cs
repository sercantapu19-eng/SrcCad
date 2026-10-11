using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;

namespace SrcCad.Commands;

public readonly record struct SketchPick(
    Point2d Point,
    int SnappedPointId,
    double SnapTolerance,
    int ClickCount);

public abstract record SketchPreviewPrimitive;
public sealed record SketchPreviewLine(Point2d A, Point2d B) : SketchPreviewPrimitive;
public sealed record SketchPreviewCircle(Point2d Center, double Radius) : SketchPreviewPrimitive;

public interface ISketchCommand : ICadCommand
{
    bool HasPendingStep { get; }
    void Pick(SketchPick pick);
    IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor);
    void CancelStep();
}

public abstract class SketchCommandBase : ISketchCommand
{
    protected readonly SketchModel Sketch;
    protected CommandContext Context = new();

    protected SketchCommandBase(SketchModel sketch) => Sketch = sketch;

    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract bool HasPendingStep { get; }

    public virtual void Begin(CommandContext context) => Context = context;
    public virtual void End(bool commit) => CancelStep();
    public abstract void Pick(SketchPick pick);
    public abstract IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor);
    public abstract void CancelStep();

    protected int ResolvePoint(SketchPick pick)
        => pick.SnappedPointId >= 0
            ? pick.SnappedPointId
            : Sketch.CreateOrSnapPoint(pick.Point, pick.SnapTolerance);

    protected void Changed(string status)
    {
        Context.ModelChanged?.Invoke();
        Context.SetStatus?.Invoke(status);
    }
}

public sealed class LineSketchCommand : SketchCommandBase
{
    private int? _startPointId;
    private int? _chainFirstPointId;

    private readonly bool construction;
    public LineSketchCommand(SketchModel sketch, bool construction = false) : base(sketch) { this.construction = construction; }
    public override string Id => construction ? "sketch.centerline" : "sketch.line";
    public override string Title => construction ? "Merkez Çizgisi" : "Çizgi";
    public override bool HasPendingStep => _startPointId.HasValue;

    public override void Begin(CommandContext context)
    {
        base.Begin(context);
        Context.SetStatus?.Invoke("Çizgi: başlangıç noktasını seçin.");
    }

    public override void Pick(SketchPick pick)
    {
        if (!_startPointId.HasValue)
        {
            var startId = ResolvePoint(pick);
            _startPointId = startId;
            _chainFirstPointId = startId;
            Context.SetStatus?.Invoke(startId == 0 ? "Çizgi Origin'den başladı." : "Çizgi başlangıcı seçildi.");
            return;
        }

        var endId = ResolvePoint(pick);
        if (endId == _startPointId.Value)
        {
            if (pick.ClickCount >= 2)
            {
                CancelStep();
                Context.SetStatus?.Invoke("Çizgi zinciri tamamlandı; Çizgi aracı aktif.");
            }
            else Context.SetStatus?.Invoke("Aynı nokta seçildi; başka bir bitiş noktası seçin.");
            return;
        }

        var newLine = Sketch.AddLine(_startPointId.Value, endId, construction);
        Sketch.AddAutomaticLineRelation(newLine.Id);
        var closes = _chainFirstPointId.HasValue &&
                     endId == _chainFirstPointId.Value &&
                     Sketch.Lines(false).Count() >= 3;

        if (closes || pick.ClickCount >= 2)
        {
            _startPointId = null;
            _chainFirstPointId = null;
            Changed(closes ? "Kapalı çizgi zinciri oluşturuldu." : "Çizgi zinciri tamamlandı; Çizgi aracı aktif.");
        }
        else
        {
            _startPointId = endId;
            Changed(endId == 0 ? "Çizgi Origin'e bağlandı; zincir devam ediyor." : "Çizgi oluşturuldu; zincir devam ediyor.");
        }
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if (!_startPointId.HasValue) return Array.Empty<SketchPreviewPrimitive>();
        return new SketchPreviewPrimitive[]
        {
            new SketchPreviewLine(Sketch.Point(_startPointId.Value).Position, cursor)
        };
    }

    public override void CancelStep()
    {
        _startPointId = null;
        _chainFirstPointId = null;
    }
}

public sealed class CornerRectangleSketchCommand : SketchCommandBase
{
    private Point2d? _first;
    public CornerRectangleSketchCommand(SketchModel sketch) : base(sketch) { }
    public override string Id => "sketch.rectangle.corner";
    public override string Title => "Köşe Noktası ile Dikdörtgen";
    public override bool HasPendingStep => _first.HasValue;

    public override void Begin(CommandContext context)
    {
        base.Begin(context);
        Context.SetStatus?.Invoke("Köşe Noktası ile Dikdörtgen: ilk köşeyi seçin.");
    }

    public override void Pick(SketchPick pick)
    {
        if (!_first.HasValue)
        {
            _first = pick.Point;
            Context.SetStatus?.Invoke("Dikdörtgen karşı köşesini seçin.");
            return;
        }
        Sketch.AddCornerRectangle(_first.Value, pick.Point, pick.SnapTolerance);
        _first = null;
        Changed("Köşe Noktası ile Dikdörtgen oluşturuldu.");
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if (!_first.HasValue) return Array.Empty<SketchPreviewPrimitive>();
        var a = _first.Value;
        var p0 = new Point2d(a.X, a.Y);
        var p1 = new Point2d(cursor.X, a.Y);
        var p2 = cursor;
        var p3 = new Point2d(a.X, cursor.Y);
        return new SketchPreviewPrimitive[]
        {
            new SketchPreviewLine(p0,p1), new SketchPreviewLine(p1,p2),
            new SketchPreviewLine(p2,p3), new SketchPreviewLine(p3,p0)
        };
    }

    public override void CancelStep() => _first = null;
}

public sealed class CenterRectangleSketchCommand : SketchCommandBase
{
    private Point2d? _center;
    public CenterRectangleSketchCommand(SketchModel sketch) : base(sketch) { }
    public override string Id => "sketch.rectangle.center";
    public override string Title => "Merkez ile Dikdörtgen";
    public override bool HasPendingStep => _center.HasValue;

    public override void Begin(CommandContext context)
    {
        base.Begin(context);
        Context.SetStatus?.Invoke("Merkez ile Dikdörtgen: merkez noktasını seçin.");
    }

    public override void Pick(SketchPick pick)
    {
        if (!_center.HasValue)
        {
            _center = pick.Point;
            Context.SetStatus?.Invoke("Merkez seçildi; bir köşe seçin.");
            return;
        }
        Sketch.AddCenterRectangle(_center.Value, pick.Point);
        _center = null;
        Changed("Merkez ile Dikdörtgen oluşturuldu; merkez tanımı ve construction çaprazları saklandı.");
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if (!_center.HasValue) return Array.Empty<SketchPreviewPrimitive>();
        var c = _center.Value;
        var dx = Math.Abs(cursor.X - c.X);
        var dy = Math.Abs(cursor.Y - c.Y);
        var p0 = new Point2d(c.X-dx,c.Y-dy);
        var p1 = new Point2d(c.X+dx,c.Y-dy);
        var p2 = new Point2d(c.X+dx,c.Y+dy);
        var p3 = new Point2d(c.X-dx,c.Y+dy);
        return new SketchPreviewPrimitive[]
        {
            new SketchPreviewLine(p0,p1), new SketchPreviewLine(p1,p2),
            new SketchPreviewLine(p2,p3), new SketchPreviewLine(p3,p0),
            new SketchPreviewLine(p0,p2), new SketchPreviewLine(p1,p3)
        };
    }

    public override void CancelStep() => _center = null;
}

public sealed class CircleSketchCommand : SketchCommandBase
{
    private Point2d? _center;
    public CircleSketchCommand(SketchModel sketch) : base(sketch) { }
    public override string Id => "sketch.circle";
    public override string Title => "Daire";
    public override bool HasPendingStep => _center.HasValue;

    public override void Begin(CommandContext context)
    {
        base.Begin(context);
        Context.SetStatus?.Invoke("Daire: merkez noktasını seçin.");
    }

    public override void Pick(SketchPick pick)
    {
        if (!_center.HasValue)
        {
            _center = pick.Point;
            Context.SetStatus?.Invoke("Daire merkezi seçildi; yarıçapı belirleyin.");
            return;
        }
        var centerId = Sketch.CreateOrSnapPoint(_center.Value, pick.SnapTolerance);
        Sketch.AddCircle(centerId, Point2d.Distance(_center.Value, pick.Point));
        _center = null;
        Changed("Daire oluşturuldu.");
    }

    public override IReadOnlyList<SketchPreviewPrimitive> Preview(Point2d cursor)
    {
        if (!_center.HasValue) return Array.Empty<SketchPreviewPrimitive>();
        return new SketchPreviewPrimitive[]
        {
            new SketchPreviewCircle(_center.Value, Point2d.Distance(_center.Value, cursor))
        };
    }

    public override void CancelStep() => _center = null;
}

