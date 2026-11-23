#if NATIVE_ACCEPTANCE
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Kernel.Occt;

internal static class NativeThreePointTests
{
    public static void Run()
    {
        int count=0;
        foreach(var p in StandardPlanes.All)
        {
            void Check(SketchModel model,double area)
            {
                if(!model.TryGetExtrusionProfile(out var loop,out _,out var circles))throw new Exception("Native three-point profile extraction failed");
                var uv=loop.SelectMany(v=>new[]{v.X,v.Y}).ToArray();
                var holes=circles.SelectMany(c=>new[]{model.Point(c.CenterPointId).Position.X,model.Point(c.CenterPointId).Position.Y,c.Radius}).ToArray();
                var volume=OcctViewportBridge.MeasureExtrudedProfileVolume(uv,Array.Empty<double>(),holes,p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,20);
                if(!double.IsFinite(volume)||Math.Abs(volume-area*20)>1e-4)throw new Exception($"Native three-point volume mismatch: {volume:R}");
                count++;
            }
            var s=new SketchModel(p);var ids=s.AddThreePointRectangle(new(0,0),new(80,60),new(-18,24),0.001);
            Check(s,3000);
            var width=s.AddDimension(SketchDimensionType.LineLength,new(50,100),entityA:ids[0]);
            var height=s.AddDimension(SketchDimensionType.LineLength,new(100,50),entityA:ids[1]);
            if(!s.SetDimensionValue(height.Id,30)||!s.SetDimensionValue(width.Id,120))throw new Exception("Native rotated rectangle edit failed");
            Check(s,3600);
            var circle=new SketchModel(p);circle.AddCornerRectangle(new(-50,-50),new(50,50),0.001);
            var c=SketchModel.CircleThrough(new(10,0),new(0,10),new(-10,0));circle.AddCircle(circle.CreatePointExact(c.Center),c.Radius);
            Check(circle,10000-Math.PI*100);
        }
        Console.WriteLine($"NATIVE THREE POINT: {count}/9 PASS");
    }
}
#endif
