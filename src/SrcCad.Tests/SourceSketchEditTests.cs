using SrcCad.Core.Geometry;
using SrcCad.Core.History;
using SrcCad.Core.Sketch;

internal static class SourceSketchEditTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool ok,string message) { if(!ok) throw new Exception("Source sketch edit: "+message); count++; }
        foreach(var plane in StandardPlanes.All)
        {
            var s=new SketchModel(plane);
            s.AddCornerRectangle(new(0,0),new(100,60),0.001);
            var corner=s.FindPointNear(new(100,60),0.001);
            var d=s.AddDimension(SketchDimensionType.HorizontalDistance,new(50,80),pointA:0,pointB:corner);
            var before=new ExtrusionState(plane,s.CaptureSnapshot(),20);
            Check(s.SetDimensionValue(d.Id,150),"source width edit accepted");
            var after=before with {Sketch=s.CaptureSnapshot()};
            var history=new TransactionalHistory<ExtrusionState>();
            var current=before;
            // Managed apply contract only. Actual OCCT volume is tested separately on Windows.
            bool Apply(ExtrusionState state)
            {
                var model=new SketchModel(state.Plane);model.RestoreSnapshot(state.Sketch);
                if(!model.TryGetExtrusionProfile(out _,out _,out _)) return false;
                current=state;return true;
            }
            Check(history.TryCommit(before,after,Apply)&&history.UndoCount==1&&current.Sketch.Points[corner].Position.X==150,"one committed source edit");
            Check(history.TryUndo(Apply)&&current.Sketch.Points[corner].Position.X==100&&current.Depth==20,"undo restores source and depth");
            Check(history.TryRedo(Apply)&&current.Sketch.Points[corner].Position.X==150,"redo reapplies source");
            history.TryUndo(Apply);
            s.DeleteEntity(s.Lines().First().Id);
            var broken=after with {Sketch=s.CaptureSnapshot()};
            Check(!history.TryCommit(before,broken,Apply)&&history.UndoCount==0&&history.RedoCount==1&&ReferenceEquals(current,before),"open profile keeps committed state and redo");
            try { history.TryCommit(before,after,_=>throw new InvalidOperationException()); } catch(InvalidOperationException) { }
            Check(history.UndoCount==0&&history.RedoCount==1,"exception does not record edit");
        }
        Console.WriteLine($"SOURCE SKETCH EDIT: {count} PASS (managed transaction contract)");
    }
}
