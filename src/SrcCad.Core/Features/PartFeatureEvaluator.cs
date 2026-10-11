using SrcCad.Core.Sketch;
using SrcCad.Core.Persistence;
namespace SrcCad.Core.Features;

public interface IPartGeometryKernel
{
    string Extrude(SketchModel source,double depth);
    string Cut(string baseBody,SketchModel source,double depth,ExtrusionTermination termination);
}
// Strings are immutable; native cache is detached from the document/viewport.
public sealed record PartFeatureOutput(string? SketchData, string? BRep);
public sealed class PartFeatureEvaluator(IPartGeometryKernel kernel) : IFeatureEvaluator<PartFeatureOutput>
{
    public PartFeatureOutput Build(FeatureNode node,IReadOnlyDictionary<Guid,PartFeatureOutput> dependencies)
    {
        // Fail closed until native topology provenance resolves the semantic support.
        if(node.References is {Length:>0})throw new NotSupportedException("Semantic face/edge support resolution is not implemented yet.");
        SketchModel Sketch(Guid id)
        {
            var data=dependencies[id].SketchData??throw new InvalidOperationException("Expected sketch input.");
            var state=PartFile.Deserialize(data);var model=new SketchModel(state.Plane);model.RestoreSnapshot(state.Sketch);return model;
        }
        return node.Parameters switch
        {
            SketchFeatureParameters s => new(PartFile.Serialize(new(s.Plane,s.Sketch,null)),null),
            ExtrudeFeatureParameters e => new(null,kernel.Extrude(Sketch(e.SketchId),e.Depth)),
            CutFeatureParameters c => new(null,kernel.Cut(dependencies[c.BaseFeatureId].BRep??throw new InvalidOperationException("Expected base solid."),
                Sketch(c.SketchId),c.Reverse?-c.Depth:c.Depth,c.Termination)),
            _=>throw new NotSupportedException("Feature evaluator unavailable.")
        };
    }
}
