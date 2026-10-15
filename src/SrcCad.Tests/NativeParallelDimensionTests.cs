#if NATIVE_ACCEPTANCE
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.History;
using SrcCad.Core.Persistence;
using SrcCad.Kernel.Occt;

internal static class NativeParallelDimensionTests
{
    public static void Run()
    {
        int count=0;
        foreach(var p in StandardPlanes.All)
        {
            void Check(SketchModel model,double expectedArea)
            {
                if(!model.TryGetExtrusionProfile(out var loop,out _,out _))throw new Exception("Parallel dimension profile failed");
                var uv=loop.SelectMany(v=>new[]{v.X,v.Y}).ToArray();
                var volume=OcctViewportBridge.MeasureExtrudedProfileVolume(uv,Array.Empty<double>(),Array.Empty<double>(),p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,20);
                if(!double.IsFinite(volume)||Math.Abs(volume-expectedArea*20)>1e-4)throw new Exception($"Native parallel volume mismatch: {volume:R}");
                count++;
            }
            var s=new SketchModel(p);var ids=s.AddThreePointRectangle(new(0,0),new(80,60),new(-18,24),0.001);
            var width=s.AddDimension(SketchDimensionType.LineLength,new(50,100),entityA:ids[0]);
            var distance=s.AddInferredDimension(s.InferDimension(new[]{s.ReferenceForEntity(ids[0]),s.ReferenceForEntity(ids[2])},new(-50,50))!);
            if(!s.SetDimensionValue(width.Id,100)||!s.SetDimensionValue(distance.Id,30))throw new Exception("Native distance setup failed");
            Check(s,3000);
            if(!s.SetDimensionValue(distance.Id,45))throw new Exception("Native distance edit failed");
            Check(s,4500);
            var copy=new SketchModel(p);copy.RestoreSnapshot(PartFile.Deserialize(PartFile.Serialize(new ExtrusionState(p,s.CaptureSnapshot(),20))).Sketch);
            if(!copy.SetDimensionValue(distance.Id,60))throw new Exception("Native distance round-trip edit failed");
            Check(copy,6000);
        }
        Console.WriteLine($"NATIVE PARALLEL DIMENSIONS: {count}/9 PASS");
    }
}
#endif
