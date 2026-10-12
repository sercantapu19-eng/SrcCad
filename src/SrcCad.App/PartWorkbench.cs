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

    private void BeginEdgeFeature(FeatureNode node)
    {
        _graphFeatureEdit=node;
        _extrudeCommandActive=true;
        _loadingGraphParameters=true;
        var value=node.Parameters switch
        {
            FilletFeatureParameters f=>f.Radius,
            ChamferFeatureParameters c=>c.Distance,
            _=>throw new InvalidOperationException("Unsupported edge feature.")
        };
        EdgeFeatureValue.Text=value.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        EdgeFeatureValueLabel.Text=node.Kind==FeatureKind.Fillet?"Radyus (mm)":"Pah Mesafesi (mm)";
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
           _graphFeatureEdit.Parameters is not (FilletFeatureParameters or ChamferFeatureParameters))return;
        var baseId=_graphFeatureEdit.Parameters switch
        {
            FilletFeatureParameters f=>f.BaseFeatureId,
            ChamferFeatureParameters c=>c.BaseFeatureId,
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
        if(node.Parameters is FilletFeatureParameters or ChamferFeatureParameters){BeginEdgeFeature(node);return;}
        if(node.Parameters is HoleFeatureParameters){BeginHoleFeature(node);return;}
        if(node.Parameters is RevolveFeatureParameters or RevolveCutFeatureParameters){BeginRevolveFeature(node);return;}
        if(node.Parameters is ShellFeatureParameters){BeginShellFeature(node);return;}
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
        _viewer.CancelExtrusionPreview();_viewer.ShowCommittedSolid();_viewer.SetCommittedSolidTransparency(0.0);
        _graphFeatureEdit=null;_extrudeCommandActive=false;
        GraphFeaturePanel.Visibility=Visibility.Collapsed;EdgeFeaturePanel.Visibility=Visibility.Collapsed;HoleFeaturePanel.Visibility=Visibility.Collapsed;RevolveFeaturePanel.Visibility=Visibility.Collapsed;ShellFeaturePanel.Visibility=Visibility.Collapsed;
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
        ShellFeaturePanel.Visibility=Visibility.Collapsed;
    }
}
