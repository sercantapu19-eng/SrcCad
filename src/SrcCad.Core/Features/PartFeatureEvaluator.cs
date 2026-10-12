using SrcCad.Core.Sketch;
using SrcCad.Core.Geometry;
using SrcCad.Core.Persistence;
namespace SrcCad.Core.Features;

public interface IPartGeometryKernel
{
    string Extrude(SketchModel source,double depth);
    string Cut(string baseBody,SketchModel source,double depth,ExtrusionTermination termination);
    string Fillet(string baseBody,IReadOnlyList<string> edgeReferences,double radius);
    string Chamfer(string baseBody,IReadOnlyList<string> edgeReferences,double distance);
    string Hole(string baseBody,string faceReference,Vector3d center,double diameter,double depth,ExtrusionTermination termination,bool reverse);
}
// Strings are immutable; native cache is detached from the document/viewport.
public sealed record PartFeatureOutput(string? SketchData, string? BRep);
public sealed class PartFeatureEvaluator(IPartGeometryKernel kernel) : IFeatureEvaluator<PartFeatureOutput>
{
    public PartFeatureOutput Build(FeatureNode node,IReadOnlyDictionary<Guid,PartFeatureOutput> dependencies)
    {
        SketchModel Sketch(Guid id)
        {
            var data=dependencies[id].SketchData??throw new InvalidOperationException("Expected sketch input.");
            var state=PartFile.Deserialize(data);var model=new SketchModel(state.Plane);model.RestoreSnapshot(state.Sketch);return model;
        }
        string Base(Guid id)=>dependencies[id].BRep??throw new InvalidOperationException("Expected base solid.");
        IReadOnlyList<string> EdgeReferences(Guid baseId)
        {
            var references=node.References??Array.Empty<FeatureReference>();
            if(references.Length==0||references.Any(r=>r.ProducerId!=baseId||r.Kind!=TopologyKind.Edge))
                throw new InvalidOperationException("Feature requires persistent edge references.");
            return references.Select(r=>r.SemanticName).ToArray();
        }
        return node.Parameters switch
        {
            SketchFeatureParameters s => new(PartFile.Serialize(new(s.Plane,s.Sketch,null)),null),
            ExtrudeFeatureParameters e => new(null,kernel.Extrude(Sketch(e.SketchId),e.Depth)),
            CutFeatureParameters c => new(null,kernel.Cut(Base(c.BaseFeatureId),
                Sketch(c.SketchId),c.Reverse?-c.Depth:c.Depth,c.Termination)),
            FilletFeatureParameters f => new(null,kernel.Fillet(Base(f.BaseFeatureId),EdgeReferences(f.BaseFeatureId),f.Radius)),
            ChamferFeatureParameters c => new(null,kernel.Chamfer(Base(c.BaseFeatureId),EdgeReferences(c.BaseFeatureId),c.Distance)),
            HoleFeatureParameters h => new(null,kernel.Hole(Base(h.BaseFeatureId),
                (node.References??Array.Empty<FeatureReference>()).Single(r=>r.Kind==TopologyKind.Face&&r.ProducerId==h.BaseFeatureId).SemanticName,
                h.Center,h.Diameter,h.Depth,h.Termination,h.Reverse)),
            _=>throw new NotSupportedException("Feature evaluator unavailable.")
        };
    }
}
