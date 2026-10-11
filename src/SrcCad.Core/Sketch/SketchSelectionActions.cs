namespace SrcCad.Core.Sketch;

public static class SketchSelectionActions
{
    public static IReadOnlyList<SketchRelationType> Relations(IReadOnlyList<GeometryReference> selected)
    {
        if(selected.Count==1)
            return selected[0].IsLine ? new[]{SketchRelationType.Horizontal,SketchRelationType.Vertical} :
                selected[0].IsPointLike && selected[0].Kind!=GeometryReferenceKind.Origin && selected[0].Kind!=GeometryReferenceKind.Midpoint ? new[]{SketchRelationType.Fixed} : Array.Empty<SketchRelationType>();
        if(selected.Count!=2||selected[0]==selected[1])return Array.Empty<SketchRelationType>();
        var a=selected[0];var b=selected[1];
        if(a.IsPointLike&&b.IsPointLike)return new[]{SketchRelationType.Horizontal,SketchRelationType.Vertical,SketchRelationType.Coincident};
        if(a.IsLine&&b.IsLine)return new[]{SketchRelationType.Parallel,SketchRelationType.Perpendicular,SketchRelationType.Horizontal,SketchRelationType.Vertical,SketchRelationType.Equal,SketchRelationType.Collinear};
        if(a.Kind==GeometryReferenceKind.Circle&&b.Kind==GeometryReferenceKind.Circle)return new[]{SketchRelationType.Equal,SketchRelationType.Concentric,SketchRelationType.Tangent};
        if(a.IsLine&&b.Kind==GeometryReferenceKind.Circle||b.IsLine&&a.Kind==GeometryReferenceKind.Circle)return new[]{SketchRelationType.Tangent};
        if(a.IsPointLike&&b.IsLine||b.IsPointLike&&a.IsLine)return new[]{SketchRelationType.Coincident,SketchRelationType.Midpoint};
        if(a.IsPointLike&&b.Kind==GeometryReferenceKind.Circle||b.IsPointLike&&a.Kind==GeometryReferenceKind.Circle)return new[]{SketchRelationType.Coincident};
        return Array.Empty<SketchRelationType>();
    }
    public static bool CanDimension(IReadOnlyList<GeometryReference> refs)
        => refs.Count==1&&!refs[0].IsPointLike || refs.Count==2 && refs[0]!=refs[1] &&
            (refs.All(r=>r.IsPointLike)||refs.All(r=>r.IsLine)||refs[0].IsLine&&refs[1].IsPointLike||refs[1].IsLine&&refs[0].IsPointLike);
}
