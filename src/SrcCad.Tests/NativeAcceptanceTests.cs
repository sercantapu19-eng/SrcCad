#if NATIVE_ACCEPTANCE
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Kernel.Occt;

internal static class NativeAcceptanceTests
{
    public static void Run()
    {
        int id=60, failures=0;
        foreach(var p in StandardPlanes.All)
        foreach(var kind in new[]{"plate","three-holes","polygon-hole","reverse"})
        {
            id++;
            double[] outer={-125,-100,125,-100,125,100,-125,100};
            double[] circles=kind=="three-holes" ? new double[]{-80,0,7.5,0,0,7.5,80,0,7.5} : Array.Empty<double>();
            double[] polygons=kind=="polygon-hole" ? new double[]{4,-20,-10,20,-10,20,10,-20,10} : Array.Empty<double>();
            double depth=kind=="reverse" ? -20 : 20;
            double expected=(50000-(kind=="three-holes"?3*Math.PI*7.5*7.5:kind=="polygon-hole"?800:0))*20;
            double actual=OcctViewportBridge.MeasureExtrudedProfileVolume(outer,polygons,circles,
                p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,
                p.Normal.X,p.Normal.Y,p.Normal.Z,depth);
            bool ok=double.IsFinite(actual)&&Math.Abs(actual-expected)<1e-4;
            Console.WriteLine($"SM-{id:000} {(ok?"PASS":"FAIL")} OCCT {p.Name} {kind}: expected={expected:R}, actual={actual:R}");
            if(!ok)failures++;
        }
        if(failures>0) throw new Exception($"{failures}/12 native B-Rep volume failures");
        Console.WriteLine("NATIVE ACCEPTANCE: 12/12 PASS");
        foreach (var p in StandardPlanes.All)
        {
            var sketch = new SketchModel(p);
            sketch.AddCornerRectangle(new(0,0),new(100,60),0.001);
            var corner = sketch.FindPointNear(new(100,60),0.001);
            var dimension = sketch.AddDimension(SketchDimensionType.HorizontalDistance,new(50,80),pointA:0,pointB:corner);
            var before = sketch.CaptureSnapshot();
            if (!sketch.SetDimensionValue(dimension.Id,150)) throw new Exception("Native source edit rejected");
            var after = sketch.CaptureSnapshot();
            foreach (var sample in new[] { (before,100.0), (after,150.0), (before,100.0), (after,150.0) })
            {
                sketch.RestoreSnapshot(sample.Item1);
                if (!sketch.TryGetExtrusionProfile(out var loop,out _,out _)) throw new Exception("Native source profile invalid");
                var coordinates = loop.SelectMany(point => new[] { point.X, point.Y }).ToArray();
                var volume = OcctViewportBridge.MeasureExtrudedProfileVolume(coordinates,Array.Empty<double>(),Array.Empty<double>(),
                    p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,20);
                if (!double.IsFinite(volume) || Math.Abs(volume-sample.Item2*60*20)>1e-4)
                    throw new Exception($"Native source edit volume mismatch: {volume:R}");
            }
        }
        Console.WriteLine("NATIVE SOURCE SKETCH REGRESSION: 12/12 PASS (before/edit/restore/reapply geometry)");
        NativeReferenceRoundtripTests.Run();
        NativeThreePointTests.Run();
        NativeParallelDimensionTests.Run();
        NativeAngleEditingTests.Run();
        NativeDeletionHistoryTests.Run();
    }
}
#endif
