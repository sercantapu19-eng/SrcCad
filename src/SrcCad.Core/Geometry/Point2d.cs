namespace SrcCad.Core.Geometry;

public readonly record struct Point2d(double X, double Y)
{
    public static double Distance(Point2d a, Point2d b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
