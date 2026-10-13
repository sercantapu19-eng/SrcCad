using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SrcCad.Core.Assembly;
using SrcCad.Core.Drawing;
using SrcCad.Core.Geometry;

namespace SrcCad.App;

public partial class MainWindow
{
    private DrawingDocument? _drawingDocument;
    private string? _drawingPath;
    private readonly Dictionary<string,string> _drawingBodies=new(StringComparer.OrdinalIgnoreCase);
    private TreeViewItem? _drawingRoot;
    private readonly Stack<DrawingDocument> _drawingUndo=new();
    private readonly Stack<DrawingDocument> _drawingRedo=new();

    private bool InDrawingMode=>_drawingDocument is not null;

    private void SetDrawingDocument(DrawingDocument next,bool record=true)
    {
        var safe=DrawingDocumentFile.Clone(next);
        if(record&&_drawingDocument is not null)
        {
            _drawingUndo.Push(DrawingDocumentFile.Clone(_drawingDocument));
            _drawingRedo.Clear();
        }
        _drawingDocument=safe;
    }

    private void ResetDrawingHistory()
    {
        _drawingUndo.Clear();_drawingRedo.Clear();
    }

    private void RestoreDrawingDocument(DrawingDocument document)
    {
        _drawingDocument=DrawingDocumentFile.Clone(document);
        _drawingBodies.Clear();
        RefreshDrawingTree();
        RenderDrawing();
    }

    internal void UndoDrawing()
    {
        if(_drawingDocument is null||_drawingUndo.Count==0){Status("Teknik resimde geri alınacak işlem yok.");return;}
        _drawingRedo.Push(DrawingDocumentFile.Clone(_drawingDocument));
        RestoreDrawingDocument(_drawingUndo.Pop());
        Status("Teknik resim işlemi geri alındı.");
    }

    internal void RedoDrawing()
    {
        if(_drawingDocument is null||_drawingRedo.Count==0){Status("Teknik resimde yinelenecek işlem yok.");return;}
        _drawingUndo.Push(DrawingDocumentFile.Clone(_drawingDocument));
        RestoreDrawingDocument(_drawingRedo.Pop());
        Status("Teknik resim işlemi yinelendi.");
    }

    private void ExitDrawingMode()
    {
        if(_drawingRoot is not null)FeatureTree.Items.Remove(_drawingRoot);
        _drawingRoot=null;_drawingDocument=null;_drawingPath=null;_drawingBodies.Clear();ResetDrawingHistory();
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
                var parentType=_drawingDocument.Views.FirstOrDefault(v=>v.Id==view.ParentViewId)?.Type??DrawingViewType.Front;
                var orientationType=view.Type is DrawingViewType.Projected or DrawingViewType.Section or DrawingViewType.Detail or DrawingViewType.Broken?parentType:view.Type;
                var (direction,up)=DrawingOrientation(orientationType);
                var allSegments=new List<double>();
                foreach(var body in DrawingBodies(view.ModelPath))
                {
                    if(view.Type==DrawingViewType.Section&&view.SectionOrigin is Vector3d sectionOrigin&&view.SectionNormal is Vector3d sectionNormal)
                        allSegments.AddRange(SrcCad.Kernel.Occt.OcctFeatureKernel.SectionEdges(body,
                            new[]{sectionOrigin.X,sectionOrigin.Y,sectionOrigin.Z},
                            new[]{sectionNormal.X,sectionNormal.Y,sectionNormal.Z},
                            new[]{direction.X,direction.Y,direction.Z},new[]{up.X,up.Y,up.Z}));
                    else
                        allSegments.AddRange(SrcCad.Kernel.Occt.OcctFeatureKernel.ProjectEdges(body,
                            new[]{direction.X,direction.Y,direction.Z},new[]{up.X,up.Y,up.Z}));
                }
                var segments=allSegments.ToArray();
                if(segments.Length<4)continue;
                var xs=new List<double>();var ys=new List<double>();
                for(var i=0;i+3<segments.Length;i+=4){xs.Add(segments[i]);xs.Add(segments[i+2]);ys.Add(segments[i+1]);ys.Add(segments[i+3]);}
                if(view.Type==DrawingViewType.Broken&&view.BreakStart is double breakStart&&view.BreakEnd is double breakEnd)
                {
                    double MapX(double x)=>x<=breakStart?x:x>=breakEnd?x-(breakEnd-breakStart):breakStart;
                    for(var i=0;i+3<segments.Length;i+=4)
                    {
                        var ax=segments[i];var ay=segments[i+1];var bx=segments[i+2];var by=segments[i+3];
                        if(ax>breakStart&&ax<breakEnd&&bx>breakStart&&bx<breakEnd)continue;
                        var x1=MapX(ax);var x2=MapX(bx);
                        segments[i]=x1;segments[i+2]=x2;
                    }
                    xs.Clear();ys.Clear();
                    for(var i=0;i+3<segments.Length;i+=4)
                    {
                        var ax=segments[i],bx=segments[i+2];
                        if(!double.IsFinite(ax)||!double.IsFinite(bx))continue;
                        xs.Add(ax);xs.Add(bx);ys.Add(segments[i+1]);ys.Add(segments[i+3]);
                    }
                }
                var cx=(xs.Min()+xs.Max())*0.5;var cy=(ys.Min()+ys.Max())*0.5;
                for(var i=0;i+3<segments.Length;i+=4)
                {
                    var x1=(segments[i]-cx)*view.Scale+view.Position.X;
                    var y1=(segments[i+1]-cy)*view.Scale+view.Position.Y;
                    var x2=(segments[i+2]-cx)*view.Scale+view.Position.X;
                    var y2=(segments[i+3]-cy)*view.Scale+view.Position.Y;
                    Line(x1,y1,x2,y2);
                }
                if(view.Type==DrawingViewType.Broken)
                {
                    var symbolX=view.Position.X;
                    var top=view.Position.Y+28;var bottom=view.Position.Y-28;
                    Line(symbolX-4,top,symbolX+2,top-6,1);Line(symbolX+2,top-6,symbolX-2,top-12,1);Line(symbolX-2,top-12,symbolX+4,top-18,1);
                    Line(symbolX-4,bottom,symbolX+2,bottom+6,1);Line(symbolX+2,bottom+6,symbolX-2,bottom+12,1);Line(symbolX-2,bottom+12,symbolX+4,bottom+18,1);
                }
                if(view.Type==DrawingViewType.Detail)
                    _viewer.AddStyledSketchCircle(view.Position.X,view.Position.Y,0,0,0,1,1,0,0,45,1);
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
        ResetDrawingHistory();
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
            SetDrawingDocument(_drawingDocument with{Views=_drawingDocument.Views.Append(view).ToArray()});
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
        SetDrawingDocument(_drawingDocument with{Views=_drawingDocument.Views.Append(view).ToArray()});
        RefreshDrawingTree(view.Id);RenderDrawing();Status(view.Name+" oluşturuldu.");
    }

    private (Vector3d Min,Vector3d Max) DrawingModelBounds(string modelPath)
    {
        var bounds=DrawingBodies(modelPath).Select(SrcCad.Kernel.Occt.OcctFeatureKernel.Bounds).Where(b=>b.Length==6).ToArray();
        if(bounds.Length==0)throw new InvalidDataException("Model sınır kutusu bulunamadı.");
        return (new Vector3d(bounds.Min(b=>b[0]),bounds.Min(b=>b[1]),bounds.Min(b=>b[2])),
                new Vector3d(bounds.Max(b=>b[3]),bounds.Max(b=>b[4]),bounds.Max(b=>b[5])));
    }

    private void DrawingSectionView_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null){Status("Önce teknik resim oluşturun.");return;}
        var parent=SelectedDrawingView();
        if(parent is null){Status("Kesit için ana görünüşü FeatureManager'dan seçin.");return;}
        try
        {
            var bounds=DrawingModelBounds(parent.ModelPath);
            var center=new Vector3d((bounds.Min.X+bounds.Max.X)*0.5,(bounds.Min.Y+bounds.Max.Y)*0.5,(bounds.Min.Z+bounds.Max.Z)*0.5);
            var (direction,up)=DrawingOrientation(parent.Type);
            var right=Vector3d.Cross(up.Normalized(),direction.Normalized()).Normalized();
            var section=new DrawingView(Guid.NewGuid(),"KESİT A-A",parent.ModelPath,DrawingViewType.Section,parent.SheetId,
                new Point2d(parent.Position.X+125,parent.Position.Y),parent.Scale,parent.Id,null,center,right);
            SetDrawingDocument(_drawingDocument with{Views=_drawingDocument.Views.Append(section).ToArray()});
            RefreshDrawingTree(section.Id);RenderDrawing();Status("Gerçek OCCT kesit eğrileriyle KESİT A-A oluşturuldu.");
        }
        catch(Exception ex){Status("Kesit oluşturulamadı: "+ex.Message);}
    }

    private void DrawingBrokenView_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var parent=SelectedDrawingView();
        if(parent is null){Status("Kopuk görünüm için ana görünüşü FeatureManager'dan seçin.");return;}
        try
        {
            var data=DrawingProjectionData(parent);
            var xs=new List<double>();
            for(var i=0;i+3<data.Segments.Length;i+=4){xs.Add(data.Segments[i]);xs.Add(data.Segments[i+2]);}
            var min=xs.Min();var max=xs.Max();var width=max-min;
            if(width<=1e-6){Status("Kopuk görünüm için görünüş genişliği yetersiz.");return;}
            var broken=new DrawingView(Guid.NewGuid(),"KOPUK "+parent.Name,parent.ModelPath,DrawingViewType.Broken,parent.SheetId,
                new Point2d(parent.Position.X+125,parent.Position.Y-80),parent.Scale,parent.Id,
                BreakStart:min+width*0.40,BreakEnd:min+width*0.60);
            SetDrawingDocument(_drawingDocument with{Views=_drawingDocument.Views.Append(broken).ToArray()});
            RefreshDrawingTree(broken.Id);RenderDrawing();Status("Kopuk görünüm oluşturuldu; orta %20 bölge kaldırıldı.");
        }
        catch(Exception ex){Status("Kopuk görünüm oluşturulamadı: "+ex.Message);}
    }

    private void DrawingDetailView_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var parent=SelectedDrawingView();
        if(parent is null){Status("Detay için ana görünüşü FeatureManager'dan seçin.");return;}
        var detail=new DrawingView(Guid.NewGuid(),"DETAY A",parent.ModelPath,DrawingViewType.Detail,parent.SheetId,
            new Point2d(parent.Position.X+125,parent.Position.Y-85),parent.Scale*2,parent.Id);
        SetDrawingDocument(_drawingDocument with{Views=_drawingDocument.Views.Append(detail).ToArray()});
        RefreshDrawingTree(detail.Id);RenderDrawing();Status("DETAY A görünüşü 2× ölçekle oluşturuldu.");
    }

    private (double Cx,double Cy,double[] Segments,Vector3d Direction,Vector3d Up) DrawingProjectionData(DrawingView view)
    {
        if(_drawingDocument is null)throw new InvalidOperationException("Drawing document missing.");
        var parentType=_drawingDocument.Views.FirstOrDefault(v=>v.Id==view.ParentViewId)?.Type??DrawingViewType.Front;
        var orientationType=view.Type is DrawingViewType.Projected or DrawingViewType.Section or DrawingViewType.Detail or DrawingViewType.Broken?parentType:view.Type;
        var (direction,up)=DrawingOrientation(orientationType);
        var segments=DrawingBodies(view.ModelPath).SelectMany(body=>
            SrcCad.Kernel.Occt.OcctFeatureKernel.ProjectEdges(body,
                new[]{direction.X,direction.Y,direction.Z},new[]{up.X,up.Y,up.Z})).ToArray();
        if(segments.Length<4)throw new InvalidOperationException("Görünüş izdüşüm geometrisi boş.");
        var xs=new List<double>();var ys=new List<double>();
        for(var i=0;i+3<segments.Length;i+=4){xs.Add(segments[i]);xs.Add(segments[i+2]);ys.Add(segments[i+1]);ys.Add(segments[i+3]);}
        return ((xs.Min()+xs.Max())*0.5,(ys.Min()+ys.Max())*0.5,segments,direction,up);
    }

    private void DrawingCenterMark_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var view=SelectedDrawingView();
        if(view is null){Status("Merkez işareti için görünüş seçin.");return;}
        try
        {
            var data=DrawingProjectionData(view);
            var circles=DrawingBodies(view.ModelPath).SelectMany(body=>
                SrcCad.Kernel.Occt.OcctFeatureKernel.ProjectCircles(body,
                    new[]{data.Direction.X,data.Direction.Y,data.Direction.Z},new[]{data.Up.X,data.Up.Y,data.Up.Z})).ToArray();
            if(circles.Length<3){Status("Bu görünüşte eksene dik dairesel kenar bulunamadı.");return;}
            var x=(circles[0]-data.Cx)*view.Scale+view.Position.X;
            var y=(circles[1]-data.Cy)*view.Scale+view.Position.Y;
            var r=Math.Max(4,circles[2]*view.Scale*0.35);
            _viewer.AddStyledSketchLine(x-r,y,0,x+r,y,0,1);
            _viewer.AddStyledSketchLine(x,y-r,0,x,y+r,0,1);
            Status("İlk dairesel kenara gerçek merkez işareti eklendi.");
        }
        catch(Exception ex){Status("Merkez işareti oluşturulamadı: "+ex.Message);}
    }

    private void DrawingHoleCallout_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var view=SelectedDrawingView();
        if(view is null){Status("Delik çağrısı için görünüş seçin.");return;}
        try
        {
            var data=DrawingProjectionData(view);
            var circles=DrawingBodies(view.ModelPath).SelectMany(body=>
                SrcCad.Kernel.Occt.OcctFeatureKernel.ProjectCircles(body,
                    new[]{data.Direction.X,data.Direction.Y,data.Direction.Z},new[]{data.Up.X,data.Up.Y,data.Up.Z})).ToArray();
            if(circles.Length<3){Status("Bu görünüşte dairesel delik/kenar bulunamadı.");return;}
            var x=(circles[0]-data.Cx)*view.Scale+view.Position.X;
            var y=(circles[1]-data.Cy)*view.Scale+view.Position.Y;
            var text=$"Ø{circles[2]*2:0.###}";
            var note=new DrawingAnnotation(Guid.NewGuid(),view.SheetId,view.Id,new Point2d(x+20,y+20),text);
            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
            RefreshDrawingTree(note.Id);RenderDrawing();Status("Delik çağrısı eklendi: "+text);
        }
        catch(Exception ex){Status("Delik çağrısı oluşturulamadı: "+ex.Message);}
    }

    private void DrawingSmartDimension_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var view=SelectedDrawingView();
        if(view is null){Status("Ölçü için görünüş seçin.");return;}
        try
        {
            var data=DrawingProjectionData(view);
            var xs=new List<double>();var ys=new List<double>();
            for(var i=0;i+3<data.Segments.Length;i+=4)
            {
                xs.Add(data.Segments[i]);xs.Add(data.Segments[i+2]);
                ys.Add(data.Segments[i+1]);ys.Add(data.Segments[i+3]);
            }
            var width=(xs.Max()-xs.Min());var height=(ys.Max()-ys.Min());
            var note=new DrawingAnnotation(Guid.NewGuid(),view.SheetId,view.Id,
                new Point2d(view.Position.X,view.Position.Y+65),$"GENEL: {width:0.###} × {height:0.###} mm");
            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
            RefreshDrawingTree(note.Id);RenderDrawing();Status("Model izdüşümünden genel ölçüler eklendi.");
        }
        catch(Exception ex){Status("Ölçü oluşturulamadı: "+ex.Message);}
    }

    private void DrawingAddNote_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var sheet=_drawingDocument.Sheets[0];
        var note=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,SelectedDrawingView()?.Id,new Point2d(20,20),"NOT");
        SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
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
            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
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
        SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
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
            _drawingDocument=DrawingDocumentFile.Load(dialog.FileName);ResetDrawingHistory();_drawingPath=dialog.FileName;
            _drawingBodies.Clear();RefreshDrawingTree();RenderDrawing();
            Status("Teknik resim açıldı: "+Path.GetFileName(dialog.FileName));
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Teknik resim açılamadı",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}
