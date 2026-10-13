using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SrcCad.Core.Features;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;

namespace SrcCad.App;

public partial class MainWindow
{
    private FeatureSession<PartFeatureOutput>? _partSession;
    private readonly List<TreeViewItem> _partNodes=new();
    private FeatureNode? _graphSketchEdit;
    private FeatureNode? _graphFeatureEdit;
    private bool _loadingGraphParameters;
    private readonly List<Vector3d> _spatialSketchPoints=new();

    private static string? LastBody(FeatureDocument document,Func<Guid,PartFeatureOutput?> output)
        => System.Linq.Enumerable.Reverse(document.Ordered()).Select(n=>output(n.Id)?.BRep).FirstOrDefault(s=>s is not null);
    private bool PublishGraph(FeatureDocument document,IReadOnlyDictionary<Guid,PartFeatureOutput> outputs)
        => _viewer.PublishFeatureBody(LastBody(document,id=>outputs.GetValueOrDefault(id)));
    private string NextGraphName(string prefix)
    {
        var names=_partSession!.Document.Features.Select(n=>n.Name).ToHashSet(StringComparer.Ordinal);
        int number=1;while(names.Contains(prefix+number))number++;
        return prefix+number;
    }
    private bool EnsurePartSession()
    {
        if(_partSession is not null)return true;
        if(_sketch is null)return false;
        try
        {
            var document=FeatureDocumentFile.FromLegacy(CaptureFeatureState());
            var sketchId=document.Features[0].Id;
            Guid? bossId=document.Features.Length>1?document.Features[1].Id:null;
            var history=_featureHistory.MapTo(s=>FeatureDocumentFile.FromLegacy(s,document.Id,sketchId,bossId));
            var session=new FeatureSession<PartFeatureOutput>(document,new PartFeatureEvaluator(new OcctPartGeometryKernel()),PublishGraph,history);
            _partSession=session;RefreshPartTree();return true;
        }
        catch(Exception ex){Status("Feature graph açılamadı: "+ex.Message);return false;}
    }
    private Guid? SelectedGraphId => (FeatureTree.SelectedItem as TreeViewItem)?.Tag is Guid id?id:null;
    private FeatureNode? SelectedGraphNode => _partSession?.Document.Features.FirstOrDefault(n=>n.Id==SelectedGraphId);
    private FeatureDocument ReplaceGraphNode(FeatureNode node)
    {
        var document=_partSession!.Document;
        return document with{Features=document.Features.Any(f=>f.Id==node.Id)
            ? document.Features.Select(f=>f.Id==node.Id?node:f).ToArray():document.Features.Append(node).ToArray()};
    }
    private string GraphError()=>_partSession!.LastBuild.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??_partSession.LastBuild.Error??"Rebuild başarısız.";
    private void RefreshPartTree(Guid? select=null)
    {
        if(_partSession is null)return;
        if(_sketchNode is not null)PartNode.Items.Remove(_sketchNode);
        if(_extrudeNode is not null)PartNode.Items.Remove(_extrudeNode);
        _sketchNode=_extrudeNode=null;
        foreach(var node in _partNodes)PartNode.Items.Remove(node);_partNodes.Clear();
        var document=_partSession.Document;
        foreach(var feature in document.Features)
        {
            var state=_partSession.State(feature.Id);
            var item=new TreeViewItem{Header=feature.Name+(state==RebuildState.Clean?"":" ["+state+"]"),Tag=feature.Id};
            var menu=new ContextMenu();
            void AddAction(string title,Action action){var entry=new MenuItem{Header=title};entry.Click+=(_,_)=>action();menu.Items.Add(entry);}
            AddAction(feature.Kind==FeatureKind.Sketch?"Çizimi Düzenle":"Özelliği Düzenle",()=>EditGraphFeature(feature.Id));
            AddAction(feature.Suppressed?"Baskılamayı Kaldır":"Baskıla",()=>CommitGraphDocument(ReplaceGraphNode(feature with{Suppressed=!feature.Suppressed})));
            AddAction("Sil",()=>DeleteGraphFeature(feature.Id));
            item.ContextMenu=menu;PartNode.Items.Add(item);_partNodes.Add(item);if(select==feature.Id)item.IsSelected=true;
        }
        _hasSolid=LastBody(document,_partSession.Output) is not null;
        RefreshCommandState();
    }
    private bool CommitGraphDocument(FeatureDocument candidate)
    {
        if(_partSession is null||_inSketch||_extrudeCommandActive)return false;
        if(!_partSession.TryCommit(candidate)){Status(GraphError());return false;}
        RefreshPartTree();return true;
    }
    private void GraphUndo(bool redo)
    {
        _lastHistoryCommand=(redo?"RedoFeatureGraph":"UndoFeatureGraph")+" from "+_lastKeyRoute;
        if(_partSession is null)return;
        var ok=redo?_partSession.Redo():_partSession.Undo();
        if(ok){_viewer.ClearSketch();ClearSketchSelection(false);RefreshPartTree();}
        Status(ok?(redo?"Feature işlemi yinelendi.":"Feature işlemi geri alındı."):GraphError());
    }
    private void DeleteGraphFeature(Guid id)
    {
        if(_partSession is null||_inSketch||_extrudeCommandActive)return;
        var document=_partSession.Document;var dependents=document.Descendants(id);
        if(dependents.Count>0&&MessageBox.Show(this,$"Bu feature ve {dependents.Count} bağımlı öğe silinecek. Devam edilsin mi?","Bağımlı feature silme",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        if(CommitGraphDocument(document.Delete(id,true)))Status("Feature ve bağımlıları tek işlemde silindi • Ctrl+Z: geri al.");
    }
    private bool TryResolveNewSketchPlane(out PlaneFrame plane)
    {
        plane=_selectedPlane;
        if(_viewer.TryGetSelectedPlanarFace(out var face))
        {
            try
            {
                plane=new PlaneFrame(
                    "Seçili Yüz",
                    new Vector3d(face.Origin.X,face.Origin.Y,face.Origin.Z),
                    new Vector3d(face.U.X,face.U.Y,face.U.Z),
                    new Vector3d(face.V.X,face.V.Y,face.V.Z),
                    new Vector3d(face.Normal.X,face.Normal.Y,face.Normal.Z)).Validate();
                return true;
            }
            catch(InvalidOperationException ex)
            {
                Status("Seçili yüz çizim düzlemine dönüştürülemedi: "+ex.Message);
                return false;
            }
        }
        if(_viewer.SelectionSummary()!="Seçim yok")
        {
            Status("Çizim için düzlemsel bir yüz seçin veya FeatureManager'dan Ön/Üst/Sağ Düzlem'i seçin.");
            return false;
        }
        return true;
    }
    private void NewProfileSketch_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||!TryResolveNewSketchPlane(out var plane))return;
        _viewer.ClearSelection();
        var sketch=new SketchModel(plane);
        var node=new FeatureNode(Guid.NewGuid(),NextGraphName("Çizim"),new SketchFeatureParameters(sketch.Plane,sketch.CaptureSnapshot()));
        BeginGraphSketch(node);
    }
    private void BeginGraphSketch(FeatureNode node)
    {
        if(_inSketch||_extrudeCommandActive||node.Parameters is not SketchFeatureParameters source||node.Suppressed)return;
        _graphSketchEdit=node;_sourceSketchEditBefore=null;
        _sketch=new SketchModel(source.Plane);_sketch.RestoreSnapshot(source.Sketch);
        _sketchUndo.Clear();_sketchRedo.Clear();
        var plane=source.Plane;
        _viewer.SetNormalView(plane.Normal.X,plane.Normal.Y,plane.Normal.Z,plane.V.X,plane.V.Y,plane.V.Z);
        _viewer.ShowCommittedSolid();
        _viewer.SetCommittedSolidTransparency(0.38);
        _viewer.FitAll();
        BeginSketchInteraction();
        PropertyTitle.Text=node.Name;
        Status(node.Name+" • "+source.Plane.Name+" • çizimden çıkış tüm bağımlı feature'ları yeniden oluşturur.");
    }
    private bool CommitGraphSketch()
    {
        if(_graphSketchEdit is null||_sketch is null)return true;
        var node=_graphSketchEdit with{Parameters=new SketchFeatureParameters(_sketch.Plane,_sketch.CaptureSnapshot())};
        if(!_partSession!.TryCommit(ReplaceGraphNode(node))){_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.38);RenderSketch();Status(GraphError()+" Çizimi düzeltin veya iptal edin.");return false;}
        _graphSketchEdit=null;_viewer.SetCommittedSolidTransparency(0.0);_viewer.ClearSketch();RefreshPartTree(node.Id);return true;
    }
    private void CancelGraphSketch()
    {
        _graphSketchEdit=null;
        _sketch=null;
        FinishSketchInteraction();_viewer.ClearSketch();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        RefreshPartTree();Status("Çizim düzenlemesi iptal edildi; feature geçmişi değişmedi.");
    }
    private FeatureNode? CutProfileCandidate()
    {
        if(_partSession is null)return null;
        if(SelectedGraphNode is {Kind:FeatureKind.Sketch} selected)return selected;

        var ordered=_partSession.Document.Ordered();
        var lastBody=-1;
        for(var i=0;i<ordered.Length;i++)
            if(_partSession.Output(ordered[i].Id)?.BRep is not null)lastBody=i;
        if(lastBody<0)return null;

        return ordered.Skip(lastBody+1).LastOrDefault(n=>n.Kind==FeatureKind.Sketch);
    }

    private void Cut_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||_partSession is null)return;
        var document=_partSession.Document;
        var sketchNode=CutProfileCandidate();
        if(sketchNode is null){Status("Kesme için katı yüzünde/düzleminde kapalı bir çizim oluşturun.");return;}

        var baseNode=document.Ordered().LastOrDefault(n=>_partSession.Output(n.Id)?.BRep is not null);
        if(baseNode is null){Status("Kesme için mevcut katı gerekiyor.");return;}

        var sketchParameters=(SketchFeatureParameters)sketchNode.Parameters;
        var reverse=string.Equals(sketchParameters.Plane.Name,"Seçili Yüz",StringComparison.Ordinal);
        var node=new FeatureNode(Guid.NewGuid(),NextGraphName("Kes-Ekstrüzyon"),
            new CutFeatureParameters(baseNode.Id,sketchNode.Id,10,ExtrusionTermination.Blind,reverse));

        // Face normals point outward. Prefer the direction into the body, then
        // probe the opposite direction for arbitrary reference-plane sketches.
        var first=_partSession.Preview(ReplaceGraphNode(node));
        if(!first.Success)
        {
            var cut=(CutFeatureParameters)node.Parameters;
            var alternate=node with{Parameters=cut with{Reverse=!cut.Reverse}};
            if(_partSession.Preview(ReplaceGraphNode(alternate)).Success)node=alternate;
        }

        BeginGraphFeature(node);
    }
    private void SheetCut_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||_partSession is null)return;
        if(!_partSession.Document.Features.Any(n=>n.Kind==FeatureKind.SheetMetalBase))
        {Status("Sac Kes yalnız sac levha parçasında kullanılabilir.");return;}
        var sketchNode=CutProfileCandidate();
        if(sketchNode is null){Status("Sac Kes için sac yüzünde kapalı bir Çizim oluşturun.");return;}
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Sac Kes için sac gövdesi gerekiyor.");return;}
        var sketch=(SketchFeatureParameters)sketchNode.Parameters;
        var reverse=string.Equals(sketch.Plane.Name,"Seçili Yüz",StringComparison.Ordinal);
        var node=new FeatureNode(Guid.NewGuid(),NextGraphName("Sac Kes"),
            new SheetMetalCutFeatureParameters(baseNode.Id,sketchNode.Id,10,ExtrusionTermination.ThroughAll,reverse));
        var first=_partSession.Preview(ReplaceGraphNode(node));
        if(!first.Success)
        {
            var cut=(SheetMetalCutFeatureParameters)node.Parameters;
            var alternate=node with{Parameters=cut with{Reverse=!cut.Reverse}};
            if(_partSession.Preview(ReplaceGraphNode(alternate)).Success)node=alternate;
        }
        BeginGraphFeature(node);
    }

    private FeatureNode? CurrentSolidFeature()
        => _partSession?.Document.Ordered().LastOrDefault(n=>_partSession.Output(n.Id)?.BRep is not null);

    private FeatureReference[] SelectedEdgeReferences(Guid producerId)
        => _viewer.SelectedEdgeReferences()
            .Where(value=>!string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Select(value=>new FeatureReference(producerId,TopologyKind.Edge,value))
            .ToArray();

    private void Fillet_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Radyus için mevcut katı gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        var node=new FeatureNode(Guid.NewGuid(),NextGraphName("Radyus"),new FilletFeatureParameters(baseNode.Id,2),
            References:SelectedEdgeReferences(baseNode.Id));
        BeginEdgeFeature(node);
    }

    private void Chamfer_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Pah için mevcut katı gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        var node=new FeatureNode(Guid.NewGuid(),NextGraphName("Pah"),new ChamferFeatureParameters(baseNode.Id,1),
            References:SelectedEdgeReferences(baseNode.Id));
        BeginEdgeFeature(node);
    }

    private void Hem_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var defaults=SheetMetalDefaults();var baseNode=CurrentSolidFeature();
        if(defaults is null||baseNode is null){Status("Hem için sac levha gövdesi gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        BeginEdgeFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Hem"),
            new SheetMetalHemFeatureParameters(baseNode.Id,15,defaults.Value.Thickness,defaults.Value.BendRadius,false),
            References:SelectedEdgeReferences(baseNode.Id)));
    }

    private void Rip_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(SheetMetalDefaults() is null||baseNode is null){Status("Yırtık için sac levha gövdesi gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        BeginEdgeFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Yırtık"),
            new SheetMetalRipFeatureParameters(baseNode.Id,1),References:SelectedEdgeReferences(baseNode.Id)));
    }

    private void CornerRelief_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(SheetMetalDefaults() is null||baseNode is null){Status("Köşe Rölyefi için sac levha gövdesi gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        BeginEdgeFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Köşe Rölyefi"),
            new SheetMetalCornerReliefFeatureParameters(baseNode.Id,3),References:SelectedEdgeReferences(baseNode.Id)));
    }

    private void WeldBead_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Kaynak Dikişi için mevcut katı gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        BeginEdgeFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Kaynak Dikişi"),
            new WeldBeadFeatureParameters(baseNode.Id,2),References:SelectedEdgeReferences(baseNode.Id)));
    }

    private void BeginEdgeFeature(FeatureNode node)
    {
        _graphFeatureEdit=node;
        _extrudeCommandActive=true;
        _loadingGraphParameters=true;
        var value=node.Parameters switch
        {
            FilletFeatureParameters f=>f.Radius,
            ChamferFeatureParameters c=>c.Distance,
            WeldBeadFeatureParameters w=>w.Radius,
            SheetMetalHemFeatureParameters h=>h.Width,
            SheetMetalRipFeatureParameters r=>r.Gap,
            SheetMetalCornerReliefFeatureParameters c=>c.Radius,
            _=>throw new InvalidOperationException("Unsupported edge feature.")
        };
        EdgeFeatureValue.Text=value.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        EdgeFeatureValueLabel.Text=node.Kind switch
        {
            FeatureKind.Fillet=>"Radyus (mm)",
            FeatureKind.Chamfer=>"Pah Mesafesi (mm)",
            FeatureKind.WeldBead=>"Kaynak Yarıçapı (mm)",
            FeatureKind.SheetMetalHem=>"Hem Genişliği (mm)",
            FeatureKind.SheetMetalRip=>"Yırtık Boşluğu (mm)",
            FeatureKind.SheetMetalCornerRelief=>"Rölyef Yarıçapı (mm)",
            _=>"Değer (mm)"
        };
        EdgeFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;
        EdgeFeatureSelectionText.Text=(node.References?.Length??0)==0?"Kenar seçilmedi":$"{node.References!.Length} kenar seçili";
        EdgeFeatureError.Text=(node.References?.Length??0)==0?"Kenar seçin.":"";
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek OCCT B-Rep • Ctrl ile çoklu kenar • canlı önizleme • ✓ tek transaction";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _loadingGraphParameters=false;
        if((node.References?.Length??0)>0)UpdateEdgeFeaturePreview();
        else{_viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();}
        RefreshCommandState();
    }

    internal void UpdateActiveEdgeFeatureSelection()
    {
        if(_partSession is null||_graphFeatureEdit is null||
           _graphFeatureEdit.Parameters is not (FilletFeatureParameters or ChamferFeatureParameters or WeldBeadFeatureParameters or
               SheetMetalHemFeatureParameters or SheetMetalRipFeatureParameters or SheetMetalCornerReliefFeatureParameters))return;
        var baseId=_graphFeatureEdit.Parameters switch
        {
            FilletFeatureParameters f=>f.BaseFeatureId,
            ChamferFeatureParameters c=>c.BaseFeatureId,
            WeldBeadFeatureParameters w=>w.BaseFeatureId,
            SheetMetalHemFeatureParameters h=>h.BaseFeatureId,
            SheetMetalRipFeatureParameters r=>r.BaseFeatureId,
            SheetMetalCornerReliefFeatureParameters c=>c.BaseFeatureId,
            _=>Guid.Empty
        };
        var references=SelectedEdgeReferences(baseId);
        _graphFeatureEdit=_graphFeatureEdit with{References=references};
        EdgeFeatureSelectionText.Text=references.Length==0?"Kenar seçilmedi":$"{references.Length} kenar seçili";
        UpdateEdgeFeaturePreview();
    }

    private FeatureDocument? EdgeFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit is null||
           !DimensionNumbers.TryParse(EdgeFeatureValue.Text,SketchDimensionType.LineLength,out var value,out _)||
           value<=1e-9||(_graphFeatureEdit.References?.Length??0)==0)return null;
        var parameters=_graphFeatureEdit.Parameters switch
        {
            FilletFeatureParameters f=>(FeatureParameters)(f with{Radius=value}),
            ChamferFeatureParameters c=>c with{Distance=value},
            WeldBeadFeatureParameters w=>w with{Radius=value},
            SheetMetalHemFeatureParameters h=>h with{Width=value},
            SheetMetalRipFeatureParameters r=>r with{Gap=value},
            SheetMetalCornerReliefFeatureParameters c=>c with{Radius=value},
            _=>throw new InvalidOperationException("Unsupported edge feature.")
        };
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=parameters});
    }

    private void EdgeFeatureParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateEdgeFeaturePreview();
    }

    private void UpdateEdgeFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit is null)return;
        if((_graphFeatureEdit.References?.Length??0)==0)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
            EdgeFeatureError.Text="En az bir kenar seçin.";return;
        }
        var candidate=EdgeFeatureCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
            EdgeFeatureError.Text="Pozitif, sonlu bir değer girin.";return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body))
        {
            _viewer.ShowCommittedSolid();
            _viewer.SetCommittedSolidTransparency(0.72);
            EdgeFeatureError.Text="";
            return;
        }
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        EdgeFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Önizleme oluşturulamadı.";
    }

    private void EdgeFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=EdgeFeatureCandidate();
        if(candidate is null||_partSession is null){EdgeFeatureError.Text="Kenar ve geçerli değer gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){EdgeFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;
        EndGraphFeature();
        RefreshPartTree(id);
        Status("3B detay özelliği onaylandı; bağımlı model yeniden oluşturuldu.");
    }

    private void EdgeFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private void Hole_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Delik için mevcut katı gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        var node=new FeatureNode(Guid.NewGuid(),NextGraphName("Delik"),
            new HoleFeatureParameters(baseNode.Id,Vector3d.Zero,10,10,ExtrusionTermination.Blind,true));
        BeginHoleFeature(node);
        UpdateActiveHoleSelection();
    }

    private void BeginHoleFeature(FeatureNode node)
    {
        if(node.Parameters is not HoleFeatureParameters hole)throw new InvalidOperationException("Unsupported hole feature.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        HoleDiameter.Text=hole.Diameter.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        HoleDepth.Text=hole.Depth.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        HoleThroughAll.IsChecked=hole.Termination==ExtrusionTermination.ThroughAll;
        HoleReverse.IsChecked=hole.Reverse;
        HoleSelectionText.Text=(node.References?.Length??0)==1
            ?$"Destek yüzü kayıtlı • merkez X={hole.Center.X:0.###} Y={hole.Center.Y:0.###} Z={hole.Center.Z:0.###}"
            :"Düzlemsel yüz seçin ve delik merkezini tıklayın.";
        HoleFeatureError.Text="";
        HoleFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;
        EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Düzlemsel yüz + konum • çap/derinlik • Tümünden Geç • canlı gerçek OCCT Boolean Cut";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        _loadingGraphParameters=false;
        if((node.References?.Length??0)==1)UpdateHoleFeaturePreview();
        RefreshCommandState();
    }

    internal void UpdateActiveHoleSelection()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not HoleFeatureParameters hole)return;
        if(!_viewer.TryGetSelectedPlanarFace(out var face))return;
        var faceReference=_viewer.SelectedFaceReference();
        if(string.IsNullOrWhiteSpace(faceReference))return;

        var center=new Vector3d(face.Origin.X,face.Origin.Y,face.Origin.Z);
        if(_viewer.ScreenToPlane(_lastMouseX,_lastMouseY,
            face.Origin.X,face.Origin.Y,face.Origin.Z,
            face.Normal.X,face.Normal.Y,face.Normal.Z,out var world))
            center=new Vector3d(world.X,world.Y,world.Z);

        _graphFeatureEdit=_graphFeatureEdit with
        {
            Parameters=hole with{Center=center},
            References=new[]{new FeatureReference(hole.BaseFeatureId,TopologyKind.Face,faceReference)}
        };
        HoleSelectionText.Text=$"Yüz seçildi • merkez X={center.X:0.###} Y={center.Y:0.###} Z={center.Z:0.###}";
        UpdateHoleFeaturePreview();
    }

    private FeatureDocument? HoleFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not HoleFeatureParameters hole||
           (_graphFeatureEdit.References?.Length??0)!=1||
           !DimensionNumbers.TryParse(HoleDiameter.Text,SketchDimensionType.LineLength,out var diameter,out _)||
           !DimensionNumbers.TryParse(HoleDepth.Text,SketchDimensionType.LineLength,out var depth,out _)||
           diameter<=1e-9||depth<=1e-9)return null;
        var parameters=hole with
        {
            Diameter=diameter,Depth=depth,
            Termination=HoleThroughAll.IsChecked==true?ExtrusionTermination.ThroughAll:ExtrusionTermination.Blind,
            Reverse=HoleReverse.IsChecked==true
        };
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=parameters});
    }

    private void HoleFeatureParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateHoleFeaturePreview();
    }

    private void UpdateHoleFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not HoleFeatureParameters)return;
        var candidate=HoleFeatureCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
            HoleFeatureError.Text=(_graphFeatureEdit.References?.Length??0)==0
                ?"Düzlemsel yüz ve delik konumu seçin.":"Pozitif çap ve derinlik girin.";
            return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body))
        {
            _viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.72);
            HoleFeatureError.Text="";return;
        }
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        HoleFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Delik önizlemesi oluşturulamadı.";
    }

    private void HoleFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=HoleFeatureCandidate();
        if(candidate is null||_partSession is null){HoleFeatureError.Text="Yüz, konum, çap ve derinlik gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){HoleFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);
        Status("Parametrik delik onaylandı; model yeniden oluşturuldu.");
    }

    private void HoleFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private static int? RevolveAxisId(FeatureNode sketchNode)
    {
        if(sketchNode.Parameters is not SketchFeatureParameters source)return null;
        var axes=source.Sketch.Entities.OfType<SketchLine>().Where(line=>line.Construction).ToArray();
        return axes.Length==1?axes[0].Id:null;
    }

    private FeatureNode? RevolveSketchCandidate(bool cut)
    {
        if(_partSession is null)return null;
        if(SelectedGraphNode is {Kind:FeatureKind.Sketch} selected&&RevolveAxisId(selected) is not null)return selected;
        var ordered=_partSession.Document.Ordered();
        if(cut)
        {
            var lastBody=-1;
            for(var i=0;i<ordered.Length;i++)if(_partSession.Output(ordered[i].Id)?.BRep is not null)lastBody=i;
            if(lastBody<0)return null;
            return ordered.Skip(lastBody+1).LastOrDefault(node=>node.Kind==FeatureKind.Sketch&&RevolveAxisId(node) is not null);
        }
        return ordered.LastOrDefault(node=>node.Kind==FeatureKind.Sketch&&RevolveAxisId(node) is not null);
    }

    private void Revolve_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var sketch=RevolveSketchCandidate(false);
        if(sketch is null||RevolveAxisId(sketch) is not int axis)
        {Status("Döndürme için kapalı profil çiziminde tam bir Merkez Çizgisi ekseni oluşturun.");return;}
        var baseNode=CurrentSolidFeature();
        BeginRevolveFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Döndürme"),
            new RevolveFeatureParameters(sketch.Id,axis,360,false,baseNode?.Id)));
    }

    private void RevolveCut_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Döndürerek Kes için mevcut katı gerekiyor.");return;}
        var sketch=RevolveSketchCandidate(true);
        if(sketch is null||RevolveAxisId(sketch) is not int axis)
        {Status("Döndürerek Kes için katıdan sonra oluşturulmuş kapalı profil + tek Merkez Çizgisi gerekir.");return;}
        BeginRevolveFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Döndürerek Kes"),
            new RevolveCutFeatureParameters(baseNode.Id,sketch.Id,axis,360,false)));
    }

    private void BeginRevolveFeature(FeatureNode node)
    {
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        var angle=node.Parameters switch
        {
            RevolveFeatureParameters r=>r.AngleDegrees,
            RevolveCutFeatureParameters r=>r.AngleDegrees,
            _=>throw new InvalidOperationException("Unsupported revolve feature.")
        };
        var reverse=node.Parameters switch
        {
            RevolveFeatureParameters r=>r.Reverse,
            RevolveCutFeatureParameters r=>r.Reverse,
            _=>false
        };
        var axis=node.Parameters switch
        {
            RevolveFeatureParameters r=>r.AxisEntityId,
            RevolveCutFeatureParameters r=>r.AxisEntityId,
            _=>-1
        };
        RevolveAngle.Text=angle.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        RevolveReverse.IsChecked=reverse;
        RevolveAxisText.Text=$"Merkez Çizgisi / Eksen: entity {axis}";
        RevolveFeatureError.Text="";
        RevolveFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;
        EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek OCCT döndürme • 0–360° • parametrik eksen • canlı B-Rep önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _loadingGraphParameters=false;
        UpdateRevolveFeaturePreview();RefreshCommandState();
    }

    private FeatureDocument? RevolveFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit is null||
           !DimensionNumbers.TryParseScalar(RevolveAngle.Text,out var angle)||
           !double.IsFinite(angle)||angle<=1e-9||angle>360.0)return null;
        var parameters=_graphFeatureEdit.Parameters switch
        {
            RevolveFeatureParameters r=>(FeatureParameters)(r with{AngleDegrees=angle,Reverse=RevolveReverse.IsChecked==true}),
            RevolveCutFeatureParameters r=>r with{AngleDegrees=angle,Reverse=RevolveReverse.IsChecked==true},
            _=>throw new InvalidOperationException("Unsupported revolve feature.")
        };
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=parameters});
    }

    private void RevolveFeatureParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateRevolveFeaturePreview();
    }

    private void UpdateRevolveFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit is null)return;
        var candidate=RevolveFeatureCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
            RevolveFeatureError.Text="Açı 0 ile 360 derece arasında olmalı.";return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){RevolveFeatureError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        RevolveFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Döndürme önizlemesi oluşturulamadı.";
    }

    private void RevolveFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=RevolveFeatureCandidate();
        if(candidate is null||_partSession is null){RevolveFeatureError.Text="Geçerli açı ve eksen gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){RevolveFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);
        Status("Parametrik döndürme özelliği onaylandı; model yeniden oluşturuldu.");
    }

    private void RevolveFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private FeatureReference[] SelectedFaceReferences(Guid producerId)
        => _viewer.SelectedFaceReferences()
            .Where(value=>!string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Select(value=>new FeatureReference(producerId,TopologyKind.Face,value))
            .ToArray();

    private SketchModel GraphSketchModel(FeatureNode node)
    {
        if(node.Parameters is not SketchFeatureParameters source)throw new InvalidOperationException("Sketch feature required.");
        var model=new SketchModel(source.Plane);model.RestoreSnapshot(source.Sketch);return model;
    }

    private static Guid? ComboFeatureId(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag is Guid id?id:null;

    private void FillSketchCombo(ComboBox combo,IEnumerable<FeatureNode> nodes,Guid? selected)
    {
        combo.Items.Clear();
        foreach(var node in nodes)
        {
            var item=new ComboBoxItem{Content=node.Name,Tag=node.Id};
            combo.Items.Add(item);
            if(node.Id==selected)combo.SelectedItem=item;
        }
        if(combo.SelectedItem is null&&combo.Items.Count>0)combo.SelectedIndex=0;
    }

    private void SpatialSketch_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        BeginSpatialSketchFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("3B Çizim"),
            new SpatialSketchFeatureParameters(Array.Empty<Vector3d>())));
    }

    private void BeginSpatialSketchFeature(FeatureNode node)
    {
        if(node.Parameters is not SpatialSketchFeatureParameters spatial)throw new InvalidOperationException("Unsupported 3D sketch.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        _spatialSketchPoints.Clear();_spatialSketchPoints.AddRange(spatial.Points);
        SpatialClosed.IsChecked=spatial.Closed;
        SpatialPointX.Text="0";SpatialPointY.Text="0";SpatialPointZ.Text="0";
        SpatialSketchPanel.Visibility=Visibility.Visible;
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Parametrik dünya-koordinatlı 3B yol • Sweep ve Yapısal Eleman girdisi • save/open/history destekli";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _loadingGraphParameters=false;
        RefreshSpatialSketchPreview();RefreshCommandState();
    }

    private void RefreshSpatialSketchPreview()
    {
        SpatialPointList.Text=_spatialSketchPoints.Count==0?"Henüz nokta yok.":string.Join("\n",
            _spatialSketchPoints.Select((p,i)=>$"{i+1}: X={p.X:0.###} Y={p.Y:0.###} Z={p.Z:0.###}"));
        SpatialSketchError.Text=_spatialSketchPoints.Count<2?"En az iki 3B nokta gerekiyor.":"";
        _viewer.ClearSketch();
        for(var i=0;i+1<_spatialSketchPoints.Count;i++)
        {
            var a=_spatialSketchPoints[i];var b=_spatialSketchPoints[i+1];
            _viewer.AddStyledSketchLine(a.X,a.Y,a.Z,b.X,b.Y,b.Z,0);
        }
        if(SpatialClosed.IsChecked==true&&_spatialSketchPoints.Count>=3)
        {
            var a=_spatialSketchPoints[^1];var b=_spatialSketchPoints[0];
            _viewer.AddStyledSketchLine(a.X,a.Y,a.Z,b.X,b.Y,b.Z,0);
        }
        foreach(var p in _spatialSketchPoints)_viewer.AddSketchPoint(p.X,p.Y,p.Z,false);
        _viewer.FitAll();
    }

    private void SpatialPointAdd_Click(object sender,RoutedEventArgs e)
    {
        if(!TryReferenceScalar(SpatialPointX.Text,out var x)||!TryReferenceScalar(SpatialPointY.Text,out var y)||
           !TryReferenceScalar(SpatialPointZ.Text,out var z))
        {SpatialSketchError.Text="X/Y/Z değerleri sonlu sayı olmalı.";return;}
        var point=new Vector3d(x,y,z);
        if(_spatialSketchPoints.Count>0&&(point-_spatialSketchPoints[^1]).Length<=1e-9)
        {SpatialSketchError.Text="Ardışık noktalar aynı olamaz.";return;}
        _spatialSketchPoints.Add(point);RefreshSpatialSketchPreview();
    }

    private void SpatialPointRemove_Click(object sender,RoutedEventArgs e)
    {
        if(_spatialSketchPoints.Count>0)_spatialSketchPoints.RemoveAt(_spatialSketchPoints.Count-1);
        RefreshSpatialSketchPreview();
    }

    private void SpatialSketchChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)RefreshSpatialSketchPreview();
    }

    private FeatureDocument? SpatialSketchCandidate()
    {
        if(_partSession is null||_graphFeatureEdit is null||_spatialSketchPoints.Count<2||
           (SpatialClosed.IsChecked==true&&_spatialSketchPoints.Count<3))return null;
        return ReplaceGraphNode(_graphFeatureEdit with
        {
            Parameters=new SpatialSketchFeatureParameters(_spatialSketchPoints.ToArray(),SpatialClosed.IsChecked==true)
        });
    }

    private void SpatialSketchOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=SpatialSketchCandidate();
        if(candidate is null||_partSession is null){SpatialSketchError.Text="Geçerli 3B yol gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){SpatialSketchError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;
        _viewer.ClearSketch();EndGraphFeature();RefreshPartTree(id);Status("Parametrik 3B Çizim oluşturuldu.");
    }

    private void SpatialSketchCancel_Click(object sender,RoutedEventArgs e)
    {
        _viewer.ClearSketch();EndGraphFeature();
    }

    private FeatureNode[] ClosedProfileSketches()
        => _partSession?.Document.Features.Where(n=>n.Kind==FeatureKind.Sketch).Where(n=>
        {
            try
            {
                var model=GraphSketchModel(n);
                return model.TryGetExtrusionProfile(out _,out _)||model.TryGetSingleCircle(out _);
            }
            catch{return false;}
        }).ToArray()??Array.Empty<FeatureNode>();

    private FeatureNode[] PathSketches()
        => _partSession?.Document.Features.Where(n=>
            n.Kind==FeatureKind.SpatialSketch ||
            (n.Kind==FeatureKind.Sketch&&((SketchFeatureParameters)n.Parameters).Sketch.Entities.OfType<SketchLine>().Any(l=>!l.Construction))).ToArray()
            ??Array.Empty<FeatureNode>();

    private static double PolygonArea(IReadOnlyList<Point2d> points)
    {
        double area=0;
        for(var i=0;i<points.Count;i++)
        {
            var a=points[i];var b=points[(i+1)%points.Count];
            area+=a.X*b.Y-b.X*a.Y;
        }
        return Math.Abs(area)*0.5;
    }

    private static double StructuralProfileArea(SketchModel profile)
    {
        if(profile.TryGetSingleCircle(out var circle))return Math.PI*circle.Radius*circle.Radius;
        if(!profile.TryGetExtrusionProfile(out var outer,out var polygonHoles,out var circleHoles))return 0;
        var area=PolygonArea(outer)-polygonHoles.Sum(PolygonArea);
        foreach(var hole in circleHoles)area-=Math.PI*hole.Radius*hole.Radius;
        return Math.Max(0,area);
    }

    private static double StructuralPathLength(SketchModel path)
        => path.Entities.OfType<SketchLine>().Where(l=>!l.Construction)
            .Sum(l=>Point2d.Distance(path.Point(l.StartPointId).Position,path.Point(l.EndPointId).Position));

    private void ProfileLibrary_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var menu=new ContextMenu();
        void Add(string title,Action action)
        {
            var item=new MenuItem{Header=title};item.Click+=(_,_)=>action();menu.Items.Add(item);
        }
        Add("Dikdörtgen Profil",()=>CreateLibraryProfile(false));
        Add("Dairesel Profil",()=>CreateLibraryProfile(true));
        menu.PlacementTarget=sender as UIElement;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;
    }

    private void CreateLibraryProfile(bool circle)
    {
        if(_partSession is null)return;
        double width=circle?40:60,height=circle?40:40;
        var title=circle?"Dairesel Profil Çapı":"Profil Genişliği";
        var first=new DimensionEditDialog(title,width,"mm",text=>
        {
            if(!DimensionNumbers.TryParseScalar(text,out var value)||value<=1e-9)return "Pozitif ölçü girin.";
            width=value;return null;
        }){Owner=this};
        if(first.ShowDialog()!=true)return;
        if(!circle)
        {
            var second=new DimensionEditDialog("Profil Yüksekliği",height,"mm",text=>
            {
                if(!DimensionNumbers.TryParseScalar(text,out var value)||value<=1e-9)return "Pozitif ölçü girin.";
                height=value;return null;
            }){Owner=this};
            if(second.ShowDialog()!=true)return;
        }

        var sketch=new SketchModel(_selectedPlane);
        if(circle)
        {
            var center=sketch.CreatePointExact(new Point2d(0,0));
            sketch.AddCircle(center,width*0.5);
        }
        else sketch.AddCornerRectangle(new Point2d(-width*0.5,-height*0.5),new Point2d(width*0.5,height*0.5),1e-9);

        var node=new FeatureNode(Guid.NewGuid(),NextGraphName(circle?"Profil Daire":"Profil Dikdörtgen"),
            new SketchFeatureParameters(sketch.Plane,sketch.CaptureSnapshot()));
        if(!_partSession.TryCommit(ReplaceGraphNode(node))){Status(GraphError());return;}
        RefreshPartTree(node.Id);
        Status($"{node.Name} oluşturuldu • {width:0.###}"+(circle?" mm çap":$" × {height:0.###} mm"));
    }

    private void Gusset_Click(object sender,RoutedEventArgs e)
    {
        if(_partSession is null||!_hasSolid){Status("Köşebent için mevcut yapısal/katı gövde gerekiyor.");return;}
        var sketch=RibSketchCandidate();
        if(sketch is null){Status("Köşebent için gövdeye temas eden açık çizgi sketch'i oluşturun.");return;}
        var baseNode=CurrentSolidFeature();
        if(baseNode is null)return;
        BeginRibFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Köşebent"),
            new RibFeatureParameters(baseNode.Id,sketch.Id,6,30,false)));
    }

    private void StructuralCutList_Click(object sender,RoutedEventArgs e)
    {
        if(_partSession is null){Status("Kesim listesi için yapısal eleman içeren parça gerekiyor.");return;}
        var members=_partSession.Document.Features.Where(f=>f.Kind==FeatureKind.StructuralMember).ToArray();
        PropertyTitle.Text="Kesim Listesi";
        if(members.Length==0)
        {
            PropertyInfo.Text="Yapısal Eleman feature'ı bulunamadı.";
            ManagerTabs.SelectedItem=PropertyManagerTab;return;
        }

        var rows=new List<string>();
        var index=1;
        foreach(var member in members)
        {
            var p=(StructuralMemberFeatureParameters)member.Parameters;
            var profile=GraphSketchModel(_partSession.Document.Node(p.ProfileSketchId));
            var path=GraphSketchModel(_partSession.Document.Node(p.PathSketchId));
            var length=StructuralPathLength(path);
            var area=StructuralProfileArea(profile);
            var volume=_partSession.Output(member.Id)?.BRep is string body
                ?SrcCad.Kernel.Occt.OcctFeatureKernel.Inspect(body)[0]:0;
            rows.Add($"{index++}. {member.Name}\n   Boy: {length:0.###} mm • Kesit Alanı: {area:0.###} mm² • Hacim: {volume:0.###} mm³");
        }
        PropertyInfo.Text=string.Join("\n\n",rows);
        ManagerTabs.SelectedItem=PropertyManagerTab;
        Status($"{members.Length} yapısal eleman için gerçek kesim listesi hesaplandı.");
    }

    private void StructuralTrim_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Kırp / Uzat için mevcut profil/katı gerekiyor.");return;}
        PlaneFrame plane;
        if(!TryResolveMirrorPlane(out plane))plane=_selectedPlane;
        BeginStructuralTrimFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Kırp"),
            new StructuralTrimFeatureParameters(baseNode.Id,plane,true)));
    }

    private void BeginStructuralTrimFeature(FeatureNode node)
    {
        if(node.Parameters is not StructuralTrimFeatureParameters trim)throw new InvalidOperationException("Unsupported structural trim.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        StructuralTrimPlaneText.Text=trim.Plane.Name;
        StructuralTrimKeepPositive.IsChecked=trim.KeepPositive;
        StructuralTrimError.Text="";
        StructuralTrimPanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;SpatialSketchPanel.Visibility=Visibility.Collapsed;
        EdgeFeaturePanel.Visibility=Visibility.Collapsed;HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        MultiSketchFeaturePanel.Visibility=Visibility.Collapsed;ShellFeaturePanel.Visibility=Visibility.Collapsed;DraftFeaturePanel.Visibility=Visibility.Collapsed;
        RibFeaturePanel.Visibility=Visibility.Collapsed;ReferenceGeometryPanel.Visibility=Visibility.Collapsed;MirrorFeaturePanel.Visibility=Visibility.Collapsed;
        PatternFeaturePanel.Visibility=Visibility.Collapsed;SheetMetalBasePanel.Visibility=Visibility.Collapsed;SheetMetalConvertPanel.Visibility=Visibility.Collapsed;
        EdgeFlangePanel.Visibility=Visibility.Collapsed;EndCapPanel.Visibility=Visibility.Collapsed;
        SpatialSketchPanel.Visibility=Visibility.Collapsed;StructuralTrimPanel.Visibility=Visibility.Collapsed;SheetBendPanel.Visibility=Visibility.Collapsed;
        BodyBooleanPanel.Visibility=Visibility.Collapsed;MoveCopyBodyPanel.Visibility=Visibility.Collapsed;DomePanel.Visibility=Visibility.Collapsed;HoleWizardPanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Gerçek OCCT yarım-uzay kırpma • parametrik düzlem • canlı B-Rep önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;
        UpdateStructuralTrimPreview();RefreshCommandState();
    }

    private FeatureDocument? StructuralTrimCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not StructuralTrimFeatureParameters trim)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=trim with{KeepPositive=StructuralTrimKeepPositive.IsChecked==true}});
    }

    private void StructuralTrimParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateStructuralTrimPreview();
    }

    private void UpdateStructuralTrimPreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not StructuralTrimFeatureParameters)return;
        var candidate=StructuralTrimCandidate();
        if(candidate is null)return;
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body))
        {
            _viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.72);StructuralTrimError.Text="";return;
        }
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        StructuralTrimError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Kırpma önizlemesi oluşturulamadı; korunacak tarafı değiştirin.";
    }

    private void StructuralTrimOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=StructuralTrimCandidate();
        if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){StructuralTrimError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Profil kırpma feature'ı onaylandı.");
    }

    private void StructuralTrimCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private void EndCap_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Uç Kapağı için mevcut katı gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        BeginEndCapFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Uç Kapağı"),
            new EndCapFeatureParameters(baseNode.Id,3,false),
            References:SelectedFaceReferences(baseNode.Id).Take(1).ToArray()));
    }

    private void BeginEndCapFeature(FeatureNode node)
    {
        if(node.Parameters is not EndCapFeatureParameters cap)throw new InvalidOperationException("Unsupported end cap.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        EndCapThickness.Text=cap.Thickness.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        EndCapReverse.IsChecked=cap.Reverse;
        EndCapSelectionText.Text=(node.References?.Length??0)==1?"Uç yüzü seçili":"Düzlemsel uç yüzü seçin.";
        EndCapError.Text=(node.References?.Length??0)==1?"":"Yüz seçimi gerekiyor.";
        EndCapPanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;SpatialSketchPanel.Visibility=Visibility.Collapsed;StructuralTrimPanel.Visibility=Visibility.Collapsed;SheetBendPanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;HoleFeaturePanel.Visibility=Visibility.Collapsed;
        RevolveFeaturePanel.Visibility=Visibility.Collapsed;MultiSketchFeaturePanel.Visibility=Visibility.Collapsed;ShellFeaturePanel.Visibility=Visibility.Collapsed;
        DraftFeaturePanel.Visibility=Visibility.Collapsed;RibFeaturePanel.Visibility=Visibility.Collapsed;ReferenceGeometryPanel.Visibility=Visibility.Collapsed;
        MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;SheetMetalBasePanel.Visibility=Visibility.Collapsed;
        SheetMetalConvertPanel.Visibility=Visibility.Collapsed;EdgeFlangePanel.Visibility=Visibility.Collapsed;EndCapPanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Gerçek OCCT uç kapağı • düzlemsel yüz • kalınlık • canlı B-Rep önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;_viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        _loadingGraphParameters=false;
        if((node.References?.Length??0)==1)UpdateEndCapPreview();
        RefreshCommandState();
    }

    internal void UpdateActiveEndCapSelection()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not EndCapFeatureParameters cap)return;
        var refs=SelectedFaceReferences(cap.BaseFeatureId).Take(1).ToArray();
        _graphFeatureEdit=_graphFeatureEdit with{References=refs};
        EndCapSelectionText.Text=refs.Length==1?"Uç yüzü seçili":"Düzlemsel uç yüzü seçin.";
        UpdateEndCapPreview();
    }

    private FeatureDocument? EndCapCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not EndCapFeatureParameters cap||
           (_graphFeatureEdit.References?.Length??0)!=1||
           !DimensionNumbers.TryParse(EndCapThickness.Text,SketchDimensionType.LineLength,out var thickness,out _)||
           thickness<=1e-9)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=cap with{Thickness=thickness,Reverse=EndCapReverse.IsChecked==true}});
    }

    private void EndCapParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateEndCapPreview();
    }

    private void UpdateEndCapPreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not EndCapFeatureParameters)return;
        var candidate=EndCapCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
            EndCapError.Text=(_graphFeatureEdit.References?.Length??0)==0?"Bir düzlemsel uç yüzü seçin.":"Pozitif kalınlık girin.";return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body))
        {
            _viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.72);EndCapError.Text="";return;
        }
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        EndCapError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Uç kapağı önizlemesi oluşturulamadı.";
    }

    private void EndCapOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=EndCapCandidate();
        if(candidate is null||_partSession is null){EndCapError.Text="Yüz ve geçerli kalınlık gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){EndCapError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Parametrik uç kapağı onaylandı.");
    }

    private void EndCapCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private void SweepBoss_Click(object sender,RoutedEventArgs e)=>StartSweepLoft(false,false);
    private void SweepCut_Click(object sender,RoutedEventArgs e)=>StartSweepLoft(false,true);
    private void StructuralMember_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var profiles=ClosedProfileSketches();
        var paths=PathSketches();
        if(profiles.Length==0){Status("Yapısal Eleman için kapalı profil çizimi gerekiyor.");return;}
        if(paths.Length==0){Status("Yapısal Eleman için çizgisel yol çizimi gerekiyor.");return;}
        var profile=SelectedGraphNode is {Kind:FeatureKind.Sketch} selected&&profiles.Any(p=>p.Id==selected.Id)?selected:profiles[^1];
        var path=paths.LastOrDefault(n=>n.Id!=profile.Id);
        if(path is null){Status("Profil ve yol farklı çizimler olmalı.");return;}
        BeginMultiSketchFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Yapısal Eleman"),
            new StructuralMemberFeatureParameters(profile.Id,path.Id)));
    }
    private void LoftBoss_Click(object sender,RoutedEventArgs e)=>StartSweepLoft(true,false);
    private void LoftCut_Click(object sender,RoutedEventArgs e)=>StartSweepLoft(true,true);
    private void BoundaryBoss_Click(object sender,RoutedEventArgs e)=>StartBoundary(false);
    private void BoundaryCut_Click(object sender,RoutedEventArgs e)=>StartBoundary(true);

    private void StartBoundary(bool cut)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var baseNode=CurrentSolidFeature();
        if(cut&&baseNode is null){Status("Sınır Kes için mevcut katı gerekiyor.");return;}
        var profiles=ClosedProfileSketches();
        if(profiles.Length<2){Status("Sınır özelliği için en az iki kapalı profil çizimi gerekiyor.");return;}
        var ids=profiles.TakeLast(2).Select(n=>n.Id).ToArray();
        FeatureNode node=cut
            ?new(Guid.NewGuid(),NextGraphName("Sınır Kes"),new BoundaryCutFeatureParameters(baseNode!.Id,ids))
            :new(Guid.NewGuid(),NextGraphName("Sınır"),new BoundaryFeatureParameters(ids,baseNode?.Id));
        BeginMultiSketchFeature(node);
    }

    private void StartSweepLoft(bool loft,bool cut)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var baseNode=CurrentSolidFeature();
        if(cut&&baseNode is null){Status((loft?"Loft Kes":"Süpürerek Kes")+" için mevcut katı gerekiyor.");return;}
        var profiles=ClosedProfileSketches();
        if(profiles.Length==0){Status("Kapalı profil çizimi gerekiyor.");return;}

        if(loft)
        {
            if(profiles.Length<2){Status("Loft için en az iki kapalı profil çizimi gerekiyor.");return;}
            var ids=profiles.TakeLast(2).Select(n=>n.Id).ToArray();
            FeatureNode node=cut
                ?new(Guid.NewGuid(),NextGraphName("Loft Kes"),new LoftCutFeatureParameters(baseNode!.Id,ids))
                :new(Guid.NewGuid(),NextGraphName("Loft"),new LoftFeatureParameters(ids,baseNode?.Id));
            BeginMultiSketchFeature(node);
            return;
        }

        var paths=PathSketches();
        if(paths.Length==0){Status("Süpürme için çizgisel bir yol çizimi gerekiyor.");return;}
        var profile=SelectedGraphNode is {Kind:FeatureKind.Sketch} selected&&profiles.Any(p=>p.Id==selected.Id)?selected:profiles[^1];
        var path=paths.LastOrDefault(n=>n.Id!=profile.Id);
        if(path is null){Status("Profil ve yol farklı çizimler olmalı.");return;}
        FeatureNode sweep=cut
            ?new(Guid.NewGuid(),NextGraphName("Süpürerek Kes"),new SweepCutFeatureParameters(baseNode!.Id,profile.Id,path.Id))
            :new(Guid.NewGuid(),NextGraphName("Süpürme"),new SweepFeatureParameters(profile.Id,path.Id,baseNode?.Id));
        BeginMultiSketchFeature(sweep);
    }

    private void BeginMultiSketchFeature(FeatureNode node)
    {
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        var profiles=ClosedProfileSketches();
        var paths=PathSketches();
        Guid? p1=null,p2=null,path=null;
        switch(node.Parameters)
        {
            case SweepFeatureParameters s:p1=s.ProfileSketchId;path=s.PathSketchId;break;
            case SweepCutFeatureParameters s:p1=s.ProfileSketchId;path=s.PathSketchId;break;
            case LoftFeatureParameters l:p1=l.ProfileSketchIds.ElementAtOrDefault(0);p2=l.ProfileSketchIds.ElementAtOrDefault(1);break;
            case LoftCutFeatureParameters l:p1=l.ProfileSketchIds.ElementAtOrDefault(0);p2=l.ProfileSketchIds.ElementAtOrDefault(1);break;
            case BoundaryFeatureParameters b:p1=b.ProfileSketchIds.ElementAtOrDefault(0);p2=b.ProfileSketchIds.ElementAtOrDefault(1);break;
            case BoundaryCutFeatureParameters b:p1=b.ProfileSketchIds.ElementAtOrDefault(0);p2=b.ProfileSketchIds.ElementAtOrDefault(1);break;
            case StructuralMemberFeatureParameters s:p1=s.ProfileSketchId;path=s.PathSketchId;break;
            default:throw new InvalidOperationException("Unsupported sweep/loft/structural feature.");
        }
        MultiSketchFeatureTitle.Text=node.Kind switch
        {
            FeatureKind.Sweep=>"Süpürme",FeatureKind.SweptCut=>"Süpürerek Kes",
            FeatureKind.Loft=>"Loft",FeatureKind.LoftedCut=>"Loft Kes",
            FeatureKind.Boundary=>"Sınır",FeatureKind.BoundaryCut=>"Sınır Kes",
            FeatureKind.StructuralMember=>"Yapısal Eleman",_=>"Süpürme / Loft"
        };
        MultiProfile2Panel.Visibility=node.Parameters is LoftFeatureParameters or LoftCutFeatureParameters or BoundaryFeatureParameters or BoundaryCutFeatureParameters?Visibility.Visible:Visibility.Collapsed;
        MultiPathPanel.Visibility=node.Parameters is SweepFeatureParameters or SweepCutFeatureParameters or StructuralMemberFeatureParameters?Visibility.Visible:Visibility.Collapsed;
        FillSketchCombo(MultiProfile1,profiles,p1);
        FillSketchCombo(MultiProfile2,profiles,p2);
        FillSketchCombo(MultiPath,paths,path);
        MultiSketchFeatureInfo.Text=node.Parameters switch
        {
            StructuralMemberFeatureParameters=>"Profil / Weldment başlangıcı • kapalı kesit + tek, dallanmayan yol • gerçek OCCT katısı.",
            SweepFeatureParameters or SweepCutFeatureParameters=>"Kapalı profil + tek, dallanmayan çizgisel yol. Profil delikleri bu ilk çekirdekte kabul edilmez.",
            _=>"En az iki kapalı profil. Profillerin sırası FeatureManager sırasına göre loft edilir."
        };
        MultiSketchFeatureError.Text="";
        MultiSketchFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        ShellFeaturePanel.Visibility=Visibility.Collapsed;DraftFeaturePanel.Visibility=Visibility.Collapsed;RibFeaturePanel.Visibility=Visibility.Collapsed;
        ReferenceGeometryPanel.Visibility=Visibility.Collapsed;MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Gerçek OCCT Süpürme/Loft • parametrik çizim bağımlılıkları • canlı B-Rep önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;
        UpdateMultiSketchFeaturePreview();RefreshCommandState();
    }

    private FeatureDocument? MultiSketchFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit is null)return null;
        var first=ComboFeatureId(MultiProfile1);
        if(first is null)return null;
        FeatureParameters? parameters=_graphFeatureEdit.Parameters switch
        {
            SweepFeatureParameters s when ComboFeatureId(MultiPath) is Guid path&&path!=first
                =>s with{ProfileSketchId=first.Value,PathSketchId=path},
            SweepCutFeatureParameters s when ComboFeatureId(MultiPath) is Guid path&&path!=first
                =>s with{ProfileSketchId=first.Value,PathSketchId=path},
            LoftFeatureParameters l when ComboFeatureId(MultiProfile2) is Guid second&&second!=first
                =>l with{ProfileSketchIds=new[]{first.Value,second}},
            LoftCutFeatureParameters l when ComboFeatureId(MultiProfile2) is Guid second&&second!=first
                =>l with{ProfileSketchIds=new[]{first.Value,second}},
            BoundaryFeatureParameters b when ComboFeatureId(MultiProfile2) is Guid second&&second!=first
                =>b with{ProfileSketchIds=new[]{first.Value,second}},
            BoundaryCutFeatureParameters b when ComboFeatureId(MultiProfile2) is Guid second&&second!=first
                =>b with{ProfileSketchIds=new[]{first.Value,second}},
            StructuralMemberFeatureParameters member when ComboFeatureId(MultiPath) is Guid memberPath&&memberPath!=first
                =>member with{ProfileSketchId=first.Value,PathSketchId=memberPath},
            _=>null
        };
        return parameters is null?null:ReplaceGraphNode(_graphFeatureEdit with{Parameters=parameters});
    }

    private void MultiSketchFeatureParameterChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateMultiSketchFeaturePreview();
    }

    private void UpdateMultiSketchFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit is null)return;
        var candidate=MultiSketchFeatureCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
            MultiSketchFeatureError.Text="Farklı ve geçerli profil/yol çizimleri seçin.";return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){MultiSketchFeatureError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        MultiSketchFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Süpürme/Loft önizlemesi oluşturulamadı.";
    }

    private void MultiSketchFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=MultiSketchFeatureCandidate();
        if(candidate is null||_partSession is null){MultiSketchFeatureError.Text="Geçerli profil/yol seçimi gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){MultiSketchFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);
        Status("Parametrik "+candidate.Node(id).Name+" onaylandı.");
    }

    private void MultiSketchFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private FeatureNode[] BodyFeatureNodes()
        => _partSession?.Document.Ordered().Where(n=>_partSession.Output(n.Id)?.BRep is not null).ToArray()
            ??Array.Empty<FeatureNode>();

    private void FillBodyCombo(ComboBox combo,IEnumerable<FeatureNode> nodes,Guid? selected)
    {
        combo.Items.Clear();
        foreach(var node in nodes)
        {
            var item=new ComboBoxItem{Content=node.Name,Tag=node.Id};
            combo.Items.Add(item);
            if(node.Id==selected)combo.SelectedItem=item;
        }
        if(combo.SelectedItem is null&&combo.Items.Count>0)combo.SelectedIndex=0;
    }

    private void BodyBoolean_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var bodies=BodyFeatureNodes();
        if(bodies.Length<2){Status("Boolean için en az iki gövde üreten feature gerekiyor.");return;}
        BeginBodyBooleanFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Birleştir"),
            new BodyBooleanFeatureParameters(bodies[^2].Id,bodies[^1].Id,BodyBooleanOperation.Add)));
    }

    private void BeginBodyBooleanFeature(FeatureNode node)
    {
        if(node.Parameters is not BodyBooleanFeatureParameters op)throw new InvalidOperationException("Unsupported body boolean.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        var bodies=BodyFeatureNodes().Where(n=>n.Id!=node.Id).ToArray();
        FillBodyCombo(BooleanBodyA,bodies,op.BodyAId);FillBodyCombo(BooleanBodyB,bodies,op.BodyBId);
        BooleanOperation.Items.Clear();
        foreach(var operation in Enum.GetValues<BodyBooleanOperation>())
            BooleanOperation.Items.Add(new ComboBoxItem{Content=operation switch
            {
                BodyBooleanOperation.Add=>"Birleştir (Add)",BodyBooleanOperation.Subtract=>"Çıkar (Subtract)",_=>"Ortak (Common)"
            },Tag=operation});
        BooleanOperation.SelectedIndex=(int)op.Operation;
        BodyBooleanError.Text="";BodyBooleanPanel.Visibility=Visibility.Visible;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Gerçek OCCT Fuse / Cut / Common • iki feature gövdesi • canlı B-Rep önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;
        UpdateBodyBooleanPreview();RefreshCommandState();
    }

    private FeatureDocument? BodyBooleanCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not BodyBooleanFeatureParameters op||
           ComboFeatureId(BooleanBodyA) is not Guid a||ComboFeatureId(BooleanBodyB) is not Guid b||a==b||
           (BooleanOperation.SelectedItem as ComboBoxItem)?.Tag is not BodyBooleanOperation operation)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=op with{BodyAId=a,BodyBId=b,Operation=operation}});
    }

    private void BodyBooleanParameterChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateBodyBooleanPreview();
    }

    private void UpdateBodyBooleanPreview()
    {
        var candidate=BodyBooleanCandidate();
        if(candidate is null||_partSession is null){BodyBooleanError.Text="İki farklı gövde ve işlem seçin.";return;}
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){BodyBooleanError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        BodyBooleanError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Boolean önizlemesi oluşturulamadı.";
    }

    private void BodyBooleanOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=BodyBooleanCandidate();
        if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){BodyBooleanError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Gövde Boolean işlemi onaylandı.");
    }

    private void MoveCopyBody_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();if(baseNode is null){Status("Taşı/Kopyala için mevcut gövde gerekiyor.");return;}
        BeginMoveCopyBodyFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Gövde Taşı"),
            new MoveCopyBodyFeatureParameters(baseNode.Id,Vector3d.Zero,Vector3d.Zero,false)));
    }

    private void BeginMoveCopyBodyFeature(FeatureNode node)
    {
        if(node.Parameters is not MoveCopyBodyFeatureParameters move)throw new InvalidOperationException("Unsupported body transform.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        MoveBodyX.Text=move.Translation.X.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        MoveBodyY.Text=move.Translation.Y.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        MoveBodyZ.Text=move.Translation.Z.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        MoveBodyRx.Text=move.RotationDegrees.X.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        MoveBodyRy.Text=move.RotationDegrees.Y.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        MoveBodyRz.Text=move.RotationDegrees.Z.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        MoveBodyKeepOriginal.IsChecked=move.KeepOriginal;MoveCopyBodyError.Text="";
        MoveCopyBodyPanel.Visibility=Visibility.Visible;PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek 3B transform • orijinali koru ile multibody kopya • canlı önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;UpdateMoveCopyBodyPreview();RefreshCommandState();
    }

    private FeatureDocument? MoveCopyBodyCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not MoveCopyBodyFeatureParameters move||
           !TryReferenceScalar(MoveBodyX.Text,out var x)||!TryReferenceScalar(MoveBodyY.Text,out var y)||!TryReferenceScalar(MoveBodyZ.Text,out var z)||
           !TryReferenceScalar(MoveBodyRx.Text,out var rx)||!TryReferenceScalar(MoveBodyRy.Text,out var ry)||!TryReferenceScalar(MoveBodyRz.Text,out var rz))
            return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=move with
        {
            Translation=new Vector3d(x,y,z),RotationDegrees=new Vector3d(rx,ry,rz),KeepOriginal=MoveBodyKeepOriginal.IsChecked==true
        }});
    }

    private void MoveCopyBodyParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateMoveCopyBodyPreview();
    }

    private void UpdateMoveCopyBodyPreview()
    {
        var candidate=MoveCopyBodyCandidate();
        if(candidate is null||_partSession is null){MoveCopyBodyError.Text="Sonlu transform değerleri girin.";return;}
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){MoveCopyBodyError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        MoveCopyBodyError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Transform önizlemesi oluşturulamadı.";
    }

    private void MoveCopyBodyOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=MoveCopyBodyCandidate();if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){MoveCopyBodyError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Gövde transformu onaylandı.");
    }

    private void SplitBody_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();if(baseNode is null)return;
        if(!TryResolveMirrorPlane(out var plane))plane=_selectedPlane;
        BeginSplitBodyFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Böl"),new SplitBodyFeatureParameters(baseNode.Id,plane)));
    }

    private void BeginSplitBodyFeature(FeatureNode node)
    {
        if(node.Parameters is not SplitBodyFeatureParameters split)throw new InvalidOperationException("Unsupported split body.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        SplitBodyPlaneText.Text=split.Plane.Name;SplitBodyError.Text="";
        SplitBodyPanel.Visibility=Visibility.Visible;PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek OCCT iki-yarım uzay bölme • her iki katı multibody sonuçta korunur";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;
        UpdateSplitBodyPreview();RefreshCommandState();
    }

    private FeatureDocument? SplitBodyCandidate()
        => _partSession is null||_graphFeatureEdit?.Parameters is not SplitBodyFeatureParameters
            ?null:ReplaceGraphNode(_graphFeatureEdit);

    private void UpdateSplitBodyPreview()
    {
        var candidate=SplitBodyCandidate();
        if(candidate is null||_partSession is null)return;
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){SplitBodyError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        SplitBodyError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Bölme düzlemi gövdeyi kesmiyor.";
    }

    private void SplitBodyOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=SplitBodyCandidate();if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){SplitBodyError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Gövde iki multibody katıya bölündü.");
    }

    private FeatureNode[] SketchPatternCandidates()
        => _partSession?.Document.Features.Where(n=>n.Parameters is SketchFeatureParameters source&&
            source.Sketch.Points.Values.Count(p=>p.Id!=0)>0).ToArray()??Array.Empty<FeatureNode>();

    private void SketchPattern_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var baseNode=CurrentSolidFeature();if(baseNode is null)return;
        var sketches=SketchPatternCandidates();
        if(sketches.Length==0){Status("Çizim Güdümlü Dizilim için en az bir noktalı sketch gerekiyor.");return;}
        var selected=SelectedGraphNode is {Kind:FeatureKind.Sketch} pick&&sketches.Any(n=>n.Id==pick.Id)?pick:sketches[^1];
        BeginSketchPatternFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Çizim Dizilimi"),
            new SketchPatternFeatureParameters(baseNode.Id,selected.Id)));
    }

    private void BeginSketchPatternFeature(FeatureNode node)
    {
        if(node.Parameters is not SketchPatternFeatureParameters pattern)throw new InvalidOperationException("Unsupported sketch pattern.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        FillSketchCombo(SketchPatternSketch,SketchPatternCandidates(),pattern.SketchId);
        SketchPatternError.Text="";SketchPatternPanel.Visibility=Visibility.Visible;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Sketch noktaları → gerçek 3B multibody kopyalar • history/rebuild destekli";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;
        UpdateSketchPatternPreview();RefreshCommandState();
    }

    private FeatureDocument? SketchPatternCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not SketchPatternFeatureParameters pattern||
           ComboFeatureId(SketchPatternSketch) is not Guid sketchId)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=pattern with{SketchId=sketchId}});
    }

    private void SketchPatternParameterChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateSketchPatternPreview();
    }

    private void UpdateSketchPatternPreview()
    {
        var candidate=SketchPatternCandidate();
        if(candidate is null||_partSession is null){SketchPatternError.Text="Nokta içeren sketch seçin.";return;}
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){SketchPatternError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        SketchPatternError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Çizim dizilimi önizlemesi oluşturulamadı.";
    }

    private void SketchPatternOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=SketchPatternCandidate();if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){SketchPatternError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Çizim güdümlü multibody dizilim onaylandı.");
    }

    private void Dome_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();if(baseNode is null)return;
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        BeginDomeFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Kubbe"),new DomeFeatureParameters(baseNode.Id,10),
            References:SelectedFaceReferences(baseNode.Id).Take(1).ToArray()));
    }

    private void BeginDomeFeature(FeatureNode node)
    {
        if(node.Parameters is not DomeFeatureParameters dome)throw new InvalidOperationException("Unsupported dome.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        DomeHeight.Text=dome.Height.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        DomeSelectionText.Text=(node.References?.Length??0)==1?"Yüz seçili":"Düzlemsel yüz seçin.";
        DomeError.Text="";DomePanel.Visibility=Visibility.Visible;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Gerçek küresel OCCT kubbe • negatif yükseklik içe çökertme • canlı önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;
        if((node.References?.Length??0)==1)UpdateDomePreview();RefreshCommandState();
    }

    internal void UpdateActiveDomeSelection()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not DomeFeatureParameters dome)return;
        var refs=SelectedFaceReferences(dome.BaseFeatureId).Take(1).ToArray();
        _graphFeatureEdit=_graphFeatureEdit with{References=refs};
        DomeSelectionText.Text=refs.Length==1?"Yüz seçili":"Düzlemsel yüz seçin.";UpdateDomePreview();
    }

    private FeatureDocument? DomeCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not DomeFeatureParameters dome||
           (_graphFeatureEdit.References?.Length??0)!=1||!TryReferenceScalar(DomeHeight.Text,out var height)||Math.Abs(height)<=1e-9)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=dome with{Height=height}});
    }

    private void DomeParameterChanged(object sender,RoutedEventArgs e){if(!_loadingGraphParameters)UpdateDomePreview();}
    private void UpdateDomePreview()
    {
        var candidate=DomeCandidate();
        if(candidate is null||_partSession is null){DomeError.Text="Yüz ve sıfırdan farklı yükseklik gerekiyor.";return;}
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){DomeError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        DomeError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Kubbe önizlemesi oluşturulamadı.";
    }
    private void DomeOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=DomeCandidate();if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){DomeError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Kubbe onaylandı.");
    }

    private void HoleWizard_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();if(baseNode is null)return;
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        BeginHoleWizardFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Delik Sihirbazı"),
            new HoleWizardFeatureParameters(baseNode.Id,Vector3d.Zero,10,20,HoleWizardType.Simple,18,5,true)));
        UpdateActiveHoleWizardSelection();
    }

    private void BeginHoleWizardFeature(FeatureNode node)
    {
        if(node.Parameters is not HoleWizardFeatureParameters hole)throw new InvalidOperationException("Unsupported hole wizard.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        HoleWizardTypeBox.Items.Clear();
        foreach(var type in Enum.GetValues<HoleWizardType>())HoleWizardTypeBox.Items.Add(new ComboBoxItem{Content=type switch
        {HoleWizardType.Simple=>"Basit",HoleWizardType.Counterbore=>"Silindirik Havşa",_=>"Konik Havşa"},Tag=type});
        HoleWizardTypeBox.SelectedIndex=(int)hole.Type;
        HoleWizardDiameter.Text=hole.Diameter.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        HoleWizardDepth.Text=hole.Depth.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        HoleWizardCounterDiameter.Text=hole.CounterDiameter.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        HoleWizardCounterDepth.Text=hole.CounterDepth.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        HoleWizardReverse.IsChecked=hole.Reverse;HoleWizardSelectionText.Text="Düzlemsel yüz seçin ve delik merkezini tıklayın.";
        HoleWizardError.Text="";HoleWizardPanel.Visibility=Visibility.Visible;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Basit / Counterbore / Countersink • gerçek Boolean kesici • persistent yüz referansı";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;RefreshCommandState();
    }

    internal void UpdateActiveHoleWizardSelection()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not HoleWizardFeatureParameters hole)return;
        if(!_viewer.TryGetSelectedPlanarFace(out var face))return;
        var faceReference=_viewer.SelectedFaceReference();if(string.IsNullOrWhiteSpace(faceReference))return;
        var center=new Vector3d(face.Origin.X,face.Origin.Y,face.Origin.Z);
        if(_viewer.ScreenToPlane(_lastMouseX,_lastMouseY,face.Origin.X,face.Origin.Y,face.Origin.Z,
            face.Normal.X,face.Normal.Y,face.Normal.Z,out var world))center=new Vector3d(world.X,world.Y,world.Z);
        _graphFeatureEdit=_graphFeatureEdit with{Parameters=hole with{Center=center},
            References=new[]{new FeatureReference(hole.BaseFeatureId,TopologyKind.Face,faceReference)}};
        HoleWizardSelectionText.Text=$"Yüz seçildi • merkez X={center.X:0.###} Y={center.Y:0.###} Z={center.Z:0.###}";
        UpdateHoleWizardPreview();
    }

    private FeatureDocument? HoleWizardCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not HoleWizardFeatureParameters hole||
           (_graphFeatureEdit.References?.Length??0)!=1||
           (HoleWizardTypeBox.SelectedItem as ComboBoxItem)?.Tag is not HoleWizardType type||
           !DimensionNumbers.TryParse(HoleWizardDiameter.Text,SketchDimensionType.LineLength,out var dia,out _)||
           !DimensionNumbers.TryParse(HoleWizardDepth.Text,SketchDimensionType.LineLength,out var depth,out _)||
           !DimensionNumbers.TryParse(HoleWizardCounterDiameter.Text,SketchDimensionType.LineLength,out var counterDia,out _)||
           !DimensionNumbers.TryParse(HoleWizardCounterDepth.Text,SketchDimensionType.LineLength,out var counterDepth,out _))return null;
        if(dia<=1e-9||depth<=1e-9||(type!=HoleWizardType.Simple&&(counterDia<=dia||counterDepth<=1e-9)))return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=hole with
        {Diameter=dia,Depth=depth,Type=type,CounterDiameter=counterDia,CounterDepth=counterDepth,Reverse=HoleWizardReverse.IsChecked==true}});
    }

    private void HoleWizardParameterChanged(object sender,RoutedEventArgs e){if(!_loadingGraphParameters)UpdateHoleWizardPreview();}
    private void HoleWizardParameterChanged(object sender,SelectionChangedEventArgs e){if(!_loadingGraphParameters)UpdateHoleWizardPreview();}
    private void UpdateHoleWizardPreview()
    {
        var candidate=HoleWizardCandidate();
        if(candidate is null||_partSession is null){HoleWizardError.Text="Yüz/konum ve geçerli delik ölçüleri gerekiyor.";return;}
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){HoleWizardError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        HoleWizardError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Delik Sihirbazı önizlemesi oluşturulamadı.";
    }
    private void HoleWizardOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=HoleWizardCandidate();if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){HoleWizardError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Delik Sihirbazı feature'ı onaylandı.");
    }

    private void AdvancedBodyCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private void Shell_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Kabuk için mevcut katı gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        BeginShellFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Kabuk"),new ShellFeatureParameters(baseNode.Id,2),
            References:SelectedFaceReferences(baseNode.Id)));
    }

    private void BeginShellFeature(FeatureNode node)
    {
        if(node.Parameters is not ShellFeatureParameters shell)throw new InvalidOperationException("Unsupported shell feature.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        ShellThickness.Text=shell.Thickness.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        ShellOutward.IsChecked=shell.Outward;
        ShellSelectionText.Text=(node.References?.Length??0)==0?"Yüz seçilmedi":$"{node.References!.Length} yüz açılacak";
        ShellFeatureError.Text=(node.References?.Length??0)==0?"En az bir yüz seçin.":"";
        ShellFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;
        EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;
        RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek OCCT kalın cidarlı kabuk • Ctrl ile çoklu açılacak yüz • canlı önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        _loadingGraphParameters=false;
        if((node.References?.Length??0)>0)UpdateShellFeaturePreview();
        else{_viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();}
        RefreshCommandState();
    }

    internal void UpdateActiveShellSelection()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not ShellFeatureParameters shell)return;
        var references=SelectedFaceReferences(shell.BaseFeatureId);
        _graphFeatureEdit=_graphFeatureEdit with{References=references};
        ShellSelectionText.Text=references.Length==0?"Yüz seçilmedi":$"{references.Length} yüz açılacak";
        UpdateShellFeaturePreview();
    }

    private FeatureDocument? ShellFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not ShellFeatureParameters shell||
           (_graphFeatureEdit.References?.Length??0)==0||
           !DimensionNumbers.TryParse(ShellThickness.Text,SketchDimensionType.LineLength,out var thickness,out _)||
           thickness<=1e-9)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=shell with{Thickness=thickness,Outward=ShellOutward.IsChecked==true}});
    }

    private void ShellFeatureParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateShellFeaturePreview();
    }

    private void UpdateShellFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not ShellFeatureParameters)return;
        var candidate=ShellFeatureCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
            ShellFeatureError.Text=(_graphFeatureEdit.References?.Length??0)==0?"En az bir açılacak yüz seçin.":"Pozitif kabuk kalınlığı girin.";
            return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body))
        {
            _viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.72);
            ShellFeatureError.Text="";return;
        }
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        ShellFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Kabuk önizlemesi oluşturulamadı.";
    }

    private void ShellFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=ShellFeatureCandidate();
        if(candidate is null||_partSession is null){ShellFeatureError.Text="Yüz ve geçerli kalınlık gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){ShellFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);
        Status("Parametrik kabuk onaylandı; model yeniden oluşturuldu.");
    }

    private void ShellFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private static PlaneFrame ReferencePlaneFrame(ReferencePlaneFeatureParameters parameters,string name="Referans Düzlem")
        => (parameters.BasePlane with
        {
            Name=name,
            Origin=parameters.BasePlane.Origin+parameters.BasePlane.Normal*parameters.Offset
        }).Validate();

    private void ReferencePlane_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        BeginReferenceGeometryFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Düzlem"),
            new ReferencePlaneFeatureParameters(_selectedPlane,20)));
    }

    private void ReferenceAxis_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        BeginReferenceGeometryFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Eksen"),
            new ReferenceAxisFeatureParameters(_selectedPlane.Origin,_selectedPlane.Normal)));
    }

    private void ReferencePoint_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        BeginReferenceGeometryFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Nokta"),
            new ReferencePointFeatureParameters(_selectedPlane.Origin)));
    }

    private void BeginReferenceGeometryFeature(FeatureNode node)
    {
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        ReferenceGeometryTypeText.Text=node.Name;
        ReferencePlaneEditor.Visibility=Visibility.Collapsed;
        ReferenceAxisEditor.Visibility=Visibility.Collapsed;
        ReferencePointEditor.Visibility=Visibility.Collapsed;
        ReferenceGeometryError.Text="";
        switch(node.Parameters)
        {
            case ReferencePlaneFeatureParameters plane:
                ReferencePlaneEditor.Visibility=Visibility.Visible;
                ReferencePlaneBaseText.Text=plane.BasePlane.Name;
                ReferencePlaneOffset.Text=plane.Offset.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                PropertyInfo.Text="Parametrik ofset düzlem • Çizim, Aynala ve Draft referansı.";
                break;
            case ReferenceAxisFeatureParameters axis:
                ReferenceAxisEditor.Visibility=Visibility.Visible;
                ReferenceAxisX.Text=axis.Origin.X.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                ReferenceAxisY.Text=axis.Origin.Y.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                ReferenceAxisZ.Text=axis.Origin.Z.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                ReferenceAxisDx.Text=axis.Direction.X.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                ReferenceAxisDy.Text=axis.Direction.Y.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                ReferenceAxisDz.Text=axis.Direction.Z.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                PropertyInfo.Text="Parametrik referans ekseni • Dairesel Dizilim ekseni.";
                break;
            case ReferencePointFeatureParameters point:
                ReferencePointEditor.Visibility=Visibility.Visible;
                ReferencePointX.Text=point.Position.X.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                ReferencePointY.Text=point.Position.Y.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                ReferencePointZ.Text=point.Position.Z.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                PropertyInfo.Text="Parametrik 3B referans noktası.";
                break;
            default:throw new InvalidOperationException("Unsupported reference geometry.");
        }
        ReferenceGeometryPanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        ShellFeaturePanel.Visibility=Visibility.Collapsed;DraftFeaturePanel.Visibility=Visibility.Collapsed;
        RibFeaturePanel.Visibility=Visibility.Collapsed;MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;ManagerTabs.SelectedItem=PropertyManagerTab;
        _loadingGraphParameters=false;RefreshCommandState();
    }

    private static bool TryReferenceScalar(string text,out double value)
    {
        if(DimensionNumbers.TryParseScalar(text,out value)&&double.IsFinite(value))return true;
        value=0;return false;
    }

    private FeatureDocument? ReferenceGeometryCandidate()
    {
        if(_partSession is null||_graphFeatureEdit is null)return null;
        FeatureParameters? parameters=_graphFeatureEdit.Parameters switch
        {
            ReferencePlaneFeatureParameters plane when TryReferenceScalar(ReferencePlaneOffset.Text,out var offset)
                => plane with{Offset=offset},
            ReferenceAxisFeatureParameters when
                TryReferenceScalar(ReferenceAxisX.Text,out var ax)&&TryReferenceScalar(ReferenceAxisY.Text,out var ay)&&TryReferenceScalar(ReferenceAxisZ.Text,out var az)&&
                TryReferenceScalar(ReferenceAxisDx.Text,out var dx)&&TryReferenceScalar(ReferenceAxisDy.Text,out var dy)&&TryReferenceScalar(ReferenceAxisDz.Text,out var dz)&&
                new Vector3d(dx,dy,dz).Length>1e-9
                => new ReferenceAxisFeatureParameters(new Vector3d(ax,ay,az),new Vector3d(dx,dy,dz).Normalized()),
            ReferencePointFeatureParameters when
                TryReferenceScalar(ReferencePointX.Text,out var px)&&TryReferenceScalar(ReferencePointY.Text,out var py)&&TryReferenceScalar(ReferencePointZ.Text,out var pz)
                => new ReferencePointFeatureParameters(new Vector3d(px,py,pz)),
            _=>null
        };
        return parameters is null?null:ReplaceGraphNode(_graphFeatureEdit with{Parameters=parameters});
    }

    private void ReferenceGeometryParameterChanged(object sender,RoutedEventArgs e)
    {
        if(_loadingGraphParameters)return;
        ReferenceGeometryError.Text=ReferenceGeometryCandidate() is null?"Geçerli, sonlu referans değerleri girin.":"";
    }

    private void ReferenceGeometryOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=ReferenceGeometryCandidate();
        if(candidate is null||_partSession is null){ReferenceGeometryError.Text="Geçerli referans geometri değerleri gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){ReferenceGeometryError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;
        var committed=candidate.Node(id);
        if(committed.Parameters is ReferencePlaneFeatureParameters plane)_selectedPlane=ReferencePlaneFrame(plane,committed.Name);
        EndGraphFeature();RefreshPartTree(id);Status(committed.Name+" oluşturuldu.");
    }

    private void ReferenceGeometryCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private void Draft_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Draft için mevcut katı gerekiyor.");return;}
        var neutral=_selectedPlane;
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        BeginDraftFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Draft"),
            new DraftFeatureParameters(baseNode.Id,neutral,3,false),
            References:SelectedFaceReferences(baseNode.Id)));
    }

    private void BeginDraftFeature(FeatureNode node)
    {
        if(node.Parameters is not DraftFeatureParameters draft)throw new InvalidOperationException("Unsupported draft feature.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        DraftAngle.Text=draft.AngleDegrees.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        DraftReverse.IsChecked=draft.Reverse;
        DraftNeutralPlaneText.Text=draft.NeutralPlane.Name;
        DraftSelectionText.Text=(node.References?.Length??0)==0?"Yüz seçilmedi":$"{node.References!.Length} yüz seçili";
        DraftFeatureError.Text=(node.References?.Length??0)==0?"En az bir draft yüzü seçin.":"";
        DraftFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        ShellFeaturePanel.Visibility=Visibility.Collapsed;MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek OCCT draft • nötr düzlem + seçili yüzler • parametrik açı • canlı B-Rep önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        _loadingGraphParameters=false;
        if((node.References?.Length??0)>0)UpdateDraftFeaturePreview();
        else{_viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();}
        RefreshCommandState();
    }

    internal void UpdateActiveDraftSelection()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not DraftFeatureParameters draft)return;
        var references=SelectedFaceReferences(draft.BaseFeatureId);
        _graphFeatureEdit=_graphFeatureEdit with{References=references};
        DraftSelectionText.Text=references.Length==0?"Yüz seçilmedi":$"{references.Length} yüz seçili";
        UpdateDraftFeaturePreview();
    }

    private FeatureDocument? DraftFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not DraftFeatureParameters draft||
           (_graphFeatureEdit.References?.Length??0)==0||
           !DimensionNumbers.TryParseScalar(DraftAngle.Text,out var angle)||
           !double.IsFinite(angle)||angle<=1e-9||angle>=89.0)return null;
        return ReplaceGraphNode(_graphFeatureEdit with
        {
            Parameters=draft with{AngleDegrees=angle,Reverse=DraftReverse.IsChecked==true}
        });
    }

    private void DraftFeatureParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateDraftFeaturePreview();
    }

    private void UpdateDraftFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not DraftFeatureParameters)return;
        var candidate=DraftFeatureCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
            DraftFeatureError.Text=(_graphFeatureEdit.References?.Length??0)==0
                ?"En az bir draft yüzü seçin.":"Açı 0 ile 89 derece arasında olmalı.";
            return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body))
        {
            _viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.72);
            DraftFeatureError.Text="";return;
        }
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        DraftFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Draft önizlemesi oluşturulamadı.";
    }

    private void DraftFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=DraftFeatureCandidate();
        if(candidate is null||_partSession is null){DraftFeatureError.Text="Yüz ve geçerli draft açısı gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){DraftFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);
        Status("Parametrik draft onaylandı; model yeniden oluşturuldu.");
    }

    private void DraftFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private static bool HasRibLines(FeatureNode node)
        => node.Parameters is SketchFeatureParameters sketch&&sketch.Sketch.Entities.OfType<SketchLine>().Any(line=>!line.Construction);

    private FeatureNode? RibSketchCandidate()
    {
        if(_partSession is null)return null;
        if(SelectedGraphNode is {Kind:FeatureKind.Sketch} selected&&HasRibLines(selected))return selected;
        var ordered=_partSession.Document.Ordered();
        var lastBody=-1;
        for(var i=0;i<ordered.Length;i++)if(_partSession.Output(ordered[i].Id)?.BRep is not null)lastBody=i;
        if(lastBody<0)return null;
        return ordered.Skip(lastBody+1).LastOrDefault(node=>node.Kind==FeatureKind.Sketch&&HasRibLines(node));
    }

    private void Rib_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Nervür için mevcut katı gerekiyor.");return;}
        var sketch=RibSketchCandidate();
        if(sketch is null)
        {Status("Nervür için katıdan sonra açık çizgi içeren bir Çizim oluşturun veya FeatureManager'dan seçin.");return;}
        BeginRibFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Nervür"),
            new RibFeatureParameters(baseNode.Id,sketch.Id,3,10,false)));
    }

    private void BeginRibFeature(FeatureNode node)
    {
        if(node.Parameters is not RibFeatureParameters rib)throw new InvalidOperationException("Unsupported rib feature.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        RibThickness.Text=rib.Thickness.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        RibDepth.Text=rib.Depth.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        RibReverse.IsChecked=rib.Reverse;
        RibSketchText.Text=_partSession?.Document.Node(rib.SketchId).Name??"Çizim";
        RibFeatureError.Text="";
        RibFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        ShellFeaturePanel.Visibility=Visibility.Collapsed;DraftFeaturePanel.Visibility=Visibility.Collapsed;
        MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek OCCT nervür • açık sketch çizgileri • kalınlık/derinlik • canlı B-Rep fuse önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _loadingGraphParameters=false;
        UpdateRibFeaturePreview();RefreshCommandState();
    }

    private FeatureDocument? RibFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not RibFeatureParameters rib||
           !DimensionNumbers.TryParse(RibThickness.Text,SketchDimensionType.LineLength,out var thickness,out _)||
           !DimensionNumbers.TryParse(RibDepth.Text,SketchDimensionType.LineLength,out var depth,out _)||
           thickness<=1e-9||depth<=1e-9)return null;
        return ReplaceGraphNode(_graphFeatureEdit with
        {
            Parameters=rib with{Thickness=thickness,Depth=depth,Reverse=RibReverse.IsChecked==true}
        });
    }

    private void RibFeatureParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateRibFeaturePreview();
    }

    private void UpdateRibFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not RibFeatureParameters)return;
        var candidate=RibFeatureCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
            RibFeatureError.Text="Pozitif kalınlık ve derinlik girin.";return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){RibFeatureError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        RibFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Nervür önizlemesi oluşturulamadı.";
    }

    private void RibFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=RibFeatureCandidate();
        if(candidate is null||_partSession is null){RibFeatureError.Text="Geçerli nervür parametreleri gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){RibFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);
        Status("Parametrik nervür onaylandı; model yeniden oluşturuldu.");
    }

    private void RibFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private string? CurrentBodyBRep()
    {
        if(!EnsurePartSession()||_partSession is null)return null;
        var node=CurrentSolidFeature();
        return node is null?null:_partSession.Output(node.Id)?.BRep;
    }

    private void InspectMeasure_Click(object sender,RoutedEventArgs e)
    {
        var measure=_viewer.SelectedTopologyMeasure();
        PropertyTitle.Text="Ölç";
        if(measure.Length<5)
        {
            PropertyInfo.Text="Tek bir kenar, yüz veya köşe seçin. Kenarda uzunluk, yüzde alan, köşede koordinat ölçülür.";
            ManagerTabs.SelectedItem=PropertyManagerTab;Status("Ölçüm için tek topolojik öğe seçin.");return;
        }
        var details=measure[0] switch
        {
            2=>$"Kenar Uzunluğu: {measure[1]:0.###} mm\nMerkez: X={measure[2]:0.###}, Y={measure[3]:0.###}, Z={measure[4]:0.###}",
            4=>$"Yüz Alanı: {measure[1]:0.###} mm²\nAğırlık Merkezi: X={measure[2]:0.###}, Y={measure[3]:0.###}, Z={measure[4]:0.###}",
            1=>$"Köşe Koordinatı: X={measure[2]:0.###}, Y={measure[3]:0.###}, Z={measure[4]:0.###}",
            _=>"Bu seçim türü henüz ölçülemiyor."
        };
        PropertyInfo.Text=details;ManagerTabs.SelectedItem=PropertyManagerTab;Status("Seçili geometri ölçüldü.");
    }

    private void InspectMassProperties_Click(object sender,RoutedEventArgs e)
    {
        var body=CurrentBodyBRep();
        PropertyTitle.Text="Kütle Özellikleri";
        if(body is null){PropertyInfo.Text="Ölçülecek katı yok.";ManagerTabs.SelectedItem=PropertyManagerTab;return;}
        try
        {
            var info=SrcCad.Kernel.Occt.OcctFeatureKernel.Inspect(body);
            PropertyInfo.Text=$"Hacim: {info[0]:0.###} mm³\nKatı Sayısı: {info[1]:0}\nYüz Sayısı: {info[2]:0}\nKenar Sayısı: {info[3]:0}\n\nMalzeme/yoğunluk tanımlı olmadığı için kütle değeri hesaplanmadı.";
            ManagerTabs.SelectedItem=PropertyManagerTab;Status("Geometrik kütle özellikleri hesaplandı.");
        }
        catch(Exception ex){PropertyInfo.Text="Kütle özellikleri hesaplanamadı: "+ex.Message;ManagerTabs.SelectedItem=PropertyManagerTab;}
    }

    private void InspectGeometry_Click(object sender,RoutedEventArgs e)
    {
        var body=CurrentBodyBRep();
        PropertyTitle.Text="Geometri Kontrolü";
        if(body is null){PropertyInfo.Text="Kontrol edilecek katı yok.";ManagerTabs.SelectedItem=PropertyManagerTab;return;}
        try
        {
            var info=SrcCad.Kernel.Occt.OcctFeatureKernel.Inspect(body);
            PropertyInfo.Text=$"OCCT B-Rep geçerli.\nKatı: {info[1]:0}\nYüz: {info[2]:0}\nKenar: {info[3]:0}\nHacim: {info[0]:0.###} mm³";
            ManagerTabs.SelectedItem=PropertyManagerTab;Status("Geometri kontrolü: geçerli B-Rep.");
        }
        catch(Exception ex)
        {
            PropertyInfo.Text="Geometri geçersiz / rebuild hatası: "+ex.Message;
            ManagerTabs.SelectedItem=PropertyManagerTab;Status("Geometri kontrolü hata buldu.");
        }
    }

    private void InspectSectionView_Click(object sender,RoutedEventArgs e)
    {
        var body=CurrentBodyBRep();
        if(body is null){Status("Kesit görünümü için katı gerekiyor.");return;}
        try
        {
            var bounds=SrcCad.Kernel.Occt.OcctFeatureKernel.Bounds(body);
            if(bounds.Length!=6)throw new InvalidOperationException("Model sınırları çözülemedi.");
            var center=new Vector3d((bounds[0]+bounds[3])*0.5,(bounds[1]+bounds[4])*0.5,(bounds[2]+bounds[5])*0.5);
            var normal=_selectedPlane.Normal.Normalized();
            var up=_selectedPlane.V.Normalized();
            var right=Vector3d.Cross(up,normal).Normalized();
            var segments=SrcCad.Kernel.Occt.OcctFeatureKernel.SectionEdges(body,
                new[]{center.X,center.Y,center.Z},new[]{normal.X,normal.Y,normal.Z},
                new[]{normal.X,normal.Y,normal.Z},new[]{up.X,up.Y,up.Z});
            if(segments.Length<4){Status("Aktif düzlem yönünde kesit eğrisi oluşmadı.");return;}
            _viewer.ClearSketch();
            var h=Vector3d.Dot(center,normal);
            Vector3d World(double x,double y)=>right*x+up*y+normal*h;
            for(var i=0;i+3<segments.Length;i+=4)
            {
                var a=World(segments[i],segments[i+1]);var b=World(segments[i+2],segments[i+3]);
                _viewer.AddStyledSketchLine(a.X,a.Y,a.Z,b.X,b.Y,b.Z,2);
            }
            _viewer.SetNormalView(normal.X,normal.Y,normal.Z,up.X,up.Y,up.Z);_viewer.FitAll();
            PropertyTitle.Text="Kesit Görünümü";
            PropertyInfo.Text=$"{_selectedPlane.Name} yönünde model merkezinden gerçek OCCT kesit eğrileri gösteriliyor. Normal model görünümüne dönmek için ISO/Ön/Üst/Sağ kullanın.";
            ManagerTabs.SelectedItem=PropertyManagerTab;
            Status($"{segments.Length/4} kesit segmenti gösterildi.");
        }
        catch(Exception ex){Status("Kesit görünümü oluşturulamadı: "+ex.Message);}
    }

    private void InspectDraftAnalysis_Click(object sender,RoutedEventArgs e)
    {
        var refs=_viewer.SelectedFaceReferences();
        PropertyTitle.Text="Draft Analizi";
        if(refs.Length==0)
        {
            PropertyInfo.Text="Draft analizi için bir veya daha fazla düzlemsel yüz seçin. Çekme yönü aktif düzlemin normalidir.";
            ManagerTabs.SelectedItem=PropertyManagerTab;Status("Draft analizi için yüz seçin.");return;
        }
        var pull=_selectedPlane.Normal.Normalized();
        var rows=new List<string>();var index=1;
        foreach(var reference in refs)
        {
            var parts=reference.Split(';');
            if(parts.Length<8||
               !double.TryParse(parts[5],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var nx)||
               !double.TryParse(parts[6],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var ny)||
               !double.TryParse(parts[7],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var nz))continue;
            var normal=new Vector3d(nx,ny,nz).Normalized();
            var dot=Math.Clamp(Vector3d.Dot(normal,pull),-1,1);
            var normalAngle=Math.Acos(Math.Abs(dot))*180/Math.PI;
            var draft=90-normalAngle;
            rows.Add($"{index++}. Draft: {draft:0.###}° • Normal/çekme: {normalAngle:0.###}° • {(dot>=0?"Pozitif":"Negatif")} yön");
        }
        PropertyInfo.Text=rows.Count==0?"Seçili yüzlerin draft normali çözülemedi.":$"Çekme yönü: {_selectedPlane.Name}\n\n"+string.Join("\n",rows);
        ManagerTabs.SelectedItem=PropertyManagerTab;Status($"{rows.Count} yüz için draft açısı hesaplandı.");
    }

    private void InspectInterference_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog
        {
            Title="Çakışma için iki SrcCad parçası seçin",
            Filter="SrcCad Parça (*.scpart)|*.scpart",
            Multiselect=true
        };
        if(dialog.ShowDialog(this)!=true)return;
        if(dialog.FileNames.Length!=2){Status("Çakışma kontrolü için tam iki .scpart seçin.");return;}
        try
        {
            var a=RebuildAssemblyComponent(dialog.FileNames[0]);var b=RebuildAssemblyComponent(dialog.FileNames[1]);
            var volume=SrcCad.Kernel.Occt.OcctFeatureKernel.CommonVolume(a,new double[6],b,new double[6]);
            PropertyTitle.Text="Çakışma";
            PropertyInfo.Text=volume>1e-8
                ?$"Çakışma var. Ortak hacim: {volume:0.###} mm³"
                :"Aynı koordinat sisteminde ortak katı hacmi bulunmadı.";
            ManagerTabs.SelectedItem=PropertyManagerTab;Status(volume>1e-8?"Çakışma bulundu.":"Çakışma bulunmadı.");
        }
        catch(Exception ex){PropertyInfo.Text="Çakışma kontrolü başarısız: "+ex.Message;ManagerTabs.SelectedItem=PropertyManagerTab;}
    }

    private void InspectCompare_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog
        {
            Title="Karşılaştırmak için iki SrcCad parçası seçin",
            Filter="SrcCad Parça (*.scpart)|*.scpart",
            Multiselect=true
        };
        if(dialog.ShowDialog(this)!=true)return;
        if(dialog.FileNames.Length!=2){Status("Karşılaştırma için tam iki .scpart seçin.");return;}
        try
        {
            var a=SrcCad.Kernel.Occt.OcctFeatureKernel.Inspect(RebuildAssemblyComponent(dialog.FileNames[0]));
            var b=SrcCad.Kernel.Occt.OcctFeatureKernel.Inspect(RebuildAssemblyComponent(dialog.FileNames[1]));
            PropertyTitle.Text="Geometri Karşılaştır";
            PropertyInfo.Text=$"A: {Path.GetFileName(dialog.FileNames[0])}\nB: {Path.GetFileName(dialog.FileNames[1])}\n\n"+
                $"Hacim A/B: {a[0]:0.###} / {b[0]:0.###} mm³  Δ={(b[0]-a[0]):0.###}\n"+
                $"Katı A/B: {a[1]:0} / {b[1]:0}\nYüz A/B: {a[2]:0} / {b[2]:0}  Δ={(b[2]-a[2]):0}\n"+
                $"Kenar A/B: {a[3]:0} / {b[3]:0}  Δ={(b[3]-a[3]):0}";
            ManagerTabs.SelectedItem=PropertyManagerTab;Status("İki model gerçek B-Rep topolojisiyle karşılaştırıldı.");
        }
        catch(Exception ex){PropertyInfo.Text="Karşılaştırma başarısız: "+ex.Message;ManagerTabs.SelectedItem=PropertyManagerTab;}
    }

    private bool TryResolveMirrorPlane(out PlaneFrame plane)
    {
        plane=_selectedPlane;
        if(_viewer.TryGetSelectedPlanarFace(out var face))
        {
            try
            {
                plane=new PlaneFrame("Seçili Yüz",
                    new Vector3d(face.Origin.X,face.Origin.Y,face.Origin.Z),
                    new Vector3d(face.U.X,face.U.Y,face.U.Z),
                    new Vector3d(face.V.X,face.V.Y,face.V.Z),
                    new Vector3d(face.Normal.X,face.Normal.Y,face.Normal.Z)).Validate();
                return true;
            }
            catch(InvalidOperationException){return false;}
        }
        if(FeatureTree.SelectedItem is TreeViewItem item&&item.Tag is string tag&&tag.StartsWith("plane:",StringComparison.Ordinal))
            return true;
        return false;
    }

    private void Mirror_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Aynala için mevcut katı gerekiyor.");return;}
        if(!TryResolveMirrorPlane(out var plane))
        {Status("Aynala için düzlemsel bir yüz veya Ön/Üst/Sağ Düzlem seçin.");return;}
        BeginMirrorFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Aynala"),new MirrorFeatureParameters(baseNode.Id,plane,true)));
    }

    private void BeginMirrorFeature(FeatureNode node)
    {
        if(node.Parameters is not MirrorFeatureParameters mirror)throw new InvalidOperationException("Unsupported mirror feature.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        MirrorPlaneText.Text=mirror.Plane.Name;
        MirrorKeepOriginal.IsChecked=mirror.KeepOriginal;
        MirrorFeatureError.Text="";
        MirrorFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;ShellFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek 3B ayna dönüşümü • parametrik düzlem • canlı B-Rep önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _loadingGraphParameters=false;
        UpdateMirrorFeaturePreview();RefreshCommandState();
    }

    private FeatureDocument? MirrorFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not MirrorFeatureParameters mirror)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=mirror with{KeepOriginal=MirrorKeepOriginal.IsChecked==true}});
    }

    private void MirrorFeatureParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateMirrorFeaturePreview();
    }

    private void UpdateMirrorFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not MirrorFeatureParameters)return;
        var candidate=MirrorFeatureCandidate();if(candidate is null)return;
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){MirrorFeatureError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        MirrorFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Ayna önizlemesi oluşturulamadı.";
    }

    private void MirrorFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=MirrorFeatureCandidate();
        if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){MirrorFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Parametrik ayna özelliği onaylandı.");
    }

    private void MirrorFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private PlaneFrame CurrentPatternFrame()
    {
        if(_viewer.TryGetSelectedPlanarFace(out var face))
        {
            try
            {
                return new PlaneFrame("Seçili Yüz",
                    new Vector3d(face.Origin.X,face.Origin.Y,face.Origin.Z),
                    new Vector3d(face.U.X,face.U.Y,face.U.Z),
                    new Vector3d(face.V.X,face.V.Y,face.V.Z),
                    new Vector3d(face.Normal.X,face.Normal.Y,face.Normal.Z)).Validate();
            }
            catch(InvalidOperationException){}
        }
        return _selectedPlane;
    }

    private void LinearPattern_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Doğrusal Dizilim için mevcut katı gerekiyor.");return;}
        var frame=CurrentPatternFrame();
        BeginPatternFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Doğrusal Dizilim"),
            new LinearPatternFeatureParameters(baseNode.Id,frame.U,50,2)));
    }

    private void CircularPattern_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Dairesel Dizilim için mevcut katı gerekiyor.");return;}
        var frame=CurrentPatternFrame();
        var axisOrigin=frame.Origin;
        var axisDirection=frame.Normal;
        if(SelectedGraphNode?.Parameters is ReferenceAxisFeatureParameters referenceAxis)
        {
            axisOrigin=referenceAxis.Origin;
            axisDirection=referenceAxis.Direction.Normalized();
        }
        BeginPatternFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Dairesel Dizilim"),
            new CircularPatternFeatureParameters(baseNode.Id,axisOrigin,axisDirection,360,4)));
    }

    private void BeginPatternFeature(FeatureNode node)
    {
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        switch(node.Parameters)
        {
            case LinearPatternFeatureParameters linear:
                PatternFeatureTitle.Text="Doğrusal Dizilim";
                PatternCount.Text=linear.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
                PatternValueLabel.Text="Aralık (mm)";
                PatternValue.Text=linear.Spacing.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                PatternReferenceText.Text=$"Yön: X={linear.Direction.X:0.###}, Y={linear.Direction.Y:0.###}, Z={linear.Direction.Z:0.###}";
                break;
            case CircularPatternFeatureParameters circular:
                PatternFeatureTitle.Text="Dairesel Dizilim";
                PatternCount.Text=circular.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
                PatternValueLabel.Text="Toplam Açı (°)";
                PatternValue.Text=circular.AngleDegrees.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
                PatternReferenceText.Text=$"Eksen: O({circular.AxisOrigin.X:0.###},{circular.AxisOrigin.Y:0.###},{circular.AxisOrigin.Z:0.###}) • N({circular.AxisDirection.X:0.###},{circular.AxisDirection.Y:0.###},{circular.AxisDirection.Z:0.###})";
                break;
            default:throw new InvalidOperationException("Unsupported pattern feature.");
        }
        PatternFeatureError.Text="";
        PatternFeaturePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        ShellFeaturePanel.Visibility=Visibility.Collapsed;MirrorFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek OCCT gövde dizilimi • parametrik adet/aralık/açı • canlı B-Rep önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        _loadingGraphParameters=false;
        UpdatePatternFeaturePreview();RefreshCommandState();
    }

    private FeatureDocument? PatternFeatureCandidate()
    {
        if(_partSession is null||_graphFeatureEdit is null||
           !int.TryParse(PatternCount.Text,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.CurrentCulture,out var count)||
           count<2||count>500||
           !DimensionNumbers.TryParseScalar(PatternValue.Text,out var value)||!double.IsFinite(value)||value<=1e-9)return null;
        var parameters=_graphFeatureEdit.Parameters switch
        {
            LinearPatternFeatureParameters linear when double.IsFinite(value)=>(FeatureParameters)(linear with{Spacing=value,Count=count}),
            CircularPatternFeatureParameters circular when value<=360.0=>circular with{AngleDegrees=value,Count=count},
            _=>null
        };
        return parameters is null?null:ReplaceGraphNode(_graphFeatureEdit with{Parameters=parameters});
    }

    private void PatternFeatureParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdatePatternFeaturePreview();
    }

    private void UpdatePatternFeaturePreview()
    {
        if(_partSession is null||_graphFeatureEdit is null)return;
        var candidate=PatternFeatureCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
            PatternFeatureError.Text="Adet 2–500 olmalı; aralık/açı pozitif ve daireselde en fazla 360° olmalı.";return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){PatternFeatureError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        PatternFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Dizilim önizlemesi oluşturulamadı.";
    }

    private void PatternFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=PatternFeatureCandidate();
        if(candidate is null||_partSession is null){PatternFeatureError.Text="Geçerli dizilim parametreleri gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){PatternFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Parametrik dizilim onaylandı.");
    }

    private void PatternFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private (double Thickness,double BendRadius,double KFactor)? SheetMetalDefaults()
    {
        if(_partSession is null)return null;
        foreach(var feature in _partSession.Document.Features.Reverse())
            switch(feature.Parameters)
            {
                case SheetMetalBaseFeatureParameters b:return (b.Thickness,b.BendRadius,b.KFactor);
                case SheetMetalConvertFeatureParameters c:return (c.Thickness,c.BendRadius,c.KFactor);
            }
        return null;
    }

    private void EdgeFlange_Click(object sender,RoutedEventArgs e)=>StartSheetEdgeFlange(false);
    private void MiterFlange_Click(object sender,RoutedEventArgs e)=>StartSheetEdgeFlange(true);
    private void Jog_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var defaults=SheetMetalDefaults();var baseNode=CurrentSolidFeature();
        if(defaults is null||baseNode is null){Status("Jog için sac levha gövdesi gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        BeginEdgeFlangeFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Jog"),
            new SheetMetalJogFeatureParameters(baseNode.Id,10,20,defaults.Value.Thickness,false),
            References:SelectedEdgeReferences(baseNode.Id)));
    }

    private void StartSheetEdgeFlange(bool miter)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var defaults=SheetMetalDefaults();
        if(defaults is null){Status((miter?"Gönye Flanşı":"Kenar Flanşı")+" için sac levha kökü gerekiyor.");return;}
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Flanş için sac gövdesi gerekiyor.");return;}
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        var refs=SelectedEdgeReferences(baseNode.Id);
        FeatureParameters parameters=miter
            ?new SheetMetalMiterFlangeFeatureParameters(baseNode.Id,25,90,defaults.Value.Thickness,defaults.Value.BendRadius,false)
            :new SheetMetalEdgeFlangeFeatureParameters(baseNode.Id,25,90,defaults.Value.Thickness,defaults.Value.BendRadius,false);
        BeginEdgeFlangeFeature(new FeatureNode(Guid.NewGuid(),NextGraphName(miter?"Gönye Flanşı":"Kenar Flanşı"),
            parameters,References:refs));
    }

    private void BeginEdgeFlangeFeature(FeatureNode node)
    {
        var values=node.Parameters switch
        {
            SheetMetalEdgeFlangeFeatureParameters f=>(BaseId:f.BaseFeatureId,First:f.Length,Second:f.AngleDegrees,Thickness:f.Thickness,BendRadius:f.BendRadius,Reverse:f.Reverse,Miter:false,Jog:false),
            SheetMetalMiterFlangeFeatureParameters f=>(BaseId:f.BaseFeatureId,First:f.Length,Second:f.AngleDegrees,Thickness:f.Thickness,BendRadius:f.BendRadius,Reverse:f.Reverse,Miter:true,Jog:false),
            SheetMetalJogFeatureParameters f=>(BaseId:f.BaseFeatureId,First:f.Offset,Second:f.Height,Thickness:f.Thickness,BendRadius:0.0,Reverse:f.Reverse,Miter:false,Jog:true),
            _=>throw new InvalidOperationException("Unsupported edge flange/jog.")
        };
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        EdgeFlangeLength.Text=values.First.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        EdgeFlangeAngle.Text=values.Second.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        EdgeFlangeLengthLabel.Text=values.Jog?"Ofset Uzunluğu (mm)":"Flanş Uzunluğu (mm)";
        EdgeFlangeAngleLabel.Text=values.Jog?"Jog Yüksekliği (mm)":"Açı (°)";
        EdgeFlangeBendRadiusLabel.Visibility=values.Jog?Visibility.Collapsed:Visibility.Visible;
        EdgeFlangeBendRadius.Visibility=values.Jog?Visibility.Collapsed:Visibility.Visible;
        EdgeFlangeThickness.Text=values.Thickness.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        EdgeFlangeBendRadius.Text=values.BendRadius.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        EdgeFlangeReverse.IsChecked=values.Reverse;
        EdgeFlangeSelectionText.Text=(node.References?.Length??0)==0
            ?(values.Miter?"En az iki bağlı sac kenarı seçin.":"Doğrusal sac kenarı seçin.")
            :$"{node.References!.Length} kenar seçili";
        EdgeFlangeError.Text=(node.References?.Length??0)==0?"Kenar seçimi gerekiyor.":"";
        EdgeFlangePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;HoleFeaturePanel.Visibility=Visibility.Collapsed;
        RevolveFeaturePanel.Visibility=Visibility.Collapsed;MultiSketchFeaturePanel.Visibility=Visibility.Collapsed;ShellFeaturePanel.Visibility=Visibility.Collapsed;
        DraftFeaturePanel.Visibility=Visibility.Collapsed;RibFeaturePanel.Visibility=Visibility.Collapsed;ReferenceGeometryPanel.Visibility=Visibility.Collapsed;
        MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;SheetMetalBasePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;PropertyInfo.Text=values.Jog
            ?"Gerçek OCCT Jog • seçili kenarda Z-step • ofset/yükseklik/kalınlık • canlı önizleme"
            :values.Miter
                ?"Gerçek çoklu kenar gönye flanşı • bağlı kenarlar aynı feature'da B-Rep olarak oluşturulur."
                :"Gerçek OCCT sac kenar flanşı • çoklu doğrusal kenar • açı/uzunluk/kalınlık/büküm yarıçapı • canlı önizleme";
        ManagerTabs.SelectedItem=PropertyManagerTab;_viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Edge);
        _loadingGraphParameters=false;
        if((node.References?.Length??0)>0)UpdateEdgeFlangePreview();
        else{_viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();}
        RefreshCommandState();
    }

    internal void UpdateActiveEdgeFlangeSelection()
    {
        if(_partSession is null||_graphFeatureEdit is null)return;
        var baseId=_graphFeatureEdit.Parameters switch
        {
            SheetMetalEdgeFlangeFeatureParameters f=>f.BaseFeatureId,
            SheetMetalMiterFlangeFeatureParameters f=>f.BaseFeatureId,
            SheetMetalJogFeatureParameters f=>f.BaseFeatureId,
            _=>Guid.Empty
        };
        if(baseId==Guid.Empty)return;
        var references=SelectedEdgeReferences(baseId);
        _graphFeatureEdit=_graphFeatureEdit with{References=references};
        EdgeFlangeSelectionText.Text=references.Length==0?"Doğrusal sac kenarı seçin.":$"{references.Length} kenar seçili";
        UpdateEdgeFlangePreview();
    }

    private FeatureDocument? EdgeFlangeCandidate()
    {
        if(_partSession is null||_graphFeatureEdit is null||
           (_graphFeatureEdit.References?.Length??0)==0||
           !DimensionNumbers.TryParse(EdgeFlangeLength.Text,SketchDimensionType.LineLength,out var first,out _)||
           !DimensionNumbers.TryParseScalar(EdgeFlangeAngle.Text,out var second)||
           !DimensionNumbers.TryParse(EdgeFlangeThickness.Text,SketchDimensionType.LineLength,out var thickness,out _))
            return null;

        if(_graphFeatureEdit.Parameters is SheetMetalJogFeatureParameters jog)
        {
            if(first<=1e-9||second<=1e-9||thickness<=1e-9)return null;
            return ReplaceGraphNode(_graphFeatureEdit with{Parameters=jog with
            {
                Offset=first,Height=second,Thickness=thickness,Reverse=EdgeFlangeReverse.IsChecked==true
            }});
        }

        if(!DimensionNumbers.TryParseScalar(EdgeFlangeBendRadius.Text,out var radius)||
           first<=1e-9||second<=1e-6||second>=179.999||thickness<=1e-9||radius<0)return null;
        FeatureParameters? parameters=_graphFeatureEdit.Parameters switch
        {
            SheetMetalEdgeFlangeFeatureParameters f=>f with{Length=first,AngleDegrees=second,Thickness=thickness,BendRadius=radius,Reverse=EdgeFlangeReverse.IsChecked==true},
            SheetMetalMiterFlangeFeatureParameters f when (_graphFeatureEdit.References?.Length??0)>=2
                =>f with{Length=first,AngleDegrees=second,Thickness=thickness,BendRadius=radius,Reverse=EdgeFlangeReverse.IsChecked==true},
            _=>null
        };
        return parameters is null?null:ReplaceGraphNode(_graphFeatureEdit with{Parameters=parameters});
    }

    private void EdgeFlangeParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateEdgeFlangePreview();
    }

    private void UpdateEdgeFlangePreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not (SheetMetalEdgeFlangeFeatureParameters or SheetMetalMiterFlangeFeatureParameters or SheetMetalJogFeatureParameters))return;
        var candidate=EdgeFlangeCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
            EdgeFlangeError.Text=(_graphFeatureEdit.References?.Length??0)==0
                ?"En az bir doğrusal sac kenarı seçin.":"Uzunluk/kalınlık > 0, açı 0–180° arasında, yarıçap ≥ 0 olmalı.";
            return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body))
        {
            _viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.72);
            EdgeFlangeError.Text="";return;
        }
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        EdgeFlangeError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Kenar Flanşı önizlemesi oluşturulamadı.";
    }

    private void EdgeFlangeOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=EdgeFlangeCandidate();
        if(candidate is null||_partSession is null){EdgeFlangeError.Text="Kenar ve geçerli flanş parametreleri gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){EdgeFlangeError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Parametrik Kenar Flanşı onaylandı.");
    }

    private void EdgeFlangeCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private FeatureNode? BaseFlangeSketchCandidate()
    {
        if(_partSession is null)return null;
        bool Closed(FeatureNode n)
        {
            if(n.Parameters is not SketchFeatureParameters)return false;
            try
            {
                var model=GraphSketchModel(n);
                return model.TryGetExtrusionProfile(out _,out _)||model.TryGetSingleCircle(out _);
            }
            catch{return false;}
        }
        if(SelectedGraphNode is {Kind:FeatureKind.Sketch} selected&&Closed(selected))return selected;
        return _partSession.Document.Features.LastOrDefault(n=>n.Kind==FeatureKind.Sketch&&Closed(n));
    }

    private void BaseFlange_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        if(_hasSolid){Status("Taban Flanş yeni sac parçanın ilk katı özelliğidir.");return;}
        var sketch=BaseFlangeSketchCandidate();
        if(sketch is null){Status("Taban Flanş için kapalı bir Çizim seçin.");return;}
        BeginSheetMetalBaseFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Taban Flanş"),
            new SheetMetalBaseFeatureParameters(sketch.Id,2,2,0.5,false)));
    }

    private void BeginSheetMetalBaseFeature(FeatureNode node)
    {
        if(node.Parameters is not SheetMetalBaseFeatureParameters sheet)throw new InvalidOperationException("Unsupported sheet metal base.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        SheetThickness.Text=sheet.Thickness.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        SheetBendRadius.Text=sheet.BendRadius.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        SheetKFactor.Text=sheet.KFactor.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        SheetReverse.IsChecked=sheet.Reverse;
        SheetMetalBaseInfo.Text=$"Kaynak çizim: {_partSession?.Document.Node(sheet.SketchId).Name} • sac parametreleri feature geçmişinde saklanır.";
        SheetMetalBaseError.Text="";
        SheetMetalBasePanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;HoleFeaturePanel.Visibility=Visibility.Collapsed;
        RevolveFeaturePanel.Visibility=Visibility.Collapsed;MultiSketchFeaturePanel.Visibility=Visibility.Collapsed;ShellFeaturePanel.Visibility=Visibility.Collapsed;
        DraftFeaturePanel.Visibility=Visibility.Collapsed;RibFeaturePanel.Visibility=Visibility.Collapsed;ReferenceGeometryPanel.Visibility=Visibility.Collapsed;
        MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Gerçek OCCT sac taban gövdesi • kalınlık/büküm yarıçapı/K-faktörü parametrik";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;
        UpdateSheetMetalBasePreview();RefreshCommandState();
    }

    private FeatureDocument? SheetMetalBaseCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not SheetMetalBaseFeatureParameters sheet||
           !DimensionNumbers.TryParse(SheetThickness.Text,SketchDimensionType.LineLength,out var thickness,out _)||
           !DimensionNumbers.TryParseScalar(SheetBendRadius.Text,out var radius)||
           !DimensionNumbers.TryParseScalar(SheetKFactor.Text,out var k)||
           thickness<=1e-9||radius<0||k<0||k>1)return null;
        return ReplaceGraphNode(_graphFeatureEdit with
        {
            Parameters=sheet with{Thickness=thickness,BendRadius=radius,KFactor=k,Reverse=SheetReverse.IsChecked==true}
        });
    }

    private void SheetMetalBaseParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateSheetMetalBasePreview();
    }

    private void UpdateSheetMetalBasePreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not SheetMetalBaseFeatureParameters)return;
        var candidate=SheetMetalBaseCandidate();
        if(candidate is null)
        {
            _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
            SheetMetalBaseError.Text="Kalınlık > 0, büküm yarıçapı ≥ 0 ve K-faktörü 0–1 olmalı.";return;
        }
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){SheetMetalBaseError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        SheetMetalBaseError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Taban Flanş önizlemesi oluşturulamadı.";
    }

    private void SheetMetalBaseOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=SheetMetalBaseCandidate();
        if(candidate is null||_partSession is null){SheetMetalBaseError.Text="Geçerli sac parametreleri gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){SheetMetalBaseError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Parametrik Taban Flanş onaylandı.");
    }

    private void SheetMetalBaseCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private FeatureNode? SheetBendSketchCandidate()
    {
        if(_partSession is null)return null;
        bool Valid(FeatureNode node)=>node.Parameters is SketchFeatureParameters source&&
            source.Sketch.Entities.OfType<SketchLine>().Count(line=>!line.Construction)==1;
        if(SelectedGraphNode is {Kind:FeatureKind.Sketch} selected&&Valid(selected))return selected;
        var ordered=_partSession.Document.Ordered();
        var lastBody=-1;for(var i=0;i<ordered.Length;i++)if(_partSession.Output(ordered[i].Id)?.BRep is not null)lastBody=i;
        return ordered.Skip(Math.Max(0,lastBody+1)).LastOrDefault(node=>node.Kind==FeatureKind.Sketch&&Valid(node))
            ??ordered.LastOrDefault(node=>node.Kind==FeatureKind.Sketch&&Valid(node));
    }

    private void SketchedBend_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();var sketch=SheetBendSketchCandidate();
        if(baseNode is null){Status("Çizilmiş Büküm için sac gövdesi gerekiyor.");return;}
        if(sketch is null){Status("Çizilmiş Büküm için tek normal çizgi içeren sketch seçin.");return;}
        BeginSheetBendFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Çizilmiş Büküm"),
            new SheetMetalBendFeatureParameters(baseNode.Id,sketch.Id,90,false)));
    }

    private void Unfold_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var source=_partSession.Document.Ordered().LastOrDefault(n=>n.Parameters is SheetMetalBendFeatureParameters b&&b.AngleDegrees>0);
        if(source?.Parameters is not SheetMetalBendFeatureParameters bend)
        {Status("Açınım için önce Çizilmiş Büküm oluşturun.");return;}
        var baseNode=CurrentSolidFeature();
        if(baseNode is null)return;
        BeginSheetBendFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Açınım"),
            new SheetMetalBendFeatureParameters(baseNode.Id,bend.SketchId,-Math.Abs(bend.AngleDegrees),bend.Reverse)));
    }

    private void Fold_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession()||_partSession is null)return;
        var source=_partSession.Document.Ordered().LastOrDefault(n=>n.Parameters is SheetMetalBendFeatureParameters b&&b.AngleDegrees<0);
        if(source?.Parameters is not SheetMetalBendFeatureParameters unfold)
        {Status("Katla için önce Açınım işlemi gerekiyor.");return;}
        var baseNode=CurrentSolidFeature();if(baseNode is null)return;
        BeginSheetBendFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Katla"),
            new SheetMetalBendFeatureParameters(baseNode.Id,unfold.SketchId,Math.Abs(unfold.AngleDegrees),unfold.Reverse)));
    }

    private void BeginSheetBendFeature(FeatureNode node)
    {
        if(node.Parameters is not SheetMetalBendFeatureParameters bend)throw new InvalidOperationException("Unsupported sheet bend.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        SheetBendAngle.Text=bend.AngleDegrees.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        SheetBendReverse.IsChecked=bend.Reverse;
        SheetBendSketchText.Text=_partSession?.Document.Node(bend.SketchId).Name??"Çizim";
        SheetBendError.Text="";
        SheetBendPanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;SpatialSketchPanel.Visibility=Visibility.Collapsed;StructuralTrimPanel.Visibility=Visibility.Collapsed;
        EdgeFeaturePanel.Visibility=Visibility.Collapsed;HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        MultiSketchFeaturePanel.Visibility=Visibility.Collapsed;ShellFeaturePanel.Visibility=Visibility.Collapsed;DraftFeaturePanel.Visibility=Visibility.Collapsed;
        RibFeaturePanel.Visibility=Visibility.Collapsed;ReferenceGeometryPanel.Visibility=Visibility.Collapsed;MirrorFeaturePanel.Visibility=Visibility.Collapsed;
        PatternFeaturePanel.Visibility=Visibility.Collapsed;SheetMetalBasePanel.Visibility=Visibility.Collapsed;SheetMetalConvertPanel.Visibility=Visibility.Collapsed;
        EdgeFlangePanel.Visibility=Visibility.Collapsed;EndCapPanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Gerçek OCCT split → rotate → fuse sac bükümü • canlı önizleme • history/rebuild";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;
        UpdateSheetBendPreview();RefreshCommandState();
    }

    private FeatureDocument? SheetBendCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not SheetMetalBendFeatureParameters bend||
           !DimensionNumbers.TryParseScalar(SheetBendAngle.Text,out var angle)||
           !double.IsFinite(angle)||Math.Abs(angle)<=1e-6||Math.Abs(angle)>=179)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=bend with{AngleDegrees=angle,Reverse=SheetBendReverse.IsChecked==true}});
    }

    private void SheetBendParameterChanged(object sender,RoutedEventArgs e)
    {
        if(!_loadingGraphParameters)UpdateSheetBendPreview();
    }

    private void UpdateSheetBendPreview()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not SheetMetalBendFeatureParameters)return;
        var candidate=SheetBendCandidate();
        if(candidate is null){SheetBendError.Text="Büküm açısı -179 ile +179 derece arasında ve sıfırdan farklı olmalı.";return;}
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body))
        {
            _viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.72);SheetBendError.Text="";return;
        }
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        SheetBendError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Büküm önizlemesi oluşturulamadı.";
    }

    private void SheetBendOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=SheetBendCandidate();
        if(candidate is null||_partSession is null){SheetBendError.Text="Geçerli büküm parametreleri gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){SheetBendError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Sac büküm/açınım işlemi onaylandı.");
    }

    private void SheetBendCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private void ConvertToSheetMetal_Click(object sender,RoutedEventArgs e)
    {
        if(_inSketch||_extrudeCommandActive||!EnsurePartSession())return;
        var baseNode=CurrentSolidFeature();
        if(baseNode is null){Status("Sac'a Dönüştür için mevcut katı gerekiyor.");return;}
        if(_partSession!.Document.Features.Any(f=>f.Kind is FeatureKind.SheetMetalBase or FeatureKind.SheetMetalConvert))
        {Status("Bu parça zaten sac levha kökü içeriyor.");return;}
        BeginSheetMetalConvertFeature(new FeatureNode(Guid.NewGuid(),NextGraphName("Sac'a Dönüştür"),
            new SheetMetalConvertFeatureParameters(baseNode.Id,2,2,0.5)));
    }

    private void BeginSheetMetalConvertFeature(FeatureNode node)
    {
        if(node.Parameters is not SheetMetalConvertFeatureParameters sheet)throw new InvalidOperationException("Unsupported sheet metal conversion.");
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;
        ConvertSheetThickness.Text=sheet.Thickness.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        ConvertSheetBendRadius.Text=sheet.BendRadius.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        ConvertSheetKFactor.Text=sheet.KFactor.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        SheetMetalConvertError.Text="";
        SheetMetalConvertPanel.Visibility=Visibility.Visible;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;HoleFeaturePanel.Visibility=Visibility.Collapsed;
        RevolveFeaturePanel.Visibility=Visibility.Collapsed;MultiSketchFeaturePanel.Visibility=Visibility.Collapsed;ShellFeaturePanel.Visibility=Visibility.Collapsed;
        DraftFeaturePanel.Visibility=Visibility.Collapsed;RibFeaturePanel.Visibility=Visibility.Collapsed;ReferenceGeometryPanel.Visibility=Visibility.Collapsed;
        MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;SheetMetalBasePanel.Visibility=Visibility.Collapsed;SheetMetalConvertPanel.Visibility=Visibility.Collapsed;EdgeFlangePanel.Visibility=Visibility.Collapsed;EndCapPanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=node.Name;PropertyInfo.Text="Mevcut gerçek B-Rep korunur; kalınlık, büküm yarıçapı ve K-faktörü feature geçmişine bağlanır.";
        ManagerTabs.SelectedItem=PropertyManagerTab;_loadingGraphParameters=false;RefreshCommandState();
    }

    private FeatureDocument? SheetMetalConvertCandidate()
    {
        if(_partSession is null||_graphFeatureEdit?.Parameters is not SheetMetalConvertFeatureParameters sheet||
           !DimensionNumbers.TryParse(ConvertSheetThickness.Text,SketchDimensionType.LineLength,out var thickness,out _)||
           !DimensionNumbers.TryParseScalar(ConvertSheetBendRadius.Text,out var radius)||
           !DimensionNumbers.TryParseScalar(ConvertSheetKFactor.Text,out var k)||
           thickness<=1e-9||radius<0||k<0||k>1)return null;
        return ReplaceGraphNode(_graphFeatureEdit with{Parameters=sheet with{Thickness=thickness,BendRadius=radius,KFactor=k}});
    }

    private void SheetMetalConvertParameterChanged(object sender,RoutedEventArgs e)
    {
        if(_loadingGraphParameters)return;
        SheetMetalConvertError.Text=SheetMetalConvertCandidate() is null?"Kalınlık > 0, yarıçap ≥ 0 ve K-faktörü 0–1 olmalı.":"";
    }

    private void SheetMetalConvertOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=SheetMetalConvertCandidate();
        if(candidate is null||_partSession is null){SheetMetalConvertError.Text="Geçerli sac parametreleri gerekiyor.";return;}
        if(!_partSession.TryCommit(candidate)){SheetMetalConvertError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Katı parametrik sac levha gövdesine dönüştürüldü.");
    }

    private void SheetMetalConvertCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();

    private void BeginGraphExtrude()
    {
        if(_inSketch||_extrudeCommandActive||_partSession is null||SelectedGraphId is not Guid id||_partSession.Document.Node(id).Kind!=FeatureKind.Sketch)return;
        BeginGraphFeature(new(Guid.NewGuid(),NextGraphName("Ekstrüzyon"),new ExtrudeFeatureParameters(id,20)));
    }
    private void EditGraphFeature(Guid id)
    {
        if(_inSketch||_extrudeCommandActive||_partSession is null)return;
        var node=_partSession.Document.Node(id);
        if(node.Suppressed){Status("Düzenlemeden önce baskılamayı kaldırın.");return;}
        if(node.Kind==FeatureKind.Sketch)BeginGraphSketch(node);
        else if(node.Kind==FeatureKind.SpatialSketch)BeginSpatialSketchFeature(node);
        else BeginGraphFeature(node);
    }
    private void BeginGraphFeature(FeatureNode node)
    {
        if(node.Parameters is FilletFeatureParameters or ChamferFeatureParameters or WeldBeadFeatureParameters or
            SheetMetalHemFeatureParameters or SheetMetalRipFeatureParameters or SheetMetalCornerReliefFeatureParameters){BeginEdgeFeature(node);return;}
        if(node.Parameters is HoleFeatureParameters){BeginHoleFeature(node);return;}
        if(node.Parameters is RevolveFeatureParameters or RevolveCutFeatureParameters){BeginRevolveFeature(node);return;}
        if(node.Parameters is SweepFeatureParameters or SweepCutFeatureParameters or LoftFeatureParameters or LoftCutFeatureParameters or
            BoundaryFeatureParameters or BoundaryCutFeatureParameters or StructuralMemberFeatureParameters){BeginMultiSketchFeature(node);return;}
        if(node.Parameters is StructuralTrimFeatureParameters){BeginStructuralTrimFeature(node);return;}
        if(node.Parameters is EndCapFeatureParameters){BeginEndCapFeature(node);return;}
        if(node.Parameters is BodyBooleanFeatureParameters){BeginBodyBooleanFeature(node);return;}
        if(node.Parameters is MoveCopyBodyFeatureParameters){BeginMoveCopyBodyFeature(node);return;}
        if(node.Parameters is SplitBodyFeatureParameters){BeginSplitBodyFeature(node);return;}
        if(node.Parameters is SketchPatternFeatureParameters){BeginSketchPatternFeature(node);return;}
        if(node.Parameters is DomeFeatureParameters){BeginDomeFeature(node);return;}
        if(node.Parameters is HoleWizardFeatureParameters){BeginHoleWizardFeature(node);return;}
        if(node.Parameters is ShellFeatureParameters){BeginShellFeature(node);return;}
        if(node.Parameters is DraftFeatureParameters){BeginDraftFeature(node);return;}
        if(node.Parameters is RibFeatureParameters){BeginRibFeature(node);return;}
        if(node.Parameters is MirrorFeatureParameters){BeginMirrorFeature(node);return;}
        if(node.Parameters is LinearPatternFeatureParameters or CircularPatternFeatureParameters){BeginPatternFeature(node);return;}
        if(node.Parameters is ReferencePlaneFeatureParameters or ReferenceAxisFeatureParameters or ReferencePointFeatureParameters){BeginReferenceGeometryFeature(node);return;}
        if(node.Parameters is SheetMetalBaseFeatureParameters){BeginSheetMetalBaseFeature(node);return;}
        if(node.Parameters is SheetMetalConvertFeatureParameters){BeginSheetMetalConvertFeature(node);return;}
        if(node.Parameters is SheetMetalBendFeatureParameters){BeginSheetBendFeature(node);return;}
        if(node.Parameters is SheetMetalEdgeFlangeFeatureParameters or SheetMetalMiterFlangeFeatureParameters or SheetMetalJogFeatureParameters){BeginEdgeFlangeFeature(node);return;}
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;_depthWheel.Reset();
        var cut=node.Parameters as CutFeatureParameters;
        var sheetCut=node.Parameters as SheetMetalCutFeatureParameters;
        var depth=cut?.Depth??sheetCut?.Depth??Math.Abs(((ExtrudeFeatureParameters)node.Parameters).Depth);
        GraphDepth.Text=depth.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        GraphReverse.IsChecked=cut?.Reverse??sheetCut?.Reverse??((ExtrudeFeatureParameters)node.Parameters).Depth<0;
        GraphThroughAll.IsChecked=(cut?.Termination??sheetCut?.Termination)==ExtrusionTermination.ThroughAll;
        GraphThroughAll.Visibility=cut is null&&sheetCut is null?Visibility.Collapsed:Visibility.Visible;
        _loadingGraphParameters=false;
        GraphFeaturePanel.Visibility=Visibility.Visible;PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek B-Rep önizleme • ✓ tek transaction • Esc iptal";
        ManagerTabs.SelectedItem=PropertyManagerTab;UpdateGraphPreview();RefreshCommandState();
    }
    private FeatureDocument? GraphCandidate()
    {
        if(_graphFeatureEdit is null||!DimensionNumbers.TryParse(GraphDepth.Text,SketchDimensionType.LineLength,out var depth,out _))return null;
        var node=_graphFeatureEdit;
        var termination=GraphThroughAll.IsChecked==true?ExtrusionTermination.ThroughAll:ExtrusionTermination.Blind;
        FeatureParameters parameters=node.Parameters switch
        {
            CutFeatureParameters cut=>cut with{Depth=depth,Reverse=GraphReverse.IsChecked==true,Termination=termination},
            SheetMetalCutFeatureParameters cut=>cut with{Depth=depth,Reverse=GraphReverse.IsChecked==true,Termination=termination},
            ExtrudeFeatureParameters extrude=>extrude with{Depth=GraphReverse.IsChecked==true?-depth:depth},
            _=>throw new InvalidOperationException("Unsupported depth feature.")
        };
        return ReplaceGraphNode(node with{Parameters=parameters});
    }
    private void GraphParameterChanged(object sender,RoutedEventArgs e){if(!_loadingGraphParameters)UpdateGraphPreview();}
    private void UpdateGraphPreview()
    {
        if(_partSession is null||_graphFeatureEdit is null)return;
        var candidate=GraphCandidate();
        if(candidate is null){GraphFeatureError.Text="Pozitif, sonlu bir derinlik girin.";_viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();return;}
        var preview=_partSession.Preview(candidate);
        var body=preview.Success?LastBody(candidate,id=>preview.Outputs.GetValueOrDefault(id)):null;
        if(body is not null&&_viewer.PreviewFeatureBody(body)){GraphFeatureError.Text="";return;}
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();
        GraphFeatureError.Text=preview.Report.Nodes.LastOrDefault(n=>n.State==RebuildState.Failed)?.Error??preview.Report.Error??"Önizleme oluşturulamadı.";
    }
    private void GraphFeatureOk_Click(object sender,RoutedEventArgs e)
    {
        var candidate=GraphCandidate();if(candidate is null||_partSession is null)return;
        if(!_partSession.TryCommit(candidate)){GraphFeatureError.Text=GraphError();return;}
        var id=_graphFeatureEdit!.Id;EndGraphFeature();RefreshPartTree(id);Status("Parametrik feature onaylandı; bağımlı model yeniden oluşturuldu.");
    }
    private void GraphFeatureCancel_Click(object sender,RoutedEventArgs e)=>EndGraphFeature();
    private void EndGraphFeature()
    {
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        _graphFeatureEdit=null;_extrudeCommandActive=false;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;SpatialSketchPanel.Visibility=Visibility.Collapsed;StructuralTrimPanel.Visibility=Visibility.Collapsed;SheetBendPanel.Visibility=Visibility.Collapsed;
        EdgeFeaturePanel.Visibility=Visibility.Collapsed;HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;MultiSketchFeaturePanel.Visibility=Visibility.Collapsed;
        ShellFeaturePanel.Visibility=Visibility.Collapsed;DraftFeaturePanel.Visibility=Visibility.Collapsed;RibFeaturePanel.Visibility=Visibility.Collapsed;ReferenceGeometryPanel.Visibility=Visibility.Collapsed;
        MirrorFeaturePanel.Visibility=Visibility.Collapsed;PatternFeaturePanel.Visibility=Visibility.Collapsed;SheetMetalBasePanel.Visibility=Visibility.Collapsed;SheetMetalConvertPanel.Visibility=Visibility.Collapsed;
        EdgeFlangePanel.Visibility=Visibility.Collapsed;EndCapPanel.Visibility=Visibility.Collapsed;BodyBooleanPanel.Visibility=Visibility.Collapsed;MoveCopyBodyPanel.Visibility=Visibility.Collapsed;
        DomePanel.Visibility=Visibility.Collapsed;HoleWizardPanel.Visibility=Visibility.Collapsed;SplitBodyPanel.Visibility=Visibility.Collapsed;SketchPatternPanel.Visibility=Visibility.Collapsed;
        _viewer.ClearSelection();_viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        ManagerTabs.SelectedItem=FeatureManagerTab;RefreshCommandState();
    }
    private bool TryOpenGraphFile(string path)
    {
        using(var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
            if(json.RootElement.GetProperty("SchemaVersion").GetInt32()!=2)return false;
        var document=FeatureDocumentFile.Load(path);
        var session=new FeatureSession<PartFeatureOutput>(document,new PartFeatureEvaluator(new OcctPartGeometryKernel()),PublishGraph);
        _partSession=session;_sketch=null;_graphSketchEdit=null;_graphFeatureEdit=null;
        _sourceSketchEditBefore=null;_featureHistory.Clear();_sketchUndo.Clear();_sketchRedo.Clear();
        _viewer.ClearSketch();RefreshPartTree();_viewer.FitAll();Status("Parametrik feature grafiği açıldı ve OCCT ile yeniden oluşturuldu.");return true;
    }
    private void ResetPartWorkbench()
    {
        _partSession=null;_graphSketchEdit=null;_graphFeatureEdit=null;
        foreach(var node in _partNodes)PartNode.Items.Remove(node);_partNodes.Clear();
        GraphFeaturePanel.Visibility=Visibility.Collapsed;
        EdgeFeaturePanel.Visibility=Visibility.Collapsed;
        HoleFeaturePanel.Visibility=Visibility.Collapsed;
        RevolveFeaturePanel.Visibility=Visibility.Collapsed;
        MultiSketchFeaturePanel.Visibility=Visibility.Collapsed;
        ShellFeaturePanel.Visibility=Visibility.Collapsed;
        DraftFeaturePanel.Visibility=Visibility.Collapsed;
        RibFeaturePanel.Visibility=Visibility.Collapsed;
        ReferenceGeometryPanel.Visibility=Visibility.Collapsed;
        MirrorFeaturePanel.Visibility=Visibility.Collapsed;
        PatternFeaturePanel.Visibility=Visibility.Collapsed;
        SheetMetalBasePanel.Visibility=Visibility.Collapsed;
        SheetMetalConvertPanel.Visibility=Visibility.Collapsed;
        EdgeFlangePanel.Visibility=Visibility.Collapsed;EndCapPanel.Visibility=Visibility.Collapsed;
        SpatialSketchPanel.Visibility=Visibility.Collapsed;StructuralTrimPanel.Visibility=Visibility.Collapsed;SheetBendPanel.Visibility=Visibility.Collapsed;
        BodyBooleanPanel.Visibility=Visibility.Collapsed;MoveCopyBodyPanel.Visibility=Visibility.Collapsed;SplitBodyPanel.Visibility=Visibility.Collapsed;
        SketchPatternPanel.Visibility=Visibility.Collapsed;DomePanel.Visibility=Visibility.Collapsed;HoleWizardPanel.Visibility=Visibility.Collapsed;
    }
}
