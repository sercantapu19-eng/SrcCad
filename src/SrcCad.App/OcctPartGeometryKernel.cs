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
}
