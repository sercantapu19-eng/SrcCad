#if NATIVE_ACCEPTANCE
using SrcCad.Core.Geometry;
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
    }
}
#endif
