using System;
using System.Drawing;
using System.Linq;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using WinForms=System.Windows.Forms;
namespace SrcCad.App;
public partial class MainWindow
{
    private int FindDimensionAtScreen(int x,int y)
    {
        if(_sketch is null||!TryGetSketchCursor(0,0,out var o,out _,false)||
            !TryGetSketchCursor(100,0,out var a,out _,false)||!TryGetSketchCursor(0,100,out var b,out _,false))return -1;
        var ax=a.X-o.X;var ay=a.Y-o.Y;var bx=b.X-o.X;var by=b.Y-o.Y;var det=ax*by-ay*bx;
        if(Math.Abs(det)<1e-12)return -1;
        int selected=-1;double best=double.MaxValue;
        using var font=new Font("Arial",14,GraphicsUnit.Pixel);
        foreach(var d in _sketch.Dimensions)
        {
            // All current dimension renderers use LabelPosition for text.
            var p=d.LabelPosition;var px=100*((p.X-o.X)*by-(p.Y-o.Y)*bx)/det;
            var py=100*(ax*(p.Y-o.Y)-ay*(p.X-o.X))/det;
            var size=WinForms.TextRenderer.MeasureText(FormatDimension(d),font);
            // OCCT text justification/font can differ by driver. Cover the text
            // extent around its anchor, then prefer the nearest label anchor.
            if(Math.Abs(x-px)>size.Width+5||Math.Abs(y-py)>Math.Max(12,size.Height))continue;
            var distance=(x-px)*(x-px)+(y-py)*(y-py);
            if(distance<best){best=distance;selected=d.Id;}
        }
        return selected;
    }
    private bool ValidateDependentDimension(SketchModel candidate)
    {
        if(_sourceSketchEditBefore?.Depth is not double depth)return true;
        var p=candidate.Plane;
        try
        {
            if(candidate.TryGetExtrusionProfile(out var loop,out var polygonHoles,out var circles))
            {
                var uv=loop.SelectMany(v=>new[]{v.X,v.Y}).ToArray();
                var polygonData=polygonHoles.SelectMany(h=>new[]{(double)h.Count}.Concat(h.SelectMany(v=>new[]{v.X,v.Y}))).ToArray();
                var holes=circles.SelectMany(c=>new[]{candidate.Point(c.CenterPointId).Position.X,candidate.Point(c.CenterPointId).Position.Y,c.Radius}).ToArray();
                var volume=SrcCad.Kernel.Occt.OcctViewportBridge.MeasureExtrudedProfileVolume(uv,polygonData,holes,p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,depth);
                return double.IsFinite(volume)&&volume>1e-12;
            }
            if(candidate.TryGetSingleCircle(out var circle))
            {
                var c=candidate.Point(circle.CenterPointId).Position;
                try{return _viewer.PreviewExtrudeCircle(c.X,c.Y,circle.Radius,p.Origin.X,p.Origin.Y,p.Origin.Z,p.U.X,p.U.Y,p.U.Z,p.V.X,p.V.Y,p.V.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,depth);}
                finally{_viewer.CancelExtrusionPreview();}
            }
            return false;
        }
        catch(InvalidOperationException){return false;}
    }
    private void MarkSourceSketchDirty()
    {
        if(_sketch is null||_sourceSketchEditBefore is null)return;
        var dirty=!SameSketch(_sourceSketchEditBefore.Sketch,_sketch.CaptureSnapshot());
        if(_sketchNode is not null)_sketchNode.Header=dirty?"Çizim1 *":"Çizim1";
        if(_extrudeNode is not null)_extrudeNode.Header=dirty?"Ekstrüzyon1 (yeniden oluşturulacak)":"Ekstrüzyon1";
        // Source sketch edit exit already rebuilds through ApplyFeatureState and
        // commits feature history only after OCCT accepts the resulting B-Rep.
    }
}
