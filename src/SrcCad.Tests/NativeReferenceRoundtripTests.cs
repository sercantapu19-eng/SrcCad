#if NATIVE_ACCEPTANCE
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.History;
using SrcCad.Core.Persistence;
using SrcCad.Kernel.Occt;

internal static class NativeReferenceRoundtripTests
{
    public static void Run()
    {
        int count=0;
        foreach(var p in StandardPlanes.All)
        {
            var s=new SketchModel(p);s.AddCornerRectangle(new(0,0),new(100,60),0.001);
            var a=s.AddCircle(s.CreatePointExact(new(30,30)),5);var b=s.AddCircle(s.CreatePointExact(new(70,30)),8);
            var center=s.ReferenceForPoint(a.CenterPointId);
            if(!s.ApplyReferenceRelation(SketchRelationType.Horizontal,new[]{center,s.ReferenceForPoint(b.CenterPointId)}))throw new Exception("Native relation setup failed");
            var y=s.AddInferredDimension(s.InferDimension(new[]{s.ReferenceForPoint(0),center},new(-20,15))!);
            var diameter=s.AddInferredDimension(s.InferDimension(new[]{s.ReferenceForEntity(a.Id)},new(30,10))!);
            var state=new ExtrusionState(p,s.CaptureSnapshot(),20);
            var reopened=PartFile.Deserialize(PartFile.Serialize(state));
            s=new SketchModel(reopened.Plane);s.RestoreSnapshot(reopened.Sketch);
            var before=s.CaptureSnapshot();
            if(!s.SetDimensionValue(y.Id,35)||!s.SetDimensionValue(diameter.Id,20))throw new Exception("Post-open driving edit failed");
            var after=s.CaptureSnapshot();
            foreach(var sample in new[]{(before,5.0),(after,10.0),(before,5.0),(after,10.0)})
            {
                s.RestoreSnapshot(sample.Item1);
                if(!s.TryGetExtrusionProfile(out var loop,out var polygons,out var circles)||polygons.Count!=0)throw new Exception("Native reference profile invalid");
                var outer=loop.SelectMany(v=>new[]{v.X,v.Y}).ToArray();
                var holes=circles.SelectMany(c=>new[]{s.Point(c.CenterPointId).Position.X,s.Point(c.CenterPointId).Position.Y,c.Radius}).ToArray();
                var volume=OcctViewportBridge.MeasureExtrudedProfileVolume(outer,Array.Empty<double>(),holes,p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,20);
                var expected=(6000-Math.PI*(sample.Item2*sample.Item2+64))*20;
                if(!double.IsFinite(volume)||Math.Abs(volume-expected)>1e-4)throw new Exception($"Post-open OCCT volume mismatch: {volume:R} vs {expected:R}");
                count++;
            }
        }
        Console.WriteLine($"NATIVE REFERENCE ROUNDTRIP: {count}/12 PASS");
    }
}
#endif
