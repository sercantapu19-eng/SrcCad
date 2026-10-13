using SrcCad.Core.Features;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.History;
using SrcCad.Core.Persistence;

internal static class FeatureGraphTests
{
    // Scheduling/transaction spy. These checks are NOT B-Rep geometry acceptance.
    private sealed record Output(double Value);
    private sealed class Evaluator : IFeatureEvaluator<Output>
    {
        public List<Guid> Built=new();
        public Guid? Fail;
        public Output Build(FeatureNode node,IReadOnlyDictionary<Guid,Output> dependencies)
        {
            Built.Add(node.Id);if(node.Id==Fail)throw new InvalidOperationException("deliberate kernel rejection");
            var own=node.Parameters switch
            {
                ExtrudeFeatureParameters p=>p.Depth,
                FilletFeatureParameters p=>p.Radius,
                ChamferFeatureParameters p=>p.Distance,
                HoleFeatureParameters p=>p.Depth,
                RevolveFeatureParameters p=>p.AngleDegrees,
                RevolveCutFeatureParameters p=>p.AngleDegrees,
                SweepFeatureParameters=>1,
                SweepCutFeatureParameters=>1,
                LoftFeatureParameters p=>p.ProfileSketchIds.Length,
                LoftCutFeatureParameters p=>p.ProfileSketchIds.Length,
                ShellFeatureParameters p=>p.Thickness,
                DraftFeatureParameters p=>p.AngleDegrees,
                RibFeatureParameters p=>p.Depth,
                MirrorFeatureParameters=>1,
                LinearPatternFeatureParameters p=>p.Count,
                CircularPatternFeatureParameters p=>p.Count,
                ReferencePlaneFeatureParameters=>1,
                ReferenceAxisFeatureParameters=>1,
                ReferencePointFeatureParameters=>1,
                SheetMetalBaseFeatureParameters p=>p.Thickness,
                SheetMetalCutFeatureParameters p=>p.Depth,
                SheetMetalEdgeFlangeFeatureParameters p=>p.Length,
                _=>1
            };
            return new(own+dependencies.Values.Sum(x=>x.Value));
        }
    }
    public static void Run()
    {
        int n=0;void Check(bool ok,string message){if(!ok)throw new Exception("Feature graph: "+message);n++;}
        var sketch=new SketchModel(StandardPlanes.Front);sketch.AddCornerRectangle(new(0,0),new(40,30),.001);
        var initial=FeatureDocumentFile.FromLegacy(new(StandardPlanes.Front,sketch.CaptureSnapshot(),20));
        var source=initial.Features[0];var boss=initial.Features[1];
        var second=new FeatureNode(Guid.NewGuid(),"Çizim2",source.Parameters);
        var cut=new FeatureNode(Guid.NewGuid(),"Kes-Ekstrüzyon1",new CutFeatureParameters(boss.Id,second.Id,10));
        var doc=initial with{Features=initial.Features.Concat(new[]{second,cut}).ToArray()};
        var evaluator=new Evaluator();var session=new FeatureSession<Output>(doc,evaluator);
        Check(evaluator.Built.SequenceEqual(doc.Ordered().Select(n=>n.Id)),"deterministic initial dependency order");
        Check(doc.Descendants(source.Id).SetEquals(new[]{boss.Id,cut.Id}),"transitive descendants include cut");
        Check(doc.Children(boss.Id).SequenceEqual(new[]{cut.Id}),"direct child graph");
        var edited=doc with{Features=doc.Features.Select(f=>f.Id==boss.Id?f with{Parameters=new ExtrudeFeatureParameters(source.Id,40)}:f).ToArray()};
        evaluator.Built.Clear();
        Check(session.TryCommit(edited),"upstream edit commits");
        Check(evaluator.Built.SequenceEqual(new[]{boss.Id,cut.Id}),"incremental downstream rebuild skips unrelated sketch");
        var after=session.Output(cut.Id);Check(after==new Output(43),"dependent evaluator output changes");
        Check(session.UndoCount==1&&session.Undo(),"one transaction undo");
        Check(((ExtrudeFeatureParameters)session.Document.Node(boss.Id).Parameters).Depth==20,"undo restores parameter");
        Check(session.Redo()&&session.Output(cut.Id)==after,"redo deterministic output");
        evaluator.Fail=cut.Id;var before=FeatureDocumentFile.Serialize(session.Document);var revision=session.Revision;var history=session.UndoCount;
        Check(!session.TryCommit(doc),"failed downstream rejects whole transaction");
        Check(FeatureDocumentFile.Serialize(session.Document)==before&&session.Output(cut.Id)==after&&session.Revision==revision&&session.UndoCount==history,"failure retains last-good graph output revision history");
        Check(session.LastBuild.Nodes.Any(s=>s.Id==cut.Id&&s.State==RebuildState.Failed),"failed feature has diagnostic");
        evaluator.Fail=null;
        var suppressed=edited with{Features=edited.Features.Select(f=>f.Id==boss.Id?f with{Suppressed=true}:f).ToArray()};
        Check(session.TryCommit(suppressed)&&session.State(boss.Id)==RebuildState.Suppressed&&session.State(cut.Id)==RebuildState.Blocked,"suppression blocks descendants");
        Check(session.Output(cut.Id) is null,"blocked feature exposes no stale successful body");
        Check(session.Undo()&&session.Output(cut.Id)==after,"unsuppress via undo rebuilds body");
        var previewRevision=session.Revision;var previewCount=session.UndoCount;
        Check(session.Preview(doc).Success&&session.Revision==previewRevision&&session.UndoCount==previewCount&&session.Output(cut.Id)==after,"preview isolated from document/history");
        bool refused=false;try{doc.Delete(boss.Id);}catch(InvalidOperationException){refused=true;}
        Check(refused,"non-cascade delete refuses dependent feature");
        Check(session.TryCommit(edited.Delete(boss.Id,true))&&session.Document.Features.Length==2,"cascade delete removes parent and cut atomically");
        Check(session.Undo()&&session.Document.Features.Length==4&&session.Output(cut.Id)==after,"delete undo restores graph and output");
        var missing=edited with{Features=edited.Features.Where(f=>f.Id!=source.Id).ToArray()};
        Check(!session.TryCommit(missing),"missing reference rejected");
        var cycle=edited with{Features=edited.Features.Select(f=>f.Id==source.Id?f with{References=new[]{new FeatureReference(cut.Id,TopologyKind.Face,"end")}}:f).ToArray()};
        Check(!session.TryCommit(cycle),"dependency cycle rejected");
        var duplicate=edited with{Features=edited.Features.Append(source).ToArray()};Check(!session.TryCommit(duplicate),"duplicate feature ID rejected");
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".scpart");
        try
        {
            FeatureDocumentFile.Save(path,suppressed);var reopened=FeatureDocumentFile.Load(path);
            Check(FeatureDocumentFile.Serialize(reopened)==FeatureDocumentFile.Serialize(suppressed),"actual save/reopen preserves parameters identity suppression graph");
            var fresh=new FeatureSession<Output>(reopened,new Evaluator());
            Check(fresh.TryCommit(edited)&&fresh.Output(cut.Id)==after,"reopened document remains editable and rebuildable");
        }
        finally{File.Delete(path);}
        var old=PartFile.Serialize(new(StandardPlanes.Front,sketch.CaptureSnapshot(),20));
        Check(FeatureDocumentFile.Deserialize(old).Features.Length==2,"schema 1 migrates to graph without lost sketch/extrusion");
        var edgeReference=new FeatureReference(boss.Id,TopologyKind.Edge,"E;10;0;0;0");
        var filletNode=new FeatureNode(Guid.NewGuid(),"Radyus1",new FilletFeatureParameters(boss.Id,2),References:new[]{edgeReference});
        var chamferNode=new FeatureNode(Guid.NewGuid(),"Pah1",new ChamferFeatureParameters(filletNode.Id,1),
            References:new[]{new FeatureReference(filletNode.Id,TopologyKind.Edge,"E;10;0;0;0")});
        var detailDoc=initial with{Features=initial.Features.Concat(new[]{filletNode,chamferNode}).ToArray()};
        var detailClone=FeatureDocumentFile.Clone(detailDoc);
        Check(detailClone.Node(filletNode.Id).Kind==FeatureKind.Fillet&&detailClone.Node(chamferNode.Id).Kind==FeatureKind.Chamfer,
            "fillet/chamfer parameter and persistent edge reference roundtrip");
        Check(detailClone.Descendants(boss.Id).SetEquals(new[]{filletNode.Id,chamferNode.Id}),
            "detail features participate in dependency graph");
        var holeNode=new FeatureNode(Guid.NewGuid(),"Delik1",
            new HoleFeatureParameters(boss.Id,new Vector3d(10,10,20),8,12),
            References:new[]{new FeatureReference(boss.Id,TopologyKind.Face,"F;1200;20;15;20;0;0;1")});
        var holeDoc=initial with{Features=initial.Features.Append(holeNode).ToArray()};
        var holeClone=FeatureDocumentFile.Clone(holeDoc);
        Check(holeClone.Node(holeNode.Id).Kind==FeatureKind.Hole&&
              ((HoleFeatureParameters)holeClone.Node(holeNode.Id).Parameters).Diameter==8,
            "hole parameters and persistent face reference roundtrip");

        var revolveSketch=new SketchModel(StandardPlanes.Front);
        var axisA=revolveSketch.CreatePointExact(new(0,-10));
        var axisB=revolveSketch.CreatePointExact(new(0,50));
        var axis=revolveSketch.AddLine(axisA,axisB,true);
        revolveSketch.AddCornerRectangle(new(5,0),new(15,20),.001);
        var revolveSketchNode=new FeatureNode(Guid.NewGuid(),"ÇizimR",new SketchFeatureParameters(StandardPlanes.Front,revolveSketch.CaptureSnapshot()));
        var revolveNode=new FeatureNode(Guid.NewGuid(),"Döndürme1",new RevolveFeatureParameters(revolveSketchNode.Id,axis.Id,360));
        var revolveDoc=new FeatureDocument(Guid.NewGuid(),new[]{revolveSketchNode,revolveNode});
        Check(FeatureDocumentFile.Clone(revolveDoc).Node(revolveNode.Id).Kind==FeatureKind.Revolve,
            "revolve axis and angle roundtrip");
        var revolvedCutNode=new FeatureNode(Guid.NewGuid(),"Döndürerek Kes1",
            new RevolveCutFeatureParameters(boss.Id,revolveSketchNode.Id,axis.Id,180));
        var revolveCutDoc=initial with{Features=initial.Features.Concat(new[]{revolveSketchNode,revolvedCutNode}).ToArray()};
        Check(FeatureDocumentFile.Clone(revolveCutDoc).Node(revolvedCutNode.Id).Kind==FeatureKind.RevolvedCut,
            "revolved cut dependencies and axis roundtrip");

        var pathSketch=new SketchModel(StandardPlanes.Right);
        var pathStart=pathSketch.CreatePointExact(new(0,0));var pathEnd=pathSketch.CreatePointExact(new(-20,0));pathSketch.AddLine(pathStart,pathEnd);
        var pathNode=new FeatureNode(Guid.NewGuid(),"Yol1",new SketchFeatureParameters(StandardPlanes.Right,pathSketch.CaptureSnapshot()));
        var sweepNode=new FeatureNode(Guid.NewGuid(),"Süpürme1",new SweepFeatureParameters(source.Id,pathNode.Id));
        var sweepDoc=new FeatureDocument(Guid.NewGuid(),new[]{source,pathNode,sweepNode});
        Check(FeatureDocumentFile.Clone(sweepDoc).Node(sweepNode.Id).Kind==FeatureKind.Sweep,"sweep profile/path dependencies roundtrip");

        var loftSketch2=new SketchModel(StandardPlanes.Front with{Origin=Vector3d.UnitZ*20,Name="Loft2"});
        loftSketch2.AddCornerRectangle(new(5,5),new(35,25),.001);
        var loft2=new FeatureNode(Guid.NewGuid(),"LoftProfil2",new SketchFeatureParameters(loftSketch2.Plane,loftSketch2.CaptureSnapshot()));
        var loftNode=new FeatureNode(Guid.NewGuid(),"Loft1",new LoftFeatureParameters(new[]{source.Id,loft2.Id}));
        var loftDoc=new FeatureDocument(Guid.NewGuid(),new[]{source,loft2,loftNode});
        Check(FeatureDocumentFile.Clone(loftDoc).Node(loftNode.Id).Kind==FeatureKind.Loft,"loft profile list roundtrip");

        var shellNode=new FeatureNode(Guid.NewGuid(),"Kabuk1",new ShellFeatureParameters(boss.Id,2),
            References:new[]{new FeatureReference(boss.Id,TopologyKind.Face,"F;1200;20;15;20;0;0;1")});
        var shellDoc=initial with{Features=initial.Features.Append(shellNode).ToArray()};
        var shellClone=FeatureDocumentFile.Clone(shellDoc);
        Check(shellClone.Node(shellNode.Id).Kind==FeatureKind.Shell&&
              ((ShellFeatureParameters)shellClone.Node(shellNode.Id).Parameters).Thickness==2,
            "shell thickness and persistent opening-face references roundtrip");
        var draftNode=new FeatureNode(Guid.NewGuid(),"Draft1",
            new DraftFeatureParameters(boss.Id,StandardPlanes.Front,3),
            References:new[]{new FeatureReference(boss.Id,TopologyKind.Face,"F;1200;20;15;20;0;0;1")});
        var draftDoc=initial with{Features=initial.Features.Append(draftNode).ToArray()};
        var draftClone=FeatureDocumentFile.Clone(draftDoc);
        Check(draftClone.Node(draftNode.Id).Kind==FeatureKind.Draft&&
              ((DraftFeatureParameters)draftClone.Node(draftNode.Id).Parameters).AngleDegrees==3,
            "draft neutral plane angle and face reference roundtrip");

        var ribSketch=new SketchModel(StandardPlanes.Front);
        var ribA=ribSketch.CreatePointExact(new(5,5));
        var ribB=ribSketch.CreatePointExact(new(35,5));
        ribSketch.AddLine(ribA,ribB);
        var ribSketchNode=new FeatureNode(Guid.NewGuid(),"ÇizimN",new SketchFeatureParameters(StandardPlanes.Front,ribSketch.CaptureSnapshot()));
        var ribNode=new FeatureNode(Guid.NewGuid(),"Nervür1",new RibFeatureParameters(boss.Id,ribSketchNode.Id,3,10));
        var ribDoc=initial with{Features=initial.Features.Concat(new[]{ribSketchNode,ribNode}).ToArray()};
        var ribClone=FeatureDocumentFile.Clone(ribDoc);
        Check(ribClone.Node(ribNode.Id).Kind==FeatureKind.Rib&&((RibFeatureParameters)ribClone.Node(ribNode.Id).Parameters).Thickness==3,
            "rib sketch dependency thickness/depth roundtrip");

        var mirrorNode=new FeatureNode(Guid.NewGuid(),"Aynala1",new MirrorFeatureParameters(boss.Id,StandardPlanes.Front));
        var mirrorDoc=initial with{Features=initial.Features.Append(mirrorNode).ToArray()};
        var mirrorClone=FeatureDocumentFile.Clone(mirrorDoc);
        Check(mirrorClone.Node(mirrorNode.Id).Kind==FeatureKind.Mirror&&
              ((MirrorFeatureParameters)mirrorClone.Node(mirrorNode.Id).Parameters).Plane.Name=="Ön Düzlem",
            "mirror plane and keep-original intent roundtrip");
        var linearNode=new FeatureNode(Guid.NewGuid(),"Doğrusal Dizilim1",
            new LinearPatternFeatureParameters(boss.Id,Vector3d.UnitX,50,4));
        var circularNode=new FeatureNode(Guid.NewGuid(),"Dairesel Dizilim1",
            new CircularPatternFeatureParameters(boss.Id,Vector3d.Zero,Vector3d.UnitZ,360,6));
        var patternDoc=initial with{Features=initial.Features.Concat(new[]{linearNode,circularNode}).ToArray()};
        var patternClone=FeatureDocumentFile.Clone(patternDoc);
        Check(patternClone.Node(linearNode.Id).Kind==FeatureKind.LinearPattern&&patternClone.Node(circularNode.Id).Kind==FeatureKind.CircularPattern,
            "linear/circular pattern parameters roundtrip");

        var refPlaneNode=new FeatureNode(Guid.NewGuid(),"Düzlem1",new ReferencePlaneFeatureParameters(StandardPlanes.Front,25));
        var refAxisNode=new FeatureNode(Guid.NewGuid(),"Eksen1",new ReferenceAxisFeatureParameters(Vector3d.Zero,Vector3d.UnitZ));
        var refPointNode=new FeatureNode(Guid.NewGuid(),"Nokta1",new ReferencePointFeatureParameters(new Vector3d(10,20,30)));
        var referenceGeometryDoc=initial with{Features=initial.Features.Concat(new[]{refPlaneNode,refAxisNode,refPointNode}).ToArray()};
        var referenceGeometryClone=FeatureDocumentFile.Clone(referenceGeometryDoc);
        Check(referenceGeometryClone.Node(refPlaneNode.Id).Kind==FeatureKind.ReferencePlane&&
              referenceGeometryClone.Node(refAxisNode.Id).Kind==FeatureKind.ReferenceAxis&&
              referenceGeometryClone.Node(refPointNode.Id).Kind==FeatureKind.ReferencePoint,
            "reference plane axis point persist through parametric document");
        Check(((ReferencePlaneFeatureParameters)referenceGeometryClone.Node(refPlaneNode.Id).Parameters).Offset==25,
            "reference plane offset roundtrip");

        var sheetBaseNode=new FeatureNode(Guid.NewGuid(),"Taban Flanş1",new SheetMetalBaseFeatureParameters(source.Id,2,2,0.5));
        var sheetDoc=new FeatureDocument(Guid.NewGuid(),new[]{source,sheetBaseNode});
        var sheetClone=FeatureDocumentFile.Clone(sheetDoc);
        Check(sheetClone.Node(sheetBaseNode.Id).Kind==FeatureKind.SheetMetalBase&&
              ((SheetMetalBaseFeatureParameters)sheetClone.Node(sheetBaseNode.Id).Parameters).KFactor==0.5,
            "sheet metal base thickness bend radius k-factor roundtrip");
        var sheetCutNode=new FeatureNode(Guid.NewGuid(),"Sac Kes1",
            new SheetMetalCutFeatureParameters(sheetBaseNode.Id,second.Id,5,ExtrusionTermination.ThroughAll,true));
        var sheetCutDoc=new FeatureDocument(Guid.NewGuid(),new[]{source,sheetBaseNode,second,sheetCutNode});
        var sheetCutClone=FeatureDocumentFile.Clone(sheetCutDoc);
        Check(sheetCutClone.Node(sheetCutNode.Id).Kind==FeatureKind.SheetMetalCut&&
              ((SheetMetalCutFeatureParameters)sheetCutClone.Node(sheetCutNode.Id).Parameters).Termination==ExtrusionTermination.ThroughAll,
            "sheet metal cut parameters and dependency roundtrip");
        var edgeFlangeNode=new FeatureNode(Guid.NewGuid(),"Kenar Flanşı1",
            new SheetMetalEdgeFlangeFeatureParameters(sheetBaseNode.Id,25,90,2,2),
            References:new[]{new FeatureReference(sheetBaseNode.Id,TopologyKind.Edge,"E;40;20;0;0")});
        var edgeFlangeDoc=new FeatureDocument(Guid.NewGuid(),new[]{source,sheetBaseNode,edgeFlangeNode});
        var edgeFlangeClone=FeatureDocumentFile.Clone(edgeFlangeDoc);
        Check(edgeFlangeClone.Node(edgeFlangeNode.Id).Kind==FeatureKind.SheetMetalEdgeFlange&&
              ((SheetMetalEdgeFlangeFeatureParameters)edgeFlangeClone.Node(edgeFlangeNode.Id).Parameters).AngleDegrees==90,
            "sheet metal edge flange metadata and persistent edge reference roundtrip");

        var memberProfile=new SketchModel(StandardPlanes.Front);memberProfile.AddCornerRectangle(new(-5,-5),new(5,5),.001);
        var memberPath=new SketchModel(StandardPlanes.Top);
        var mp0=memberPath.CreatePointExact(new(0,0));var mp1=memberPath.CreatePointExact(new(100,0));memberPath.AddLine(mp0,mp1);
        var profileNode=new FeatureNode(Guid.NewGuid(),"Profil1",new SketchFeatureParameters(memberProfile.Plane,memberProfile.CaptureSnapshot()));
        var pathNode=new FeatureNode(Guid.NewGuid(),"Yol1",new SketchFeatureParameters(memberPath.Plane,memberPath.CaptureSnapshot()));
        var memberNode=new FeatureNode(Guid.NewGuid(),"Yapısal Eleman1",new StructuralMemberFeatureParameters(profileNode.Id,pathNode.Id));
        var memberDoc=new FeatureDocument(Guid.NewGuid(),new[]{profileNode,pathNode,memberNode});
        var memberClone=FeatureDocumentFile.Clone(memberDoc);
        Check(memberClone.Node(memberNode.Id).Kind==FeatureKind.StructuralMember,
            "structural member profile/path dependency roundtrip");
        Check(memberClone.Descendants(profileNode.Id).Contains(memberNode.Id)&&memberClone.Descendants(pathNode.Id).Contains(memberNode.Id),
            "structural member depends on profile and path sketches");

        var external=session.Document;((IDictionary<int,SketchPoint>)((SketchFeatureParameters)external.Node(source.Id).Parameters).Sketch.Points).Clear();
        Check(((SketchFeatureParameters)session.Document.Node(source.Id).Parameters).Sketch.Points.Count>0,"external snapshot cannot mutate committed document");
        var unchangedRevision=session.Revision;var unchangedHistory=session.UndoCount;
        Check(session.TryCommit(session.Document)&&session.Revision==unchangedRevision&&session.UndoCount==unchangedHistory,"no-op creates no history or revision");
        evaluator.Fail=boss.Id;
        Check(!session.Undo()&&session.UndoCount==unchangedHistory&&session.Revision==unchangedRevision,"failed undo retains history and current document");
        evaluator.Fail=null;
        var reference=new FeatureReference(boss.Id,TopologyKind.Face,"extrusion:end-cap");
        var referenced=doc with{Features=doc.Features.Select(f=>f.Id==second.Id?f with{References=new[]{reference}}:f).ToArray()};
        Check(FeatureDocumentFile.Clone(referenced).Node(second.Id).References!.Single()==reference,"semantic provenance roundtrip without transient indices");
        var chain=new List<FeatureNode>{source,boss};var parent=boss.Id;
        for(int i=0;i<50;i++){var next=new FeatureNode(Guid.NewGuid(),"Cut"+i,new CutFeatureParameters(parent,source.Id,1));chain.Add(next);parent=next.Id;}
        var large=new FeatureDocument(Guid.NewGuid(),chain.ToArray());var trace=new Evaluator();var largeSession=new FeatureSession<Output>(large,trace);
        Check(trace.Built.Count==52&&large.Descendants(source.Id).Count==51,"52-node dependency smoke");
        trace.Built.Clear();var largeEdit=large with{Features=large.Features.Select(f=>f.Id==boss.Id?f with{Parameters=new ExtrudeFeatureParameters(source.Id,30)}:f).ToArray()};
        Check(largeSession.TryCommit(largeEdit)&&trace.Built.Count==51&&trace.Built[0]==boss.Id&&trace.Built[^1]==parent,"large downstream chain deterministic incremental rebuild");
        bool acceptPublication=true;int publications=0;
        var visual=new FeatureSession<Output>(doc,new Evaluator(),(_,_)=>{publications++;return acceptPublication;});
        var visualRevision=visual.Revision;
        Check(visual.Preview(edited).Success&&publications==1,"preview never publishes committed viewport body");
        acceptPublication=false;
        Check(!visual.TryCommit(edited)&&visual.UndoCount==0&&visual.Revision==visualRevision,"publication rejection retains document and history");
        Check(((ExtrudeFeatureParameters)visual.Document.Node(boss.Id).Parameters).Depth==20,"rejected publication retains old parameters");
        acceptPublication=true;Check(visual.TryCommit(edited)&&visual.UndoCount==1,"accepted publication commits once");
        acceptPublication=false;Check(!visual.Undo()&&visual.UndoCount==1&&visual.RedoCount==0,"rejected Undo publication does not move history");
        acceptPublication=true;Check(visual.Undo()&&visual.Redo(),"published undo and redo restore history");
        var legacyHistory=new TransactionalHistory<ExtrusionState>();
        var original=new ExtrusionState(StandardPlanes.Front,sketch.CaptureSnapshot(),null);
        var twenty=original with{Depth=20};var forty=original with{Depth=40};
        legacyHistory.Record(original,twenty);legacyHistory.Record(twenty,forty);
        Check(legacyHistory.TryUndo(_=>true),"legacy history creates redo branch");
        var stableDoc=Guid.NewGuid();var stableSketch=Guid.NewGuid();var stableBoss=Guid.NewGuid();
        FeatureDocument Convert(ExtrusionState state)=>FeatureDocumentFile.FromLegacy(state,stableDoc,stableSketch,stableBoss);
        var converted=legacyHistory.MapTo(Convert);
        var migrated=new FeatureSession<Output>(Convert(twenty),new Evaluator(),importedHistory:converted);
        Check(migrated.UndoCount==1&&migrated.RedoCount==1,"migration preserves both history stacks");
        Check(migrated.Redo()&&((ExtrudeFeatureParameters)migrated.Document.Node(stableBoss).Parameters).Depth==40,"migrated redo edits existing stable feature ID");
        Check(migrated.Undo()&&migrated.Undo()&&migrated.Document.Features.Length==1,"migration preserves undo to pre-extrude sketch");
        Check(migrated.Redo()&&migrated.Document.Node(stableBoss).Id==stableBoss,"migration redo restores same feature identity");
        Console.WriteLine($"FEATURE GRAPH: {n} PASS (dependency/transaction/persistence contracts; evaluator spy is NOT B-Rep acceptance)");
    }
}
