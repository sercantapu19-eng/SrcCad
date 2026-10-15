using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

/// <summary>Selection/placement state machine shared by desktop input and regressions.</summary>
public sealed class SmartDimensionSession
{
    private readonly List<GeometryReference> references=new();
    public IReadOnlyList<GeometryReference> References => references;
    public bool CanPlace => references.Count==2 || references.Count==1&&!references[0].IsPointLike;
    public void Reset() => references.Clear();
    public void Preselect(IEnumerable<GeometryReference> selection)
    {
        Reset();references.AddRange(selection.Distinct().Take(2));
    }
    public bool Pick(SketchModel model,Point2d cursor,double tolerance)
    {
        var hit=model.HitReference(cursor,tolerance);
        if(references.Count==0)
        {if(hit is not null)references.Add(hit);return false;}
        if(references.Count==1 && hit is not null && hit!=references[0])
        {
            if((references[0].IsPointLike&&(hit.IsPointLike||hit.IsLine)) ||
               (references[0].IsLine&&(hit.IsLine||hit.IsPointLike)))
            {references.Add(hit);return false;}
        }
        return CanPlace;
    }
    public SketchDimension? Preview(SketchModel model,Point2d label) => model.InferDimension(references,label);
}
