using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

/// <summary>Shared by the live preview and committed driving dimension.</summary>
public static class SmartDimensionPlacement
{
    public static SketchDimensionType BetweenPoints(Point2d a, Point2d b, Point2d label)
    {
        var dx = Math.Abs(b.X - a.X);
        var dy = Math.Abs(b.Y - a.Y);
        var outsideX = Math.Max(Math.Max(a.X, b.X) < label.X ? label.X - Math.Max(a.X, b.X) : Math.Min(a.X, b.X) - label.X, 0);
        var outsideY = Math.Max(Math.Max(a.Y, b.Y) < label.Y ? label.Y - Math.Max(a.Y, b.Y) : Math.Min(a.Y, b.Y) - label.Y, 0);
        // Above/below the endpoints: horizontal; beside them: vertical.
        // Within their bounding box: aligned distance. Corner regions use the
        // nearest extension direction, independent of selection order.
        if (outsideY > 0 && outsideY >= outsideX) return SketchDimensionType.HorizontalDistance;
        if (outsideX > 0) return SketchDimensionType.VerticalDistance;
        if (dy < 1e-9) return SketchDimensionType.HorizontalDistance;
        if (dx < 1e-9) return SketchDimensionType.VerticalDistance;
        return SketchDimensionType.PointDistance;
    }
}
