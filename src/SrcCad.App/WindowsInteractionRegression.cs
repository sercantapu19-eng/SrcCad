using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using WinForms=System.Windows.Forms;

namespace SrcCad.App;

public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);

    // Opt-in separate process. Tests HWND messages, WPF/WinForms adapters, native
    // projection and live model together; it is not a substitute for manual UX QA.
    private async void RunWindowsInteractionRegression()
    {
        var report=Environment.GetEnvironmentVariable("SRCCAD_UI_REGRESSION_REPORT")!;
        var checks=new List<string>();string? failure=null;
        void Check(bool ok,string name) {if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Key(WinForms.Keys key)
        {
            SetFocus(_viewportPanel.Handle);
            SendMessage(_viewportPanel.Handle,0x100,(IntPtr)(int)key,IntPtr.Zero);
            SendMessage(_viewportPanel.Handle,0x101,(IntPtr)(int)key,IntPtr.Zero);
        }
        (int X,int Y) Pixel(Point2d p)
        {
            if(!TryGetSketchCursor(0,0,out var o,out _,false)||
               !TryGetSketchCursor(100,0,out var x,out _,false)||
               !TryGetSketchCursor(0,100,out var y,out _,false))throw new Exception("Native screen projection unavailable");
            var ax=x.X-o.X;var ay=x.Y-o.Y;var bx=y.X-o.X;var by=y.Y-o.Y;
            var det=ax*by-ay*bx;if(Math.Abs(det)<1e-12)throw new Exception("Projection singular");
            return ((int)Math.Round(100*((p.X-o.X)*by-(p.Y-o.Y)*bx)/det),(int)Math.Round(100*(ax*(p.Y-o.Y)-ay*(p.X-o.X))/det));
        }
        void Mouse(Point2d p,bool click=false)
        {
            var q=Pixel(p);
            if(q.X<0||q.Y<0||q.X>=_viewportPanel.Width||q.Y>=_viewportPanel.Height)throw new Exception("Test pick outside viewport");
            var pos=(IntPtr)((q.Y<<16)|(q.X&0xffff));
            SendMessage(_viewportPanel.Handle,0x200,IntPtr.Zero,pos);
            if(click)
            {
                SetFocus(_viewportPanel.Handle);
                SendMessage(_viewportPanel.Handle,0x201,(IntPtr)1,pos);
                SendMessage(_viewportPanel.Handle,0x202,IntPtr.Zero,pos);
            }
        }
        try
        {
            await Task.Delay(300);
            Check(_viewer.IsInitialized(),"native viewport initialized");
            StartSketch_Click(this,new RoutedEventArgs());
            var ids=_sketch!.AddCornerRectangle(new(-60,-40),new(60,40),0.001);
            var center=_sketch.CreatePointExact(new(20,10));var circle=_sketch.AddCircle(center,8);
            RenderSketch();_viewer.FitAll();await Task.Delay(100);
            ActivateSketchCommand(new SrcCad.Commands.ThreePointCircleCommand(_sketch));
            Mouse(new(30,-40));Check(_hoverSketchEntityId==ids[0],"edge hover before first drawing pick");
            Mouse(new(-60,-40));Check(_pointerTarget?.Reference.Kind==GeometryReferenceKind.Endpoint,"endpoint snap feedback in command");
            Mouse(new(0,-40));Check(_pointerTarget?.Reference.Kind==GeometryReferenceKind.Midpoint,"midpoint snap feedback in command");
            Mouse(new(20,10));Check(_pointerTarget?.Reference.Kind==GeometryReferenceKind.CenterPoint,"center snap feedback in command");
            Key(WinForms.Keys.Escape);Check(_sketchCommand is null,"HWND Escape leaves drawing mode");
            Mouse(new(30,-40),true);Check(_selectedSketchEntities.Contains(ids[0]),"left click selects line");
            Mouse(new(20,10),true);Check(_selectedSketchPoints.Contains(center)&&_selectedSketchEntities.Count==0,"center selection replaces line");
            Mouse(new(85,65),true);Check(_selectedSketchPoints.Count==0&&_selectedSketchEntities.Count==0,"blank click clears selection");
            Mouse(new(30,-40),true);Key(WinForms.Keys.Delete);
            Check(_sketch.Entity(ids[0]) is null,"HWND Delete removes selected geometry");
            keybd_event(0x11,0,0,UIntPtr.Zero);await Task.Delay(50);Key(WinForms.Keys.Z);
            keybd_event(0x11,0,2,UIntPtr.Zero);await Task.Delay(50);
            Check(_sketch.Entity(ids[0]) is not null,"HWND Ctrl Z restores deletion");
            keybd_event(0x11,0,0,UIntPtr.Zero);await Task.Delay(50);Key(WinForms.Keys.Y);
            keybd_event(0x11,0,2,UIntPtr.Zero);await Task.Delay(50);
            Check(_sketch.Entity(ids[0]) is null,"HWND Ctrl Y repeats deletion");
        }
        catch(Exception ex) {failure=ex.ToString();}
        finally
        {
            keybd_event(0x11,0,2,UIntPtr.Zero);
            File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new {Scope="Windows HWND interaction; manual visual acceptance separate",Passed=checks.Count,Failed=failure is null?0:1,Checks=checks,Failure=failure},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown(failure is null?0:1);
        }
    }
}
