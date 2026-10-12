#if NATIVE_ACCEPTANCE
using SrcCad.Core.Features;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Kernel.Occt;
using SrcCad.App;
internal static class NativeFeatureGraphTests
{
    public static void Run()
    {
        int n=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("Native feature graph: "+message);n++;}
        foreach(var plane in StandardPlanes.All)
        {
            var sketch=new SketchModel(plane);sketch.AddCornerRectangle(new(0,0),new(40,30),.001);
            var doc=FeatureDocumentFile.FromLegacy(new(plane,sketch.CaptureSnapshot(),20));
            var boss=doc.Features[1];
            var tool=new SketchModel(plane);tool.AddCircle(tool.CreatePointExact(new(20,15)),5);
            var toolNode=new FeatureNode(Guid.NewGuid(),"Çizim2",new SketchFeatureParameters(plane,tool.CaptureSnapshot()));
            var cut=new FeatureNode(Guid.NewGuid(),"Kes-Ekstrüzyon1",new CutFeatureParameters(boss.Id,toolNode.Id,10));
            doc=doc with{Features=doc.Features.Concat(new[]{toolNode,cut}).ToArray()};
            var evaluator=new PartFeatureEvaluator(new OcctPartGeometryKernel());
            var session=new FeatureSession<PartFeatureOutput>(doc,evaluator);

            var bossBody=session.Output(boss.Id)!.BRep!;
            var bossEdges=OcctFeatureKernel.EdgeReferences(bossBody);
            Check(bossEdges.Length>=4,"extruded body exposes semantic edge references");
            var filletNode=new FeatureNode(Guid.NewGuid(),"Radyus1",new FilletFeatureParameters(boss.Id,2),
                References:new[]{new FeatureReference(boss.Id,TopologyKind.Edge,bossEdges[0])});
            var filletDoc=doc with{Features=doc.Features.Append(filletNode).ToArray()};
            var filletPreview=session.Preview(filletDoc);
            Check(filletPreview.Success,"native OCCT fillet preview succeeds");
            var filletInspect=OcctFeatureKernel.Inspect(filletPreview.Outputs[filletNode.Id].BRep!);
            Check(filletInspect[0]>0&&filletInspect[1]==1&&filletInspect[2]>=6,"native fillet produces valid solid B-Rep");

            var filletEdges=OcctFeatureKernel.EdgeReferences(filletPreview.Outputs[filletNode.Id].BRep!);
            var chamferNode=new FeatureNode(Guid.NewGuid(),"Pah1",new ChamferFeatureParameters(filletNode.Id,1),
                References:new[]{new FeatureReference(filletNode.Id,TopologyKind.Edge,filletEdges[0])});
            var detailDoc=filletDoc with{Features=filletDoc.Features.Append(chamferNode).ToArray()};
            var detailPreview=session.Preview(detailDoc);
            Check(detailPreview.Success,"native OCCT chamfer after fillet succeeds");
            var chamferInspect=OcctFeatureKernel.Inspect(detailPreview.Outputs[chamferNode.Id].BRep!);
            Check(chamferInspect[0]>0&&chamferInspect[1]==1,"native chamfer produces valid solid B-Rep");

            void Volume(double expected,string message)
            {
                var data=OcctFeatureKernel.Inspect(session.Output(cut.Id)!.BRep!);
                Check(Math.Abs(data[0]-expected)<1e-4&&data[1]==1&&data[2]>6&&data[3]>12,message+" valid OCCT volume/solid/topology");
            }
            Volume(24000-Math.PI*25*10,"blind cut");
            var through=doc with{Features=doc.Features.Select(f=>f.Id==cut.Id?f with{Parameters=((CutFeatureParameters)f.Parameters) with{Termination=ExtrusionTermination.ThroughAll}}:f).ToArray()};
            var revisionBeforePreview=session.Revision;
            var preview=session.Preview(through);
            Check(preview.Success&&Math.Abs(OcctFeatureKernel.Inspect(preview.Outputs[cut.Id].BRep!)[0]-(24000-Math.PI*25*20))<1e-4&&session.Revision==revisionBeforePreview&&session.UndoCount==0,"preview builds actual candidate B-Rep without document/history mutation");
            Check(session.TryCommit(through),"edit termination commits");Volume(24000-Math.PI*25*20,"through all");
            var deeper=through with{Features=through.Features.Select(f=>f.Id==boss.Id?f with{Parameters=((ExtrudeFeatureParameters)f.Parameters) with{Depth=40}}:f).ToArray()};
            Check(session.TryCommit(deeper),"upstream extrusion edit commits");Volume(48000-Math.PI*25*40,"upstream rebuild extends through cut");
            Check(session.Undo(),"undo upstream");Volume(24000-Math.PI*25*20,"undo restored B-Rep");
            Check(session.Redo(),"redo upstream");Volume(48000-Math.PI*25*40,"redo restored B-Rep");
            var reverse=deeper with{Features=deeper.Features.Select(f=>f.Id==cut.Id?f with{Parameters=((CutFeatureParameters)f.Parameters) with{Reverse=true}}:f).ToArray()};
            var oldRevision=session.Revision;
            Check(!session.TryCommit(reverse)&&session.Revision==oldRevision,"reverse missing material rejects atomically");Volume(48000-Math.PI*25*40,"failed edit keeps previous B-Rep");
            var topPlane=plane with{Origin=plane.Origin+plane.Normal*20};
            var topCut=doc with{Features=doc.Features.Select(f=>f.Id==toolNode.Id?f with{Parameters=new SketchFeatureParameters(topPlane,tool.CaptureSnapshot())}:
                f.Id==cut.Id?f with{Parameters=((CutFeatureParameters)f.Parameters) with{Reverse=true}}:f).ToArray()};
            Check(session.TryCommit(topCut),"reverse cut from upper datum plane commits");
            Volume(24000-Math.PI*25*10,"reverse cut removes actual material");
            Check(session.TryCommit(topCut.Delete(cut.Id)),"cut delete commits");
            Check(session.Output(cut.Id) is null&&Math.Abs(OcctFeatureKernel.Inspect(session.Output(boss.Id)!.BRep!)[0]-24000)<1e-4,"delete restores uncut base B-Rep");
            Check(session.Undo(),"delete cut undo");Volume(24000-Math.PI*25*10,"delete undo returns cut B-Rep");
            var suppressed=topCut with{Features=topCut.Features.Select(f=>f.Id==cut.Id?f with{Suppressed=true}:f).ToArray()};
            Check(session.TryCommit(suppressed)&&session.Output(cut.Id) is null,"suppress cut removes body output");
            Check(session.Undo(),"unsuppress through undo");Volume(24000-Math.PI*25*10,"unsuppress rebuilds cut");
            var largeTool=new SketchModel(plane);largeTool.AddCornerRectangle(new(-1,-1),new(41,31),.001);
            var destructive=through with{Features=through.Features.Select(f=>f.Id==toolNode.Id?f with{Parameters=new SketchFeatureParameters(plane,largeTool.CaptureSnapshot())}:f).ToArray()};
            Check(!session.TryCommit(destructive),"empty solid cut rejects atomically");Volume(24000-Math.PI*25*10,"failed empty cut retains reverse cut");
            Check(session.TryCommit(deeper),"restore deep model before file roundtrip");
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".scpart");
            try
            {
                FeatureDocumentFile.Save(path,session.Document);
                session=new FeatureSession<PartFeatureOutput>(FeatureDocumentFile.Load(path),evaluator);
                Volume(48000-Math.PI*25*40,"save reopen reconstructs actual B-Rep");
                Check(session.TryCommit(doc),"reopened feature can edit depth/termination");Volume(24000-Math.PI*25*10,"reopened edit drives B-Rep");
            }
            finally{File.Delete(path);}
        }
        Console.WriteLine($"NATIVE FEATURE GRAPH: {n}/81 PASS");
    }
}
#endif
