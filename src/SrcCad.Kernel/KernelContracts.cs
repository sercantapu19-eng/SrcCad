using SrcCad.Core.Geometry;

namespace SrcCad.Kernel;

public readonly record struct KernelShapeId(Guid Value)
{
    public static KernelShapeId New() => new(Guid.NewGuid());
}

public interface IModelingKernel
{
    string KernelName { get; }
    string KernelVersion { get; }
    KernelShapeId MakeBox(double x, double y, double z);
    KernelShapeId ExtrudeClosedProfile(
        IReadOnlyList<(double U, double V)> outerLoop,
        PlaneFrame plane,
        double depth);
}
