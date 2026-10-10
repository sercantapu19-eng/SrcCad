namespace SrcCad.Core.Geometry;

public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static Vector3d Zero => new(0, 0, 0);
    public static Vector3d UnitX => new(1, 0, 0);
    public static Vector3d UnitY => new(0, 1, 0);
    public static Vector3d UnitZ => new(0, 0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3d Normalized()
    {
        var len = Length;
        if (len < 1e-12) throw new InvalidOperationException("Sıfır vektör normalize edilemez.");
        return new(X / len, Y / len, Z / len);
    }

    public static double Dot(Vector3d a, Vector3d b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3d Cross(Vector3d a, Vector3d b)
        => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    public static Vector3d operator +(Vector3d a, Vector3d b)
        => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3d operator -(Vector3d a, Vector3d b)
        => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3d operator *(Vector3d a, double s)
        => new(a.X * s, a.Y * s, a.Z * s);
}
