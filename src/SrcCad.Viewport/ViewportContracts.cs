using SrcCad.Core.Geometry;

namespace SrcCad.Viewport;

public enum SelectionKind
{
    None, SketchPoint, SketchCurve, Vertex, Edge, Face, Plane, Feature
}

public readonly record struct SelectionHit(
    SelectionKind Kind,
    long NativeId,
    double ScreenDistancePx);

public interface ICadViewport
{
    void SetOrthographic(bool enabled);
    void LookNormalTo(PlaneFrame plane);
    void FitAll();
    SelectionHit HitTest(double screenX, double screenY, double tolerancePx);
}
