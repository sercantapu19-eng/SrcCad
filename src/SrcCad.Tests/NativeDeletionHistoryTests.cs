#if NATIVE_ACCEPTANCE
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Kernel.Occt;
internal static class NativeDeletionHistoryTests
{
    public static void Run()
    {
        int count=0;
        foreach(var p in StandardPlanes.All)
        {
            var s=new SketchModel(p);
            var lines=s.AddCornerRectangle(new(0,0),new(40,30),0.001);
            var undo=new Stack<SketchSnapshot>();var redo=new Stack<SketchSnapshot>();
            void CheckRebuiltVolume()
            {
                if(!s.TryGetExtrusionProfile(out var loop,out _,out _))throw new Exception("Restored source profile invalid");
                var uv=loop.SelectMany(v=>new[]{v.X,v.Y}).ToArray();
                var volume=OcctViewportBridge.MeasureExtrudedProfileVolume(uv,Array.Empty<double>(),Array.Empty<double>(),p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,20);
                if(!double.IsFinite(volume)||Math.Abs(volume-24000)>1e-5)throw new Exception("Delete Undo native rebuild volume mismatch");
                count++;
            }
            CheckRebuiltVolume();
            undo.Push(SketchDeletion.Apply(s,new[]{lines[0]})!);
            if(s.TryGetExtrusionProfile(out _,out _,out _))throw new Exception("Deleted edge still forms extrusion profile");
            SketchSnapshotHistory.Restore(s,undo,redo);CheckRebuiltVolume();
            SketchSnapshotHistory.Restore(s,redo,undo);
            if(s.TryGetExtrusionProfile(out _,out _,out _))throw new Exception("Redo failed to break source profile");
            SketchSnapshotHistory.Restore(s,undo,redo);CheckRebuiltVolume();
        }
        Console.WriteLine($"NATIVE DELETION HISTORY: {count}/9 PASS");
    }
}
#endif
