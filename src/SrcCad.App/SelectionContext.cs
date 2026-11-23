using System;
using System.Linq;
using SrcCad.Core.Sketch;
using WinForms=System.Windows.Forms;

namespace SrcCad.App;
public partial class MainWindow
{
    private GeometryReference[] CurrentSketchReferences()
        => _sketch is null ? Array.Empty<GeometryReference>() :
            _selectedSketchPoints.OrderBy(x=>x).Select(_sketch.ReferenceForPoint)
                .Concat(_selectedSketchEntities.OrderBy(x=>x).Select(_sketch.ReferenceForEntity)).ToArray();
    private void ShowPendingSelectionContext()
    {
        if(!_selectionContextPending)return;
        _selectionContextPending=false;
        if(_inSketch&&_sketchCommand is null&&!_smartDimensionMode)ShowSelectionContext();
    }
    private void ShowSelectionContext()
    {
        _viewportContextMenu.Items.Clear();
        PopulateSelectionActions();
        if(_viewportContextMenu.Items.Count==0){Status("Bu seçim için uygulanabilir ilişki veya ölçü yok.");return;}
        var p=_viewportPanel.PointToClient(WinForms.Cursor.Position);
        _viewportContextMenu.Show(_viewportPanel,Math.Clamp(p.X,0,Math.Max(0,_viewportPanel.Width-160)),Math.Clamp(p.Y,0,Math.Max(0,_viewportPanel.Height-120)));
    }
    private void PopulateSelectionActions()
    {
        var refs=CurrentSketchReferences();
        foreach(var relation in SketchSelectionActions.Relations(refs))
            AddContextRelationItem(relation switch
            {
                SketchRelationType.Horizontal=>"Yatay",SketchRelationType.Vertical=>"Dikey",
                SketchRelationType.Coincident=>"Çakışık",SketchRelationType.Parallel=>"Paralel",
                SketchRelationType.Perpendicular=>"Dik",SketchRelationType.Equal=>"Eşit",
                SketchRelationType.Collinear=>"Eşdoğrusal",SketchRelationType.Concentric=>"Eşmerkezli",
                SketchRelationType.Tangent=>"Teğet",SketchRelationType.Midpoint=>"Orta nokta",
                SketchRelationType.Fixed=>"Sabitle",_=>relation.ToString()
            },relation);
        if(SketchSelectionActions.CanDimension(refs))
        {
            if(_viewportContextMenu.Items.Count>0)_viewportContextMenu.Items.Add(new WinForms.ToolStripSeparator());
            var item=new WinForms.ToolStripMenuItem("Akıllı Ölçü");
            item.Click+=(_,_)=>StartSmartDimensionMode();
            _viewportContextMenu.Items.Add(item);
        }
    }
}
