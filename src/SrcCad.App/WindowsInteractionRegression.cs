using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();

    // Opt-in separate process. Tests HWND messages, WPF/WinForms adapters, native
    // projection and live model together; it is not a substitute for manual UX QA.
    private async void RunWindowsInteractionRegression()
    {
        var report=Environment.GetEnvironmentVariable("SRCCAD_UI_REGRESSION_REPORT")!;
        var checks=new List<string>();string? failure=null;
        void Step(string name) => WindowsRegressionJournal.Write(new {Completed=false,Passed=checks.Count,Failed=0,Step=name,Checks=checks,KeyRoute=_lastKeyRoute,HistoryCommand=_lastHistoryCommand,UndoDepth=_sketchUndo.Count,RedoDepth=_sketchRedo.Count});
        void Check(bool ok,string name) {if(!ok)throw new InvalidOperationException(name);checks.Add(name);Step(name);}
        void Key(WinForms.Keys key)
        {
            Step("Key: "+key);
            SetFocus(_viewportPanel.Handle);
            SendMessage(_viewportPanel.Handle,0x100,(IntPtr)(int)key,IntPtr.Zero);
            SendMessage(_viewportPanel.Handle,0x101,(IntPtr)(int)key,IntPtr.Zero);
        }
        async Task Chord(WinForms.Keys key, bool viewport = true)
        {
            Step("Physical Ctrl+" + key);
            var window = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Activate(); SetForegroundWindow(window);
            if (viewport) SetFocus(_viewportPanel.Handle);
            await Task.Delay(50);
            if (GetForegroundWindow() != window || (viewport && GetFocus() != _viewportPanel.Handle))
                throw new InvalidOperationException("Keyboard test requires foreground SrcCad and target focus");
            // The whole chord goes through the OS input queue. Mixing injected Ctrl
            // with SendMessage(Z) bypasses accelerator preprocessing and key state.
            try
            {
                keybd_event(0x11,0,0,UIntPtr.Zero);
                keybd_event((byte)key,0,0,UIntPtr.Zero);
                keybd_event((byte)key,0,2,UIntPtr.Zero);
            }
            finally { keybd_event(0x11,0,2,UIntPtr.Zero); }
            await Task.Delay(150);
            Step("Physical chord dispatched: " + key);
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
            Step($"Mouse: {p} click={click}");
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
        void Drag(Point2d a,Point2d b)
        {
            var p=Pixel(a);var q=Pixel(b);
            var begin=(IntPtr)((p.Y<<16)|(p.X&0xffff));var end=(IntPtr)((q.Y<<16)|(q.X&0xffff));
            Step("Window drag");SetFocus(_viewportPanel.Handle);
            SendMessage(_viewportPanel.Handle,0x201,(IntPtr)1,begin);
            SendMessage(_viewportPanel.Handle,0x200,(IntPtr)1,end);
            SendMessage(_viewportPanel.Handle,0x202,IntPtr.Zero,end);
        }
        async Task EditLabel(int id,string value,bool cancel=false,bool invalid=false)
        {
            bool opened=false;Exception? dialogFailure=null;
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
            timer.Tick+=(_,_)=>
            {
                timer.Stop();
                var dialog=OwnedWindows.OfType<DimensionEditDialog>().SingleOrDefault();
                if(dialog is null)return;
                opened=true;
                try
                {
                    var editor=((System.Windows.Controls.Grid)dialog.Content).Children.OfType<System.Windows.Controls.TextBox>().Single();
                    Check(editor.SelectionLength==editor.Text.Length,"dimension value selected on open");
                    editor.Text=value;
                    var source=System.Windows.PresentationSource.FromVisual(dialog)!;
                    if(cancel)
                        dialog.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
                    else
                    {
                        editor.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});
                        if(invalid)
                        {
                            Check(dialog.IsVisible,"invalid dimension stays open for correction");
                            dialog.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
                        }
                    }
                }
                catch(Exception ex){dialogFailure=ex;if(dialog.IsVisible)dialog.Close();}
            };
            Step("Dimension double click: "+id);timer.Start();
            var pixel=Pixel(_sketch!.Dimension(id)!.LabelPosition);
            SetFocus(_viewportPanel.Handle);
            SendMessage(_viewportPanel.Handle,0x203,(IntPtr)1,(IntPtr)((pixel.Y<<16)|(pixel.X&0xffff)));
            SendMessage(_viewportPanel.Handle,0x202,IntPtr.Zero,(IntPtr)((pixel.Y<<16)|(pixel.X&0xffff)));
            await Task.Delay(160);timer.Stop();
            if(dialogFailure is not null)throw dialogFailure;
            Check(opened,"double click opens shared dimension editor");
        }
        try
        {
            await Task.Delay(300);
            Check(_viewer.IsInitialized(),"native viewport initialized");
            StartSketch_Click(this,new RoutedEventArgs());
            var ids=_sketch!.AddCornerRectangle(new(-60,-40),new(60,40),0.001);
            var center=_sketch.CreatePointExact(new(20,10));var circle=_sketch.AddCircle(center,8);
            var center2=_sketch.CreatePointExact(new(-25,15));_sketch.AddCircle(center2,8);
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
            await Chord(WinForms.Keys.Z);
            Check(_sketch.Entity(ids[0]) is not null,"HWND Ctrl Z restores deletion");
            await Chord(WinForms.Keys.Y);
            Check(_sketch.Entity(ids[0]) is null,"HWND Ctrl Y repeats deletion");
            Mouse(_sketch.Point(center).Position,true);
            keybd_event(0x11,0,0,UIntPtr.Zero);await Task.Delay(50);
            Mouse(_sketch.Point(center2).Position,true);
            Check(_selectedSketchPoints.Count==2,"Ctrl click accumulates circle centers without drag");
            keybd_event(0x11,0,2,UIntPtr.Zero);await Task.Delay(100);
            Check(_viewportContextMenu.Visible,"Ctrl release opens selection context");
            var options=_viewportContextMenu.Items.OfType<WinForms.ToolStripMenuItem>().Where(i=>i.Tag is SketchRelationType).ToArray();
            Check(options.Length==3&&options.Any(i=>Equals(i.Tag,SketchRelationType.Horizontal))&&!options.Any(i=>Equals(i.Tag,SketchRelationType.Tangent)),"center popup contains only valid relation types");
            options.Single(i=>Equals(i.Tag,SketchRelationType.Horizontal)).PerformClick();_viewportContextMenu.Close();
            Check(Math.Abs(_sketch.Point(center).Position.Y-_sketch.Point(center2).Position.Y)<1e-6,"popup Horizontal moves real centers");
            ShowSketchRelations_Click(this,new RoutedEventArgs());
            Check(_viewportContextMenu.Visible,"Relations button opens actual selection actions");
            _viewportContextMenu.Items.OfType<WinForms.ToolStripMenuItem>().Single(i=>i.Text=="Akıllı Ölçü").PerformClick();
            Check(_smartDimensionMode&&_dimensionSession.References.Count==2,"context Smart Dimension preserves both references");
            Key(WinForms.Keys.Escape);
            Key(WinForms.Keys.Space);
            Check(_orientationWindow?.IsVisible==true,"HWND Space opens View Orientation");
            var viewButtons=((System.Windows.Controls.WrapPanel)_orientationWindow!.Content).Children.OfType<System.Windows.Controls.Button>().ToArray();
            Check(viewButtons.Length==8&&viewButtons.All(b=>b.IsEnabled),"all standard views and sketch Normal To available");
            viewButtons.Single(b=>(string)b.Tag=="Back").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(TryGetSketchCursor(100,100,out var backA,out _,false)&&TryGetSketchCursor(200,100,out var backB,out _,false)&&backB.X<backA.X,"Back changes native camera projection");
            Key(WinForms.Keys.Space);
            ((System.Windows.Controls.WrapPanel)_orientationWindow!.Content).Children.OfType<System.Windows.Controls.Button>().Single(b=>(string)b.Tag=="Normal").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(TryGetSketchCursor(100,100,out var frontA,out _,false)&&TryGetSketchCursor(200,100,out var frontB,out _,false)&&frontB.X>frontA.X,"Normal To restores sketch camera projection");
            var trimLine=_sketch.AddLine(_sketch.CreatePointExact(new(-80,-25)),_sketch.CreatePointExact(new(80,-25)));
            RenderSketch();_viewer.FitAll();
            Trim_Click(this,new RoutedEventArgs());
            Mouse(new(0,-25));
            var span=_sketch.PreviewLineTrim(new(0,-25),_viewer.PixelToWorldLength(10));
            Check(span is not null&&Math.Abs(span.Start.X+60)<1e-6&&Math.Abs(span.End.X-60)<1e-6&&_sketch.Entity(trimLine.Id) is not null,"trim hover targets bounded segment without mutation");
            Mouse(new(0,-25),true);
            Check(_sketch.Entity(trimLine.Id) is null,"trim HWND click changes actual geometry");
            Key(WinForms.Keys.Escape);
            await Chord(WinForms.Keys.Z);
            Check(_sketch.Entity(trimLine.Id) is not null,"trim HWND Undo restores original line");
            var crossingLine=_sketch.AddLine(_sketch.CreatePointExact(new(-150,70)),_sketch.CreatePointExact(new(150,70)));
            RenderSketch();_viewer.FitAll();
            Drag(new(-30,60),new(30,80));
            Check(!_selectedSketchEntities.Contains(crossingLine.Id),"left-to-right window excludes partial line");
            Drag(new(30,60),new(-30,80));
            Check(_selectedSketchEntities.Contains(crossingLine.Id),"right-to-left crossing includes intersected line");
            Key(WinForms.Keys.Delete);Check(_sketch.Entity(crossingLine.Id) is null,"window selection Delete removes selected line");
            await Chord(WinForms.Keys.Z);
            Check(_sketch.Entity(crossingLine.Id) is not null,"window selection Delete Undo restores line");
            _sketch=new SketchModel(StandardPlanes.Front);ClearSketchSelection(false);_sketchUndo.Clear();_sketchRedo.Clear();
            var pivot=_sketch.CreatePointExact(new(0,0));var endA=_sketch.CreatePointExact(new(100,0));var endB=_sketch.CreatePointExact(new(70.71067811865,70.71067811865));
            _sketch.SetPointFixed(pivot,true);_sketch.SetPointFixed(endA,true);
            var rayA=_sketch.AddLine(pivot,endA);var rayB=_sketch.AddLine(pivot,endB);
            _sketch.AddRelation(SketchRelationType.Horizontal,rayA.Id);
            var lineDimension=_sketch.AddDimension(SketchDimensionType.LineLength,new(20,85),entityA:rayB.Id);
            var angle=_sketch.AddDimension(SketchDimensionType.Angle,new(60,25),entityA:rayA.Id,entityB:rayB.Id);
            _sketch.SetDimensionValue(angle.Id,45);RenderSketch();_viewer.FitAll();
            var oldPoint=_sketch.Point(endB).Position;var oldRevision=_sketch.Revision;
            await EditLabel(angle.Id,"60");
            Check(Math.Abs(_sketch.Measure(_sketch.Dimension(angle.Id)!)-60)<1e-6&&Point2d.Distance(oldPoint,_sketch.Point(endB).Position)>1,"angle Enter moves actual geometry");
            Check(!_sketch.DefinitionInfo().IsConflicting&&_sketch.Revision>oldRevision,"angle constraints and revision update");
            await Chord(WinForms.Keys.Z);
            Check(Math.Abs(_sketch.Dimension(angle.Id)!.Value-45)<1e-6,"angle Ctrl Z restores 45");
            await Chord(WinForms.Keys.Y);
            Check(Math.Abs(_sketch.Dimension(angle.Id)!.Value-60)<1e-6,"angle Ctrl Y restores 60");
            await EditLabel(angle.Id,"80",cancel:true);
            Check(Math.Abs(_sketch.Dimension(angle.Id)!.Value-60)<1e-6,"angle Esc preserves prior value");
            foreach(var bad in new[]{"","NaN","Infinity","180"})
            {
                await EditLabel(angle.Id,bad,invalid:true);
                Check(Math.Abs(_sketch.Dimension(angle.Id)!.Value-60)<1e-6,"invalid edit preserves geometry: "+bad);
            }
            await EditLabel(angle.Id,"45,5");
            Check(Math.Abs(_sketch.Dimension(angle.Id)!.Value-45.5)<1e-6,"decimal comma angle edit");
            await EditLabel(lineDimension.Id,"120");
            Check(Math.Abs(_sketch.Measure(_sketch.Dimension(lineDimension.Id)!)-120)<1e-6,"linear double click uses same lifecycle");
            var disk=_sketch.AddCircle(_sketch.CreatePointExact(new(-30,-30)),8);
            var diameter=_sketch.AddDimension(SketchDimensionType.CircleDiameter,new(-65,-60),entityA:disk.Id);RenderSketch();
            await EditLabel(diameter.Id,"20");
            Check(Math.Abs(((SketchCircle)_sketch.Entity(disk.Id)!).Radius-10)<1e-6,"diameter double click drives radius");
            foreach(var type in new[]{SketchDimensionType.HorizontalDistance,SketchDimensionType.VerticalDistance,SketchDimensionType.PointDistance,SketchDimensionType.PointLineDistance,SketchDimensionType.ParallelLineDistance})
            {
                _sketch=new SketchModel(StandardPlanes.Front);ClearSketchSelection(false);
                var point=_sketch.CreatePointExact(new(30,40));SketchDimension testDimension;
                if(type is SketchDimensionType.PointLineDistance or SketchDimensionType.ParallelLineDistance)
                {
                    var a=_sketch.CreatePointExact(new(-100,0));var b=_sketch.CreatePointExact(new(100,0));_sketch.SetPointFixed(a,true);_sketch.SetPointFixed(b,true);
                    var carrier=_sketch.AddLine(a,b);var target=_sketch.ReferenceForPoint(point);
                    if(type==SketchDimensionType.ParallelLineDistance)target=_sketch.ReferenceForEntity(_sketch.AddLine(point,_sketch.CreatePointExact(new(80,40))).Id);
                    testDimension=_sketch.AddInferredDimension(_sketch.InferDimension(new[]{target,_sketch.ReferenceForEntity(carrier.Id)},new(60,80))!);
                }
                else testDimension=_sketch.AddDimension(type,new(60,80),pointA:0,pointB:point);
                RenderSketch();_viewer.FitAll();var targetValue=testDimension.Value*1.2;
                await EditLabel(testDimension.Id,targetValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Check(Math.Abs(_sketch.Measure(_sketch.Dimension(testDimension.Id)!)-targetValue)<1e-6,"double-click audit geometry: "+type);
            }

            // Additional deletion coverage follows all original 78 checks.
            foreach (var scenario in new[] { "circle", "multi", "window", "crossing", "construction", "dimension", "relation" })
            {
                _sketch = new SketchModel(StandardPlanes.Front);
                ClearSketchSelection(false); _sketchUndo.Clear(); _sketchRedo.Clear();
                _sketch.AddCornerRectangle(new(-60,-40),new(60,40),0.001);
                var c = _sketch.AddCircle(_sketch.CreatePointExact(new(10,10)),8);
                var line = _sketch.AddLine(_sketch.CreatePointExact(new(-30,-10)),_sketch.CreatePointExact(new(30,-10)),true);
                var dimension = _sketch.AddDimension(SketchDimensionType.CircleDiameter,new(25,25),entityA:c.Id);
                var relation = _sketch.AddRelation(SketchRelationType.Horizontal,line.Id);
                _sketch.SolveRelations(); RenderSketch(); UpdateSketchPropertyPanel(); _viewer.FitAll();
                var before = _sketch.CaptureSnapshot(); var dof = _sketch.DefinitionInfo();
                if (scenario == "window") Drag(new(-40,-20),new(40,30));
                else if (scenario == "multi")
                {
                    Mouse(new(18,10),true);
                    keybd_event(0x11,0,0,UIntPtr.Zero); await Task.Delay(50);
                    Mouse(new(15,-10),true);
                    keybd_event(0x11,0,2,UIntPtr.Zero); await Task.Delay(100);
                    _viewportContextMenu.Close();
                    Check(_selectedSketchEntities.Count==2,"Ctrl multi Delete fixture has two entities");
                }
                else if (scenario == "crossing") Drag(new(5,-15),new(-5,-5));
                else if (scenario == "construction") Mouse(new(15,-10),true);
                else if (scenario == "dimension") Mouse(dimension.LabelPosition,true);
                else if (scenario == "relation")
                {
                    SketchRelationsList.SelectedItem = SketchRelationsList.Items.OfType<System.Windows.Controls.ListBoxItem>().Single(i=>Equals(i.Tag,relation.Id));
                    SketchRelationsList.Focus();
                    // Keyboard routed by WPF from the actual PropertyManager focus.
                    keybd_event((byte)WinForms.Keys.Delete,0,0,UIntPtr.Zero);
                    keybd_event((byte)WinForms.Keys.Delete,0,2,UIntPtr.Zero);
                    await Task.Delay(150);
                }
                else Mouse(new(18,10),true);
                if (scenario != "relation") Key(WinForms.Keys.Delete);
                var deleted = _sketch.CaptureSnapshot();
                Check(!SameSketch(before,deleted) && _sketchUndo.Count==1,"Delete single history entry: "+scenario);
                if (scenario == "circle") Check(_sketch.Dimension(dimension.Id) is null,"Delete dependent dimension cleanup");
                if (scenario == "relation") SketchRelationsList.Focus();
                await Chord(WinForms.Keys.Z,viewport:scenario != "relation");
                Check(SameSketch(before,_sketch.CaptureSnapshot()),"Delete Undo exact semantic snapshot: "+scenario);
                Check(_sketch.DefinitionInfo()==dof && _sketchUndo.Count==0 && _sketchRedo.Count==1,"Delete Undo DOF and one command: "+scenario);
                await Chord(WinForms.Keys.Y,viewport:scenario != "relation");
                Check(SameSketch(deleted,_sketch.CaptureSnapshot()),"Delete Redo exact semantic snapshot: "+scenario);
            }
        }
        catch(Exception ex) {failure=ex.ToString();}
        finally
        {
            keybd_event(0x11,0,2,UIntPtr.Zero);
            WindowsRegressionJournal.Write(new {Completed=true,Scope="Windows HWND interaction; manual visual acceptance separate",Passed=checks.Count,Failed=failure is null?0:1,Checks=checks,Failure=failure,KeyRoute=_lastKeyRoute,HistoryCommand=_lastHistoryCommand,UndoDepth=_sketchUndo.Count,RedoDepth=_sketchRedo.Count});
            Application.Current.Shutdown(failure is null?0:1);
        }
    }
}
