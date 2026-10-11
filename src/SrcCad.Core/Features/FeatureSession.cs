using SrcCad.Core.History;

namespace SrcCad.Core.Features;

public sealed record FeatureBuildStatus(Guid Id, RebuildState State, string? Error = null);
public sealed record FeatureBuildReport(bool Success, IReadOnlyList<FeatureBuildStatus> Nodes, string? Error = null);
public sealed record FeaturePreview<T>(FeatureBuildReport Report, IReadOnlyDictionary<Guid,T> Outputs) where T:class
{
    public bool Success=>Report.Success;
}
// Outputs must be immutable, owned values (e.g. B-Rep bytes), not mutable viewer state.
public interface IFeatureEvaluator<T> where T : class
{
    T Build(FeatureNode node, IReadOnlyDictionary<Guid,T> dependencies);
}

public sealed class FeatureSession<T> where T : class
{
    private FeatureDocument document;
    private Dictionary<Guid,T> outputs=new();
    private Dictionary<Guid,RebuildState> states=new();
    private readonly TransactionalHistory<FeatureDocument> history=new();
    private readonly IFeatureEvaluator<T> evaluator;
    public long Revision {get;private set;}
    public int UndoCount=>history.UndoCount;
    public int RedoCount=>history.RedoCount;
    // Deep copies prevent mutable sketch snapshots escaping the transaction boundary.
    public FeatureDocument Document=>FeatureDocumentFile.Clone(document);
    public T? Output(Guid id)=>outputs.GetValueOrDefault(id);
    public RebuildState State(Guid id)=>states[id];
    public FeatureBuildReport LastBuild {get;private set;}=new(true,Array.Empty<FeatureBuildStatus>());
    public FeatureSession(FeatureDocument initial,IFeatureEvaluator<T> evaluator)
    {
        this.evaluator=evaluator;document=new(initial.Id,Array.Empty<FeatureNode>());
        if(!Apply(initial))throw new InvalidOperationException(LastBuild.Error);
    }
    public bool TryCommit(FeatureDocument candidate)
    {
        var before=Document;
        try { if(FeatureDocumentFile.Serialize(before)==FeatureDocumentFile.Serialize(candidate))return true; }
        catch(Exception ex) { LastBuild=new(false,Array.Empty<FeatureBuildStatus>(),ex.Message);return false; }
        if(!Apply(candidate))return false;
        history.Record(before,Document);return true;
    }
    public bool Undo()=>history.TryUndo(Apply);
    public bool Redo()=>history.TryRedo(Apply);
    // Preview executes the identical rebuild path in an isolated session: no live mutation/history.
    public FeaturePreview<T> Preview(FeatureDocument candidate)
    {
        var preview=new FeatureSession<T>(this);
        preview.Apply(candidate);
        return new(preview.LastBuild,new System.Collections.ObjectModel.ReadOnlyDictionary<Guid,T>(
            preview.LastBuild.Success?preview.outputs:new Dictionary<Guid,T>()));
    }
    private FeatureSession(FeatureSession<T> source)
    {
        document=source.document;evaluator=source.evaluator;
        outputs=new(source.outputs);states=new(source.states);
    }
    private bool Apply(FeatureDocument candidate)
    {
        var report=new List<FeatureBuildStatus>();
        try
        {
            var safe=FeatureDocumentFile.Clone(candidate);
            if(safe.Id!=document.Id)throw new InvalidOperationException("Document identity cannot change in a transaction.");
            var previous=document.Features.ToDictionary(n=>n.Id);
            var dirty=new HashSet<Guid>(safe.Features.Where(n=>!previous.TryGetValue(n.Id,out var old)||FeatureDocumentFile.Fingerprint(n)!=FeatureDocumentFile.Fingerprint(old)).Select(n=>n.Id));
            foreach(var id in dirty.ToArray())dirty.UnionWith(safe.Descendants(id));
            var next=new Dictionary<Guid,T>();var nextStates=new Dictionary<Guid,RebuildState>();
            bool failed=false;
            foreach(var node in safe.Ordered())
            {
                if(node.Suppressed){nextStates[node.Id]=RebuildState.Suppressed;report.Add(new(node.Id,RebuildState.Suppressed));continue;}
                if(node.Dependencies.Any(id=>!next.ContainsKey(id)))
                {nextStates[node.Id]=RebuildState.Blocked;report.Add(new(node.Id,RebuildState.Blocked,"Parent is suppressed, blocked or failed."));continue;}
                if(!dirty.Contains(node.Id)&&outputs.TryGetValue(node.Id,out var cached))next[node.Id]=cached;
                else
                {
                    report.Add(new(node.Id,RebuildState.Dirty));
                    try
                    {
                        var parents=node.Dependencies.ToDictionary(id=>id,id=>next[id]);
                        next[node.Id]=evaluator.Build(node,parents)??throw new InvalidOperationException("Kernel returned no output.");
                    }
                    catch(Exception ex){failed=true;nextStates[node.Id]=RebuildState.Failed;report.Add(new(node.Id,RebuildState.Failed,ex.Message));continue;}
                }
                nextStates[node.Id]=RebuildState.Clean;report.Add(new(node.Id,RebuildState.Clean));
            }
            LastBuild=new(!failed,report,failed?"Feature rebuild failed; previous document and B-Rep retained.":null);
            if(failed)return false;
            document=safe;outputs=next;states=nextStates;Revision++;return true;
        }
        catch(Exception ex){LastBuild=new(false,report,ex.Message);return false;}
    }
}
