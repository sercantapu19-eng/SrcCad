using System.Text.Json;
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
    string Revolve(string? baseBody,SketchModel source,int axisEntityId,double angleDegrees,bool reverse);
    string RevolveCut(string baseBody,SketchModel source,int axisEntityId,double angleDegrees,bool reverse);
    string Sweep(string? baseBody,SketchModel profile,SketchModel path);
    string SweepSpatial(string? baseBody,SketchModel profile,IReadOnlyList<Vector3d> path);
    string SweepCut(string baseBody,SketchModel profile,SketchModel path);
    string SweepCutSpatial(string baseBody,SketchModel profile,IReadOnlyList<Vector3d> path);
    string Loft(string? baseBody,IReadOnlyList<SketchModel> profiles);
    string LoftCut(string baseBody,IReadOnlyList<SketchModel> profiles);
    string Shell(string baseBody,IReadOnlyList<string> faceReferences,double thickness,bool outward);
    string Draft(string baseBody,IReadOnlyList<string> faceReferences,PlaneFrame neutralPlane,double angleDegrees,bool reverse);
    string Rib(string baseBody,SketchModel source,double thickness,double depth,bool reverse);
    string Mirror(string baseBody,PlaneFrame plane,bool keepOriginal);
    string LinearPattern(string baseBody,Vector3d direction,double spacing,int count);
    string CircularPattern(string baseBody,Vector3d axisOrigin,Vector3d axisDirection,double angleDegrees,int count);
    string EdgeFlange(string baseBody,IReadOnlyList<string> edgeReferences,double length,double angleDegrees,double thickness,double bendRadius,bool reverse);
    string Hem(string baseBody,IReadOnlyList<string> edgeReferences,double width,double thickness,double bendRadius,bool reverse);
    string Jog(string baseBody,IReadOnlyList<string> edgeReferences,double height,double offset,double thickness,bool reverse);
    string Rip(string baseBody,IReadOnlyList<string> edgeReferences,double gap);
    string CornerRelief(string baseBody,IReadOnlyList<string> edgeReferences,double radius);
    string EndCap(string baseBody,string faceReference,double thickness,bool reverse);
    string WeldBead(string baseBody,IReadOnlyList<string> edgeReferences,double radius);
}
// Strings are immutable; native cache is detached from the document/viewport.
public sealed record PartFeatureOutput(string? SketchData, string? BRep, string? SpatialPathData = null);
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
        IReadOnlyList<Vector3d> SpatialPath(Guid id)
        {
            var json=dependencies[id].SpatialPathData??throw new InvalidOperationException("Expected 3D sketch path.");
            return JsonSerializer.Deserialize<Vector3d[]>(json)??throw new InvalidDataException("Invalid 3D sketch path data.");
        }
        bool IsSpatial(Guid id)=>dependencies[id].SpatialPathData is not null;
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
            SpatialSketchFeatureParameters s => new(null,null,JsonSerializer.Serialize(s.Points)),
            ExtrudeFeatureParameters e => new(null,kernel.Extrude(Sketch(e.SketchId),e.Depth)),
            CutFeatureParameters c => new(null,kernel.Cut(Base(c.BaseFeatureId),
                Sketch(c.SketchId),c.Reverse?-c.Depth:c.Depth,c.Termination)),
            FilletFeatureParameters f => new(null,kernel.Fillet(Base(f.BaseFeatureId),EdgeReferences(f.BaseFeatureId),f.Radius)),
            ChamferFeatureParameters c => new(null,kernel.Chamfer(Base(c.BaseFeatureId),EdgeReferences(c.BaseFeatureId),c.Distance)),
            HoleFeatureParameters h => new(null,kernel.Hole(Base(h.BaseFeatureId),
                (node.References??Array.Empty<FeatureReference>()).Single(r=>r.Kind==TopologyKind.Face&&r.ProducerId==h.BaseFeatureId).SemanticName,
                h.Center,h.Diameter,h.Depth,h.Termination,h.Reverse)),
            RevolveFeatureParameters r => new(null,kernel.Revolve(r.BaseFeatureId is Guid rb?Base(rb):null,Sketch(r.SketchId),r.AxisEntityId,r.AngleDegrees,r.Reverse)),
            RevolveCutFeatureParameters r => new(null,kernel.RevolveCut(Base(r.BaseFeatureId),Sketch(r.SketchId),r.AxisEntityId,r.AngleDegrees,r.Reverse)),
            SweepFeatureParameters s => new(null,IsSpatial(s.PathSketchId)
                ?kernel.SweepSpatial(s.BaseFeatureId is Guid sb?Base(sb):null,Sketch(s.ProfileSketchId),SpatialPath(s.PathSketchId))
                :kernel.Sweep(s.BaseFeatureId is Guid sb2?Base(sb2):null,Sketch(s.ProfileSketchId),Sketch(s.PathSketchId))),
            SweepCutFeatureParameters s => new(null,IsSpatial(s.PathSketchId)
                ?kernel.SweepCutSpatial(Base(s.BaseFeatureId),Sketch(s.ProfileSketchId),SpatialPath(s.PathSketchId))
                :kernel.SweepCut(Base(s.BaseFeatureId),Sketch(s.ProfileSketchId),Sketch(s.PathSketchId))),
            LoftFeatureParameters l => new(null,kernel.Loft(l.BaseFeatureId is Guid lb?Base(lb):null,l.ProfileSketchIds.Select(Sketch).ToArray())),
            LoftCutFeatureParameters l => new(null,kernel.LoftCut(Base(l.BaseFeatureId),l.ProfileSketchIds.Select(Sketch).ToArray())),
            ShellFeatureParameters s => new(null,kernel.Shell(Base(s.BaseFeatureId),
                (node.References??Array.Empty<FeatureReference>()).Where(r=>r.Kind==TopologyKind.Face&&r.ProducerId==s.BaseFeatureId).Select(r=>r.SemanticName).ToArray(),
                s.Thickness,s.Outward)),
            DraftFeatureParameters d => new(null,kernel.Draft(Base(d.BaseFeatureId),
                (node.References??Array.Empty<FeatureReference>()).Where(r=>r.Kind==TopologyKind.Face&&r.ProducerId==d.BaseFeatureId).Select(r=>r.SemanticName).ToArray(),
                d.NeutralPlane,d.AngleDegrees,d.Reverse)),
            RibFeatureParameters r => new(null,kernel.Rib(Base(r.BaseFeatureId),Sketch(r.SketchId),r.Thickness,r.Depth,r.Reverse)),
            MirrorFeatureParameters m => new(null,kernel.Mirror(Base(m.BaseFeatureId),m.Plane,m.KeepOriginal)),
            LinearPatternFeatureParameters p => new(null,kernel.LinearPattern(Base(p.BaseFeatureId),p.Direction,p.Spacing,p.Count)),
            CircularPatternFeatureParameters p => new(null,kernel.CircularPattern(Base(p.BaseFeatureId),p.AxisOrigin,p.AxisDirection,p.AngleDegrees,p.Count)),
            ReferencePlaneFeatureParameters => new(null,null),
            ReferenceAxisFeatureParameters => new(null,null),
            ReferencePointFeatureParameters => new(null,null),
            SheetMetalBaseFeatureParameters s => new(null,kernel.Extrude(Sketch(s.SketchId),s.Reverse?-s.Thickness:s.Thickness)),
            SheetMetalCutFeatureParameters s => new(null,kernel.Cut(Base(s.BaseFeatureId),Sketch(s.SketchId),s.Reverse?-s.Depth:s.Depth,s.Termination)),
            SheetMetalEdgeFlangeFeatureParameters s => new(null,kernel.EdgeFlange(Base(s.BaseFeatureId),EdgeReferences(s.BaseFeatureId),
                s.Length,s.AngleDegrees,s.Thickness,s.BendRadius,s.Reverse)),
            SheetMetalMiterFlangeFeatureParameters s => new(null,kernel.EdgeFlange(Base(s.BaseFeatureId),EdgeReferences(s.BaseFeatureId),
                s.Length,s.AngleDegrees,s.Thickness,s.BendRadius,s.Reverse)),
            SheetMetalConvertFeatureParameters s => new(null,Base(s.BaseFeatureId)),
            SheetMetalHemFeatureParameters s => new(null,kernel.Hem(Base(s.BaseFeatureId),EdgeReferences(s.BaseFeatureId),s.Width,s.Thickness,s.BendRadius,s.Reverse)),
            SheetMetalJogFeatureParameters s => new(null,kernel.Jog(Base(s.BaseFeatureId),EdgeReferences(s.BaseFeatureId),s.Height,s.Offset,s.Thickness,s.Reverse)),
            SheetMetalRipFeatureParameters s => new(null,kernel.Rip(Base(s.BaseFeatureId),EdgeReferences(s.BaseFeatureId),s.Gap)),
            SheetMetalCornerReliefFeatureParameters s => new(null,kernel.CornerRelief(Base(s.BaseFeatureId),EdgeReferences(s.BaseFeatureId),s.Radius)),
            StructuralMemberFeatureParameters s => new(null,IsSpatial(s.PathSketchId)
                ?kernel.SweepSpatial(null,Sketch(s.ProfileSketchId),SpatialPath(s.PathSketchId))
                :kernel.Sweep(null,Sketch(s.ProfileSketchId),Sketch(s.PathSketchId))),
            EndCapFeatureParameters e => new(null,kernel.EndCap(Base(e.BaseFeatureId),
                (node.References??Array.Empty<FeatureReference>()).Single(r=>r.Kind==TopologyKind.Face&&r.ProducerId==e.BaseFeatureId).SemanticName,
                e.Thickness,e.Reverse)),
            WeldBeadFeatureParameters w => new(null,kernel.WeldBead(Base(w.BaseFeatureId),EdgeReferences(w.BaseFeatureId),w.Radius)),
            _=>throw new NotSupportedException("Feature evaluator unavailable.")
        };
    }
}
