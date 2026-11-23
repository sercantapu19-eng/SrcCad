namespace SrcCad.Core.Sketch;

// One selection (including dependent objects) is one atomic model operation.
public static class SketchDeletion
{
    public static SketchSnapshot? Apply(SketchModel model, IEnumerable<int>? entities = null,
        IEnumerable<int>? points = null, IEnumerable<int>? dimensions = null, IEnumerable<int>? relations = null)
    {
        var es = (entities ?? Array.Empty<int>()).Where(id => model.Entity(id) is not null).Distinct().ToArray();
        var ps = (points ?? Array.Empty<int>()).Where(id => id != 0 && model.Points.ContainsKey(id)).Distinct().ToArray();
        var ds = (dimensions ?? Array.Empty<int>()).Where(id => model.Dimension(id) is not null).Distinct().ToArray();
        var rs = (relations ?? Array.Empty<int>()).Where(id => model.Relations.Any(r => r.Id == id)).Distinct().ToArray();
        if (es.Length + ps.Length + ds.Length + rs.Length == 0) return null;
        var before = model.CaptureSnapshot();
        var candidate = new SketchModel(model.Plane);
        candidate.RestoreSnapshot(before);
        foreach (var id in rs) candidate.RemoveRelation(id);
        foreach (var id in ds) candidate.DeleteDimension(id);
        foreach (var id in es) candidate.DeleteEntity(id);
        foreach (var id in ps) candidate.DeletePoint(id);
        candidate.SolveRelations();
        model.RestoreSnapshot(candidate.CaptureSnapshot(), notifyChange: true);
        return before;
    }
}

// Shared by all sketch edits, keyboard routes and regression tests.
public static class SketchSnapshotHistory
{
    public static bool Restore(SketchModel model, Stack<SketchSnapshot> source, Stack<SketchSnapshot> destination)
    {
        if (source.Count == 0) return false;
        var current = model.CaptureSnapshot();
        model.RestoreSnapshot(source.Peek(), notifyChange: true);
        source.Pop();
        destination.Push(current);
        return true;
    }
}
