using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SrcCad.App;
public partial class MainWindow
{
    private Window? _orientationWindow;
    private void ShowViewOrientation()
    {
        if(_orientationWindow is not null){_orientationWindow.Activate();return;}
        var panel=new WrapPanel{Margin=new Thickness(12)};
        var window=new Window{Title="Görünüş Yönü",Owner=this,Width=330,Height=245,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel};
        _orientationWindow=window;
        foreach(var item in new[]{("Front","Ön"),("Back","Arka"),("Top","Üst"),("Bottom","Alt"),("Left","Sol"),("Right","Sağ"),("Iso","İzometrik"),("Normal","Çizime Dik")})
        {
            var button=new Button{Content=item.Item2,Tag=item.Item1,Width=92,Height=48,Margin=new Thickness(3),IsEnabled=item.Item1!="Normal"||_sketch is not null};
            button.Click+=(_,_)=>
            {
                ApplyViewOrientation((string)button.Tag);
                window.Close();_viewportPanel.Focus();
            };
            panel.Children.Add(button);
        }
        window.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Escape or Key.Space){window.Close();e.Handled=true;}};
        window.Closed+=(_,_)=>_orientationWindow=null;
        window.Show();
    }
    private void ApplyViewOrientation(string view)
    {
        switch(view)
        {
            case "Front":_viewer.SetFrontView();break;
            case "Back":_viewer.SetNormalView(0,0,-1,0,1,0);break;
            case "Top":_viewer.SetTopView();break;
            case "Bottom":_viewer.SetNormalView(0,-1,0,0,0,1);break;
            case "Left":_viewer.SetNormalView(-1,0,0,0,1,0);break;
            case "Right":_viewer.SetRightView();break;
            case "Iso":_viewer.SetIsoView();break;
            case "Normal" when _sketch is not null:
                var p=_sketch.Plane;
                _viewer.SetNormalView(p.Normal.X,p.Normal.Y,p.Normal.Z,p.V.X,p.V.Y,p.V.Z);break;
        }
        _viewer.Redraw();
    }
}
