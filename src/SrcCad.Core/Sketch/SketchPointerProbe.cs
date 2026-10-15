using SrcCad.Core.Geometry;
namespace SrcCad.Core.Sketch;

public sealed record SketchPointerTarget(GeometryReference Reference, Point2d Position, int PointId, string Label);

// Shared by command preview and commit: a marker must describe the actual pick.
public static class SketchPointerProbe
{
    public static SketchPointerTarget? Probe(SketchModel model,Point2d cursor,double tolerance)
    {
        var reference=model.HitReference(cursor,tolerance,true);
        if(reference is null)return null;
        if(reference.IsPointLike)
            return new(reference,model.ResolvePoint(reference),model.ReferencePointId(reference),reference.Kind switch
            {
                GeometryReferenceKind.Origin=>"Origin",
                GeometryReferenceKind.CenterPoint=>"Merkez",
                GeometryReferenceKind.Midpoint=>"Orta nokta",
                GeometryReferenceKind.Endpoint=>"Uç nokta",
                _=>"Nokta"
            });
        return new(reference,cursor,-1,reference.IsLine?"Kenar":"Daire");
    }
}
