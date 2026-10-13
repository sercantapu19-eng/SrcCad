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
    private Guid? _drawingEditingViewId;
    private Guid? _drawingEditingAnnotationId;

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
        DrawingViewPanel.Visibility=Visibility.Collapsed;_drawingEditingViewId=null;
        DrawingAnnotationPanel.Visibility=Visibility.Collapsed;_drawingEditingAnnotationId=null;
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
            var source=RebuildAssemblyComponent(
                AssemblyDocumentFile.ResolveSourcePath(full,component.SourcePath));
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
                var menu=new ContextMenu();
                var edit=new MenuItem{Header="Görünüşü Düzenle"};edit.Click+=(_,_)=>BeginDrawingViewEdit(view.Id);menu.Items.Add(edit);
                var del=new MenuItem{Header="Görünüşü Sil"};del.Click+=(_,_)=>DeleteDrawingView(view.Id);menu.Items.Add(del);
                item.ContextMenu=menu;
                sheetNode.Items.Add(item);
                if(selected==view.Id)item.IsSelected=true;
            }
            foreach(var note in _drawingDocument.Annotations.Where(a=>a.SheetId==sheet.Id))
            {
                var item=new TreeViewItem{Header="Not: "+note.Text,Tag=note.Id};
                var menu=new ContextMenu();
                var edit=new MenuItem{Header="Notu / Ölçüyü Düzenle"};edit.Click+=(_,_)=>BeginDrawingAnnotationEdit(note.Id);menu.Items.Add(edit);
                var del=new MenuItem{Header="Notu Sil"};del.Click+=(_,_)=>DeleteDrawingAnnotation(note.Id);menu.Items.Add(del);
                item.ContextMenu=menu;
                sheetNode.Items.Add(item);
                if(selected==note.Id)item.IsSelected=true;
            }
            _drawingRoot.Items.Add(sheetNode);
        }
        FeatureTree.Items.Add(_drawingRoot);ManagerTabs.SelectedItem=FeatureManagerTab;
    }

    private static bool TryDrawingNumber(string text,out double value)
        => double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.CurrentCulture,out value)||
           double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value);

    private void BeginDrawingViewEdit(Guid id)
    {
        if(_drawingDocument is null)return;
        DrawingView view;
        try{view=_drawingDocument.View(id);}catch{Status("Düzenlenecek görünüş bulunamadı.");return;}
        _drawingEditingViewId=id;
        DrawingViewName.Text=view.Name;
        DrawingViewX.Text=view.Position.X.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        DrawingViewY.Text=view.Position.Y.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        DrawingViewScale.Text=view.Scale.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        DrawingViewInfo.Text=$"{view.Type} • {(view.ParentViewId is null?"Ana görünüş":"Bağlı görünüş")}";
        DrawingViewError.Text="";
        DrawingViewPanel.Visibility=Visibility.Visible;
        PropertyTitle.Text="Görünüşü Düzenle";
        PropertyInfo.Text="Ad, sayfa konumu ve ölçek kalıcı .scdrw verisidir. Değişiklikler gerçek model izdüşümünü yeniden çizer.";
        ManagerTabs.SelectedItem=PropertyManagerTab;
    }

    private void DrawingViewOk_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null||_drawingEditingViewId is not Guid id)return;
        if(!TryDrawingNumber(DrawingViewX.Text,out var x)||!TryDrawingNumber(DrawingViewY.Text,out var y)||
           !TryDrawingNumber(DrawingViewScale.Text,out var scale)||!double.IsFinite(scale)||scale<=1e-9||
           string.IsNullOrWhiteSpace(DrawingViewName.Text))
        {
            DrawingViewError.Text="Ad boş olamaz; X/Y sonlu, ölçek pozitif olmalı.";return;
        }
        DrawingView current;
        try{current=_drawingDocument.View(id);}catch{DrawingViewError.Text="Görünüş bulunamadı.";return;}
        var updated=current with{Name=DrawingViewName.Text.Trim(),Position=new Point2d(x,y),Scale=scale};
        SetDrawingDocument(_drawingDocument with{Views=_drawingDocument.Views.Select(v=>v.Id==id?updated:v).ToArray()});
        DrawingViewPanel.Visibility=Visibility.Collapsed;_drawingEditingViewId=null;
        RefreshDrawingTree(id);RenderDrawing();Status(updated.Name+" güncellendi.");
    }

    private void DrawingViewCancel_Click(object sender,RoutedEventArgs e)
    {
        DrawingViewPanel.Visibility=Visibility.Collapsed;_drawingEditingViewId=null;ManagerTabs.SelectedItem=FeatureManagerTab;
    }

    internal bool DeleteSelectedDrawingTreeItem()
    {
        if(_drawingDocument is null||FeatureTree.SelectedItem is not TreeViewItem item||item.Tag is not Guid id)return false;
        if(_drawingDocument.Views.Any(v=>v.Id==id)){DeleteDrawingView(id);return true;}
        if(_drawingDocument.Annotations.Any(a=>a.Id==id)){DeleteDrawingAnnotation(id);return true;}
        return false;
    }

    private void DeleteDrawingView(Guid id)
    {
        if(_drawingDocument is null)return;
        var before=_drawingDocument.Views.Length;
        SetDrawingDocument(_drawingDocument.RemoveView(id,true));
        var removed=before-_drawingDocument.Views.Length;
        RefreshDrawingTree();RenderDrawing();
        Status(removed==1?"Görünüş silindi.":$"{removed} bağlı görünüş silindi.");
    }

    private void BeginDrawingAnnotationEdit(Guid id)
    {
        if(_drawingDocument is null)return;
        var note=_drawingDocument.Annotations.FirstOrDefault(a=>a.Id==id);
        if(note is null){Status("Düzenlenecek teknik resim notu bulunamadı.");return;}
        _drawingEditingAnnotationId=id;
        DrawingAnnotationText.Text=note.Text;
        DrawingAnnotationX.Text=note.Position.X.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        DrawingAnnotationY.Text=note.Position.Y.ToString("G17",System.Globalization.CultureInfo.CurrentCulture);
        DrawingAnnotationError.Text="";
        DrawingAnnotationPanel.Visibility=Visibility.Visible;
        PropertyTitle.Text="Not / Ölçü Düzenle";
        PropertyInfo.Text="Metin ve sayfa konumu .scdrw içinde kalıcıdır ve Undo/Redo ile geri alınabilir.";
        ManagerTabs.SelectedItem=PropertyManagerTab;
    }

    private void DrawingAnnotationOk_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null||_drawingEditingAnnotationId is not Guid id)return;
        if(string.IsNullOrWhiteSpace(DrawingAnnotationText.Text)||
           !TryDrawingNumber(DrawingAnnotationX.Text,out var x)||!TryDrawingNumber(DrawingAnnotationY.Text,out var y))
        {
            DrawingAnnotationError.Text="Metin boş olamaz; X/Y sonlu olmalı.";return;
        }
        var note=_drawingDocument.Annotations.FirstOrDefault(a=>a.Id==id);
        if(note is null){DrawingAnnotationError.Text="Not bulunamadı.";return;}
        var editedText=DrawingAnnotationText.Text.Trim();
        var textChanged=!string.Equals(editedText,note.Text,StringComparison.Ordinal);
        var updated=note with
        {
            Text=editedText,
            Position=new Point2d(x,y),
            Kind=textChanged?DrawingAnnotationKind.Text:note.Kind,
            Data=textChanged?null:note.Data
        };
        SetDrawingDocument(_drawingDocument with
        {
            Annotations=_drawingDocument.Annotations.Select(a=>a.Id==id?updated:a).ToArray()
        });
        DrawingAnnotationPanel.Visibility=Visibility.Collapsed;_drawingEditingAnnotationId=null;
        RefreshDrawingTree(id);RenderDrawing();Status("Teknik resim notu/ölçüsü güncellendi.");
    }

    private void DrawingAnnotationCancel_Click(object sender,RoutedEventArgs e)
    {
        DrawingAnnotationPanel.Visibility=Visibility.Collapsed;_drawingEditingAnnotationId=null;ManagerTabs.SelectedItem=FeatureManagerTab;
    }

    private void DeleteDrawingAnnotation(Guid id)
    {
        if(_drawingDocument is null)return;
        try
        {
            SetDrawingDocument(_drawingDocument.RemoveAnnotation(id));
            RefreshDrawingTree();RenderDrawing();Status("Teknik resim notu/ölçüsü silindi.");
        }
        catch(InvalidOperationException){Status("Silinecek teknik resim notu bulunamadı.");}
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
        {
            RenderDrawingDimensionGraphics(note);
            if(note.Kind!=DrawingAnnotationKind.CenterMark)
            {
                var text=ResolveDrawingAnnotationText(note);
                _viewer.AddSketchText(note.Position.X,note.Position.Y,0,text,0);
            }
        }
        _viewer.SetTopView();_viewer.FitAll();
    }

    private void RenderDrawingDimensionGraphics(DrawingAnnotation note)
    {
        if(_drawingDocument is null||note.ViewId is not Guid viewId)return;
        var view=_drawingDocument.Views.FirstOrDefault(v=>v.Id==viewId);
        if(view is null)return;
        void Line(Point2d a,Point2d b)=>_viewer.AddStyledSketchLine(a.X,a.Y,0,b.X,b.Y,0,1);

        if(note.Kind==DrawingAnnotationKind.LinearDimension&&ResolveLinearDimension(view,note.Data) is ProjectedSegment segment)
        {
            var a=DrawingToSheet(view,segment.X1,segment.Y1);
            var b=DrawingToSheet(view,segment.X2,segment.Y2);
            var dx=b.X-a.X;var dy=b.Y-a.Y;var length=Math.Sqrt(dx*dx+dy*dy);
            if(length<=1e-9)return;
            var nx=-dy/length;var ny=dx/length;
            var mid=new Point2d((a.X+b.X)*0.5,(a.Y+b.Y)*0.5);
            var offset=(note.Position.X-mid.X)*nx+(note.Position.Y-mid.Y)*ny;
            if(Math.Abs(offset)<6)offset=offset<0?-12:12;
            var qa=new Point2d(a.X+nx*offset,a.Y+ny*offset);
            var qb=new Point2d(b.X+nx*offset,b.Y+ny*offset);
            Line(a,qa);Line(b,qb);Line(qa,qb);
            var tick=3.0;
            Line(new Point2d(qa.X-dx/length*tick-nx*tick,qa.Y-dy/length*tick-ny*tick),
                 new Point2d(qa.X+dx/length*tick+nx*tick,qa.Y+dy/length*tick+ny*tick));
            Line(new Point2d(qb.X-dx/length*tick-nx*tick,qb.Y-dy/length*tick-ny*tick),
                 new Point2d(qb.X+dx/length*tick+nx*tick,qb.Y+dy/length*tick+ny*tick));
        }
        else if(note.Kind==DrawingAnnotationKind.DiameterDimension&&ResolveDiameterDimension(view,note.Data) is ProjectedCircle circle)
        {
            var center=DrawingToSheet(view,circle.X,circle.Y);
            var radius=circle.Radius*view.Scale;
            var direction=new Vector3d(note.Position.X-center.X,note.Position.Y-center.Y,0);
            var len=direction.Length;
            var ux=len>1e-9?direction.X/len:1.0;var uy=len>1e-9?direction.Y/len:0.0;
            var a=new Point2d(center.X-ux*radius,center.Y-uy*radius);
            var b=new Point2d(center.X+ux*radius,center.Y+uy*radius);
            Line(a,b);Line(b,note.Position);
        }
        else if(note.Kind==DrawingAnnotationKind.RadiusDimension&&ResolveRadiusDimension(view,note.Data) is ProjectedCircle radiusCircle)
        {
            var center=DrawingToSheet(view,radiusCircle.X,radiusCircle.Y);
            var radius=radiusCircle.Radius*view.Scale;
            var dx=note.Position.X-center.X;var dy=note.Position.Y-center.Y;
            var len=Math.Sqrt(dx*dx+dy*dy);var ux=len>1e-9?dx/len:1.0;var uy=len>1e-9?dy/len:0.0;
            var rim=new Point2d(center.X+ux*radius,center.Y+uy*radius);
            Line(center,rim);Line(rim,note.Position);
        }
        else if(note.Kind==DrawingAnnotationKind.AngularDimension&&ResolveAngularDimension(view,note.Data) is { } angular)
        {
            var a1=DrawingToSheet(view,angular.A.X1,angular.A.Y1);
            var a2=DrawingToSheet(view,angular.A.X2,angular.A.Y2);
            var b1=DrawingToSheet(view,angular.B.X1,angular.B.Y1);
            var b2=DrawingToSheet(view,angular.B.X2,angular.B.Y2);
            var am=new Point2d((a1.X+a2.X)*0.5,(a1.Y+a2.Y)*0.5);
            var bm=new Point2d((b1.X+b2.X)*0.5,(b1.Y+b2.Y)*0.5);
            Line(am,note.Position);Line(bm,note.Position);
        }
        else if(note.Kind==DrawingAnnotationKind.CenterMark&&ResolveRadiusDimension(view,note.Data) is ProjectedCircle markCircle)
        {
            var center=DrawingToSheet(view,markCircle.X,markCircle.Y);
            var half=Math.Max(4,markCircle.Radius*view.Scale*0.35);
            Line(new Point2d(center.X-half,center.Y),new Point2d(center.X+half,center.Y));
            Line(new Point2d(center.X,center.Y-half),new Point2d(center.X,center.Y+half));
        }
    }

    private string ResolveDrawingAnnotationText(DrawingAnnotation note)
    {
        if(_drawingDocument is null)return note.Text;
        try
        {
            var view=note.ViewId is Guid viewId?_drawingDocument.Views.FirstOrDefault(v=>v.Id==viewId):null;
            return note.Kind switch
            {
                DrawingAnnotationKind.OverallSize when view is not null=>ResolveOverallSize(view),
                DrawingAnnotationKind.LinearDimension when view is not null=>
                    ResolveLinearDimension(view,note.Data) is ProjectedSegment segment?$"{segment.Length:0.###} mm":"?",
                DrawingAnnotationKind.DiameterDimension when view is not null=>
                    ResolveDiameterDimension(view,note.Data) is ProjectedCircle circle?$"Ø{circle.Radius*2:0.###}":"Ø?",
                DrawingAnnotationKind.RadiusDimension when view is not null=>
                    ResolveRadiusDimension(view,note.Data) is ProjectedCircle radiusCircle?$"R{radiusCircle.Radius:0.###}":"R?",
                DrawingAnnotationKind.AngularDimension when view is not null=>
                    ResolveAngularDimension(view,note.Data) is { } angular?$"{AngularDegrees(angular.A,angular.B):0.###}°":"?°",
                DrawingAnnotationKind.HoleCallout when view is not null=>ResolveHoleCallout(view,note.Data),
                DrawingAnnotationKind.BillOfMaterials when view is not null=>ResolveBom(view),
                DrawingAnnotationKind.Balloon=>note.Data is string number&&number.Length>0?"BALON "+number:note.Text,
                _=>note.Text
            };
        }
        catch{return note.Text;}
    }

    private sealed record ProjectedSegment(double X1,double Y1,double X2,double Y2)
    {
        public double Length=>Math.Sqrt((X2-X1)*(X2-X1)+(Y2-Y1)*(Y2-Y1));
        public Point2d Mid=>new((X1+X2)*0.5,(Y1+Y2)*0.5);
    }
    private sealed record ProjectedCircle(double X,double Y,double Radius);

    private static string DrawingNumber(double value)
        => value.ToString("G17",System.Globalization.CultureInfo.InvariantCulture);

    private static double PointSegmentDistance(Point2d p,Point2d a,Point2d b)
    {
        var dx=b.X-a.X;var dy=b.Y-a.Y;var l2=dx*dx+dy*dy;
        if(l2<1e-18)return Point2d.Distance(p,a);
        var t=Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy)/l2,0,1);
        return Point2d.Distance(p,new Point2d(a.X+dx*t,a.Y+dy*t));
    }

    private static string EncodeLinearDimension(ProjectedSegment segment)
        =>$"L;{DrawingNumber(segment.X1)};{DrawingNumber(segment.Y1)};{DrawingNumber(segment.X2)};{DrawingNumber(segment.Y2)}";

    private static string EncodeDiameterDimension(ProjectedCircle circle)
        =>$"D;{DrawingNumber(circle.X)};{DrawingNumber(circle.Y)};{DrawingNumber(circle.Radius)}";

    private static string EncodeRadiusDimension(ProjectedCircle circle)
        =>$"R;{DrawingNumber(circle.X)};{DrawingNumber(circle.Y)};{DrawingNumber(circle.Radius)}";

    private static string EncodeAngularDimension(ProjectedSegment a,ProjectedSegment b)
        =>$"A;{DrawingNumber(a.X1)};{DrawingNumber(a.Y1)};{DrawingNumber(a.X2)};{DrawingNumber(a.Y2)};"+
          $"{DrawingNumber(b.X1)};{DrawingNumber(b.Y1)};{DrawingNumber(b.X2)};{DrawingNumber(b.Y2)}";

    private static bool TryDrawingData(string? data,char type,int count,out double[] values)
    {
        values=Array.Empty<double>();
        if(string.IsNullOrWhiteSpace(data))return false;
        var fields=data.Split(';');
        if(fields.Length!=count+1||fields[0].Length!=1||fields[0][0]!=type)return false;
        values=new double[count];
        for(var i=0;i<count;i++)
            if(!double.TryParse(fields[i+1],System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out values[i])||!double.IsFinite(values[i]))
                return false;
        return true;
    }

    private ProjectedSegment[] DrawingProjectedSegments(DrawingView view)
    {
        var data=DrawingProjectionData(view);
        var result=new List<ProjectedSegment>();
        for(var i=0;i+3<data.Segments.Length;i+=4)
            result.Add(new(data.Segments[i],data.Segments[i+1],data.Segments[i+2],data.Segments[i+3]));
        return result.ToArray();
    }

    private ProjectedCircle[] DrawingProjectedCircles(DrawingView view)
    {
        var data=DrawingProjectionData(view);
        var values=DrawingBodies(view.ModelPath).SelectMany(body=>
            SrcCad.Kernel.Occt.OcctFeatureKernel.ProjectCircles(body,
                new[]{data.Direction.X,data.Direction.Y,data.Direction.Z},
                new[]{data.Up.X,data.Up.Y,data.Up.Z})).ToArray();
        var result=new List<ProjectedCircle>();
        for(var i=0;i+2<values.Length;i+=3)result.Add(new(values[i],values[i+1],values[i+2]));
        return result.ToArray();
    }

    private ProjectedSegment? ResolveLinearDimension(DrawingView view,string? encoded)
    {
        if(!TryDrawingData(encoded,'L',4,out var wanted))return null;
        var target=new ProjectedSegment(wanted[0],wanted[1],wanted[2],wanted[3]);
        var targetLength=Math.Max(target.Length,1e-9);var targetMid=target.Mid;
        return DrawingProjectedSegments(view)
            .OrderBy(segment=>
            {
                var lengthScore=Math.Abs(segment.Length-target.Length)/targetLength;
                var midScore=Point2d.Distance(segment.Mid,targetMid)/Math.Max(1.0,targetLength);
                var ax=(target.X2-target.X1)/targetLength;var ay=(target.Y2-target.Y1)/targetLength;
                var len=Math.Max(segment.Length,1e-9);var bx=(segment.X2-segment.X1)/len;var by=(segment.Y2-segment.Y1)/len;
                var directionScore=1-Math.Abs(ax*bx+ay*by);
                return lengthScore+midScore+directionScore*2;
            }).FirstOrDefault();
    }

    private ProjectedCircle? ResolveDiameterDimension(DrawingView view,string? encoded)
    {
        if(!TryDrawingData(encoded,'D',3,out var wanted))return null;
        return ResolveCircleDimension(view,wanted);
    }

    private ProjectedCircle? ResolveRadiusDimension(DrawingView view,string? encoded)
    {
        if(!TryDrawingData(encoded,'R',3,out var wanted))return null;
        return ResolveCircleDimension(view,wanted);
    }

    private ProjectedCircle? ResolveCircleDimension(DrawingView view,double[] wanted)
    {
        var target=new ProjectedCircle(wanted[0],wanted[1],wanted[2]);
        return DrawingProjectedCircles(view)
            .OrderBy(circle=>
                Point2d.Distance(new Point2d(circle.X,circle.Y),new Point2d(target.X,target.Y))/Math.Max(1.0,target.Radius)+
                Math.Abs(circle.Radius-target.Radius)/Math.Max(1e-9,target.Radius))
            .FirstOrDefault();
    }

    private static double SegmentMatchScore(ProjectedSegment segment,ProjectedSegment target)
    {
        var targetLength=Math.Max(target.Length,1e-9);
        var lengthScore=Math.Abs(segment.Length-target.Length)/targetLength;
        var midScore=Point2d.Distance(segment.Mid,target.Mid)/Math.Max(1.0,targetLength);
        var ax=(target.X2-target.X1)/targetLength;var ay=(target.Y2-target.Y1)/targetLength;
        var len=Math.Max(segment.Length,1e-9);var bx=(segment.X2-segment.X1)/len;var by=(segment.Y2-segment.Y1)/len;
        return lengthScore+midScore+(1-Math.Abs(ax*bx+ay*by))*2;
    }

    private (ProjectedSegment A,ProjectedSegment B)? ResolveAngularDimension(DrawingView view,string? encoded)
    {
        if(!TryDrawingData(encoded,'A',8,out var wanted))return null;
        var aTarget=new ProjectedSegment(wanted[0],wanted[1],wanted[2],wanted[3]);
        var bTarget=new ProjectedSegment(wanted[4],wanted[5],wanted[6],wanted[7]);
        var segments=DrawingProjectedSegments(view);
        if(segments.Length<2)return null;
        var a=segments.OrderBy(segment=>SegmentMatchScore(segment,aTarget)).First();
        var b=segments.Where(segment=>!ReferenceEquals(segment,a))
            .OrderBy(segment=>SegmentMatchScore(segment,bTarget)).FirstOrDefault();
        return b is null?null:(a,b);
    }

    private static double AngularDegrees(ProjectedSegment a,ProjectedSegment b)
    {
        var adx=a.X2-a.X1;var ady=a.Y2-a.Y1;var bdx=b.X2-b.X1;var bdy=b.Y2-b.Y1;
        var al=Math.Sqrt(adx*adx+ady*ady);var bl=Math.Sqrt(bdx*bdx+bdy*bdy);
        if(al<=1e-9||bl<=1e-9)return 0;
        var dot=Math.Clamp((adx*bdx+ady*bdy)/(al*bl),-1.0,1.0);
        var angle=Math.Acos(dot)*180.0/Math.PI;
        return angle>180?360-angle:angle;
    }

    private Point2d DrawingToSheet(DrawingView view,double x,double y)
    {
        var data=DrawingProjectionData(view);
        return new Point2d((x-data.Cx)*view.Scale+view.Position.X,(y-data.Cy)*view.Scale+view.Position.Y);
    }

    private string ResolveOverallSize(DrawingView view)
    {
        var data=DrawingProjectionData(view);
        var xs=new List<double>();var ys=new List<double>();
        for(var i=0;i+3<data.Segments.Length;i+=4)
        {
            xs.Add(data.Segments[i]);xs.Add(data.Segments[i+2]);
            ys.Add(data.Segments[i+1]);ys.Add(data.Segments[i+3]);
        }
        return $"GENEL: {xs.Max()-xs.Min():0.###} × {ys.Max()-ys.Min():0.###} mm";
    }

    private string ResolveHoleCallout(DrawingView view,string? data)
    {
        var projection=DrawingProjectionData(view);
        var circles=DrawingBodies(view.ModelPath).SelectMany(body=>
            SrcCad.Kernel.Occt.OcctFeatureKernel.ProjectCircles(body,
                new[]{projection.Direction.X,projection.Direction.Y,projection.Direction.Z},
                new[]{projection.Up.X,projection.Up.Y,projection.Up.Z})).ToArray();
        if(circles.Length<3)return "Ø?";
        var index=0;
        if(int.TryParse(data,out var parsed)&&parsed>=0&&parsed*3+2<circles.Length)index=parsed;
        return $"Ø{circles[index*3+2]*2:0.###}";
    }

    private static IEnumerable<string> EnumerateBomParts(string modelPath,HashSet<string>? recursion=null)
    {
        var full=Path.GetFullPath(modelPath);
        if(string.Equals(Path.GetExtension(full),".scpart",StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.GetFileNameWithoutExtension(full);
            yield break;
        }
        if(!string.Equals(Path.GetExtension(full),".scasm",StringComparison.OrdinalIgnoreCase))yield break;

        recursion??=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(!recursion.Add(full))throw new InvalidDataException("BOM alt montaj döngüsü algılandı: "+Path.GetFileName(full));
        try
        {
            var assembly=AssemblyDocumentFile.Load(full);
            var directory=Path.GetDirectoryName(full)??Environment.CurrentDirectory;
            foreach(var component in assembly.Components.Where(c=>!c.Suppressed))
            {
                var child=Path.IsPathRooted(component.SourcePath)
                    ?component.SourcePath
                    :Path.Combine(directory,component.SourcePath);
                foreach(var part in EnumerateBomParts(child,recursion))yield return part;
            }
        }
        finally{recursion.Remove(full);}
    }

    private static (string Name,int Count)[] BomRows(string assemblyPath)
        => EnumerateBomParts(assemblyPath)
            .GroupBy(name=>name,StringComparer.OrdinalIgnoreCase)
            .OrderBy(group=>group.Key,StringComparer.OrdinalIgnoreCase)
            .Select(group=>(group.Key,group.Count()))
            .ToArray();

    private static string ResolveBom(DrawingView view)
    {
        if(!string.Equals(Path.GetExtension(view.ModelPath),".scasm",StringComparison.OrdinalIgnoreCase))return "BOM";
        var rows=BomRows(view.ModelPath);
        return rows.Length==0?"BOM\n(Boş)":"BOM\n"+string.Join("\n",rows.Select((row,i)=>$"{i+1}. {row.Name}   Adet: {row.Count}"));
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
            Point2d pointer=view.Position;
            if(_viewer.ScreenToPlane(_lastMouseX,_lastMouseY,0,0,0,0,0,1,out var world))
                pointer=new Point2d(world.X,world.Y);
            var circles=DrawingProjectedCircles(view);
            if(circles.Length==0){Status("Bu görünüşte eksene dik dairesel kenar bulunamadı.");return;}
            var choice=circles.Select(circle=>
            {
                var center=DrawingToSheet(view,circle.X,circle.Y);
                return (Circle:circle,Center:center,Distance:Point2d.Distance(pointer,center));
            }).OrderBy(x=>x.Distance).First();
            var note=new DrawingAnnotation(Guid.NewGuid(),view.SheetId,view.Id,choice.Center,"MERKEZ",
                DrawingAnnotationKind.CenterMark,EncodeRadiusDimension(choice.Circle));
            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
            RefreshDrawingTree(note.Id);RenderDrawing();Status("İlişkili merkez işareti eklendi ve .scdrw içine kaydedilecek.");
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
            var note=new DrawingAnnotation(Guid.NewGuid(),view.SheetId,view.Id,new Point2d(x+20,y+20),text,
                DrawingAnnotationKind.HoleCallout,"0");
            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
            RefreshDrawingTree(note.Id);RenderDrawing();Status("Delik çağrısı eklendi: "+text);
        }
        catch(Exception ex){Status("Delik çağrısı oluşturulamadı: "+ex.Message);}
    }

    private void DrawingSmartDimension_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var view=SelectedDrawingView();
        if(view is null){Status("Ölçü için FeatureManager'dan görünüş seçin.");return;}
        try
        {
            var data=DrawingProjectionData(view);
            Point2d pointer=view.Position;
            if(_viewer.ScreenToPlane(_lastMouseX,_lastMouseY,0,0,0,0,0,1,out var world))
                pointer=new Point2d(world.X,world.Y);

            var segments=DrawingProjectedSegments(view);
            var circles=DrawingProjectedCircles(view);
            var segmentChoice=segments.Select(segment=>
            {
                var a=DrawingToSheet(view,segment.X1,segment.Y1);
                var b=DrawingToSheet(view,segment.X2,segment.Y2);
                return (Segment:segment,Distance:PointSegmentDistance(pointer,a,b),A:a,B:b);
            }).OrderBy(x=>x.Distance).FirstOrDefault();

            var circleChoice=circles.Select(circle=>
            {
                var center=DrawingToSheet(view,circle.X,circle.Y);
                var displayedRadius=circle.Radius*view.Scale;
                var distance=Math.Abs(Point2d.Distance(pointer,center)-displayedRadius);
                return (Circle:circle,Distance:distance,Center:center);
            }).OrderBy(x=>x.Distance).FirstOrDefault();

            DrawingAnnotation note;
            if(circles.Length>0&&circleChoice.Distance<=segmentChoice.Distance)
            {
                var position=new Point2d(circleChoice.Center.X+circleChoice.Circle.Radius*view.Scale+15,circleChoice.Center.Y+12);
                note=new DrawingAnnotation(Guid.NewGuid(),view.SheetId,view.Id,position,
                    $"Ø{circleChoice.Circle.Radius*2:0.###}",DrawingAnnotationKind.DiameterDimension,
                    EncodeDiameterDimension(circleChoice.Circle));
            }
            else if(segments.Length>0)
            {
                var mid=new Point2d((segmentChoice.A.X+segmentChoice.B.X)*0.5,(segmentChoice.A.Y+segmentChoice.B.Y)*0.5);
                var dx=segmentChoice.B.X-segmentChoice.A.X;var dy=segmentChoice.B.Y-segmentChoice.A.Y;
                var len=Math.Sqrt(dx*dx+dy*dy);var nx=len>1e-9?-dy/len:0;var ny=len>1e-9?dx/len:1;
                var position=new Point2d(mid.X+nx*14,mid.Y+ny*14);
                note=new DrawingAnnotation(Guid.NewGuid(),view.SheetId,view.Id,position,
                    $"{segmentChoice.Segment.Length:0.###} mm",DrawingAnnotationKind.LinearDimension,
                    EncodeLinearDimension(segmentChoice.Segment));
            }
            else throw new InvalidOperationException("Görünüşte ölçülendirilebilir geometri bulunamadı.");

            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
            RefreshDrawingTree(note.Id);RenderDrawing();
            Status(note.Kind==DrawingAnnotationKind.DiameterDimension
                ?"Dairesel geometriye ilişkili çap ölçüsü eklendi."
                :"Projeksiyon kenarına ilişkili doğrusal ölçü eklendi.");
        }
        catch(Exception ex){Status("Ölçü oluşturulamadı: "+ex.Message);}
    }

    private void DrawingRadiusDimension_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var view=SelectedDrawingView();
        if(view is null){Status("Yarıçap ölçüsü için FeatureManager'dan görünüş seçin.");return;}
        try
        {
            Point2d pointer=view.Position;
            if(_viewer.ScreenToPlane(_lastMouseX,_lastMouseY,0,0,0,0,0,1,out var world))
                pointer=new Point2d(world.X,world.Y);
            var circles=DrawingProjectedCircles(view);
            if(circles.Length==0)throw new InvalidOperationException("Görünüşte dairesel geometri yok.");
            var choice=circles.Select(circle=>
            {
                var center=DrawingToSheet(view,circle.X,circle.Y);
                var distance=Math.Abs(Point2d.Distance(pointer,center)-circle.Radius*view.Scale);
                return (Circle:circle,Center:center,Distance:distance);
            }).OrderBy(x=>x.Distance).First();
            var position=new Point2d(choice.Center.X+choice.Circle.Radius*view.Scale+18,choice.Center.Y+14);
            var note=new DrawingAnnotation(Guid.NewGuid(),view.SheetId,view.Id,position,
                $"R{choice.Circle.Radius:0.###}",DrawingAnnotationKind.RadiusDimension,
                EncodeRadiusDimension(choice.Circle));
            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
            RefreshDrawingTree(note.Id);RenderDrawing();Status("İlişkili yarıçap ölçüsü eklendi.");
        }
        catch(Exception ex){Status("Yarıçap ölçüsü oluşturulamadı: "+ex.Message);}
    }

    private void DrawingAngularDimension_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var view=SelectedDrawingView();
        if(view is null){Status("Açı ölçüsü için FeatureManager'dan görünüş seçin.");return;}
        try
        {
            Point2d pointer=view.Position;
            if(_viewer.ScreenToPlane(_lastMouseX,_lastMouseY,0,0,0,0,0,1,out var world))
                pointer=new Point2d(world.X,world.Y);
            var segments=DrawingProjectedSegments(view);
            if(segments.Length<2)throw new InvalidOperationException("Açı için en az iki izdüşüm kenarı gerekiyor.");
            var ranked=segments.Select(segment=>
            {
                var a=DrawingToSheet(view,segment.X1,segment.Y1);
                var b=DrawingToSheet(view,segment.X2,segment.Y2);
                return (Segment:segment,Distance:PointSegmentDistance(pointer,a,b));
            }).OrderBy(x=>x.Distance).ToArray();
            var first=ranked[0].Segment;
            var second=ranked.Skip(1).Select(x=>x.Segment)
                .FirstOrDefault(candidate=>AngularDegrees(first,candidate)>1e-3&&AngularDegrees(first,candidate)<179.999);
            if(second is null)throw new InvalidOperationException("Yakında açılı iki farklı kenar bulunamadı.");
            var firstMid=DrawingToSheet(view,first.Mid.X,first.Mid.Y);
            var secondMid=DrawingToSheet(view,second.Mid.X,second.Mid.Y);
            var position=new Point2d((firstMid.X+secondMid.X)*0.5+16,(firstMid.Y+secondMid.Y)*0.5+16);
            var angle=AngularDegrees(first,second);
            var note=new DrawingAnnotation(Guid.NewGuid(),view.SheetId,view.Id,position,
                $"{angle:0.###}°",DrawingAnnotationKind.AngularDimension,
                EncodeAngularDimension(first,second));
            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
            RefreshDrawingTree(note.Id);RenderDrawing();Status("İlişkili açı ölçüsü eklendi.");
        }
        catch(Exception ex){Status("Açı ölçüsü oluşturulamadı: "+ex.Message);}
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
        var rows=BomRows(view.ModelPath);
        var lines=rows.Select((row,i)=>$"{i+1}. {row.Name}   Adet: {row.Count}").ToArray();
        PropertyTitle.Text="Malzeme Listesi (BOM)";
        PropertyInfo.Text=lines.Length==0?"Montajda görünür bileşen yok.":string.Join("\n",lines);
        ManagerTabs.SelectedItem=PropertyManagerTab;
        if(lines.Length>0)
        {
            var sheet=_drawingDocument.Sheets[0];
            var text="BOM\n"+string.Join("\n",lines);
            var note=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,view.Id,new Point2d(sheet.Width-120,30),text,
                DrawingAnnotationKind.BillOfMaterials);
            SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
            RefreshDrawingTree(note.Id);RenderDrawing();
        }
        Status($"{rows.Length} kalem BOM oluşturuldu; alt montajlar dahil.");
    }

    private void DrawingBalloon_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null)return;
        var view=SelectedDrawingView();
        if(view is null){Status("Balon için bir görünüş seçin.");return;}
        var sheet=_drawingDocument.Sheets[0];
        var number=_drawingDocument.Annotations.Count(a=>a.Text.StartsWith("BALON ",StringComparison.Ordinal))+1;
        var note=new DrawingAnnotation(Guid.NewGuid(),sheet.Id,view.Id,
            new Point2d(view.Position.X+35,view.Position.Y+35),"BALON "+number,
            DrawingAnnotationKind.Balloon,number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetDrawingDocument(_drawingDocument with{Annotations=_drawingDocument.Annotations.Append(note).ToArray()});
        RefreshDrawingTree(note.Id);RenderDrawing();Status("Balon "+number+" eklendi.");
    }

    private static string DxfText(string value)
        => value.Replace("\r"," ").Replace("\n"," ").Replace("\0"," ");

    private IEnumerable<(double X1,double Y1,double X2,double Y2)> DrawingViewDxfLines(DrawingView view)
    {
        if(_drawingDocument is null)yield break;
        var parentType=_drawingDocument.Views.FirstOrDefault(v=>v.Id==view.ParentViewId)?.Type??DrawingViewType.Front;
        var orientationType=view.Type is DrawingViewType.Projected or DrawingViewType.Section or DrawingViewType.Detail or DrawingViewType.Broken
            ?parentType:view.Type;
        var (direction,up)=DrawingOrientation(orientationType);
        var allSegments=new List<double>();
        foreach(var body in DrawingBodies(view.ModelPath))
        {
            if(view.Type==DrawingViewType.Section&&view.SectionOrigin is Vector3d sectionOrigin&&view.SectionNormal is Vector3d sectionNormal)
                allSegments.AddRange(SrcCad.Kernel.Occt.OcctFeatureKernel.SectionEdges(body,
                    new[]{sectionOrigin.X,sectionOrigin.Y,sectionOrigin.Z},
                    new[]{sectionNormal.X,sectionNormal.Y,sectionNormal.Z},
                    new[]{direction.X,direction.Y,direction.Z},
                    new[]{up.X,up.Y,up.Z}));
            else
                allSegments.AddRange(SrcCad.Kernel.Occt.OcctFeatureKernel.ProjectEdges(body,
                    new[]{direction.X,direction.Y,direction.Z},
                    new[]{up.X,up.Y,up.Z}));
        }

        var segments=allSegments.ToArray();
        if(segments.Length<4)yield break;
        var xs=new List<double>();var ys=new List<double>();
        for(var i=0;i+3<segments.Length;i+=4)
        {
            xs.Add(segments[i]);xs.Add(segments[i+2]);
            ys.Add(segments[i+1]);ys.Add(segments[i+3]);
        }

        double MapBrokenX(double x)
        {
            if(view.Type!=DrawingViewType.Broken||view.BreakStart is not double start||view.BreakEnd is not double end)return x;
            return x<=start?x:x>=end?x-(end-start):double.NaN;
        }

        var cx=(xs.Min()+xs.Max())*0.5;
        var cy=(ys.Min()+ys.Max())*0.5;
        for(var i=0;i+3<segments.Length;i+=4)
        {
            var ax=MapBrokenX(segments[i]);var bx=MapBrokenX(segments[i+2]);
            if(!double.IsFinite(ax)||!double.IsFinite(bx))continue;
            yield return (
                (ax-cx)*view.Scale+view.Position.X,
                (segments[i+1]-cy)*view.Scale+view.Position.Y,
                (bx-cx)*view.Scale+view.Position.X,
                (segments[i+3]-cy)*view.Scale+view.Position.Y);
        }
    }

    private IEnumerable<(double X1,double Y1,double X2,double Y2)> DrawingDimensionDxfLines(DrawingAnnotation note)
    {
        if(_drawingDocument is null||note.ViewId is not Guid viewId)yield break;
        var view=_drawingDocument.Views.FirstOrDefault(v=>v.Id==viewId);
        if(view is null)yield break;

        if(note.Kind==DrawingAnnotationKind.LinearDimension&&ResolveLinearDimension(view,note.Data) is ProjectedSegment segment)
        {
            var a=DrawingToSheet(view,segment.X1,segment.Y1);
            var b=DrawingToSheet(view,segment.X2,segment.Y2);
            var dx=b.X-a.X;var dy=b.Y-a.Y;var length=Math.Sqrt(dx*dx+dy*dy);
            if(length<=1e-9)yield break;
            var nx=-dy/length;var ny=dx/length;
            var mid=new Point2d((a.X+b.X)*0.5,(a.Y+b.Y)*0.5);
            var offset=(note.Position.X-mid.X)*nx+(note.Position.Y-mid.Y)*ny;
            if(Math.Abs(offset)<6)offset=offset<0?-12:12;
            var qa=new Point2d(a.X+nx*offset,a.Y+ny*offset);
            var qb=new Point2d(b.X+nx*offset,b.Y+ny*offset);
            yield return(a.X,a.Y,qa.X,qa.Y);
            yield return(b.X,b.Y,qb.X,qb.Y);
            yield return(qa.X,qa.Y,qb.X,qb.Y);
        }
        else if(note.Kind==DrawingAnnotationKind.DiameterDimension&&ResolveDiameterDimension(view,note.Data) is ProjectedCircle circle)
        {
            var center=DrawingToSheet(view,circle.X,circle.Y);
            var radius=circle.Radius*view.Scale;
            var dx=note.Position.X-center.X;var dy=note.Position.Y-center.Y;
            var len=Math.Sqrt(dx*dx+dy*dy);var ux=len>1e-9?dx/len:1.0;var uy=len>1e-9?dy/len:0.0;
            var a=new Point2d(center.X-ux*radius,center.Y-uy*radius);
            var b=new Point2d(center.X+ux*radius,center.Y+uy*radius);
            yield return(a.X,a.Y,b.X,b.Y);
            yield return(b.X,b.Y,note.Position.X,note.Position.Y);
        }
        else if(note.Kind==DrawingAnnotationKind.RadiusDimension&&ResolveRadiusDimension(view,note.Data) is ProjectedCircle radiusCircle)
        {
            var center=DrawingToSheet(view,radiusCircle.X,radiusCircle.Y);
            var radius=radiusCircle.Radius*view.Scale;
            var dx=note.Position.X-center.X;var dy=note.Position.Y-center.Y;
            var len=Math.Sqrt(dx*dx+dy*dy);var ux=len>1e-9?dx/len:1.0;var uy=len>1e-9?dy/len:0.0;
            var rim=new Point2d(center.X+ux*radius,center.Y+uy*radius);
            yield return(center.X,center.Y,rim.X,rim.Y);
            yield return(rim.X,rim.Y,note.Position.X,note.Position.Y);
        }
        else if(note.Kind==DrawingAnnotationKind.AngularDimension&&ResolveAngularDimension(view,note.Data) is { } angular)
        {
            var a1=DrawingToSheet(view,angular.A.X1,angular.A.Y1);
            var a2=DrawingToSheet(view,angular.A.X2,angular.A.Y2);
            var b1=DrawingToSheet(view,angular.B.X1,angular.B.Y1);
            var b2=DrawingToSheet(view,angular.B.X2,angular.B.Y2);
            var am=new Point2d((a1.X+a2.X)*0.5,(a1.Y+a2.Y)*0.5);
            var bm=new Point2d((b1.X+b2.X)*0.5,(b1.Y+b2.Y)*0.5);
            yield return(am.X,am.Y,note.Position.X,note.Position.Y);
            yield return(bm.X,bm.Y,note.Position.X,note.Position.Y);
        }
        else if(note.Kind==DrawingAnnotationKind.CenterMark&&ResolveRadiusDimension(view,note.Data) is ProjectedCircle markCircle)
        {
            var center=DrawingToSheet(view,markCircle.X,markCircle.Y);
            var half=Math.Max(4,markCircle.Radius*view.Scale*0.35);
            yield return(center.X-half,center.Y,center.X+half,center.Y);
            yield return(center.X,center.Y-half,center.X,center.Y+half);
        }
    }

    private void DrawingExportDxf_Click(object sender,RoutedEventArgs e)
    {
        if(_drawingDocument is null||_drawingDocument.Sheets.Length==0)
        {Status("DXF aktarımı için teknik resim oluşturun.");return;}

        var dialog=new SaveFileDialog
        {
            Title="Teknik Resmi DXF Olarak Aktar",
            Filter="AutoCAD DXF (*.dxf)|*.dxf",
            DefaultExt=".dxf",
            AddExtension=true,
            FileName=string.IsNullOrWhiteSpace(_drawingPath)
                ?"SrcCad_Teknik_Resim.dxf"
                :Path.GetFileNameWithoutExtension(_drawingPath)+".dxf"
        };
        if(dialog.ShowDialog(this)!=true)return;

        try
        {
            var sheet=_drawingDocument.Sheets[0];
            var inv=System.Globalization.CultureInfo.InvariantCulture;
            using var writer=new StreamWriter(dialog.FileName,false,new System.Text.UTF8Encoding(false));
            void Pair(int code,string value){writer.WriteLine(code.ToString(inv));writer.WriteLine(value);}
            void Line(double x1,double y1,double x2,double y2,string layer="GEOMETRY")
            {
                Pair(0,"LINE");Pair(8,layer);
                Pair(10,x1.ToString("G17",inv));Pair(20,y1.ToString("G17",inv));Pair(30,"0");
                Pair(11,x2.ToString("G17",inv));Pair(21,y2.ToString("G17",inv));Pair(31,"0");
            }
            void Text(double x,double y,string text,string layer="ANNOTATION")
            {
                Pair(0,"TEXT");Pair(8,layer);
                Pair(10,x.ToString("G17",inv));Pair(20,y.ToString("G17",inv));Pair(30,"0");
                Pair(40,"3.5");Pair(1,DxfText(text));
            }

            Pair(0,"SECTION");Pair(2,"HEADER");
            Pair(9,"$INSUNITS");Pair(70,"4"); // millimetres
            Pair(0,"ENDSEC");
            Pair(0,"SECTION");Pair(2,"ENTITIES");

            Line(0,0,sheet.Width,0,"SHEET");
            Line(sheet.Width,0,sheet.Width,sheet.Height,"SHEET");
            Line(sheet.Width,sheet.Height,0,sheet.Height,"SHEET");
            Line(0,sheet.Height,0,0,"SHEET");

            foreach(var view in _drawingDocument.Views.Where(v=>v.SheetId==sheet.Id))
            {
                foreach(var segment in DrawingViewDxfLines(view))
                    Line(segment.X1,segment.Y1,segment.X2,segment.Y2);
                Text(view.Position.X,view.Position.Y-12,view.Name,"VIEW_LABEL");
            }

            foreach(var note in _drawingDocument.Annotations.Where(a=>a.SheetId==sheet.Id))
            {
                foreach(var dimensionLine in DrawingDimensionDxfLines(note))
                    Line(dimensionLine.X1,dimensionLine.Y1,dimensionLine.X2,dimensionLine.Y2,"DIMENSION");
                if(note.Kind!=DrawingAnnotationKind.CenterMark)
                    Text(note.Position.X,note.Position.Y,ResolveDrawingAnnotationText(note),
                        note.Kind is DrawingAnnotationKind.LinearDimension or DrawingAnnotationKind.DiameterDimension or DrawingAnnotationKind.RadiusDimension or DrawingAnnotationKind.AngularDimension
                            ?"DIMENSION":"ANNOTATION");
            }

            Pair(0,"ENDSEC");Pair(0,"EOF");
            Status("DXF oluşturuldu: "+Path.GetFileName(dialog.FileName));
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,ex.Message,"DXF aktarımı başarısız",MessageBoxButton.OK,MessageBoxImage.Error);
        }
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
