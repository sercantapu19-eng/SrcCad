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

            var sheetNode=new FeatureNode(Guid.NewGuid(),"Taban Flanş1",
                new SheetMetalBaseFeatureParameters(doc.Features[0].Id,2,2,0.5));
            var sheetDoc=new FeatureDocument(Guid.NewGuid(),new[]{doc.Features[0],sheetNode});
            var sheetSession=new FeatureSession<PartFeatureOutput>(sheetDoc,evaluator);
            var sheetInspect=OcctFeatureKernel.Inspect(sheetSession.Output(sheetNode.Id)!.BRep!);
            Check(Math.Abs(sheetInspect[0]-40*30*2)<1e-4&&sheetInspect[1]==1,
                "sheet metal base flange builds real thickness B-Rep");
            var sheetCutSketch=new SketchModel(plane);
            sheetCutSketch.AddCircle(sheetCutSketch.CreatePointExact(new(20,15)),5);
            var sheetCutSketchNode=new FeatureNode(Guid.NewGuid(),"SacKesProfil",new SketchFeatureParameters(plane,sheetCutSketch.CaptureSnapshot()));
            var sheetCutNode=new FeatureNode(Guid.NewGuid(),"Sac Kes1",
                new SheetMetalCutFeatureParameters(sheetNode.Id,sheetCutSketchNode.Id,4,ExtrusionTermination.ThroughAll,false));
            var sheetCutDoc=new FeatureDocument(Guid.NewGuid(),new[]{doc.Features[0],sheetNode,sheetCutSketchNode,sheetCutNode});
            var sheetCutSession=new FeatureSession<PartFeatureOutput>(sheetCutDoc,evaluator);
            var sheetCutInspect=OcctFeatureKernel.Inspect(sheetCutSession.Output(sheetCutNode.Id)!.BRep!);
            Check(sheetCutInspect[0]<sheetInspect[0]&&sheetCutInspect[1]==1,
                "sheet metal cut removes real material through thickness");

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

            var faceRefs=OcctFeatureKernel.FaceReferences(bossBody);
            Check(faceRefs.Length>=6,"extruded body exposes persistent planar face references");
            var topCenter=plane.Origin+plane.U*20+plane.V*15+plane.Normal*20;
            string? workingFace=null;
            foreach(var faceRef in faceRefs)
            {
                try
                {
                    var drilled=OcctFeatureKernel.Hole(bossBody,faceRef,topCenter.X,topCenter.Y,topCenter.Z,6,10,false,true);
                    if(OcctFeatureKernel.Inspect(drilled)[0]<24000){workingFace=faceRef;break;}
                }
                catch(InvalidOperationException){}
            }
            Check(workingFace is not null,"native hole resolves a planar support and removes material");
            var holeNode=new FeatureNode(Guid.NewGuid(),"Delik1",
                new HoleFeatureParameters(boss.Id,topCenter,6,10),
                References:new[]{new FeatureReference(boss.Id,TopologyKind.Face,workingFace!)});
            var holeDoc=doc with{Features=doc.Features.Append(holeNode).ToArray()};
            var holePreview=session.Preview(holeDoc);
            Check(holePreview.Success,"face-referenced hole preview succeeds through feature graph");
            var holeInspect=OcctFeatureKernel.Inspect(holePreview.Outputs[holeNode.Id].BRep!);
            Check(holeInspect[0]<24000&&holeInspect[1]==1,"hole feature produces valid material-removing B-Rep");

            string? shellFace=null;
            string? shelledBody=null;
            foreach(var faceRef in faceRefs)
            {
                try
                {
                    var candidate=OcctFeatureKernel.Shell(bossBody,new[]{faceRef},2,false);
                    var info=OcctFeatureKernel.Inspect(candidate);
                    if(info[0]>0&&info[0]<24000){shellFace=faceRef;shelledBody=candidate;break;}
                }
                catch(InvalidOperationException){}
            }
            Check(shellFace is not null&&shelledBody is not null,"native shell resolves removable face and builds hollow body");
            var shellNode=new FeatureNode(Guid.NewGuid(),"Kabuk1",new ShellFeatureParameters(boss.Id,2),
                References:new[]{new FeatureReference(boss.Id,TopologyKind.Face,shellFace!)});
            var shellDoc=doc with{Features=doc.Features.Append(shellNode).ToArray()};
            var shellPreview=session.Preview(shellDoc);
            Check(shellPreview.Success,"shell preview succeeds through feature graph");
            var shellInspect=OcctFeatureKernel.Inspect(shellPreview.Outputs[shellNode.Id].BRep!);
            Check(shellInspect[0]>0&&shellInspect[0]<24000&&shellInspect[1]==1,"shell feature produces valid hollow solid");

            string? draftFace=null;
            string? draftedBody=null;
            var neutralFrame=new[]{plane.Origin.X,plane.Origin.Y,plane.Origin.Z,plane.Normal.X,plane.Normal.Y,plane.Normal.Z};
            foreach(var faceRef in faceRefs)
            {
                try
                {
                    var candidate=OcctFeatureKernel.Draft(bossBody,new[]{faceRef},neutralFrame,3,false);
                    var info=OcctFeatureKernel.Inspect(candidate);
                    if(info[0]>0&&info[1]==1){draftFace=faceRef;draftedBody=candidate;break;}
                }
                catch(InvalidOperationException){}
            }
            Check(draftFace is not null&&draftedBody is not null,"native draft resolves a face against neutral plane");
            var draftNode=new FeatureNode(Guid.NewGuid(),"Draft1",new DraftFeatureParameters(boss.Id,plane,3),
                References:new[]{new FeatureReference(boss.Id,TopologyKind.Face,draftFace!)});
            var draftDoc=doc with{Features=doc.Features.Append(draftNode).ToArray()};
            var draftPreview=session.Preview(draftDoc);
            Check(draftPreview.Success,"draft preview succeeds through feature graph");
            var draftInspect=OcctFeatureKernel.Inspect(draftPreview.Outputs[draftNode.Id].BRep!);
            Check(draftInspect[0]>0&&draftInspect[1]==1,"draft feature produces valid solid B-Rep");

            var ribPlane=plane with{Origin=plane.Origin+plane.Normal*20,Name="RibFace"};
            var ribSketch=new SketchModel(ribPlane);
            var ribA=ribSketch.CreatePointExact(new(5,15));
            var ribB=ribSketch.CreatePointExact(new(35,15));
            ribSketch.AddLine(ribA,ribB);
            var ribSketchNode=new FeatureNode(Guid.NewGuid(),"ÇizimN",new SketchFeatureParameters(ribPlane,ribSketch.CaptureSnapshot()));
            var ribNode=new FeatureNode(Guid.NewGuid(),"Nervür1",new RibFeatureParameters(boss.Id,ribSketchNode.Id,3,10,false));
            var ribDoc=initial with{Features=initial.Features.Concat(new[]{ribSketchNode,ribNode}).ToArray()};
            var ribPreview=session.Preview(ribDoc);
            Check(ribPreview.Success,"sketch-driven rib preview succeeds through feature graph");
            var ribInspect=OcctFeatureKernel.Inspect(ribPreview.Outputs[ribNode.Id].BRep!);
            Check(ribInspect[0]>24000&&ribInspect[1]==1,"native rib fuses real material to base solid");

            var mirrorPlane=new PlaneFrame("Ayna",plane.Origin,plane.U,plane.V,plane.Normal).Validate();
            var mirrorBody=OcctFeatureKernel.Mirror(bossBody,
                new[]{mirrorPlane.Origin.X,mirrorPlane.Origin.Y,mirrorPlane.Origin.Z,
                      mirrorPlane.U.X,mirrorPlane.U.Y,mirrorPlane.U.Z,
                      mirrorPlane.Normal.X,mirrorPlane.Normal.Y,mirrorPlane.Normal.Z},true);
            var mirrorInspect=OcctFeatureKernel.Inspect(mirrorBody);
            Check(Math.Abs(mirrorInspect[0]-48000)<1e-4&&mirrorInspect[1]==2,
                "native mirror keep-original creates two valid solids");
            var mirrorNode=new FeatureNode(Guid.NewGuid(),"Aynala1",new MirrorFeatureParameters(boss.Id,mirrorPlane,true));
            var mirrorDoc=doc with{Features=doc.Features.Append(mirrorNode).ToArray()};
            var mirrorPreview=session.Preview(mirrorDoc);
            Check(mirrorPreview.Success&&OcctFeatureKernel.Inspect(mirrorPreview.Outputs[mirrorNode.Id].BRep!)[1]==2,
                "mirror feature graph preview rebuilds multi-solid B-Rep");

            var linearBody=OcctFeatureKernel.LinearPattern(bossBody,new[]{1.0,0.0,0.0},60,4);
            var linearInspect=OcctFeatureKernel.Inspect(linearBody);
            Check(Math.Abs(linearInspect[0]-96000)<1e-3&&linearInspect[1]==4,
                "native linear pattern creates four spaced body instances");
            var linearNode=new FeatureNode(Guid.NewGuid(),"Doğrusal Dizilim1",
                new LinearPatternFeatureParameters(boss.Id,Vector3d.UnitX,60,4));
            var linearDoc=doc with{Features=doc.Features.Append(linearNode).ToArray()};
            var linearPreview=session.Preview(linearDoc);
            Check(linearPreview.Success&&OcctFeatureKernel.Inspect(linearPreview.Outputs[linearNode.Id].BRep!)[1]==4,
                "linear pattern rebuilds through feature graph");

            var circularNode=new FeatureNode(Guid.NewGuid(),"Dairesel Dizilim1",
                new CircularPatternFeatureParameters(boss.Id,new Vector3d(100,100,0),Vector3d.UnitZ,360,4));
            var circularDoc=doc with{Features=doc.Features.Append(circularNode).ToArray()};
            var circularPreview=session.Preview(circularDoc);
            Check(circularPreview.Success,"circular pattern preview succeeds through feature graph");
            var circularInspect=OcctFeatureKernel.Inspect(circularPreview.Outputs[circularNode.Id].BRep!);
            Check(Math.Abs(circularInspect[0]-96000)<1e-2&&circularInspect[1]==4,
                "native circular pattern creates four rotated body instances");

            var sweepProfile=new SketchModel(plane);
            sweepProfile.AddCircle(sweepProfile.CreatePointExact(new(0,0)),3);
            var sweepProfileNode=new FeatureNode(Guid.NewGuid(),"SweepProfil",new SketchFeatureParameters(plane,sweepProfile.CaptureSnapshot()));
            var pathNormal=Vector3d.Cross(plane.Normal,plane.V).Normalized();
            var sweepPathPlane=new PlaneFrame("SweepPath",plane.Origin,plane.Normal,plane.V,pathNormal).Validate();
            var sweepPath=new SketchModel(sweepPathPlane);
            var sp0=sweepPath.CreatePointExact(new(0,0));var sp1=sweepPath.CreatePointExact(new(20,0));sweepPath.AddLine(sp0,sp1);
            var sweepPathNode=new FeatureNode(Guid.NewGuid(),"SweepYol",new SketchFeatureParameters(sweepPathPlane,sweepPath.CaptureSnapshot()));
            var sweepNode=new FeatureNode(Guid.NewGuid(),"Süpürme1",new SweepFeatureParameters(sweepProfileNode.Id,sweepPathNode.Id));
            var sweepDoc=new FeatureDocument(Guid.NewGuid(),new[]{sweepProfileNode,sweepPathNode,sweepNode});
            var sweepSession=new FeatureSession<PartFeatureOutput>(sweepDoc,evaluator);
            var sweepInspect=OcctFeatureKernel.Inspect(sweepSession.Output(sweepNode.Id)!.BRep!);
            Check(Math.Abs(sweepInspect[0]-Math.PI*9*20)<1e-2&&sweepInspect[1]==1,"straight-path circular sweep creates valid solid");

            var loftPlane2=plane with{Origin=plane.Origin+plane.Normal*20,Name="Loft2"};
            var loft1=new SketchModel(plane);loft1.AddCircle(loft1.CreatePointExact(new(0,0)),5);
            var loft2=new SketchModel(loftPlane2);loft2.AddCircle(loft2.CreatePointExact(new(0,0)),10);
            var loftNode1=new FeatureNode(Guid.NewGuid(),"LoftP1",new SketchFeatureParameters(plane,loft1.CaptureSnapshot()));
            var loftNode2=new FeatureNode(Guid.NewGuid(),"LoftP2",new SketchFeatureParameters(loftPlane2,loft2.CaptureSnapshot()));
            var loftFeature=new FeatureNode(Guid.NewGuid(),"Loft1",new LoftFeatureParameters(new[]{loftNode1.Id,loftNode2.Id}));
            var loftDoc=new FeatureDocument(Guid.NewGuid(),new[]{loftNode1,loftNode2,loftFeature});
            var loftSession=new FeatureSession<PartFeatureOutput>(loftDoc,evaluator);
            var loftInspect=OcctFeatureKernel.Inspect(loftSession.Output(loftFeature.Id)!.BRep!);
            var loftExpected=Math.PI*20.0/3.0*(25+50+100);
            Check(Math.Abs(loftInspect[0]-loftExpected)<1e-1&&loftInspect[1]==1,"two-circle loft creates frustum solid");

            var revolveSketch=new SketchModel(plane);
            var ra=revolveSketch.CreatePointExact(new(0,-10));
            var rb=revolveSketch.CreatePointExact(new(0,30));
            var revolveAxis=revolveSketch.AddLine(ra,rb,true);
            revolveSketch.AddCornerRectangle(new(5,0),new(15,20),.001);
            var revolveSketchNode=new FeatureNode(Guid.NewGuid(),"ÇizimR",new SketchFeatureParameters(plane,revolveSketch.CaptureSnapshot()));
            var revolveNode=new FeatureNode(Guid.NewGuid(),"Döndürme1",new RevolveFeatureParameters(revolveSketchNode.Id,revolveAxis.Id,360));
            var revolveDoc=new FeatureDocument(Guid.NewGuid(),new[]{revolveSketchNode,revolveNode});
            var revolveSession=new FeatureSession<PartFeatureOutput>(revolveDoc,evaluator);
            var revolveInspect=OcctFeatureKernel.Inspect(revolveSession.Output(revolveNode.Id)!.BRep!);
            Check(Math.Abs(revolveInspect[0]-4000*Math.PI)<1e-3&&revolveInspect[1]==1,
                "360-degree revolve creates expected annular cylinder B-Rep");

            var rcSketch=new SketchModel(plane);
            var rca=rcSketch.CreatePointExact(new(20,-5));
            var rcb=rcSketch.CreatePointExact(new(20,35));
            var rcAxis=rcSketch.AddLine(rca,rcb,true);
            rcSketch.AddCornerRectangle(new(18,5),new(22,25),.001);
            var rcSketchNode=new FeatureNode(Guid.NewGuid(),"ÇizimRC",new SketchFeatureParameters(plane,rcSketch.CaptureSnapshot()));
            var rcNode=new FeatureNode(Guid.NewGuid(),"Döndürerek Kes1",new RevolveCutFeatureParameters(boss.Id,rcSketchNode.Id,rcAxis.Id,360));
            var rcDoc=initial with{Features=initial.Features.Concat(new[]{rcSketchNode,rcNode}).ToArray()};
            var rcPreview=session.Preview(rcDoc);
            Check(rcPreview.Success,"revolved cut preview succeeds against real base body");
            var rcInspect=OcctFeatureKernel.Inspect(rcPreview.Outputs[rcNode.Id].BRep!);
            Check(rcInspect[0]>0&&rcInspect[0]<24000&&rcInspect[1]==1,"revolved cut removes real material");

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
        Console.WriteLine($"NATIVE FEATURE GRAPH: {n} PASS");
    }
}
#endif
