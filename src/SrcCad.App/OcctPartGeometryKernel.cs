using System;
using System.Collections.Generic;
using System.Linq;
using SrcCad.Core.Features;
using SrcCad.Core.Sketch;
using SrcCad.Kernel.Occt;
namespace SrcCad.App;

internal sealed class OcctPartGeometryKernel : IPartGeometryKernel
{
    private sealed record Profile(double[] Outer,double[] Holes,double[] CircleHoles,double[] Circle,double[] Frame);
    private static Profile Pack(SketchModel sketch,double depth)
    {
        var p=sketch.Plane;
        double[] frame={p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,depth};
        if(sketch.TryGetExtrusionProfile(out var loop,out var polygonHoles,out var circles))
        {
            var holes=new List<double>();
            foreach(var hole in polygonHoles){holes.Add(hole.Count);foreach(var point in hole){holes.Add(point.X);holes.Add(point.Y);}}
            var round=circles.SelectMany(c=>new[]{sketch.Point(c.CenterPointId).Position.X,sketch.Point(c.CenterPointId).Position.Y,c.Radius}).ToArray();
            return new(loop.SelectMany(v=>new[]{v.X,v.Y}).ToArray(),holes.ToArray(),round,Array.Empty<double>(),frame);
        }
        if(sketch.TryGetSingleCircle(out var circle))
        {
            var center=sketch.Point(circle.CenterPointId).Position;
            return new(Array.Empty<double>(),Array.Empty<double>(),Array.Empty<double>(),new[]{center.X,center.Y,circle.Radius},frame);
        }
        throw new InvalidOperationException("Feature requires a valid closed sketch profile.");
    }
    public string Extrude(SketchModel source,double depth)
    {
        var p=Pack(source,depth);return OcctFeatureKernel.Extrude(p.Outer,p.Holes,p.CircleHoles,p.Circle,p.Frame);
    }
    public string Cut(string baseBody,SketchModel source,double depth,ExtrusionTermination termination)
    {
        var p=Pack(source,depth);return OcctFeatureKernel.Cut(baseBody,p.Outer,p.Holes,p.CircleHoles,p.Circle,p.Frame,termination==ExtrusionTermination.ThroughAll);
    }
    public string Fillet(string baseBody,IReadOnlyList<string> edgeReferences,double radius)
        => OcctFeatureKernel.Fillet(baseBody,edgeReferences.ToArray(),radius);
    public string Chamfer(string baseBody,IReadOnlyList<string> edgeReferences,double distance)
        => OcctFeatureKernel.Chamfer(baseBody,edgeReferences.ToArray(),distance);
    public string Hole(string baseBody,string faceReference,SrcCad.Core.Geometry.Vector3d center,double diameter,double depth,
        ExtrusionTermination termination,bool reverse)
        => OcctFeatureKernel.Hole(baseBody,faceReference,center.X,center.Y,center.Z,diameter,depth,
            termination==ExtrusionTermination.ThroughAll,reverse);

    private static double[] Axis(SketchModel source,int axisEntityId)
    {
        var line=source.Entity(axisEntityId) as SketchLine;
        if(line is null||!line.Construction)throw new InvalidOperationException("Revolve requires a construction centerline axis.");
        var a=source.Plane.SketchToWorld(source.Point(line.StartPointId).Position.X,source.Point(line.StartPointId).Position.Y);
        var b=source.Plane.SketchToWorld(source.Point(line.EndPointId).Position.X,source.Point(line.EndPointId).Position.Y);
        if((b-a).Length<1e-9)throw new InvalidOperationException("Revolve axis has zero length.");
        return new[]{a.X,a.Y,a.Z,b.X,b.Y,b.Z};
    }
    public string Revolve(string? baseBody,SketchModel source,int axisEntityId,double angleDegrees,bool reverse)
    {
        var p=Pack(source,1);return OcctFeatureKernel.Revolve(baseBody,p.Outer,p.Holes,p.CircleHoles,p.Circle,p.Frame,Axis(source,axisEntityId),angleDegrees,reverse);
    }
    public string RevolveCut(string baseBody,SketchModel source,int axisEntityId,double angleDegrees,bool reverse)
    {
        var p=Pack(source,1);return OcctFeatureKernel.RevolveCut(baseBody,p.Outer,p.Holes,p.CircleHoles,p.Circle,p.Frame,Axis(source,axisEntityId),angleDegrees,reverse);
    }
    private static double[] PackSweepPath(SketchModel path)
    {
        var lines=path.Entities.OfType<SketchLine>().Where(l=>!l.Construction).ToArray();
        if(lines.Length==0)throw new InvalidOperationException("Sweep path requires non-construction lines.");
        var byPoint=lines.SelectMany(l=>new[]{(Point:l.StartPointId,Line:l),(Point:l.EndPointId,Line:l)})
            .GroupBy(x=>x.Point).ToDictionary(g=>g.Key,g=>g.Select(x=>x.Line).Distinct().ToArray());
        if(byPoint.Values.Any(x=>x.Length>2))throw new InvalidOperationException("Sweep path must be a single non-branching chain.");
        var ends=byPoint.Where(x=>x.Value.Length==1).Select(x=>x.Key).ToArray();
        if(ends.Length is not (0 or 2))throw new InvalidOperationException("Sweep path must be open or closed.");
        var start=ends.Length==2?ends[0]:lines[0].StartPointId;
        var ordered=new List<int>{start};var used=new HashSet<int>();var current=start;
        while(used.Count<lines.Length)
        {
            var next=byPoint[current].FirstOrDefault(l=>!used.Contains(l.Id));
            if(next is null)throw new InvalidOperationException("Sweep path contains disconnected segments.");
            used.Add(next.Id);
            current=next.StartPointId==current?next.EndPointId:next.StartPointId;
            ordered.Add(current);
        }
        var values=new List<double>();
        foreach(var id in ordered)
        {
            var uv=path.Point(id).Position;var w=path.Plane.SketchToWorld(uv.X,uv.Y);
            values.AddRange(new[]{w.X,w.Y,w.Z});
        }
        return values.ToArray();
    }

    private static Profile SimpleProfile(SketchModel sketch)
    {
        var p=Pack(sketch,1);
        if(p.Holes.Length>0||p.CircleHoles.Length>0)
            throw new InvalidOperationException("Sweep/Loft currently requires a single outer profile without holes.");
        return p;
    }

    private static double[] PackSpatialPath(IReadOnlyList<SrcCad.Core.Geometry.Vector3d> path)
    {
        if(path.Count<2)throw new InvalidOperationException("3D sweep path requires at least two points.");
        var values=new List<double>();
        for(var i=0;i<path.Count;i++)
        {
            var p=path[i];
            if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z))
                throw new InvalidOperationException("3D path point must be finite.");
            if(i>0&&(p-path[i-1]).Length<=1e-9)throw new InvalidOperationException("3D path contains duplicate consecutive points.");
            values.AddRange(new[]{p.X,p.Y,p.Z});
        }
        return values.ToArray();
    }

    public string Sweep(string? baseBody,SketchModel profile,SketchModel path)
    {
        var p=SimpleProfile(profile);
        return OcctFeatureKernel.Sweep(baseBody,p.Outer,p.Circle,p.Frame,PackSweepPath(path));
    }
    public string SweepSpatial(string? baseBody,SketchModel profile,IReadOnlyList<SrcCad.Core.Geometry.Vector3d> path)
    {
        var p=SimpleProfile(profile);
        return OcctFeatureKernel.Sweep(baseBody,p.Outer,p.Circle,p.Frame,PackSpatialPath(path));
    }
    public string SweepCut(string baseBody,SketchModel profile,SketchModel path)
    {
        var p=SimpleProfile(profile);
        return OcctFeatureKernel.SweepCut(baseBody,p.Outer,p.Circle,p.Frame,PackSweepPath(path));
    }
    public string SweepCutSpatial(string baseBody,SketchModel profile,IReadOnlyList<SrcCad.Core.Geometry.Vector3d> path)
    {
        var p=SimpleProfile(profile);
        return OcctFeatureKernel.SweepCut(baseBody,p.Outer,p.Circle,p.Frame,PackSpatialPath(path));
    }
    public string Loft(string? baseBody,IReadOnlyList<SketchModel> profiles)
    {
        if(profiles.Count<2)throw new InvalidOperationException("Loft requires at least two profiles.");
        var packed=profiles.Select(SimpleProfile).ToArray();
        return OcctFeatureKernel.Loft(baseBody,packed.Select(p=>p.Outer).ToArray(),packed.Select(p=>p.Circle).ToArray(),packed.Select(p=>p.Frame).ToArray());
    }
    public string LoftCut(string baseBody,IReadOnlyList<SketchModel> profiles)
    {
        if(profiles.Count<2)throw new InvalidOperationException("Loft cut requires at least two profiles.");
        var packed=profiles.Select(SimpleProfile).ToArray();
        return OcctFeatureKernel.LoftCut(baseBody,packed.Select(p=>p.Outer).ToArray(),packed.Select(p=>p.Circle).ToArray(),packed.Select(p=>p.Frame).ToArray());
    }

    public string Shell(string baseBody,IReadOnlyList<string> faceReferences,double thickness,bool outward)
        => OcctFeatureKernel.Shell(baseBody,faceReferences.ToArray(),thickness,outward);
    public string Draft(string baseBody,IReadOnlyList<string> faceReferences,SrcCad.Core.Geometry.PlaneFrame neutralPlane,double angleDegrees,bool reverse)
        => OcctFeatureKernel.Draft(baseBody,faceReferences.ToArray(),
            new[]{neutralPlane.Origin.X,neutralPlane.Origin.Y,neutralPlane.Origin.Z,
                  neutralPlane.Normal.X,neutralPlane.Normal.Y,neutralPlane.Normal.Z},
            angleDegrees,reverse);
    public string Rib(string baseBody,SketchModel source,double thickness,double depth,bool reverse)
    {
        var p=source.Plane;
        var lines=source.Entities.OfType<SketchLine>().Where(line=>!line.Construction).ToArray();
        if(lines.Length==0)throw new InvalidOperationException("Rib requires at least one non-construction sketch line.");
        var data=new List<double>();
        foreach(var line in lines)
        {
            var a=source.Point(line.StartPointId).Position;
            var b=source.Point(line.EndPointId).Position;
            if(Point2d.Distance(a,b)<=1e-9)continue;
            data.AddRange(new[]{a.X,a.Y,b.X,b.Y});
        }
        if(data.Count==0)throw new InvalidOperationException("Rib sketch contains no usable line.");
        return OcctFeatureKernel.Rib(baseBody,data.ToArray(),
            new[]{p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z},
            thickness,depth,reverse);
    }
    public string Mirror(string baseBody,SrcCad.Core.Geometry.PlaneFrame plane,bool keepOriginal)
        => OcctFeatureKernel.Mirror(baseBody,
            new[]{plane.Origin.X,plane.Origin.Y,plane.Origin.Z,plane.U.X,plane.U.Y,plane.U.Z,plane.Normal.X,plane.Normal.Y,plane.Normal.Z},
            keepOriginal);
    public string LinearPattern(string baseBody,SrcCad.Core.Geometry.Vector3d direction,double spacing,int count)
        => OcctFeatureKernel.LinearPattern(baseBody,new[]{direction.X,direction.Y,direction.Z},spacing,count);
    public string CircularPattern(string baseBody,SrcCad.Core.Geometry.Vector3d axisOrigin,SrcCad.Core.Geometry.Vector3d axisDirection,double angleDegrees,int count)
        => OcctFeatureKernel.CircularPattern(baseBody,new[]{axisOrigin.X,axisOrigin.Y,axisOrigin.Z,axisDirection.X,axisDirection.Y,axisDirection.Z},angleDegrees,count);
    public string EdgeFlange(string baseBody,IReadOnlyList<string> edgeReferences,double length,double angleDegrees,double thickness,double bendRadius,bool reverse)
        => OcctFeatureKernel.EdgeFlange(baseBody,edgeReferences.ToArray(),length,angleDegrees,thickness,bendRadius,reverse);
    public string Hem(string baseBody,IReadOnlyList<string> edgeReferences,double width,double thickness,double bendRadius,bool reverse)
        => OcctFeatureKernel.Hem(baseBody,edgeReferences.ToArray(),width,thickness,bendRadius,reverse);
    public string Jog(string baseBody,IReadOnlyList<string> edgeReferences,double height,double offset,double thickness,bool reverse)
        => OcctFeatureKernel.Jog(baseBody,edgeReferences.ToArray(),height,offset,thickness,reverse);
    public string Rip(string baseBody,IReadOnlyList<string> edgeReferences,double gap)
        => OcctFeatureKernel.Rip(baseBody,edgeReferences.ToArray(),gap);
    public string CornerRelief(string baseBody,IReadOnlyList<string> edgeReferences,double radius)
        => OcctFeatureKernel.CornerRelief(baseBody,edgeReferences.ToArray(),radius);
    public string EndCap(string baseBody,string faceReference,double thickness,bool reverse)
        => OcctFeatureKernel.EndCap(baseBody,faceReference,thickness,reverse);
    public string WeldBead(string baseBody,IReadOnlyList<string> edgeReferences,double radius)
        => OcctFeatureKernel.WeldBead(baseBody,edgeReferences.ToArray(),radius);
}
