namespace SrcCad.Core.Geometry;

public static class StandardPlanes
{
    public static PlaneFrame Front { get; } =
        new PlaneFrame("Ön Düzlem", Vector3d.Zero, Vector3d.UnitX, Vector3d.UnitY, Vector3d.UnitZ).Validate();

    public static PlaneFrame Top { get; } =
        new PlaneFrame("Üst Düzlem", Vector3d.Zero, Vector3d.UnitX, new Vector3d(0, 0, -1), Vector3d.UnitY).Validate();

    public static PlaneFrame Right { get; } =
        new PlaneFrame("Sağ Düzlem", Vector3d.Zero, new Vector3d(0, 0, -1), Vector3d.UnitY, Vector3d.UnitX).Validate();

    public static IReadOnlyList<PlaneFrame> All { get; } =
        new[] { Front, Top, Right };
}
