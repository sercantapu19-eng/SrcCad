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
        if(_sketch is null||!_hasSolid)return false;
        try
        {
            var document=FeatureDocumentFile.FromLegacy(CaptureFeatureState());
            var sketchId=document.Features[0].Id;var bossId=document.Features[1].Id;
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
        if(node.Kind==FeatureKind.Sketch)BeginGraphSketch(node);else BeginGraphFeature(node);
    }
    private void BeginGraphFeature(FeatureNode node)
    {
        _graphFeatureEdit=node;_extrudeCommandActive=true;_loadingGraphParameters=true;_depthWheel.Reset();
        var cut=node.Parameters as CutFeatureParameters;
        var depth=cut?.Depth??Math.Abs(((ExtrudeFeatureParameters)node.Parameters).Depth);
        GraphDepth.Text=depth.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        GraphReverse.IsChecked=cut?.Reverse??((ExtrudeFeatureParameters)node.Parameters).Depth<0;
        GraphThroughAll.IsChecked=cut?.Termination==ExtrusionTermination.ThroughAll;
        GraphThroughAll.Visibility=cut is null?Visibility.Collapsed:Visibility.Visible;
        _loadingGraphParameters=false;
        GraphFeaturePanel.Visibility=Visibility.Visible;PropertyTitle.Text=node.Name;
        PropertyInfo.Text="Gerçek B-Rep önizleme • ✓ tek transaction • Esc iptal";
        ManagerTabs.SelectedItem=PropertyManagerTab;UpdateGraphPreview();RefreshCommandState();
    }
    private FeatureDocument? GraphCandidate()
    {
        if(_graphFeatureEdit is null||!DimensionNumbers.TryParse(GraphDepth.Text,SketchDimensionType.LineLength,out var depth,out _))return null;
        var node=_graphFeatureEdit;
        var parameters=node.Parameters is CutFeatureParameters cut
            ? (FeatureParameters)(cut with{Depth=depth,Reverse=GraphReverse.IsChecked==true,Termination=GraphThroughAll.IsChecked==true?ExtrusionTermination.ThroughAll:ExtrusionTermination.Blind})
            : ((ExtrudeFeatureParameters)node.Parameters) with{Depth=GraphReverse.IsChecked==true?-depth:depth};
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
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_graphFeatureEdit=null;_extrudeCommandActive=false;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;ManagerTabs.SelectedItem=FeatureManagerTab;RefreshCommandState();
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
    }
}
