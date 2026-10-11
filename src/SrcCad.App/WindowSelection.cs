using System;
using System.Collections.Generic;
using System.Drawing;
using SrcCad.Core.Geometry;
using WinForms=System.Windows.Forms;
namespace SrcCad.App;
public partial class MainWindow
{
    private Point? _boxStart;
    private Rectangle? _boxFrame;
    private bool _boxAdditive;
    private readonly HashSet<int> _selectedBoxDimensions=new();
    private void BeginWindowSelection(int x,int y,bool additive)
    {_boxStart=new Point(x,y);_boxAdditive=additive;_viewportPanel.Capture=true;}
    private void EraseWindowFrame()
    {
        if(_boxFrame is Rectangle r)WinForms.ControlPaint.DrawReversibleFrame(r,Color.Black,WinForms.FrameStyle.Dashed);
        _boxFrame=null;
    }
    private void UpdateWindowSelection(int x,int y)
    {
        if(_boxStart is not Point start)return;
        EraseWindowFrame();
        if(Math.Abs(x-start.X)<4||Math.Abs(y-start.Y)<4)return;
        var a=_viewportPanel.PointToScreen(start);var b=_viewportPanel.PointToScreen(new Point(x,y));
        _boxFrame=Rectangle.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));
        WinForms.ControlPaint.DrawReversibleFrame(_boxFrame.Value,Color.Black,WinForms.FrameStyle.Dashed);
        Status(x>=start.X?"Pencere seçimi: tamamen içerideki öğeler":"Kesişen seçim: pencereye değen öğeler");
    }
    private void FinishWindowSelection(int x,int y,bool cancel=false)
    {
        var start=_boxStart;var dragged=_boxFrame is not null;
        EraseWindowFrame();_boxStart=null;_viewportPanel.Capture=false;
        if(cancel||!dragged||start is null||_sketch is null)return;
        var p=start.Value;
        var points=new List<Point2d>();
        foreach(var q in new[]{p,new Point(x,p.Y),new Point(x,y),new Point(p.X,y)})
        {
            if(!TryGetSketchCursor(q.X,q.Y,out var uv,out _,false))return;
            points.Add(uv);
        }
        var selection=_sketch.SelectWindow(points,x<p.X);
        if(!_boxAdditive)ClearSketchSelection(false);
        foreach(var id in selection.Entities)_selectedSketchEntities.Add(id);
        foreach(var id in selection.Points)_selectedSketchPoints.Add(id);
        foreach(var id in selection.Dimensions)_selectedBoxDimensions.Add(id);
        RenderSketch();UpdateSketchPropertyPanel();
    }
}
