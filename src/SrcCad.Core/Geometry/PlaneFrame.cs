namespace SrcCad.Core.Geometry;

public sealed record PlaneFrame(
    string Name,
    Vector3d Origin,
    Vector3d U,
    Vector3d V,
    Vector3d Normal)
{
    public PlaneFrame Validate(double tolerance = 1e-9)
    {
        var u = U.Normalized();
        var v = V.Normalized();
        var n = Normal.Normalized();

        if (Math.Abs(Vector3d.Dot(u, v)) > tolerance ||
            Math.Abs(Vector3d.Dot(u, n)) > tolerance ||
            Math.Abs(Vector3d.Dot(v, n)) > tolerance)
            throw new InvalidOperationException($"{Name}: frame eksenleri dik değil.");

        var cross = Vector3d.Cross(u, v).Normalized();
        if ((cross - n).Length > tolerance)
            throw new InvalidOperationException($"{Name}: U x V = Normal şartı sağlanmıyor.");

        return this;
    }

    public Vector3d SketchToWorld(double u, double v)
        => Origin + U * u + V * v;

    public Point2d WorldToSketch(Vector3d world)
    {
        var d = world - Origin;
        return new Point2d(Vector3d.Dot(d, U.Normalized()), Vector3d.Dot(d, V.Normalized()));
    }
}
