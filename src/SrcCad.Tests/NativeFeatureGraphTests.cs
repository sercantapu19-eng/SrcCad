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

            string? flangeEdge=null;string? flangeBody=null;
            foreach(var edgeRef in OcctFeatureKernel.EdgeReferences(sheetSession.Output(sheetNode.Id)!.BRep!))
            {
                try
                {
                    var candidate=OcctFeatureKernel.EdgeFlange(sheetSession.Output(sheetNode.Id)!.BRep!,new[]{edgeRef},12,90,2,0,false);
                    var info=OcctFeatureKernel.Inspect(candidate);
                    if(info[0]>sheetInspect[0]&&info[1]==1){flangeEdge=edgeRef;flangeBody=candidate;break;}
                }
                catch(InvalidOperationException){}
            }
            Check(flangeEdge is not null&&flangeBody is not null,"native edge flange resolves a straight sheet edge and adds joined material");
            var edgeFlangeNode=new FeatureNode(Guid.NewGuid(),"Kenar Flanşı1",
                new SheetMetalEdgeFlangeFeatureParameters(sheetNode.Id,12,90,2,0),
                References:new[]{new FeatureReference(sheetNode.Id,TopologyKind.Edge,flangeEdge!)});
            var edgeFlangeDoc=new FeatureDocument(Guid.NewGuid(),new[]{doc.Features[0],sheetNode,edgeFlangeNode});
            var edgeFlangeSession=new FeatureSession<PartFeatureOutput>(edgeFlangeDoc,evaluator);
            var edgeFlangeInspect=OcctFeatureKernel.Inspect(edgeFlangeSession.Output(edgeFlangeNode.Id)!.BRep!);
            Check(edgeFlangeInspect[0]>sheetInspect[0]&&edgeFlangeInspect[1]==1,
                "sheet metal edge flange rebuilds through feature graph");

            var bossBody=session.Output(boss.Id)!.BRep!;
            var stepPath=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".step");
            var igesPath=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".iges");
            try
            {
                OcctFeatureKernel.ExportStep(bossBody,stepPath);
                var stepBody=OcctFeatureKernel.ImportStep(stepPath);
                var stepInfo=OcctFeatureKernel.Inspect(stepBody);
                Check(Math.Abs(stepInfo[0]-24000)<1e-2&&stepInfo[1]>=1,
                    "STEP export/import preserves solid volume");

                OcctFeatureKernel.ExportIges(bossBody,igesPath);
                var igesBody=OcctFeatureKernel.ImportIges(igesPath);
                var igesInfo=OcctFeatureKernel.Inspect(igesBody);
                Check(Math.Abs(igesInfo[0]-24000)<1e-2&&igesInfo[1]>=1,
                    "IGES export/import preserves solid volume");
            }
            finally
            {
                if(File.Exists(stepPath))File.Delete(stepPath);
                if(File.Exists(igesPath))File.Delete(igesPath);
            }

            var moved=OcctFeatureKernel.Transform(bossBody,new[]{100.0,0.0,0.0,0.0,0.0,0.0});
            var movedInfo=OcctFeatureKernel.Inspect(moved);
            Check(Math.Abs(movedInfo[0]-24000)<1e-5,"detached assembly transform preserves solid volume");
            var sameCommon=OcctFeatureKernel.CommonVolume(bossBody,new double[6],bossBody,new double[6]);
            Check(Math.Abs(sameCommon-24000)<1e-5,"assembly interference returns full overlap volume");
            var farCommon=OcctFeatureKernel.CommonVolume(bossBody,new double[6],bossBody,new[]{1000.0,0.0,0.0,0.0,0.0,0.0});
            Check(farCommon<1e-8,"assembly interference returns zero for separated components");
            var projected=OcctFeatureKernel.ProjectEdges(bossBody,new[]{0.0,-1.0,0.0},new[]{0.0,0.0,1.0});
            Check(projected.Length>=16&&projected.Length%4==0,"technical drawing projection returns line segments");
            var bounds=OcctFeatureKernel.Bounds(bossBody);
            Check(bounds.Length==6&&bounds[3]>bounds[0]&&bounds[4]>bounds[1]&&bounds[5]>bounds[2],"technical drawing kernel exposes model bounds");
            var section=OcctFeatureKernel.SectionEdges(bossBody,
                new[]{(bounds[0]+bounds[3])*0.5,(bounds[1]+bounds[4])*0.5,(bounds[2]+bounds[5])*0.5},
                new[]{1.0,0.0,0.0},new[]{0.0,-1.0,0.0},new[]{0.0,0.0,1.0});
            Check(section.Length>=4&&section.Length%4==0,"technical drawing section kernel returns intersection segments");

            var movedCopy=OcctFeatureKernel.MoveCopy(bossBody,new[]{60.0,0.0,0.0,0.0,0.0,0.0},true);
            var movedCopyInfo=OcctFeatureKernel.Inspect(movedCopy);
            Check(Math.Abs(movedCopyInfo[0]-48000)<1e-3&&movedCopyInfo[1]==2,
                "move/copy body keep-original creates real two-solid B-Rep");

            var offsetPattern=OcctFeatureKernel.PatternOffsets(bossBody,new[]{60.0,0.0,0.0,120.0,0.0,0.0});
            var offsetPatternInfo=OcctFeatureKernel.Inspect(offsetPattern);
            Check(Math.Abs(offsetPatternInfo[0]-72000)<1e-3&&offsetPatternInfo[1]==3,
                "sketch-pattern offsets create real multibody copies");

            var overlapBody=OcctFeatureKernel.Transform(bossBody,new[]{20.0,0.0,0.0,0.0,0.0,0.0});
            var booleanAdd=OcctFeatureKernel.Boolean(bossBody,overlapBody,(int)BodyBooleanOperation.Add);
            var booleanAddInfo=OcctFeatureKernel.Inspect(booleanAdd);
            Check(booleanAddInfo[0]>24000&&booleanAddInfo[0]<48000&&booleanAddInfo[1]==1,
                "boolean add fuses overlapping real solids");
            var booleanCommon=OcctFeatureKernel.Boolean(bossBody,overlapBody,(int)BodyBooleanOperation.Common);
            Check(OcctFeatureKernel.Inspect(booleanCommon)[0]>0,
                "boolean common returns shared solid volume");
            var booleanSubtract=OcctFeatureKernel.Boolean(bossBody,overlapBody,(int)BodyBooleanOperation.Subtract);
            Check(OcctFeatureKernel.Inspect(booleanSubtract)[0]>0&&OcctFeatureKernel.Inspect(booleanSubtract)[0]<24000,
                "boolean subtract removes overlapping body material");

            var splitPlane=new PlaneFrame("Split",plane.Origin+plane.U*20,plane.V,plane.Normal,plane.U).Validate();
            var splitBody=OcctFeatureKernel.SplitToPlane(bossBody,new[]{
                splitPlane.Origin.X,splitPlane.Origin.Y,splitPlane.Origin.Z,
                splitPlane.U.X,splitPlane.U.Y,splitPlane.U.Z,
                splitPlane.V.X,splitPlane.V.Y,splitPlane.V.Z,
                splitPlane.Normal.X,splitPlane.Normal.Y,splitPlane.Normal.Z});
            var splitInfo=OcctFeatureKernel.Inspect(splitBody);
            Check(Math.Abs(splitInfo[0]-24000)<1e-3&&splitInfo[1]==2,
                "split body preserves volume as two real solids");

            var cylinderSketch=new SketchModel(plane);
            cylinderSketch.AddCircle(cylinderSketch.CreatePointExact(new(0,0)),8);
            var cylinderBody=new OcctPartGeometryKernel().Extrude(cylinderSketch,30);
            var cylinderFaces=OcctFeatureKernel.FaceReferences(cylinderBody);
            var cylindricalRef=cylinderFaces.FirstOrDefault(value=>value.StartsWith("C;",StringComparison.Ordinal));
            Check(cylindricalRef is not null,"cylindrical body exposes persistent cylindrical face reference");
            var cylinderFrame=OcctFeatureKernel.FaceFrame(cylinderBody,cylindricalRef!);
            Check(cylinderFrame.Length>=8&&
                  Math.Abs(new Vector3d(cylinderFrame[3],cylinderFrame[4],cylinderFrame[5]).Length-1)<1e-8&&
                  Math.Abs(cylinderFrame[6]-1)<1e-9&&Math.Abs(cylinderFrame[7]-8)<1e-6,
                "cylindrical face resolves normalized axis and radius frame for concentric/tangent mates");

            var bossEdges=OcctFeatureKernel.EdgeReferences(bossBody);
            Check(bossEdges.Length>=4,"extruded body exposes semantic edge references");

            string? weldEdge=null;string? weldedBody=null;
            foreach(var edgeRef in bossEdges)
            {
                try
                {
                    var candidate=OcctFeatureKernel.WeldBead(bossBody,new[]{edgeRef},0.75);
                    var info=OcctFeatureKernel.Inspect(candidate);
                    if(info[0]>24000&&info[1]>=1){weldEdge=edgeRef;weldedBody=candidate;break;}
                }
                catch(InvalidOperationException){}
            }
            Check(weldEdge is not null&&weldedBody is not null,"native weld bead sweeps and fuses real material on a selected edge");
            var weldNode=new FeatureNode(Guid.NewGuid(),"Kaynak Dikişi1",new WeldBeadFeatureParameters(boss.Id,0.75),
                References:new[]{new FeatureReference(boss.Id,TopologyKind.Edge,weldEdge!)});
            var weldDoc=doc with{Features=doc.Features.Append(weldNode).ToArray()};
            var weldPreview=session.Preview(weldDoc);
            Check(weldPreview.Success&&OcctFeatureKernel.Inspect(weldPreview.Outputs[weldNode.Id].BRep!)[0]>24000,
                "weld bead rebuilds through feature graph");
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
            string? endFace=null;string? cappedBody=null;
            foreach(var faceRef in faceRefs)
            {
                try
                {
                    var candidate=OcctFeatureKernel.EndCap(bossBody,faceRef,2,false);
                    var info=OcctFeatureKernel.Inspect(candidate);
                    if(info[0]>24000){endFace=faceRef;cappedBody=candidate;break;}
                }
                catch(InvalidOperationException){}
            }
            Check(endFace is not null&&cappedBody is not null,"native end cap resolves a planar face and adds material");
            var endCapNode=new FeatureNode(Guid.NewGuid(),"Uç Kapağı1",new EndCapFeatureParameters(boss.Id,2),
                References:new[]{new FeatureReference(boss.Id,TopologyKind.Face,endFace!)});
            var endCapDoc=doc with{Features=doc.Features.Append(endCapNode).ToArray()};
            var endCapPreview=session.Preview(endCapDoc);
            Check(endCapPreview.Success&&OcctFeatureKernel.Inspect(endCapPreview.Outputs[endCapNode.Id].BRep!)[0]>24000,
                "end cap rebuilds through feature graph");
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
            var wizardBody=OcctFeatureKernel.HoleWizard(bossBody,workingFace!,topCenter.X,topCenter.Y,topCenter.Z,
                6,10,(int)HoleWizardType.Counterbore,12,3,true);
            var wizardInfo=OcctFeatureKernel.Inspect(wizardBody);
            Check(wizardInfo[0]>0&&wizardInfo[0]<24000&&wizardInfo[1]==1,
                "native Hole Wizard counterbore removes real stepped material");

            string? domeFace=null;string? domeBody=null;
            foreach(var faceRef in faceRefs)
            {
                foreach(var domeHeight in new[]{5.0,-5.0})
                {
                    try
                    {
                        var candidate=OcctFeatureKernel.Dome(bossBody,faceRef,domeHeight);
                        var info=OcctFeatureKernel.Inspect(candidate);
                        if(info[0]>0&&Math.Abs(info[0]-24000)>1e-5){domeFace=faceRef;domeBody=candidate;break;}
                    }
                    catch(InvalidOperationException){}
                }
                if(domeBody is not null)break;
            }
            Check(domeFace is not null&&domeBody is not null,"native dome resolves planar face and changes real B-Rep volume");
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

            var hollowProfile=new SketchModel(plane);
            hollowProfile.AddCornerRectangle(new(-10,-8),new(10,8),.001);
            hollowProfile.AddCircle(hollowProfile.CreatePointExact(new(0,0)),3);
            var hollowProfileNode=new FeatureNode(Guid.NewGuid(),"KutuProfil",new SketchFeatureParameters(plane,hollowProfile.CaptureSnapshot()));
            var hollowSweepNode=new FeatureNode(Guid.NewGuid(),"DelikliSüpürme",
                new SweepFeatureParameters(hollowProfileNode.Id,sweepPathNode.Id));
            var hollowSweepDoc=new FeatureDocument(Guid.NewGuid(),new[]{hollowProfileNode,sweepPathNode,hollowSweepNode});
            var hollowSweepSession=new FeatureSession<PartFeatureOutput>(hollowSweepDoc,evaluator);
            var hollowSweepInspect=OcctFeatureKernel.Inspect(hollowSweepSession.Output(hollowSweepNode.Id)!.BRep!);
            var hollowSweepExpected=(20*16-Math.PI*9)*20;
            Check(Math.Abs(hollowSweepInspect[0]-hollowSweepExpected)<1e-1&&hollowSweepInspect[1]==1,
                "sweep preserves circular inner loop as hollow profile");

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

            var hollowLoft1=new SketchModel(plane);
            hollowLoft1.AddCornerRectangle(new(-10,-8),new(10,8),.001);
            hollowLoft1.AddCircle(hollowLoft1.CreatePointExact(new(0,0)),3);
            var hollowLoft2=new SketchModel(loftPlane2);
            hollowLoft2.AddCornerRectangle(new(-10,-8),new(10,8),.001);
            hollowLoft2.AddCircle(hollowLoft2.CreatePointExact(new(0,0)),3);
            var hollowLoftNode1=new FeatureNode(Guid.NewGuid(),"HollowLoft1",new SketchFeatureParameters(plane,hollowLoft1.CaptureSnapshot()));
            var hollowLoftNode2=new FeatureNode(Guid.NewGuid(),"HollowLoft2",new SketchFeatureParameters(loftPlane2,hollowLoft2.CaptureSnapshot()));
            var hollowLoftFeature=new FeatureNode(Guid.NewGuid(),"DelikliLoft",
                new LoftFeatureParameters(new[]{hollowLoftNode1.Id,hollowLoftNode2.Id}));
            var hollowLoftDoc=new FeatureDocument(Guid.NewGuid(),new[]{hollowLoftNode1,hollowLoftNode2,hollowLoftFeature});
            var hollowLoftSession=new FeatureSession<PartFeatureOutput>(hollowLoftDoc,evaluator);
            var hollowLoftInspect=OcctFeatureKernel.Inspect(hollowLoftSession.Output(hollowLoftFeature.Id)!.BRep!);
            var hollowLoftExpected=(20*16-Math.PI*9)*20;
            Check(Math.Abs(hollowLoftInspect[0]-hollowLoftExpected)<1e-1&&hollowLoftInspect[1]==1,
                "loft preserves matching circular inner loops");

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
