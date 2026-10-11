#if NATIVE_ACCEPTANCE
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Kernel.Occt;
internal static class NativeAngleEditingTests
{
    public static void Run()
    {
        int count=0;
        foreach(var p in StandardPlanes.All)
        {
            var s=new SketchModel(p);var o=s.CreatePointExact(new(0,0));var a=s.CreatePointExact(new(100,0));var b=s.CreatePointExact(new(70.71067811865,70.71067811865));
            s.SetPointFixed(o,true);s.SetPointFixed(a,true);
            var first=s.AddLine(o,a);var second=s.AddLine(o,b);s.AddLine(b,a);
            s.AddRelation(SketchRelationType.Horizontal,first.Id);
            s.AddDimension(SketchDimensionType.LineLength,new(20,85),entityA:second.Id);
            var d=s.AddDimension(SketchDimensionType.Angle,new(60,25),entityA:first.Id,entityB:second.Id);
            if(!s.SetDimensionValue(d.Id,45))throw new Exception("Native angle setup failed");
            void Check(double degrees)
            {
                if(!s.TryGetExtrusionProfile(out var loop,out _,out _))throw new Exception("Native angle profile failed");
                var uv=loop.SelectMany(v=>new[]{v.X,v.Y}).ToArray();
                var volume=OcctViewportBridge.MeasureExtrudedProfileVolume(uv,Array.Empty<double>(),Array.Empty<double>(),p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,20);
                var expected=5000*Math.Sin(degrees*Math.PI/180)*20;
                if(!double.IsFinite(volume)||Math.Abs(volume-expected)>1e-4)throw new Exception($"Native angle volume mismatch {volume:R} vs {expected:R}");
                count++;
            }
            Check(45);var before=s.CaptureSnapshot();
            if(!new DimensionEditSession(s,d.Id).TryCommit("60",out var error))throw new Exception(error);
            Check(60);var after=s.CaptureSnapshot();
            s.RestoreSnapshot(before,true);Check(45);
            s.RestoreSnapshot(after,true);Check(60);
        }
        Console.WriteLine($"NATIVE ANGLE EDITING: {count}/12 PASS");
    }
}
#endif
