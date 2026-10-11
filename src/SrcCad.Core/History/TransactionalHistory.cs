namespace SrcCad.Core.History;

/// <summary>Moves history only after the model/kernel accepts a complete target state.</summary>
public sealed class TransactionalHistory<T>
{
    private sealed record Change(T Before, T After);
    private readonly Stack<Change> undo = new();
    private readonly Stack<Change> redo = new();
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;

    public void Record(T before, T after)
    {
        undo.Push(new Change(before, after));
        redo.Clear();
    }

    public bool TryUndo(Func<T, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (undo.Count == 0 || !apply(undo.Peek().Before)) return false;
        redo.Push(undo.Pop());
        return true;
    }

    public bool TryRedo(Func<T, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (redo.Count == 0 || !apply(redo.Peek().After)) return false;
        undo.Push(redo.Pop());
        return true;
    }

    public void Clear() { undo.Clear(); redo.Clear(); }
}
