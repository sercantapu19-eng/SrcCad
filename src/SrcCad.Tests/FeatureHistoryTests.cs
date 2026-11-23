using SrcCad.Core.Geometry;
using SrcCad.Core.History;
using SrcCad.Core.Sketch;

internal static class FeatureHistoryTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value, string message) { count++; if(!value) throw new Exception("Feature history: "+message); }
        var sketch=new SketchModel(StandardPlanes.Front);
        sketch.AddCornerRectangle(new(0,0),new(100,60),0.001);
        var blank=new ExtrusionState(sketch.Plane,sketch.CaptureSnapshot(),null);
        var solid=blank with { Depth=20 };
        var edited=solid with { Depth=30 };
        var history=new TransactionalHistory<ExtrusionState>();
        var applied=solid;
        bool Apply(ExtrusionState state) { applied=state;return true; }
        history.Record(blank,solid);
        Check(history.TryUndo(Apply)&&applied.Depth is null,"creation undo must remove feature");
        Check(history.TryRedo(Apply)&&applied.Depth==20,"creation redo must restore feature");
        history.Record(solid,edited);
        Check(history.TryUndo(Apply)&&applied.Depth==20,"edit undo must restore previous depth");
        Check(history.TryRedo(Apply)&&applied.Depth==30,"edit redo must restore new depth");
        history.Record(edited,blank);
        Check(history.TryUndo(Apply)&&applied.Depth==30,"delete undo must restore feature");
        Check(history.TryRedo(Apply)&&applied.Depth is null,"delete redo must remove feature");
        int u=history.UndoCount,r=history.RedoCount;
        Check(!history.TryUndo(_=>false)&&history.UndoCount==u&&history.RedoCount==r,"kernel rejection must not move history");
        try { history.TryUndo(_=>throw new InvalidOperationException("kernel failure")); }
        catch(InvalidOperationException) { }
        Check(history.UndoCount==u&&history.RedoCount==r,"kernel exception must not move history");
        history.TryUndo(Apply);
        u=history.UndoCount;r=history.RedoCount;
        Check(!history.TryRedo(_=>false)&&history.UndoCount==u&&history.RedoCount==r,"failed redo must remain retryable");
        history.Record(edited,solid);
        Check(history.RedoCount==0,"new successful edit must invalidate redo");
        // History owns captured geometry, not the live sketch's mutable collections.
        sketch.AddCircle(0,5);
        var restored=new SketchModel(blank.Plane);restored.RestoreSnapshot(blank.Sketch);
        Check(restored.Entities.Count==4&&!restored.Circles().Any(),"history must retain original sketch snapshot");
        history.Clear();
        Check(history.UndoCount==0&&history.RedoCount==0&&!history.TryUndo(Apply)&&!history.TryRedo(Apply),"new document clears both histories");
        Console.WriteLine($"FEATURE HISTORY: {count}/12 PASS (managed state transitions; native/UI still require Windows)");
    }
}
