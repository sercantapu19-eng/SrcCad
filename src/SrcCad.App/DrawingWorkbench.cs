using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SrcCad.Core.Drawing;
using SrcCad.Core.Geometry;

namespace SrcCad.App;

public partial class MainWindow
{
    private DrawingDocument? _drawingDocument;
    private string? _drawingPath;
    private readonly Dictionary<string,string> _drawingBodies=new(StringComparer.OrdinalIgnoreCase);
    private TreeViewItem? _drawingRoot;

    private bool InDrawingMode=>_drawingDocument is not null;

    private void ExitDrawingMode()
    {
        if(_drawingRoot is not null)FeatureTree.Items.Remove(_drawingRoot);
        _drawingRoot=null;_drawingDocument=null;_drawingPath=null;_drawingBodies.Clear();
        PartNode.Visibility=Visibility.Visible;
    }

    private static (Vector3d Direction,Vector3d Up) DrawingOrientation(DrawingViewType type)
        => type switch
        {
            DrawingViewType.Top=>(new Vector3d(0,0,-1),new Vector3d(0,1,0)),
            DrawingViewType.Right=>(new Vector3d(-1,0,0),new Vector3d(0,0,1)),
            DrawingViewType.Isometric=>(new Vector3d(-1,-1,-1).Normalized(),new Vector3d(0,0,1)),
            _=>(new Vector3d(0,-1,0),new Vector3d(0,0,1))
        };

    private string DrawingBody(string modelPath)
    {
        var full=Path.GetFullPath(modelPath);
        if(_drawingBodies.TryGetValue(full,out var body))return body;
        body=RebuildAssemblyComponent(full);
        _drawingBodies[full]=body;
        return body;
    }

    private IEnumerable<string> DrawingBodies(string modelPath)
    {
        var full=Path.GetFullPath(modelPath);
        if(!string.Equals(Path.GetExtension(full),".scasm",StringComparison.OrdinalIgnoreCase))
        {
            yield return DrawingBody(full);yield break;
        }

        var assembly=AssemblyDocumentFile.Load(full);
        foreach(var component in assembly.Components.Where(c=>!c.Suppressed))
        {
            var source=RebuildAssemblyComponent(component.SourcePath);
            var t=component.Transform;
            var transformed=SrcCad.Kernel.Occt.OcctFeatureKernel.Transform(source,
                new[]{t.Translation.X,t.Translation.Y,t.Translation.Z,t.RotationDegrees.X,t.RotationDegrees.Y,t.RotationDegrees.Z});
            if(component.MirrorPlane is PlaneFrame mirror)
                transformed=SrcCad.Kernel.Occt.OcctFeatureKernel.Mirror(transformed,
                    new[]{mirror.Origin.X,mirror.Origin.Y,mirror.Origin.Z,mirror.U.X,mirror.U.Y,mirror.U.Z,mirror.Normal.X,mirror.Normal.Y,mirror.Normal.Z},false);
            yield return transformed;
        }
    }

    private void RefreshDrawingTree(Guid? selected=null)
    {
        if(_drawingDocument is null)return;
        if(_drawingRoot is not null)FeatureTree.Items.Remove(_drawingRoot);
        PartNode.Visibility=Visibility.Collapsed;
        _drawingRoot=new TreeViewItem{Header="Teknik Resim1",IsExpanded=true,Tag="drawing-root"};
        foreach(var sheet in _drawingDocument.Sheets)
        {
            var sheetNode=new TreeViewItem{Header=$"{sheet.Name} ({sheet.Width:0.#} × {sheet.Height:0.#} mm)",Tag=sheet.Id,IsExpanded=true};
            foreach(var view in _drawingDocument.Views.Where(v=>v.SheetId==sheet.Id))
            {
                var item=new TreeViewItem{Header=$"{view.Name} [{view.Type}] 1:{1.0/view.Scale:0.###}",Tag=view.Id};
                sheetNode.Items.Add(item);
                if(selected==view.Id)item.IsSelected=true;
            }
            foreach(var note in _drawingDocument.Annotations.Where(a=>a.SheetId==sheet.Id))
                sheetNode.Items.Add(new TreeViewItem{Header="Not: "+note.Text,Tag=note.Id});
            _drawingRoot.Items.Add(sheetNode);
        }
        FeatureTree.Items.Add(_drawingRoot);ManagerTabs.SelectedItem=FeatureManagerTab;
    }

    private void RenderDrawing()
    {
        if(_drawingDocument is null||_drawingDocument.Sheets.Length==0)return;
        _viewer.ClearModel();
        _viewer.ClearSketch();
        var sheet=_drawingDocument.Sheets[0];
        void Line(double x1,double y1,double x2,double y2,int state=0)
            => _viewer.AddStyledSketchLine(x1,y1,0,x2,y2,0,state);
        Line(0,0,sheet.Width,0);Line(sheet.Width,0,sheet.Width,sheet.Height);
        Line(sheet.Width,sheet.Height,0,sheet.Height);Line(0,sheet.Height,0,0);

        foreach(var view in _drawingDocument.Views.Where(v=>v.SheetId==sheet.Id))
        {
            try
            {
                var (direction,up)=DrawingOrientation(view.Type==DrawingViewType.Projected
                    ?(_drawingDocument.Views.FirstOrDefault(v=>v.Id==view.ParentViewId)?.Type??DrawingViewType.Front)
                    :view.Type);
                var allSegments=new List<double>();
                foreach(var body in DrawingBodies(view.ModelPath))
                    allSegments.AddRange(SrcCad.Kernel.Occt.OcctFeatureKernel.ProjectEdges(body,
                        new[]{direction.X,direction.Y,direction.Z},new[]{up.X,up.Y,up.Z}));
                var segments=allSegments.ToArray();
                if(segments.Length<4)continue;
                var xs=new List<double>();var ys=new List<double>();
                for(var i=0;i+3<segments.Length;i+=4){xs.Add(segments[i]);xs.Add(segments[i+2]);ys.Add(segments[i+1]);ys.Add(segments[i+3]);}
                var cx=(xs.Min()+xs.Max())*0.5;var cy=(ys.Min()+ys.Max())*0.5;
                for(var i=0;i+3<segments.Length;i+=4)
                {
                    var x1=(segments[i]-cx)*view.Scale+view.Position.X;
                    var y1=(segments[i+1]-cy)*view.Scale+view.Position.Y;
                    var x2=(segments[i+2]-cx)*view.Scale+view.Position.X;
                    var y2=(segments[i+3]-cy)*view.Scale+view.Position.Y;
                    Line(x1,y1,x2,y2);
                }
                _viewer.AddSketchText(view.Position.X,view.Position.Y-12,0,view.Name,0);
            }
            catch(Exception ex){Status(view.Name+" görünümü oluşturulamadı: "+ex.Message);}
        }
        foreach(var note in _drawingDocument.Annotations.Where(a=>a.SheetId==sheet.Id))
            _viewer.AddSketchText(note.Position.X,note.Position.Y,0,note.Text,0);
        _viewer.SetTopView();_viewer.FitAll();
    }

    private void NewDrawing_Click(object sender,RoutedEventArgs e)
    {
        NewPart_Click(sender,e);
        var sheet=new DrawingSheet(Guid.NewGuid(),"A3",420,297,DrawingProjectionStandard.FirstAngle);
        _drawingDocument=new DrawingDocument(Guid.NewGuid(),new[]{sheet},Array.Empty<DrawingView>(),Array.Empty<DrawingAnnotation>());
        _drawingPath=null;PartNode.Visibility=Visibility.Collapsed;
        RefreshDrawingTree();RenderDrawing();CommandTabs.SelectedIndex=5;
        Status("Yeni Teknik Resim • Model Görünümü ile .scpart seçin.");
    }

    private void DrawingModelView_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)NewDrawing_Click(sender,e);
        if(_drawingDocument is null)return;
        var dialog=new OpenFileDialog{Title="Teknik Resim Modeli",Filter="SrcCad Model (*.scpart;*.scasm)|*.scpart;*.scasm|SrcCad Parça (*.scpart)|*.scpart|SrcCad Montaj (*.scasm)|*.scasm"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            _=DrawingBodies(dialog.FileName).FirstOrDefault()??throw new InvalidDataException("Model görünüşü için geometri bulunamadı.");
            var sheet=_drawingDocument.Sheets[0];
            var view=new DrawingView(Guid.NewGuid(),"Ön Görünüş",Path.GetFullPath(dialog.FileName),
                DrawingViewType.Front,sheet.Id,new Point2d(sheet.Width*0.35,sheet.Height*0.55),1.0);
            _drawingDocument=_drawingDocument with{Views=_drawingDocument.Views.Append(view).ToArray()};
            RefreshDrawingTree(view.Id);RenderDrawing();Status("Gerçek B-Rep ön görünüşü teknik resme eklendi.");
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Model görünümü",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    private DrawingView? SelectedDrawingView()
    {
        if(_drawingDocument is null||(FeatureTree.SelectedItem as TreeViewItem)?.Tag is not Guid id)return null;
        return _drawingDocument.Views.FirstOrDefault(v=>v.Id==id);
    }

    private void DrawingProjectedView_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null){Status("Önce teknik resim oluşturun.");return;}
        var parent=SelectedDrawingView();
        if(parent is null){Status("İzdüşüm için FeatureManager'dan bir görünüş seçin.");return;}
        var next=parent.Type switch
        {
            DrawingViewType.Front=>DrawingViewType.Top,
            DrawingViewType.Top=>DrawingViewType.Right,
            _=>DrawingViewType.Right
        };
        var offset=next==DrawingViewType.Top?new Point2d(0,-90):new Point2d(120,0);
        var view=new DrawingView(Guid.NewGuid(),next==DrawingViewType.Top?"Üst Görünüş":"Sağ Görünüş",
            parent.ModelPath,next,parent.SheetId,new Point2d(parent.Position.X+offset.X,parent.Position.Y+offset.Y),parent.Scale,parent.Id);
        _drawingDocument=_drawingDocument with{Views=_drawingDocument.Views.Append(view).ToArray()};
        RefreshDrawingTree(view.Id);RenderDrawing();Status(view.Name+" oluşturuldu.");
    }

    private void DrawingAddNote_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var sheet=_drawingDocument.Sheets[0];
        var note=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,SelectedDrawingView()?.Id,new Point2d(20,20),"NOT");
        _drawingDocument=_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()};
        RefreshDrawingTree(note.Id);RenderDrawing();Status("Teknik resim notu eklendi.");
    }

    private void DrawingBom_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null){Status("BOM için teknik resim gerekiyor.");return;}
        var view=SelectedDrawingView()??_drawingDocument.Views.FirstOrDefault();
        if(view is null||!string.Equals(Path.GetExtension(view.ModelPath),".scasm",StringComparison.OrdinalIgnoreCase))
        {Status("Malzeme Listesi için montaj (.scasm) görünüşü seçin.");return;}
        var assembly=AssemblyDocumentFile.Load(view.ModelPath);
        var groups=assembly.Components.Where(c=>!c.Suppressed)
            .GroupBy(c=>Path.GetFileNameWithoutExtension(c.SourcePath),StringComparer.OrdinalIgnoreCase)
            .OrderBy(g=>g.Key).ToArray();
        var lines=groups.Select((g,i)=>$"{i+1}. {g.Key}   Adet: {g.Count()}").ToArray();
        PropertyTitle.Text="Malzeme Listesi (BOM)";
        PropertyInfo.Text=lines.Length==0?"Montajda görünür bileşen yok.":string.Join("\n",lines);
        ManagerTabs.SelectedItem=PropertyManagerTab;
        if(lines.Length>0)
        {
            var sheet=_drawingDocument.Sheets[0];
            var text="BOM\n"+string.Join("\n",lines);
            var note=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,view.Id,new Point2d(sheet.Width-120,30),text);
            _drawingDocument=_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()};
            RefreshDrawingTree(note.Id);RenderDrawing();
        }
        Status($"{groups.Length} kalem BOM oluşturuldu.");
    }

    private void DrawingBalloon_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var view=SelectedDrawingView();
        if(view is null){Status("Balon için bir görünüş seçin.");return;}
        var sheet=_drawingDocument.Sheets[0];
        var number=_drawingDocument.Annotations.Count(a=>a.Text.StartsWith("BALON ",StringComparison.Ordinal))+1;
        var note=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,view.Id,
            new Point2d(view.Position.X+35,view.Position.Y+35),"BALON "+number);
        _drawingDocument=_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()};
        RefreshDrawingTree(note.Id);RenderDrawing();Status("Balon "+number+" eklendi.");
    }

    private void SaveDrawing_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null){Status("Kaydedilecek teknik resim yok.");return;}
        var path=_drawingPath;
        if(string.IsNullOrWhiteSpace(path))
        {
            var dialog=new SaveFileDialog{Title="Teknik Resmi Kaydet",Filter="SrcCad Teknik Resim (*.scdrw)|*.scdrw",DefaultExt=".scdrw",AddExtension=true};
            if(dialog.ShowDialog(this)!=true)return;
            path=dialog.FileName;
        }
        DrawingDocumentFile.Save(path!,_drawingDocument);_drawingPath=path;
        Status("Teknik resim kaydedildi: "+Path.GetFileName(path));
    }

    private void OpenDrawing_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="SrcCad Teknik Resim Aç",Filter="SrcCad Teknik Resim (*.scdrw)|*.scdrw"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            NewDrawing_Click(sender,e);
            _drawingDocument=DrawingDocumentFile.Load(dialog.FileName);_drawingPath=dialog.FileName;
            _drawingBodies.Clear();RefreshDrawingTree();RenderDrawing();
            Status("Teknik resim açıldı: "+Path.GetFileName(dialog.FileName));
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Teknik resim açılamadı",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}
