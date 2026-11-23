using System.Diagnostics;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;

internal static class PerformanceSmokeTests
{
    public static void Run()
    {
        foreach(var size in new[]{100,500})
        {
            var s=new SketchModel(StandardPlanes.Front);var ids=new int[size];
            for(int i=0;i<size;i++)ids[i]=s.AddLine(s.CreatePointExact(new(i*4,10)),s.CreatePointExact(new(i*4+2,12))).Id;
            var timer=Stopwatch.StartNew();
            for(int query=0;query<200;query++)
            {
                int i=query%size;
                var hit=s.HitReference(new(i*4+1,11),0.01);
                if(hit is null||hit.Id!=ids[i]||!hit.IsLine)throw new Exception("Performance hit-test correctness regression");
            }
            timer.Stop();Console.WriteLine($"PERFORMANCE OBSERVATION: {size} entities, 200 body hits, {timer.Elapsed.TotalMilliseconds:0.###} ms (host-specific, not a Windows UI latency gate)");
        }
        Console.WriteLine("PERFORMANCE SMOKE: 2/2 PASS (hit-test correctness at scale)");
    }
}
