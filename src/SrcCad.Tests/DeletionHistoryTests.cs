using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
internal static class DeletionHistoryTests
{
    public static void Run()
    {
        int n=0; void Check(bool ok,string name){if(!ok)throw new Exception("Deletion history: "+name);n++;}
        bool Same(SketchSnapshot a,SketchSnapshot b) => a.NextPointId==b.NextPointId && a.NextEntityId==b.NextEntityId &&
            a.NextRelationId==b.NextRelationId && a.NextDimensionId==b.NextDimensionId &&
            a.Points.OrderBy(x=>x.Key).SequenceEqual(b.Points.OrderBy(x=>x.Key)) && a.Entities.SequenceEqual(b.Entities) &&
            a.Relations.SequenceEqual(b.Relations) && a.Dimensions.SequenceEqual(b.Dimensions) && a.CenterRectangles.SequenceEqual(b.CenterRectangles);
        foreach(var plane in StandardPlanes.All)
        foreach(var scenario in new[]{"line","circle","multi","window","crossing","relation","dimension","dependent","construction","contour"})
        {
            var s=new SketchModel(plane);
            var rectangle=s.AddCornerRectangle(new(-40,-30),new(40,30),0.001);
            var c=s.AddCircle(s.CreatePointExact(new(5,5)),5);
            var construction=s.AddLine(s.CreatePointExact(new(-5,0)),s.CreatePointExact(new(5,0)),true);
            var dimension=s.AddDimension(SketchDimensionType.CircleDiameter,new(10,10),entityA:c.Id);
            var relation=s.AddRelation(SketchRelationType.Horizontal,construction.Id);
            s.SolveRelations();
            var before=s.CaptureSnapshot(); var definition=s.DefinitionInfo();var revision=s.Revision;
            var undo=new Stack<SketchSnapshot>();var redo=new Stack<SketchSnapshot>();
            int[] entities=Array.Empty<int>(),points=Array.Empty<int>(),dims=Array.Empty<int>(),rels=Array.Empty<int>();
            if(scenario is "line" or "contour") entities=new[]{rectangle[0]};
            if(scenario is "circle" or "dependent") entities=new[]{c.Id};
            if(scenario=="multi") entities=new[]{rectangle[0],c.Id,construction.Id};
            if(scenario=="construction") entities=new[]{construction.Id};
            if(scenario=="dimension") dims=new[]{dimension.Id};
            if(scenario=="relation") rels=new[]{relation.Id};
            if(scenario is "window" or "crossing")
            {
                Point2d[] box={new(-10,-10),new(10,-10),new(10,10),new(-10,10)};
                var selected=s.SelectWindow(box,scenario=="crossing");
                entities=selected.Entities.ToArray();points=selected.Points.ToArray();
            }
            var saved=SketchDeletion.Apply(s,entities,points,dims,rels);
            Check(saved is not null&&!Same(before,s.CaptureSnapshot()),scenario+" changes actual model");
            undo.Push(saved!);var after=s.CaptureSnapshot();
            Check(s.Revision>revision,scenario+" advances revision for dependency invalidation");
            Check(entities.All(id=>s.Entity(id) is null)&&dims.All(id=>s.Dimension(id) is null)&&rels.All(id=>s.Relations.All(r=>r.Id!=id)),scenario+" removes targets");
            if(scenario=="dependent")Check(s.Dimension(dimension.Id) is null,"dependent dimension cleanup");
            if(scenario=="contour")Check(!s.TryGetExtrusionProfile(out _,out _,out _),"deleted edge invalidates extrusion source");
            var deletedRevision=s.Revision;
            Check(SketchSnapshotHistory.Restore(s,undo,redo)&&undo.Count==0&&redo.Count==1,scenario+" one undo restores whole selection");
            Check(Same(before,s.CaptureSnapshot()),scenario+" exact entity point reference dimension relation counters restored");
            Check(s.DefinitionInfo()==definition,scenario+" solver DOF restored");
            Check(s.Revision>deletedRevision,scenario+" undo invalidates downstream cache");
            if(scenario=="contour")Check(s.TryGetExtrusionProfile(out _,out _,out _),"undo restores valid extrusion source");
            Check(SketchSnapshotHistory.Restore(s,redo,undo)&&Same(after,s.CaptureSnapshot()),scenario+" redo exact deletion");
            Check(SketchSnapshotHistory.Restore(s,undo,redo)&&Same(before,s.CaptureSnapshot()),scenario+" repeated restore stable");
            Check(SketchDeletion.Apply(s,new[]{-1},new[]{0},new[]{-1},new[]{-1}) is null,"no-op has no history entry");
        }
        Console.WriteLine($"DELETION HISTORY: {n} PASS (shared app transaction/restore; Windows input and native rebuild pending; Arc unsupported)");
    }
}
