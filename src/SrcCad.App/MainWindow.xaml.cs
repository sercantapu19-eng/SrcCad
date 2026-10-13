using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SrcCad.Commands;
using SrcCad.Core.Geometry;
using SrcCad.Core.History;
using SrcCad.Core.Sketch;
using SrcCad.Core.Persistence;
using SrcCad.Kernel.Occt;
using WinForms = System.Windows.Forms;

namespace SrcCad.App;

public partial class MainWindow : Window
{
    private readonly OcctViewportBridge _viewer = new();
    private readonly CadViewportPanel _viewportPanel;
    private readonly CadCommandManager _commandManager = new();

    private PlaneFrame _selectedPlane = StandardPlanes.Front;
    private SketchModel? _sketch;
    private ISketchCommand? _sketchCommand;
    private CommandContext? _commandContext;
    private bool _inSketch;
    private bool _extrudeCommandActive;
    private bool _hasSolid;
    private double _extrudeDepth = 20.0;
    private ExtrusionState? _sourceSketchEditBefore;
    private bool _editingExtrude;
    private readonly ExtrusionDepthWheel _depthWheel = new();
    private readonly TransactionalHistory<ExtrusionState> _featureHistory = new();

    private bool ControlPressed => Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
        (WinForms.Control.ModifierKeys & WinForms.Keys.Control) != 0;
    private bool _middleDown;
    private bool _panMode;
    private int _lastMouseX;
    private int _lastMouseY;

    private TreeViewItem? _sketchNode;
    private TreeViewItem? _extrudeNode;

    private readonly HashSet<int> _selectedSketchEntities = new();
    private readonly HashSet<int> _selectedSketchPoints = new();
    private int _selectedSketchDimensionId = -1;
    private int _hoverSketchEntityId = -1;
    private int _hoverSketchPointId = -1;
    private int _hoverSketchDimensionId = -1;
    private SketchPointerTarget? _pointerTarget;
    private int _dragSketchPointId = -1;
    private int _dragSketchDimensionId = -1;
    private SketchSnapshot? _dragSketchSnapshot;
    private bool _dragSketchChanged;

    private bool _selectionContextPending;
    private bool _smartDimensionMode;
    private readonly SmartDimensionSession _dimensionSession = new();
    private bool _smartDimensionWaitingPlacement => _dimensionSession.CanPlace;

    private readonly Stack<SketchSnapshot> _sketchUndo = new();
    private readonly Stack<SketchSnapshot> _sketchRedo = new();

    private readonly WinForms.ContextMenuStrip _viewportContextMenu = new();

    public MainWindow()
    {
        InitializeComponent();

        _viewportPanel = new CadViewportPanel
        {
            Dock = WinForms.DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(233, 241, 247),
            TabStop = true
        };
        ViewportHost.Child = _viewportPanel;

        _viewportPanel.HandleCreated += ViewportPanel_HandleCreated;
        _viewportPanel.Resize += (_, _) => _viewer.Resize();
        _viewportPanel.MouseEnter += (_, _) => _viewportPanel.Focus();
        _viewportPanel.MouseDown += ViewportPanel_MouseDown;
        _viewportPanel.MouseUp += ViewportPanel_MouseUp;
        _viewportPanel.MouseMove += ViewportPanel_MouseMove;
        _viewportPanel.MouseWheel += ViewportPanel_MouseWheel;
        _viewportPanel.MouseLeave += (_,_) =>
        {
            _pointerTarget=null;
            _hoverSketchDimensionId=_hoverSketchPointId=_hoverSketchEntityId=-1;
            if(_inSketch)RenderSketch();
        };
        // Preserve the native modifier snapshot. Do not manufacture WPF events or
        // query WPF keyboard state while the WinForms HWND owns the input.
        _viewportPanel.CommandKey = (keyData) => HandleWindowKey(
            KeyInterop.KeyFromVirtualKey((int)(keyData & WinForms.Keys.KeyCode)),
            (keyData & WinForms.Keys.Control) != 0, nativeViewport: true);
        _viewportPanel.KeyDown += (_, e) =>
        {
            if (HandleWindowKey(KeyInterop.KeyFromVirtualKey((int)e.KeyCode), e.Control, nativeViewport: true))
            { e.Handled = true; e.SuppressKeyPress = true; }
        };

        _viewportPanel.KeyUp += (_,e) => {if(e.KeyCode is WinForms.Keys.ControlKey or WinForms.Keys.LControlKey or WinForms.Keys.RControlKey) ShowPendingSelectionContext();};
        PreviewKeyUp += (_,e) => {if(e.Key is Key.LeftCtrl or Key.RightCtrl) ShowPendingSelectionContext();};
        BuildViewportContextMenu();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SketchTab.IsEnabled = false;
        FrontPlaneNode.IsSelected = true;
        RefreshCommandState();
        Status("Ön Düzlem seçili. Çizim komutuyla başlayabilirsiniz.");
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SRCCAD_UI_REGRESSION_REPORT")))
            Dispatcher.BeginInvoke(new Action(RunWindowsInteractionRegression));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _commandManager.Cancel();
        _viewer.Dispose();
    }

    private void ViewportPanel_HandleCreated(object? sender, EventArgs e)
    {
        if (_viewer.IsInitialized()) return;
        WindowsRegressionJournal.Write(new {Completed=false,Passed=0,Failed=0,Step="OCCT viewport initialization"});
        var ok = _viewer.Init(_viewportPanel.Handle);
        if (!ok)
        {
            Status("OCCT viewport başlatılamadı. COLLECT_DIAGNOSTICS.bat çalıştırın.");
            if(WindowsRegressionJournal.Active)throw new InvalidOperationException("OCCT viewport initialization failed");
            MessageBox.Show("OCCT 3B görüntüleyici başlatılamadı.", "SrcCad", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _viewer.SetOrthographic(true);
        _viewer.SetIsoView();
        _viewer.SetSelectionMode(CadSelectionMode.Face);
        Status("OCCT 3B viewport hazır • Orta tuş: döndür • Ctrl+Orta: kaydır • Wheel: zoom");
    }

    private string _lastKeyRoute = "No keyboard input";
    private string _lastHistoryCommand = "None";
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        => e.Handled = HandleWindowKey(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Control), nativeViewport: false);

    private bool HandleWindowKey(Key key, bool control, bool nativeViewport)
    {
        _lastKeyRoute = $"{(nativeViewport ? "HWND" : "WPF")} {key} Ctrl={control} undo={_sketchUndo.Count} redo={_sketchRedo.Count}";
        // A value editor owns Delete, F and text Undo/Redo while it has focus.
        if (!nativeViewport && !_viewportPanel.ContainsFocus && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase && key != Key.Escape)
            return false;
        if (_inSketch && control && key == Key.Z)
        {
            UndoSketch();
            return true;
        }

        if (_inSketch && control && key == Key.Y)
        {
            RedoSketch();
            return true;
        }

        if (!_inSketch && !_extrudeCommandActive &&
            control && key == Key.Z)
        {
            UndoFeature();
            return true;
        }

        if (!_inSketch && !_extrudeCommandActive &&
            control && key == Key.Y)
        {
            RedoFeature();
            return true;
        }

        if (control && key is Key.S or Key.O)
        {
            if(InDrawingMode)
            {
                if(key==Key.S)SaveDrawing_Click(this,new RoutedEventArgs());
                else OpenDrawing_Click(this,new RoutedEventArgs());
            }
            else if(InAssemblyMode)
            {
                if(key==Key.S)SaveAssembly_Click(this,new RoutedEventArgs());
                else OpenAssembly_Click(this,new RoutedEventArgs());
            }
            else
            {
                if (key == Key.S) SavePart_Click(this,new RoutedEventArgs());
                else OpenPart_Click(this,new RoutedEventArgs());
            }
            return true;
        }

        if (control && key == Key.N)
        {
            NewPart_Click(this, new RoutedEventArgs());
            return true;
        }

        if (key == Key.Space)
        {
            ShowViewOrientation();return true;
        }
        if (control && key is Key.D8 or Key.NumPad8)
        {
            NormalToSelectedSupport();
            return true;
        }
        if (key == Key.F)
        {
            _viewer.FitAll();
            return true;
        }

        if(key==Key.Delete&&!_inSketch&&!_extrudeCommandActive&&_partSession is not null&&FeatureTree.IsKeyboardFocusWithin&&SelectedGraphId is Guid graphId)
        {DeleteGraphFeature(graphId);return true;}
        if (key == Key.Delete && !_inSketch && !_extrudeCommandActive &&
            FeatureTree.IsKeyboardFocusWithin && FeatureTree.SelectedItem == _extrudeNode && _hasSolid)
        {
            DeleteExtrusionFeature();
            return true;
        }

        if (key == Key.Delete && _inSketch)
        {
            if (!_viewportPanel.ContainsFocus && SketchRelationsList.IsKeyboardFocusWithin)
                DeleteRelation_Click(this,new RoutedEventArgs());
            else if (!_viewportPanel.ContainsFocus && SketchDimensionsList.IsKeyboardFocusWithin && SketchDimensionsList.SelectedItem is ListBoxItem selected && selected.Tag is int id)
            {
                CommitSketchDeletion(dimensions: new[] { id });
            }
            else DeleteSketchSelection();
            return true;
        }

        if (key == Key.Escape)
        {
            if(_boxStart is not null){FinishWindowSelection(0,0,true);return true;}
            if (_extrudeCommandActive)
            {
                CancelExtrude();
            }
            else if (_inSketch && _smartDimensionMode)
            {
                _smartDimensionMode = false;
                ResetSmartDimensionTargets();
                ClearSketchSelection();
                Status("Akıllı Ölçülendirme kapatıldı • seçim modu.");
            }
            else if (_inSketch && _sketchCommand is null && (_sourceSketchEditBefore is not null || _graphSketchEdit is not null))
            {
                CancelSourceSketchEdit_Click(this, new RoutedEventArgs());
            }
            else if (_inSketch)
            {
                LeaveSketchCommand();
            }
            return true;
        }
        return false;
    }

    private void ViewportPanel_MouseDown(object? sender, WinForms.MouseEventArgs e)
    {
        _viewportPanel.Focus();
        _lastMouseX = e.X;
        _lastMouseY = e.Y;

        if (e.Button == WinForms.MouseButtons.Middle)
        {
            _middleDown = true;
            _panMode = (WinForms.Control.ModifierKeys & WinForms.Keys.Control) == WinForms.Keys.Control;
            if (!_panMode) _viewer.StartRotation(e.X, e.Y);
            return;
        }

        if (e.Button == WinForms.MouseButtons.Right)
        {
            if (_inSketch && _sketchCommand is not null)
            {
                _sketchCommand.CancelStep();
                _viewer.ClearPreview();
                Status($"{_sketchCommand.Title}: mevcut adım iptal edildi; araç aktif.");
                return;
            }

            if (_inSketch)
                SelectSketchAt(e.X, e.Y, false, preferExistingSelection: true);
            else
            {
                _viewer.MoveTo(e.X, e.Y);
                if (_viewer.SelectionSummary() == "Seçim yok")
                    _viewer.SelectDetected(false);
            }

            ShowViewportContextMenu(e.X, e.Y);
            return;
        }

        if (e.Button != WinForms.MouseButtons.Left) return;

        if (_inSketch)
        {
            var dimensionHit=FindDimensionAtScreen(e.X,e.Y);
            if(e.Clicks>=2&&dimensionHit>=0)
            {
                LeaveSketchCommand();_smartDimensionMode=false;ResetSmartDimensionTargets();
                _selectedSketchDimensionId=dimensionHit;
                EditDimension(dimensionHit);return;
            }
            if (_sketchCommand is not null)
                HandleSketchClick(e.X, e.Y, e.Clicks);
            else
                HandleSketchSelectionOrDimensionClick(e.X, e.Y, e.Clicks);
        }
        else
        {
            _viewer.MoveTo(e.X, e.Y);
            var toggle = (WinForms.Control.ModifierKeys & WinForms.Keys.Control) == WinForms.Keys.Control;
            var selected = _viewer.SelectDetected(toggle);
            UpdateActiveEdgeFeatureSelection();
            UpdateActiveHoleSelection();
            UpdateActiveShellSelection();
            UpdateActiveDraftSelection();
            UpdateActiveEdgeFlangeSelection();
            UpdateActiveEndCapSelection();
            Status(selected ? _viewer.SelectionSummary() : "Seçim yok");
            if (selected && e.Clicks >= 2 && !toggle)
                NormalToSelectedSupport();
        }
    }

    private void ViewportPanel_MouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Middle)
        {
            _middleDown = false;
            _panMode = false;
        }

        if (e.Button == WinForms.MouseButtons.Left)
        {
            if(_boxStart is not null){FinishWindowSelection(e.X,e.Y);return;}
            if (_dragSketchChanged && _dragSketchSnapshot is not null)
            {
                _sketchUndo.Push(_dragSketchSnapshot);
                _sketchRedo.Clear();
            }

            _dragSketchPointId = -1;
            _dragSketchDimensionId = -1;
            _dragSketchSnapshot = null;
            _dragSketchChanged = false;
        }
    }

    private void ViewportPanel_MouseMove(object? sender, WinForms.MouseEventArgs e)
    {
        if(_boxStart is not null){UpdateWindowSelection(e.X,e.Y);return;}
        if (_middleDown)
        {
            if (_panMode)
            {
                var dx = e.X - _lastMouseX;
                var dy = e.Y - _lastMouseY;
                _viewer.Pan(dx, -dy);
                _lastMouseX = e.X;
                _lastMouseY = e.Y;
            }
            else
            {
                _viewer.Rotate(e.X, e.Y);
            }
            return;
        }

        if (_inSketch && _sketchCommand is not null)
        {
            UpdateSketchHover(e.X,e.Y);
            UpdateSketchCommandPreview(e.X,e.Y);
            return;
        }

        if (_inSketch &&
            _sketchCommand is null &&
            !_smartDimensionMode &&
            _dragSketchDimensionId >= 0 &&
            e.Button == WinForms.MouseButtons.Left &&
            _sketch is not null &&
            TryGetSketchCursor(e.X, e.Y, out var dimensionLabelUv, out _, snap: false))
        {
            _sketch.SetDimensionLabel(_dragSketchDimensionId, dimensionLabelUv);
            _dragSketchChanged = true;
            RenderSketch();
            Status($"Ölçü{_dragSketchDimensionId} konumu taşınıyor • sürücü değeri değişmedi.");
            return;
        }

        if (_inSketch &&
            _sketchCommand is null &&
            !_smartDimensionMode &&
            _dragSketchPointId > 0 &&
            e.Button == WinForms.MouseButtons.Left &&
            _sketch is not null &&
            TryGetSketchCursor(e.X, e.Y, out var dragUv, out _, snap: false))
        {
            if (!_sketch.Point(_dragSketchPointId).Fixed)
            {
                if (!_sketch.TryMovePoint(_dragSketchPointId, dragUv))
                {
                    Status("Nokta bu konuma taşınamadı; ilişkiler korunarak işlem geri alındı.");
                    return;
                }
                _dragSketchChanged = true;
                RenderSketch();
                Status($"P{_dragSketchPointId} sürükleniyor • ilişkiler ve sürücü ölçüler korunuyor.");
            }
            return;
        }

        if (_inSketch && _smartDimensionMode && _smartDimensionWaitingPlacement &&
            _sketchCommand is null &&
            TryGetSketchCursor(e.X, e.Y, out var dimensionUv, out _, snap: false))
        {
            UpdateSketchHover(e.X,e.Y);
            RenderSketch();
            var previewDimension = BuildPendingSmartDimension(dimensionUv);
            if (previewDimension is not null)
            {
                RenderDimension(previewDimension);
                _viewer.Redraw();
            }
            return;
        }

        if (_inSketch && _sketchCommand is null)
        {
            UpdateSketchHover(e.X, e.Y);
            return;
        }

        if (!_inSketch)
            _viewer.MoveTo(e.X, e.Y); // OCCT dynamic pre-highlight.
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_extrudeCommandActive) return;
        AdjustExtrusionDepth(e.Delta);
        e.Handled = true;
    }

    private void AdjustExtrusionDepth(int delta)
    {
        if(_graphFeatureEdit is not null)
        {
            if(_graphFeatureEdit.Parameters is FilletFeatureParameters or ChamferFeatureParameters or HoleFeatureParameters or RevolveFeatureParameters or RevolveCutFeatureParameters or SweepFeatureParameters or SweepCutFeatureParameters or LoftFeatureParameters or LoftCutFeatureParameters or ShellFeatureParameters or DraftFeatureParameters or RibFeatureParameters or MirrorFeatureParameters or LinearPatternFeatureParameters or CircularPatternFeatureParameters or ReferencePlaneFeatureParameters or ReferenceAxisFeatureParameters or ReferencePointFeatureParameters or SheetMetalBaseFeatureParameters or SheetMetalEdgeFlangeFeatureParameters)return;
            if(DimensionNumbers.TryParseScalar(GraphDepth.Text,out var value))
                GraphDepth.Text=_depthWheel.Apply(value,delta).ToString("G17",CultureInfo.CurrentCulture);
            return;
        }
        // Keep invalid/zero input visible; never silently substitute a different solid.
        var text = ExtrudeDepthTextBox.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth) ||
            !double.IsFinite(depth)) return;
        depth = _depthWheel.Apply(depth, delta);
        ExtrudeDepthTextBox.Text = depth.ToString("0.###", CultureInfo.CurrentCulture);
        ExtrudeDepthTextBox.CaretIndex = ExtrudeDepthTextBox.Text.Length;
        // TextChanged rebuilds the real OCCT candidate; zero invalidates the preview.
        Status($"Ekstrüzyon derinliği: {depth:0.###} mm • mouse tekeri ±10 mm");
    }

    private void ViewportPanel_MouseWheel(object? sender, WinForms.MouseEventArgs e)
    {
        if (_extrudeCommandActive)
        {
            AdjustExtrusionDepth(e.Delta);
            return;
        }
        _viewer.ZoomWheel(e.Delta);
    }

    private bool TryGetSketchCursor(int x, int y, out Point2d uv, out int snappedPointId, bool snap = true)
    {
        uv = default;
        snappedPointId = -1;
        if (_sketch is null || !_viewer.IsInitialized()) return false;

        var p = _sketch.Plane;
        if (!_viewer.ScreenToPlane(x, y,
                p.Origin.X, p.Origin.Y, p.Origin.Z,
                p.Normal.X, p.Normal.Y, p.Normal.Z,
                out var world))
            return false;

        uv = p.WorldToSketch(new Vector3d(world.X, world.Y, world.Z));
        if (snap)
        {
            var tolerance = Math.Max(1e-6, _viewer.PixelToWorldLength(10));
            var target = SketchPointerProbe.Probe(_sketch,uv,tolerance);
            if (target?.Reference.IsPointLike == true)
            {
                snappedPointId = target.PointId;
                uv = target.Position;
            }
        }
        return true;
    }

    private void UpdateSketchHover(int x, int y)
    {
        if (_sketch is null || !TryGetSketchCursor(x, y, out var uv, out _, snap:false)) return;
        var tolerance = Math.Max(1e-6, _viewer.PixelToWorldLength(10));

        var dimensionId = _sketchCommand is null ? FindDimensionAtScreen(x,y) : -1;
        var target = dimensionId < 0 ? SketchPointerProbe.Probe(_sketch,uv,tolerance) : null;
        var reference = target?.Reference;
        var markerChanged = target != _pointerTarget;
        _pointerTarget = target;
        var pointId = reference is { IsPointLike: true } ? _sketch.ReferencePointId(reference) : -1;
        var entityId = reference is { IsPointLike: false } ? reference.Id : -1;

        if (dimensionId == _hoverSketchDimensionId &&
            pointId == _hoverSketchPointId &&
            entityId == _hoverSketchEntityId && !markerChanged)
            return;

        _hoverSketchDimensionId = dimensionId;
        _hoverSketchPointId = pointId;
        _hoverSketchEntityId = reference?.Kind == GeometryReferenceKind.Midpoint ? reference.Id : entityId;
        RenderSketch();
    }

    private bool SelectSketchAt(int x, int y, bool toggle, bool preferExistingSelection = false)
    {
        if (_sketch is null || !TryGetSketchCursor(x, y, out var uv, out _)) return false;
        var tolerance = Math.Max(1e-6, _viewer.PixelToWorldLength(10));

        var dimensionId = FindDimensionAtScreen(x,y);
        var reference = dimensionId < 0 ? _sketch.HitReference(uv, tolerance) : null;
        var pointId = reference is { IsPointLike: true } ? _sketch.ReferencePointId(reference) : -1;
        var entityId = reference is { IsPointLike: false } ? reference.Id : -1;

        var alreadySelected =
            dimensionId >= 0 && _selectedSketchDimensionId == dimensionId ||
            pointId >= 0 && _selectedSketchPoints.Contains(pointId) ||
            entityId >= 0 && _selectedSketchEntities.Contains(entityId);

        if (preferExistingSelection && alreadySelected)
            return true;

        if (!toggle)
            ClearSketchSelection(false);

        if (dimensionId >= 0)
        {
            _selectedSketchDimensionId = toggle && _selectedSketchDimensionId == dimensionId ? -1 : dimensionId;
        }
        else if (pointId >= 0)
        {
            if (toggle && _selectedSketchPoints.Contains(pointId))
                _selectedSketchPoints.Remove(pointId);
            else
                _selectedSketchPoints.Add(pointId);
        }
        else if (entityId >= 0)
        {
            if (toggle && _selectedSketchEntities.Contains(entityId))
                _selectedSketchEntities.Remove(entityId);
            else
                _selectedSketchEntities.Add(entityId);
        }
        else
        {
            if (!toggle)
                ClearSketchSelection(false);
            RenderSketch();
            UpdateSketchPropertyPanel();
            return false;
        }

        RenderSketch();
        UpdateSketchPropertyPanel();
        return true;
    }

    private void HandleSketchSelectionOrDimensionClick(int x, int y, int clickCount)
    {
        if (_sketch is null || !TryGetSketchCursor(x, y, out var uv, out _)) return;
        var tolerance = Math.Max(1e-6, _viewer.PixelToWorldLength(10));

        var dimensionId = FindDimensionAtScreen(x,y);
        if (clickCount >= 2 && dimensionId >= 0)
        {
            EditDimension(dimensionId);
            return;
        }

        if (_smartDimensionMode)
        {
            HandleSmartDimensionPick(uv, tolerance);
            return;
        }

        var toggle = (WinForms.Control.ModifierKeys & WinForms.Keys.Control) == WinForms.Keys.Control;
        if(!SelectSketchAt(x, y, toggle))
        {
            BeginWindowSelection(x,y,toggle);return;
        }
        _selectionContextPending = toggle && CurrentSketchReferences().Length >= 2;

        // Ölçü metni tek tıkla seçilip SOLIDWORKS'teki gibi sürüklenebilir.
        // Değer yalnız çift tıklamada düzenlenir; sürükleme sadece label konumunu değiştirir.
        if (dimensionId >= 0)
        {
            _dragSketchDimensionId = dimensionId;
            _dragSketchPointId = -1;
            _dragSketchSnapshot = _sketch.CaptureSnapshot();
            _dragSketchChanged = false;
            return;
        }

        var pointId = _sketch.FindPointNear(uv, tolerance);
        if (!toggle && pointId > 0 && !_sketch.Point(pointId).Fixed)
        {
            _dragSketchPointId = pointId;
            _dragSketchDimensionId = -1;
            _dragSketchSnapshot = _sketch.CaptureSnapshot();
            _dragSketchChanged = false;
        }
    }

    private void HandleSmartDimensionPick(Point2d uv, double tolerance)
    {
        if (_sketch is null) return;
        if (_dimensionSession.Pick(_sketch, uv, tolerance)) CreateSmartDimensionAt(uv);
        else
        {
            Status(_dimensionSession.CanPlace ? "Ölçüyü üst/alta veya sağ/sola taşıyın; konumu tıklayın." : "Nokta, merkez, Origin veya kenar seçin.");
            RenderSketch();
        }
    }

    private SketchDimension? BuildPendingSmartDimension(Point2d label)
        => _sketch is null ? null : _dimensionSession.Preview(_sketch, label);

    private void CreateSmartDimensionAt(Point2d label)
    {
        if (_sketch is null) return;
        var candidate = _dimensionSession.Preview(_sketch, label);
        if (candidate is null) { Status("Bu referanslar arasında ölçü oluşturulamadı."); return; }
        var before = _sketch.CaptureSnapshot();
        var dimension = _sketch.AddInferredDimension(candidate);
        ResetSmartDimensionTargets();
        RenderSketch();
        if (EditDimension(dimension.Id, removeOnCancel: true))
        {
            _sketchUndo.Push(before);
            _sketchRedo.Clear();
            Status($"Ölçü{dimension.Id} geometriye uygulandı.");
        }
        else _sketch.RestoreSnapshot(before);
        RenderSketch();
        UpdateSketchPropertyPanel();
    }

    private void ResetSmartDimensionTargets() => _dimensionSession.Reset();

    private bool EditDimension(int dimensionId, bool removeOnCancel = false)
    {
        if (_sketch?.Dimension(dimensionId) is not SketchDimension dimension)
            return false;

        var session=new DimensionEditSession(_sketch,dimensionId);
        var beforeEdit=session.Before;
        var unit=dimension.Type==SketchDimensionType.Angle?"°":"mm";
        var dialog=new DimensionEditDialog("Ölçüyü Değiştir",dimension.Value,unit,
            text=>session.TryCommit(text,out var error,ValidateDependentDimension)?null:error){Owner=this};
        if(dialog.ShowDialog()!=true)
        {
            if(removeOnCancel)_sketch.DeleteDimension(dimensionId);
            RenderSketch();UpdateSketchPropertyPanel();return false;
        }
        MarkSourceSketchDirty();
        if (!removeOnCancel && !SameSketch(beforeEdit, _sketch.CaptureSnapshot()))
        {
            _sketchUndo.Push(beforeEdit);
            _sketchRedo.Clear();
        }

        _selectedSketchDimensionId = dimensionId;
        RenderSketch();
        UpdateSketchPropertyPanel();
        return true;
    }

    private void ClearSketchSelection(bool render = true)
    {
        _selectionContextPending=false;
        _viewportContextMenu.Close();
        _selectedBoxDimensions.Clear();
        _selectedSketchEntities.Clear();
        _selectedSketchPoints.Clear();
        _selectedSketchDimensionId = -1;
        if (render)
        {
            RenderSketch();
            UpdateSketchPropertyPanel();
        }
    }

    private void BuildViewportContextMenu()
    {
        _viewportContextMenu.ShowImageMargin = false;
    }

    private void ShowViewportContextMenu(int x, int y)
    {
        _viewportContextMenu.Items.Clear();

        if (_inSketch)
        {
            var delete = new WinForms.ToolStripMenuItem("Sil");
            delete.Enabled = _selectedSketchEntities.Count > 0 ||
                             _selectedSketchPoints.Any(id => id != 0) ||
                             _selectedSketchDimensionId >= 0;
            delete.Click += (_, _) => DeleteSketchSelection();
            _viewportContextMenu.Items.Add(delete);

            _viewportContextMenu.Items.Add(new WinForms.ToolStripSeparator());

            PopulateSelectionActions();

            var clear = new WinForms.ToolStripMenuItem("Seçimi Temizle");
            clear.Click += (_, _) => ClearSketchSelection();
            _viewportContextMenu.Items.Add(clear);
        }
        else
        {
            var summary = new WinForms.ToolStripMenuItem(_viewer.SelectionSummary()) { Enabled = false };
            _viewportContextMenu.Items.Add(summary);

            if ((_hasSolid || _partSession is not null) && _viewer.TryGetSelectedPlanarFace(out _))
            {
                var normalTo = new WinForms.ToolStripMenuItem("Yüzeye Dik Bak (Ctrl+8)");
                normalTo.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => NormalToSelectedSupport()));
                _viewportContextMenu.Items.Add(normalTo);

                var sketchOnFace = new WinForms.ToolStripMenuItem("Bu Yüzde Çizim Başlat");
                sketchOnFace.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
                    NewProfileSketch_Click(this,new RoutedEventArgs())));
                _viewportContextMenu.Items.Add(sketchOnFace);
            }

            if (CutButton.IsEnabled)
            {
                var cut = new WinForms.ToolStripMenuItem("Ekstrüzyon Kes");
                cut.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
                    Cut_Click(this,new RoutedEventArgs())));
                _viewportContextMenu.Items.Add(cut);
            }

            _viewportContextMenu.Items.Add(new WinForms.ToolStripSeparator());

            AddSolidSelectionModeMenu("Yüz seçimi", CadSelectionMode.Face);
            AddSolidSelectionModeMenu("Kenar seçimi", CadSelectionMode.Edge);
            AddSolidSelectionModeMenu("Köşe seçimi", CadSelectionMode.Vertex);

            _viewportContextMenu.Items.Add(new WinForms.ToolStripSeparator());

            var clear = new WinForms.ToolStripMenuItem("Seçimi Temizle");
            clear.Click += (_, _) =>
            {
                _viewer.ClearSelection();
                Status("Seçim temizlendi.");
            };
            _viewportContextMenu.Items.Add(clear);

            var fit = new WinForms.ToolStripMenuItem("Ekrana Sığdır (F)");
            fit.Click += (_, _) => _viewer.FitAll();
            _viewportContextMenu.Items.Add(fit);
        }

        _viewportContextMenu.Show(_viewportPanel, x, y);
    }

    private void AddContextRelationItem(string title, SketchRelationType type)
    {
        var item = new WinForms.ToolStripMenuItem(title);
        item.Tag=type;
        item.Click += (_, _) => ApplySketchRelation(type);
        _viewportContextMenu.Items.Add(item);
    }

    private void AddSolidSelectionModeMenu(string title, CadSelectionMode mode)
    {
        var item = new WinForms.ToolStripMenuItem(title);
        item.Click += (_, _) =>
        {
            _viewer.SetSelectionMode(mode);
            Status($"{title} aktif.");
        };
        _viewportContextMenu.Items.Add(item);
    }

    private void ResetSketchInteraction()
    {
        if(_boxStart is not null)FinishWindowSelection(0,0,true);
        _sketchCommand?.CancelStep();
        _viewer.ClearPreview();
        _dragSketchPointId = _dragSketchDimensionId = -1;
        _hoverSketchPointId = _hoverSketchEntityId = _hoverSketchDimensionId = -1;
        _dragSketchSnapshot = null;
        _dragSketchChanged = false;
    }

    private static bool SameSketch(SketchSnapshot a, SketchSnapshot b)
        => a.NextPointId == b.NextPointId && a.NextEntityId == b.NextEntityId &&
           a.NextRelationId == b.NextRelationId && a.NextDimensionId == b.NextDimensionId &&
           a.Points.OrderBy(p => p.Key).SequenceEqual(b.Points.OrderBy(p => p.Key)) &&
           a.Entities.SequenceEqual(b.Entities) && a.Relations.SequenceEqual(b.Relations) &&
           a.Dimensions.SequenceEqual(b.Dimensions) && a.CenterRectangles.SequenceEqual(b.CenterRectangles);

    private void UndoSketch()
    {
        _lastHistoryCommand = "UndoSketch from " + _lastKeyRoute;
        if (_sketch is null || _sketchUndo.Count == 0)
        {
            Status("Geri alınacak çizim işlemi yok.");
            return;
        }

        ResetSketchInteraction();
        SketchSnapshotHistory.Restore(_sketch, _sketchUndo, _sketchRedo);
        MarkSourceSketchDirty();
        ClearSketchSelection(false);
        ResetSmartDimensionTargets();
        RenderSketch();
        UpdateSketchPropertyPanel();
        RefreshCommandState();
        Status("Çizim işlemi geri alındı • Ctrl+Y ile yineleyebilirsiniz.");
    }

    private void RedoSketch()
    {
        _lastHistoryCommand = "RedoSketch from " + _lastKeyRoute;
        if (_sketch is null || _sketchRedo.Count == 0)
        {
            Status("Yinelenecek çizim işlemi yok.");
            return;
        }

        ResetSketchInteraction();
        SketchSnapshotHistory.Restore(_sketch, _sketchRedo, _sketchUndo);
        MarkSourceSketchDirty();
        ClearSketchSelection(false);
        ResetSmartDimensionTargets();
        RenderSketch();
        UpdateSketchPropertyPanel();
        RefreshCommandState();
        Status("Çizim işlemi yinelendi • Ctrl+Z ile geri alabilirsiniz.");
    }

    private void CommitSketchDeletion(int[]? dimensions = null, int[]? relations = null)
    {
        if (_sketch is null) return;
        var before = SketchDeletion.Apply(_sketch, dimensions: dimensions, relations: relations);
        if (before is null) return;
        _sketchUndo.Push(before);
        _sketchRedo.Clear();
        ResetSketchInteraction();
        ClearSketchSelection(false);
        ResetSmartDimensionTargets();
        MarkSourceSketchDirty();
        RenderSketch();
        UpdateSketchPropertyPanel();
        RefreshCommandState();
    }

    private void DeleteSketchSelection()
    {
        if (_sketch is null) return;
        if (_selectedSketchDimensionId < 0 && _selectedBoxDimensions.Count == 0 &&
            _selectedSketchEntities.Count == 0 &&
            !_selectedSketchPoints.Any(id => id != 0))
            return;

        var before = SketchDeletion.Apply(_sketch, _selectedSketchEntities, _selectedSketchPoints,
            _selectedBoxDimensions.Append(_selectedSketchDimensionId));
        if (before is null) return;
        _sketchUndo.Push(before);
        _sketchRedo.Clear();
        ResetSketchInteraction();
        ClearSketchSelection(false);
        ResetSmartDimensionTargets();
        MarkSourceSketchDirty();
        RenderSketch();
        UpdateSketchPropertyPanel();
        RefreshCommandState();
        Status("Seçili çizim öğeleri silindi.");
    }

    private void HandleSketchClick(int x, int y, int clickCount)
    {
        if (_sketch is null || !TryGetSketchCursor(x, y, out var uv, out var snapId, snap:_sketchCommand is not TrimSketchCommand)) return;

        if (_sketchCommand is null)
        {
            Status("Çizim seçim modu • Çizgi, Dikdörtgen veya Daire aracı seçin.");
            return;
        }

        var pick = new SketchPick(
            uv,
            snapId,
            Math.Max(1e-6, _viewer.PixelToWorldLength(10)),
            clickCount);

        var before = _sketch.CaptureSnapshot();
        try
        {
            _sketchCommand.Pick(pick);
            if (!SameSketch(before, _sketch.CaptureSnapshot()))
            {
                _sketchUndo.Push(before);
                _sketchRedo.Clear();
            }
            _viewer.ClearPreview();
        }
        catch (Exception ex)
        {
            _sketch.RestoreSnapshot(before);
            _sketchCommand.CancelStep();
            RenderSketch();
            Status($"{_sketchCommand.Title}: {ex.Message}");
        }

        RefreshCommandState();
    }

    private void UpdateSketchCommandPreview(int x, int y)
    {
        if (_sketch is null || _sketchCommand is null ||
            !TryGetSketchCursor(x, y, out var current, out _, snap:_sketchCommand is not TrimSketchCommand))
            return;

        if(_sketchCommand is TrimSketchCommand trim)trim.Tolerance=Math.Max(1e-6,_viewer.PixelToWorldLength(10));
        _viewer.ClearPreview();
        foreach (var primitive in _sketchCommand.Preview(current))
        {
            switch (primitive)
            {
                case SketchPreviewLine line:
                    PreviewLine(line.A, line.B);
                    break;
                case SketchPreviewCircle circle:
                    PreviewCircle(circle.Center, circle.Radius);
                    break;
            }
        }
        _viewer.Redraw();
    }

    private Vector3d World(Point2d uv)
        => _sketch!.Plane.SketchToWorld(uv.X, uv.Y);

    private void PreviewLine(Point2d a, Point2d b)
    {
        var wa = World(a);
        var wb = World(b);
        if ((wa - wb).Length < 1e-9) return;
        _viewer.AddPreviewLine(wa.X, wa.Y, wa.Z, wb.X, wb.Y, wb.Z);
    }

    private void PreviewCircle(Point2d centerUv, double radius)
    {
        if (_sketch is null || radius <= 1e-9) return;
        var c = World(centerUv);
        var p = _sketch.Plane;
        _viewer.AddPreviewCircle(c.X, c.Y, c.Z,
            p.Normal.X, p.Normal.Y, p.Normal.Z,
            p.U.X, p.U.Y, p.U.Z,
            radius);
    }

    private void RenderSketch()
    {
        if (_sketch is null || !_viewer.IsInitialized()) return;
        _viewer.ClearSketch();

        var definition = _sketch.DefinitionInfo();
        var baseGeometryState = definition.IsConflicting
            ? 4
            : definition.IsFullyDefined
                ? 3
                : 0;

        // Reference axes in the real sketch plane.
        var axisU0 = _sketch.Plane.SketchToWorld(-100, 0);
        var axisU1 = _sketch.Plane.SketchToWorld(100, 0);
        var axisV0 = _sketch.Plane.SketchToWorld(0, -100);
        var axisV1 = _sketch.Plane.SketchToWorld(0, 100);
        _viewer.AddStyledSketchLine(axisU0.X, axisU0.Y, axisU0.Z, axisU1.X, axisU1.Y, axisU1.Z, 1);
        _viewer.AddStyledSketchLine(axisV0.X, axisV0.Y, axisV0.Z, axisV1.X, axisV1.Y, axisV1.Z, 1);

        foreach (var line in _sketch.Lines())
        {
            var aUv = _sketch.Point(line.StartPointId).Position;
            var bUv = _sketch.Point(line.EndPointId).Position;
            var a = _sketch.Plane.SketchToWorld(aUv.X, aUv.Y);
            var b = _sketch.Plane.SketchToWorld(bUv.X, bUv.Y);

            var state = _selectedSketchEntities.Contains(line.Id) || _hoverSketchEntityId == line.Id
                ? 2 : line.Construction ? 1 : baseGeometryState;

            _viewer.AddStyledSketchLine(a.X, a.Y, a.Z, b.X, b.Y, b.Z, state);
        }

        foreach (var circle in _sketch.Circles())
        {
            var centerUv = _sketch.Point(circle.CenterPointId).Position;
            var center = _sketch.Plane.SketchToWorld(centerUv.X, centerUv.Y);
            var p = _sketch.Plane;

            var state = _selectedSketchEntities.Contains(circle.Id) || _hoverSketchEntityId == circle.Id
                ? 2 : circle.Construction ? 1 : baseGeometryState;

            _viewer.AddStyledSketchCircle(
                center.X, center.Y, center.Z,
                p.Normal.X, p.Normal.Y, p.Normal.Z,
                p.U.X, p.U.Y, p.U.Z,
                circle.Radius,
                state);
        }

        foreach (var point in _sketch.Points.Values)
        {
            var w = _sketch.Plane.SketchToWorld(point.Position.X, point.Position.Y);
            var state =
                _selectedSketchPoints.Contains(point.Id) || _hoverSketchPointId == point.Id
                    ? 2
                    : point.Id == 0
                        ? 7
                        : 6;

            _viewer.AddStyledSketchPoint(w.X, w.Y, w.Z, state);
        }

        if (_pointerTarget is { Reference.IsPointLike: true } target)
        {
            var p=World(target.Position);var size=Math.Max(1e-6,_viewer.PixelToWorldLength(5));
            foreach(var pair in new[]{(new Point2d(target.Position.X-size,target.Position.Y-size),new Point2d(target.Position.X+size,target.Position.Y+size)),
                (new Point2d(target.Position.X-size,target.Position.Y+size),new Point2d(target.Position.X+size,target.Position.Y-size))})
            {
                var a=World(pair.Item1);var b=World(pair.Item2);
                _viewer.AddStyledSketchLine(a.X,a.Y,a.Z,b.X,b.Y,b.Z,2);
            }
            var label=World(new Point2d(target.Position.X+size*2,target.Position.Y+size*2));
            _viewer.AddSketchText(label.X,label.Y,label.Z,target.Label,2);
        }
        foreach (var dimension in _sketch.Dimensions)
            RenderDimension(dimension);

        _viewer.Redraw();
        UpdateSketchPropertyPanel();
    }

    private void RenderDimension(SketchDimension dimension)
    {
        if (_sketch is null) return;

        var state = _selectedBoxDimensions.Contains(dimension.Id) || _selectedSketchDimensionId == dimension.Id ||
                    _hoverSketchDimensionId == dimension.Id
            ? 2
            : 5;

        if (dimension.ReferenceA is not null)
        {
            var (a,b) = _sketch.DimensionReferencePoints(dimension);
            if (dimension.Type == SketchDimensionType.HorizontalDistance)
            {
                var q0 = new Point2d(a.X,dimension.LabelPosition.Y); var q1 = new Point2d(b.X,dimension.LabelPosition.Y);
                AddDimensionLine(a,q0,state); AddDimensionLine(b,q1,state); AddDimensionLine(q0,q1,state);
                AddDimensionText(dimension.LabelPosition,dimension,state);
            }
            else if (dimension.Type == SketchDimensionType.VerticalDistance)
            {
                var q0 = new Point2d(dimension.LabelPosition.X,a.Y); var q1 = new Point2d(dimension.LabelPosition.X,b.Y);
                AddDimensionLine(a,q0,state); AddDimensionLine(b,q1,state); AddDimensionLine(q0,q1,state);
                AddDimensionText(dimension.LabelPosition,dimension,state);
            }
            else RenderAlignedLinearDimension(a,b,dimension.LabelPosition,dimension,state);
            return;
        }

        if (dimension.Type == SketchDimensionType.LineLength &&
            _sketch.Entity(dimension.EntityA) is SketchLine line)
        {
            var a = _sketch.Point(line.StartPointId).Position;
            var b = _sketch.Point(line.EndPointId).Position;
            RenderAlignedLinearDimension(a, b, dimension.LabelPosition, dimension, state);
            return;
        }

        if (dimension.Type == SketchDimensionType.HorizontalDistance &&
            dimension.PointA >= 0 && dimension.PointB >= 0)
        {
            var a = _sketch.Point(dimension.PointA).Position;
            var b = _sketch.Point(dimension.PointB).Position;
            var y = dimension.LabelPosition.Y;
            var q0 = new Point2d(a.X, y);
            var q1 = new Point2d(b.X, y);
            AddDimensionLine(a, q0, state);
            AddDimensionLine(b, q1, state);
            AddDimensionLine(q0, q1, state);
            AddDimensionText(new Point2d(dimension.LabelPosition.X, y), dimension, state);
            return;
        }

        if (dimension.Type == SketchDimensionType.VerticalDistance &&
            dimension.PointA >= 0 && dimension.PointB >= 0)
        {
            var a = _sketch.Point(dimension.PointA).Position;
            var b = _sketch.Point(dimension.PointB).Position;
            var x = dimension.LabelPosition.X;
            var q0 = new Point2d(x, a.Y);
            var q1 = new Point2d(x, b.Y);
            AddDimensionLine(a, q0, state);
            AddDimensionLine(b, q1, state);
            AddDimensionLine(q0, q1, state);
            AddDimensionText(new Point2d(x, dimension.LabelPosition.Y), dimension, state);
            return;
        }

        if (dimension.Type == SketchDimensionType.PointDistance &&
            dimension.PointA >= 0 && dimension.PointB >= 0)
        {
            var a = _sketch.Point(dimension.PointA).Position;
            var b = _sketch.Point(dimension.PointB).Position;
            RenderAlignedLinearDimension(a, b, dimension.LabelPosition, dimension, state);
            return;
        }

        if (dimension.Type == SketchDimensionType.CircleDiameter &&
            _sketch.Entity(dimension.EntityA) is SketchCircle circle)
        {
            var center = _sketch.Point(circle.CenterPointId).Position;
            var dx = dimension.LabelPosition.X - center.X;
            var dy = dimension.LabelPosition.Y - center.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9) { dx = 1; dy = 0; length = 1; }
            var ux = dx / length;
            var uy = dy / length;
            var edge = new Point2d(center.X + ux * circle.Radius, center.Y + uy * circle.Radius);
            AddDimensionLine(center, edge, state);
            AddDimensionLine(edge, dimension.LabelPosition, state);
            AddDimensionText(dimension.LabelPosition, dimension, state);
            return;
        }

        if (dimension.Type == SketchDimensionType.Angle &&
            _sketch.Entity(dimension.EntityA) is SketchLine first &&
            _sketch.Entity(dimension.EntityB) is SketchLine second)
        {
            RenderAngularDimension(first, second, dimension, state);
            return;
        }

        AddDimensionText(dimension.LabelPosition, dimension, state);
    }

    private void RenderAlignedLinearDimension(
        Point2d a,
        Point2d b,
        Point2d label,
        SketchDimension dimension,
        int state)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return;

        var nx = -dy / len;
        var ny = dx / len;
        var mid = new Point2d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        var offset = (label.X - mid.X) * nx + (label.Y - mid.Y) * ny;

        var q0 = new Point2d(a.X + nx * offset, a.Y + ny * offset);
        var q1 = new Point2d(b.X + nx * offset, b.Y + ny * offset);
        AddDimensionLine(a, q0, state);
        AddDimensionLine(b, q1, state);
        AddDimensionLine(q0, q1, state);

        // Text is projected to the dimension line, so it no longer creates
        // the long diagonal leader seen in V1.3.
        var text = new Point2d(
            label.X - nx * ((label.X - q0.X) * nx + (label.Y - q0.Y) * ny),
            label.Y - ny * ((label.X - q0.X) * nx + (label.Y - q0.Y) * ny));
        AddDimensionText(text, dimension, state);
    }

    private void RenderAngularDimension(
        SketchLine first,
        SketchLine second,
        SketchDimension dimension,
        int state)
    {
        if (_sketch is null) return;

        var firstIds = new[] { first.StartPointId, first.EndPointId };
        var secondIds = new[] { second.StartPointId, second.EndPointId };
        var sharedId = firstIds.Intersect(secondIds).FirstOrDefault(-1);

        var pivot = sharedId >= 0
            ? _sketch.Point(sharedId).Position
            : DimensionAnchor(dimension);

        Point2d FarPoint(SketchLine line)
        {
            if (sharedId >= 0)
                return _sketch.Point(line.StartPointId == sharedId ? line.EndPointId : line.StartPointId).Position;

            var a = _sketch.Point(line.StartPointId).Position;
            var b = _sketch.Point(line.EndPointId).Position;
            return Point2d.Distance(a, pivot) >= Point2d.Distance(b, pivot) ? a : b;
        }

        var p1 = FarPoint(first);
        var p2 = FarPoint(second);
        var a1 = Math.Atan2(p1.Y - pivot.Y, p1.X - pivot.X);
        var a2 = Math.Atan2(p2.Y - pivot.Y, p2.X - pivot.X);

        while (a2 - a1 > Math.PI) a2 -= Math.PI * 2;
        while (a2 - a1 < -Math.PI) a2 += Math.PI * 2;

        var radius = Math.Max(
            8.0,
            Point2d.Distance(pivot, dimension.LabelPosition));

        var r1 = new Point2d(pivot.X + Math.Cos(a1) * radius, pivot.Y + Math.Sin(a1) * radius);
        var r2 = new Point2d(pivot.X + Math.Cos(a2) * radius, pivot.Y + Math.Sin(a2) * radius);
        AddDimensionLine(pivot, r1, state);
        AddDimensionLine(pivot, r2, state);

        const int segments = 18;
        var previous = r1;
        for (var i = 1; i <= segments; i++)
        {
            var a = a1 + (a2 - a1) * i / segments;
            var current = new Point2d(
                pivot.X + Math.Cos(a) * radius,
                pivot.Y + Math.Sin(a) * radius);
            AddDimensionLine(previous, current, state);
            previous = current;
        }

        AddDimensionText(dimension.LabelPosition, dimension, state);
    }

    private void AddDimensionLine(Point2d a, Point2d b, int state)
    {
        var wa = World(a);
        var wb = World(b);
        _viewer.AddStyledSketchLine(
            wa.X, wa.Y, wa.Z,
            wb.X, wb.Y, wb.Z,
            state);
    }

    private void AddDimensionText(Point2d p, SketchDimension dimension, int state)
    {
        var world = World(p);
        _viewer.AddSketchText(
            world.X, world.Y, world.Z,
            FormatDimension(dimension),
            state);
    }

    private Point2d DimensionAnchor(SketchDimension dimension)
    {
        if (_sketch is null) return dimension.LabelPosition;

        if (dimension.Type == SketchDimensionType.LineLength &&
            _sketch.Entity(dimension.EntityA) is SketchLine line)
        {
            var a = _sketch.Point(line.StartPointId).Position;
            var b = _sketch.Point(line.EndPointId).Position;
            return new Point2d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        }

        if (dimension.Type == SketchDimensionType.CircleDiameter &&
            _sketch.Entity(dimension.EntityA) is SketchCircle circle)
            return _sketch.Point(circle.CenterPointId).Position;

        if (dimension.PointA >= 0 && dimension.PointB >= 0 &&
            _sketch.Points.ContainsKey(dimension.PointA) &&
            _sketch.Points.ContainsKey(dimension.PointB))
        {
            var a = _sketch.Point(dimension.PointA).Position;
            var b = _sketch.Point(dimension.PointB).Position;
            return new Point2d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        }

        if (dimension.Type == SketchDimensionType.Angle &&
            _sketch.Entity(dimension.EntityA) is SketchLine la &&
            _sketch.Entity(dimension.EntityB) is SketchLine lb)
        {
            var idsA = new[] { la.StartPointId, la.EndPointId };
            var idsB = new[] { lb.StartPointId, lb.EndPointId };
            var shared = idsA.Intersect(idsB).FirstOrDefault(-1);
            if (shared >= 0) return _sketch.Point(shared).Position;

            var a = _sketch.Point(la.StartPointId).Position;
            var b = _sketch.Point(lb.StartPointId).Position;
            return new Point2d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        }

        return dimension.LabelPosition;
    }

    private string FormatDimension(SketchDimension dimension)
    {
        if (_sketch is null) return string.Empty;
        var value = _sketch.Measure(dimension);
        var prefix = dimension.Type == SketchDimensionType.CircleDiameter ? "Ø" : "";
        var suffix = dimension.Type == SketchDimensionType.Angle ? "°" : "";
        var driven = dimension.Driving ? "" : " (Ref)";
        return $"{prefix}{value:0.###}{suffix}{driven}";
    }

    private void ActivateSketchCommand(ISketchCommand command)
    {
        if (!_inSketch || _sketch is null || _commandContext is null)
        {
            Status("Önce bir düzlem seçip Çizim komutunu başlatın.");
            return;
        }

        _viewer.ClearPreview();
        _smartDimensionMode = false;
        ResetSmartDimensionTargets();
        _commandManager.Start(command, _commandContext);
        _sketchCommand = command;
        ClearSketchSelection(false);
        RenderSketch();
    }

    private void Trim_Click(object sender,RoutedEventArgs e)
    {
        if(_sketch is null)return;
        ActivateSketchCommand(new TrimSketchCommand(_sketch,Math.Max(1e-6,_viewer.PixelToWorldLength(10))));
        Status("Çizgi Kırp • Turuncu bölüm silinecek • Çizgi/daire kesişimlerinde durur • Esc: çık");
    }

    private void SelectTool_Click(object sender,RoutedEventArgs e)
    {
        LeaveSketchCommand();
        _smartDimensionMode=false;
        ResetSmartDimensionTargets();
        _viewportPanel.Focus();
        UpdateSketchPropertyPanel();
    }

    private void LeaveSketchCommand()
    {
        _commandManager.Cancel();
        _sketchCommand = null;
        _viewer.ClearPreview();
        Status("Seçim modu • Sol tık: seç • Ctrl: seçime ekle • Delete: sil • Esc: araçtan çık");
    }

    private void UpdateSketchPropertyPanel()
    {
        if (_sketch is null)
        {
            SketchPropertyPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SketchPropertyPanel.Visibility = _inSketch ? Visibility.Visible : Visibility.Collapsed;
        if (!_inSketch) return;

        var info = _sketch.DefinitionInfo();
        SketchDefinitionText.Text = info.Text;

        var selectionParts = new List<string>();
        if (_selectedSketchPoints.Count > 0)
            selectionParts.Add("Nokta: " + string.Join(", ", _selectedSketchPoints.OrderBy(x => x).Select(x => $"P{x}")));
        if (_selectedSketchEntities.Count > 0)
            selectionParts.Add("Obje: " + string.Join(", ", _selectedSketchEntities.OrderBy(x => x).Select(x => $"E{x}")));
        if (_selectedSketchDimensionId >= 0)
            selectionParts.Add($"Ölçü: D{_selectedSketchDimensionId}");

        SketchSelectionText.Text = selectionParts.Count == 0
            ? "Seçim yok"
            : string.Join(Environment.NewLine, selectionParts);

        SketchRelationsList.Items.Clear();
        foreach (var relation in _sketch.Relations)
        {
            SketchRelationsList.Items.Add(new ListBoxItem
            {
                Content = $"R{relation.Id} • {RelationText(relation)}",
                Tag = relation.Id
            });
        }

        SketchDimensionsList.Items.Clear();
        foreach (var dimension in _sketch.Dimensions)
        {
            SketchDimensionsList.Items.Add(new ListBoxItem
            {
                Content = $"D{dimension.Id} • {FormatDimension(dimension)}",
                Tag = dimension.Id
            });
        }
    }

    private string RelationText(SketchRelation relation)
    {
        if (relation.ReferenceA is not null)
            return $"{relation.Type} • {relation.ReferenceA.Kind}:{relation.ReferenceA.Id} ↔ {relation.ReferenceB!.Kind}:{relation.ReferenceB.Id}";
        return relation.Type switch
        {
            SketchRelationType.Horizontal => $"Yatay • E{relation.EntityA}",
            SketchRelationType.Vertical => $"Dikey • E{relation.EntityA}",
            SketchRelationType.Coincident => $"Çakışık • P{relation.PointA} ↔ P{relation.PointB}",
            SketchRelationType.Parallel => $"Paralel • E{relation.EntityA} ↔ E{relation.EntityB}",
            SketchRelationType.Perpendicular => $"Dik • E{relation.EntityA} ↔ E{relation.EntityB}",
            SketchRelationType.Equal => $"Eşit • E{relation.EntityA} ↔ E{relation.EntityB}",
            SketchRelationType.Collinear => $"Eşdoğrusal • E{relation.EntityA} ↔ E{relation.EntityB}",
            SketchRelationType.Concentric => $"Eşmerkezli • E{relation.EntityA} ↔ E{relation.EntityB}",
            SketchRelationType.Fixed => $"Sabit • P{relation.PointA}",
            _ => relation.Type.ToString()
        };
    }

    private void SmartDimension_Click(object sender, RoutedEventArgs e)
        => StartSmartDimensionMode();

    private void StartSmartDimensionMode()
    {
        if (!_inSketch || _sketch is null) return;
        LeaveSketchCommand();

        _smartDimensionMode = true;
        ResetSmartDimensionTargets();

        _viewportContextMenu.Close();
        _dimensionSession.Preselect(CurrentSketchReferences());

        PropertyTitle.Text = "Akıllı Ölçülendirme";
        PropertyInfo.Text =
            "Geometriyi seçin → ölçüyü taşıyın → konumu tıklayın → değeri girin. " +
            "Açı için iki çizgiyi Ctrl ile seçebilirsiniz.";
        ManagerTabs.SelectedItem = PropertyManagerTab;
        UpdateSketchPropertyPanel();
        RenderSketch();

        if (_smartDimensionWaitingPlacement)
            Status("Akıllı Ölçülendirme: ölçünün konumunu tıklayın.");
        else if (_dimensionSession.References.Count == 1)
            Status("Akıllı Ölçülendirme: ikinci noktayı seçin.");
        else
            Status("Akıllı Ölçülendirme: çizgi, daire veya iki nokta seçin.");
    }

    private void ShowSketchRelations_Click(object sender, RoutedEventArgs e)
    {
        if (!_inSketch) return;
        LeaveSketchCommand();
        _smartDimensionMode = false;
        ResetSmartDimensionTargets();
        ManagerTabs.SelectedItem = PropertyManagerTab;
        UpdateSketchPropertyPanel();
        if(CurrentSketchReferences().Length==0)
            Status("İlişki için Ctrl ile geometri seçin; Ctrl bırakıldığında seçenekler açılır.");
        else ShowSelectionContext();
    }

    private void HorizontalRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Horizontal);

    private void VerticalRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Vertical);

    private void CoincidentRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Coincident);

    private void ParallelRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Parallel);

    private void PerpendicularRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Perpendicular);

    private void EqualRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Equal);

    private void TangentRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Tangent);

    private void MidpointRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Midpoint);

    private void FixRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Fixed);

    private void CollinearRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Collinear);

    private void ConcentricRelation_Click(object sender, RoutedEventArgs e)
        => ApplySketchRelation(SketchRelationType.Concentric);

    private void ApplySketchRelation(SketchRelationType type)
    {
        if (_sketch is null || !_inSketch) return;

        var before = _sketch.CaptureSnapshot();
        var references = CurrentSketchReferences();
        var ok = _sketch.ApplyReferenceRelation(type, references);

        if (!ok)
        {
            Status(type switch
            {
                SketchRelationType.Horizontal or SketchRelationType.Vertical =>
                    "Tek çizgi veya iki nokta/merkez/Origin referansı seçin.",
                SketchRelationType.Coincident =>
                    "Çakışık için iki nokta seçin (Ctrl ile çoklu seçim).",
                SketchRelationType.Parallel or SketchRelationType.Perpendicular or SketchRelationType.Equal or SketchRelationType.Collinear =>
                    "Bu ilişki için iki çizgi seçin (Ctrl ile çoklu seçim).",
                SketchRelationType.Concentric =>
                    "Eşmerkezli için iki daire seçin (Ctrl ile çoklu seçim).",
                SketchRelationType.Fixed =>
                    "Sabitlemek için en az bir nokta seçin.",
                _ => "Seçim bu ilişkiye uygun değil."
            });
            return;
        }

        _sketchUndo.Push(before);
        _sketchRedo.Clear();
        _sketch.SolveRelations();
        RenderSketch();
        UpdateSketchPropertyPanel();
        RefreshCommandState();
        Status($"{type} ilişkisi uygulandı; geometri gerçekten güncellendi.");
    }

    private void DeleteRelation_Click(object sender, RoutedEventArgs e)
    {
        if (_sketch is null || SketchRelationsList.SelectedItem is not ListBoxItem item || item.Tag is not int relationId)
            return;

        CommitSketchDeletion(relations: new[] { relationId });
        Status($"R{relationId} ilişkisi silindi.");
    }

    private void EditSelectedDimension_Click(object sender, RoutedEventArgs e)
    {
        if (SketchDimensionsList.SelectedItem is ListBoxItem item && item.Tag is int dimensionId)
            EditDimension(dimensionId);
        else if (_selectedSketchDimensionId >= 0)
            EditDimension(_selectedSketchDimensionId);
    }

    private void SketchDimensionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => EditSelectedDimension_Click(sender, e);

    private void StartSketch_Click(object sender, RoutedEventArgs e)
    {
        if (_inSketch || _extrudeCommandActive) return;

        // SolidWorks-like workflow: once a body exists, "Çizim" means create a
        // new sketch on the selected planar face or standard plane. Existing
        // sketches are edited from FeatureManager (double-click / context edit).
        if (_hasSolid || _partSession is not null)
        {
            NewProfileSketch_Click(sender,e);
            return;
        }

        _sketch ??= new SketchModel(_selectedPlane);
        if (!_inSketch)
        {
            if (!_hasSolid) _featureHistory.Clear();
            _sketchUndo.Clear();
            _sketchRedo.Clear();
        }
        BeginSketchInteraction();
    }

    private void BeginSketchInteraction()
    {
        _inSketch = true;
        _smartDimensionMode = false;
        ResetSmartDimensionTargets();
        ClearSketchSelection(false);
        _commandContext = new CommandContext
        {
            SetStatus = Status,
            ModelChanged = () =>
            {
                RenderSketch();
                RefreshCommandState();
            }
        };
        LeaveSketchCommand();

        SketchTab.IsEnabled = true;
        CommandTabs.SelectedItem = SketchTab;

        if (_partSession is null && _sketchNode is null)
        {
            _sketchNode = new TreeViewItem { Header = "Çizim1", Tag = "sketch:1" };
            PartNode.Items.Add(_sketchNode);
        }

        var p = _sketch.Plane;
        _viewer.SetNormalView(p.Normal.X, p.Normal.Y, p.Normal.Z, p.V.X, p.V.Y, p.V.Z);
        RenderSketch();
        if(_partSession is null)_viewer.FitAll();
        PropertyTitle.Text = "Çizim1";
        PropertyInfo.Text = $"{p.Name} üzerinde gerçek 3B sketch. Origin ve mevcut endpoint'ler otomatik yakalanır.";
        ManagerTabs.SelectedItem = PropertyManagerTab;
        Status($"Çizim1 • {p.Name} • Çizgi, Dikdörtgen veya Daire seçin.");
        RefreshCommandState();
    }

    private void ExitSketch_Click(object sender, RoutedEventArgs e)
    {
        if (!_inSketch) return;
        if (_graphSketchEdit is not null && !CommitGraphSketch()) return;
        if (_sourceSketchEditBefore is { } before)
        {
            var after = CaptureFeatureState();
            if (!SameSketch(before.Sketch, after.Sketch))
            {
                if (!_featureHistory.TryCommit(before, after, ApplyFeatureState))
                {
                    _viewer.HideCommittedSolid();
                    RenderSketch();
                    Status("Kaynak çizimden katı oluşturulamadı. Çizimi düzeltin veya Düzenlemeyi İptal Et ile eski katıya dönün.");
                    return;
                }
            }
            _viewer.ShowCommittedSolid();
            _viewer.ClearSketch();
            _viewer.ClearPreview();
            _sourceSketchEditBefore = null;
        }
        FinishSketchInteraction();
        if(_partSession is not null){Status("Çizim onaylandı; bağımlı feature grafiği yeniden oluşturuldu.");return;}
        if (_hasSolid)
            Status("Çizim düzenlemesi uygulandı • bağımlı Ekstrüzyon1 yeniden oluşturuldu • Ctrl+Z ile geri alın.");
        else if (_sketch is not null && (_sketch.TryGetClosedPolyline(out _) || _sketch.TryGetSingleCircle(out _)))
            Status("Çizimden çıkıldı • kapalı profil hazır • Ekstrüzyon kullanılabilir.");
        else
            Status("Çizimden çıkıldı • ekstrüzyon için tek kapalı profil gerekiyor.");

        RefreshCommandState();
    }

    private void FinishSketchInteraction()
    {
        LeaveSketchCommand();
        _inSketch = false;
        _smartDimensionMode = false;
        ResetSmartDimensionTargets();
        ClearSketchSelection(false);
        _commandContext = null;
        SketchPropertyPanel.Visibility = Visibility.Collapsed;
        SketchTab.IsEnabled = false;
        CommandTabs.SelectedItem = FeaturesTab;
        ManagerTabs.SelectedItem = FeatureManagerTab;

        RefreshCommandState();
    }

    private void CancelSourceSketchEdit_Click(object sender, RoutedEventArgs e)
    {
        if(_graphSketchEdit is not null){CancelGraphSketch();return;}
        if (_sourceSketchEditBefore is not { } before || _sketch is null) return;
        _sketch.RestoreSnapshot(before.Sketch,notifyChange:true);
        MarkSourceSketchDirty();
        // Exit sees the unchanged snapshot: no rebuild and no history entry.
        ExitSketch_Click(sender, e);
        Status("Çizim düzenlemesi iptal edildi • önceki katı ve parametreler korundu.");
    }

    private void CommandFamily_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void Line_Click(object sender, RoutedEventArgs e)
    {
        if (_sketch is not null) ActivateSketchCommand(new LineSketchCommand(_sketch));
    }

    private void ThreePointCircle_Click(object sender,RoutedEventArgs e)
    {
        if (_sketch is not null) ActivateSketchCommand(new ThreePointCircleCommand(_sketch));
    }

    private void ThreePointRectangle_Click(object sender,RoutedEventArgs e)
    {
        if (_sketch is not null) ActivateSketchCommand(new ThreePointRectangleCommand(_sketch));
    }

    private void Centerline_Click(object sender, RoutedEventArgs e)
    {
        if (_sketch is not null) ActivateSketchCommand(new LineSketchCommand(_sketch, construction: true));
    }

    private void CornerRectangle_Click(object sender, RoutedEventArgs e)
    {
        if (_sketch is not null) ActivateSketchCommand(new CornerRectangleSketchCommand(_sketch));
    }

    private void CenterRectangle_Click(object sender, RoutedEventArgs e)
    {
        if (_sketch is not null) ActivateSketchCommand(new CenterRectangleSketchCommand(_sketch));
    }

    private void Circle_Click(object sender, RoutedEventArgs e)
    {
        if (_sketch is not null) ActivateSketchCommand(new CircleSketchCommand(_sketch));
    }

    private void Polygon_Click(object sender,RoutedEventArgs e)
    {
        if(_sketch is null)return;
        var sides=6;
        var dialog=new DimensionEditDialog("Çokgen Kenar Sayısı",sides,"kenar",text=>
        {
            if(!int.TryParse(text,NumberStyles.Integer,CultureInfo.CurrentCulture,out var parsed)||parsed<3||parsed>100)
                return "Kenar sayısı 3 ile 100 arasında tam sayı olmalı.";
            sides=parsed;return null;
        }){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        ActivateSketchCommand(new PolygonSketchCommand(_sketch,sides));
    }

    private bool PromptSketchNumber(string title,string unit,double initial,Func<double,bool> valid,out double value)
    {
        var result=initial;
        var dialog=new DimensionEditDialog(title,initial,unit,text=>
        {
            if(!DimensionNumbers.TryParseScalar(text,out var parsed)||!valid(parsed))return "Geçerli bir değer girin.";
            result=parsed;return null;
        }){Owner=this};
        var ok=dialog.ShowDialog()==true;value=result;return ok;
    }

    private void ApplySketchTransform(Func<IReadOnlyList<int>> transform,string success)
    {
        if(_sketch is null||!_inSketch)return;
        var before=_sketch.CaptureSnapshot();
        try
        {
            var created=transform();
            _sketchUndo.Push(before);_sketchRedo.Clear();
            _selectedSketchEntities.Clear();
            foreach(var id in created)_selectedSketchEntities.Add(id);
            MarkSourceSketchDirty();RenderSketch();UpdateSketchPropertyPanel();RefreshCommandState();Status(success);
        }
        catch(Exception ex)
        {
            _sketch.RestoreSnapshot(before);RenderSketch();Status(ex.Message);
        }
    }

    private void ExtendSketch_Click(object sender,RoutedEventArgs e)
    {
        if(_sketch is null||_selectedSketchEntities.Count==0){Status("Uzat için çizgi seçin.");return;}
        var ids=_selectedSketchEntities.ToArray();
        ApplySketchTransform(()=>
        {
            _sketch.ExtendLinesToNearest(ids);
            return ids;
        },"Seçili çizgiler en yakın çizgi/daire sınırına uzatıldı.");
    }

    private void OffsetSketch_Click(object sender,RoutedEventArgs e)
    {
        if(_sketch is null||_selectedSketchEntities.Count==0){Status("Ofset için çizgi veya daire seçin.");return;}
        if(!PromptSketchNumber("Ofset Mesafesi","mm",10,v=>Math.Abs(v)>1e-9,out var distance))return;
        var ids=_selectedSketchEntities.ToArray();
        ApplySketchTransform(()=>_sketch.OffsetEntities(ids,distance),$"Seçili geometri {distance:0.###} mm ofsetlendi.");
    }

    private void MirrorSketch_Click(object sender,RoutedEventArgs e)
    {
        if(_sketch is null)return;
        var axes=_selectedSketchEntities.Select(id=>_sketch.Entity(id)).OfType<SketchLine>().Where(l=>l.Construction).ToArray();
        if(axes.Length!=1){Status("Aynala için aynalanacak geometriyle birlikte tam bir Merkez Çizgisi seçin.");return;}
        var source=_selectedSketchEntities.Where(id=>id!=axes[0].Id).ToArray();
        ApplySketchTransform(()=>_sketch.MirrorEntities(source,axes[0].Id),"Seçili çizim geometrisi merkez çizgisine göre aynalandı.");
    }

    private void LinearSketchPattern_Click(object sender,RoutedEventArgs e)
    {
        if(_sketch is null||_selectedSketchEntities.Count==0){Status("Doğrusal dizilim için geometri seçin.");return;}
        if(!PromptSketchNumber("Dizilim Adedi","adet",3,v=>v>=2&&v<=500&&Math.Abs(v-Math.Round(v))<1e-9,out var countValue))return;
        if(!PromptSketchNumber("Dizilim Aralığı","mm",20,v=>v>1e-9,out var spacing))return;
        var axis=_selectedSketchEntities.Select(id=>_sketch.Entity(id)).OfType<SketchLine>().FirstOrDefault(l=>l.Construction);
        double dx=1,dy=0;
        var ids=_selectedSketchEntities.AsEnumerable();
        if(axis is not null)
        {
            var a=_sketch.Point(axis.StartPointId).Position;var b=_sketch.Point(axis.EndPointId).Position;
            dx=b.X-a.X;dy=b.Y-a.Y;ids=ids.Where(id=>id!=axis.Id);
        }
        var source=ids.ToArray();var count=(int)Math.Round(countValue);
        ApplySketchTransform(()=>_sketch.LinearPatternEntities(source,dx,dy,spacing,count),$"{count} adet doğrusal çizim dizilimi oluşturuldu.");
    }

    private void CircularSketchPattern_Click(object sender,RoutedEventArgs e)
    {
        if(_sketch is null||_selectedSketchEntities.Count==0){Status("Dairesel dizilim için geometri seçin.");return;}
        if(!PromptSketchNumber("Dizilim Adedi","adet",4,v=>v>=2&&v<=500&&Math.Abs(v-Math.Round(v))<1e-9,out var countValue))return;
        if(!PromptSketchNumber("Toplam Açı","°",360,v=>v>1e-9&&v<=360,out var angle))return;
        var center=_selectedSketchPoints.Count==1?_sketch.Point(_selectedSketchPoints.Single()).Position:_sketch.Point(0).Position;
        var count=(int)Math.Round(countValue);var source=_selectedSketchEntities.ToArray();
        ApplySketchTransform(()=>_sketch.CircularPatternEntities(source,center,angle,count),$"{count} adet dairesel çizim dizilimi oluşturuldu.");
    }

    private bool RebuildExtrusion(double depth)
    {
        if (_sketch is null || !double.IsFinite(depth) || Math.Abs(depth) < 1e-9) return false;

        var p = _sketch.Plane;
        bool ok;
        if (_sketch.TryGetExtrusionProfile(out var loop, out var polygonHoles, out var holes))
        {
            var uv = new double[loop.Count * 2];
            for (var i = 0; i < loop.Count; i++)
            {
                uv[i * 2] = loop[i].X;
                uv[i * 2 + 1] = loop[i].Y;
            }

            var polygonHoleData = new List<double>();
            foreach (var hole in polygonHoles)
            {
                polygonHoleData.Add(hole.Count);
                foreach (var point in hole)
                {
                    polygonHoleData.Add(point.X);
                    polygonHoleData.Add(point.Y);
                }
            }

            var holeData = new double[holes.Count * 3];
            for (var i = 0; i < holes.Count; i++)
            {
                var circle = holes[i];
                var center = _sketch.Point(circle.CenterPointId).Position;
                holeData[i * 3] = center.X;
                holeData[i * 3 + 1] = center.Y;
                holeData[i * 3 + 2] = circle.Radius;
            }

            ok = _viewer.PreviewExtrudeProfile(uv, polygonHoleData.ToArray(), holeData,
                p.Origin.X, p.Origin.Y, p.Origin.Z,
                p.U.X, p.U.Y, p.U.Z,
                p.V.X, p.V.Y, p.V.Z,
                p.Normal.X, p.Normal.Y, p.Normal.Z,
                depth);
        }
        else if (_sketch.TryGetSingleCircle(out var circle))
        {
            var center = _sketch.Point(circle.CenterPointId).Position;
            ok = _viewer.PreviewExtrudeCircle(center.X, center.Y, circle.Radius,
                p.Origin.X, p.Origin.Y, p.Origin.Z,
                p.U.X, p.U.Y, p.U.Z,
                p.V.X, p.V.Y, p.V.Z,
                p.Normal.X, p.Normal.Y, p.Normal.Z,
                depth);
        }
        else return false;

        if (!ok || !_viewer.CommitExtrusion()) return false;
        _extrudeDepth = depth;
        _hasSolid = true;
        if(_extrudeNode is not null)_extrudeNode.Header="Ekstrüzyon1";
        if(_sketchNode is not null)_sketchNode.Header="Çizim1";
        _viewer.ClearSketch();
        _viewer.ClearPreview();
        _viewer.SetSelectionMode(CadSelectionMode.Face);
        return true;
    }

    private ExtrusionState CaptureFeatureState()
        => new(_sketch!.Plane, _sketch.CaptureSnapshot(), _hasSolid ? _extrudeDepth : null);

    private bool ApplyFeatureState(ExtrusionState state)
    {
        var previousSketch = _sketch;
        var candidate = new SketchModel(state.Plane);
        candidate.RestoreSnapshot(state.Sketch);
        _sketch = candidate;
        if (state.Depth is double depth)
        {
            if (!RebuildExtrusion(depth))
            {
                _sketch = previousSketch;
                _viewer.CancelExtrusionPreview();
                _viewer.ShowCommittedSolid();
                return false;
            }
            EnsureExtrudeNode();
        }
        else
        {
            _viewer.ClearModel();
            _hasSolid = false;
            if (_extrudeNode is not null) PartNode.Items.Remove(_extrudeNode);
            _extrudeNode = null;
            RenderSketch();
        }
        ResetSketchInteraction();
        ClearSketchSelection(false);
        RefreshCommandState();
        return true;
    }

    private void EnsureExtrudeNode()
    {
        if (_extrudeNode is not null) return;
        _extrudeNode = new TreeViewItem { Header = "Ekstrüzyon1", Tag = "feature:extrude1" };
        PartNode.Items.Add(_extrudeNode);
    }

    private void UndoFeature()
    {
        if(_partSession is not null){GraphUndo(false);return;}
        Status(_featureHistory.TryUndo(ApplyFeatureState)?"Özellik işlemi geri alındı.":"Geri alınacak işlem yok veya rebuild başarısız.");
    }
    private void RedoFeature()
    {
        if(_partSession is not null){GraphUndo(true);return;}
        Status(_featureHistory.TryRedo(ApplyFeatureState)?"Özellik işlemi yinelendi.":"Yinelenecek işlem yok veya rebuild başarısız.");
    }

    private void DeleteExtrusionFeature()
    {
        if (_sketch is null || !_hasSolid || _extrudeCommandActive) return;
        var before = CaptureFeatureState();
        var after = before with { Depth = null };
        if (!ApplyFeatureState(after)) return;
        _featureHistory.Record(before, after);
        Status("Ekstrüzyon1 silindi; Çizim1 korundu • Ctrl+Z ile geri alın.");
    }

    private void Extrude_Click(object sender, RoutedEventArgs e)
    {
        if(_partSession is not null){BeginGraphExtrude();return;}
        if (_sketch is null || _inSketch) return;
        if (!_sketch.TryGetExtrusionProfile(out _, out _) && !_sketch.TryGetSingleCircle(out _))
        {
            Status("Ekstrüzyon için kapalı dış profil gerekiyor. İç daireler delik olarak kullanılabilir.");
            return;
        }

        _depthWheel.Reset();
        _extrudeCommandActive = true;
        ExtrudeDepthTextBox.Text = _extrudeDepth.ToString("0.###", CultureInfo.CurrentCulture);
        ExtrudePanel.Visibility = Visibility.Visible;
        PropertyTitle.Text = "Ekstrüzyon";
        PropertyInfo.Text = "Kapalı Çizim1 profilini kendi düzlem normalinde gerçek OCCT B-Rep katıya dönüştürür.";
        ManagerTabs.SelectedItem = PropertyManagerTab;
        UpdateExtrudePreview();
        Status("Ekstrüzyon önizlemesi • mouse tekeri: ±10 mm • ✓ ile onaylayın.");
    }

    private void ExtrudeDepth_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_extrudeCommandActive && IsLoaded) UpdateExtrudePreview();
    }

    private bool TryReadDepth(out double depth)
    {
        var text = ExtrudeDepthTextBox.Text.Trim();
        var parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out depth) ||
                     double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out depth);
        return parsed && double.IsFinite(depth) && Math.Abs(depth) >= 1e-9;
    }

    private void UpdateExtrudePreview()
    {
        if (_sketch is null || !_extrudeCommandActive) return;
        // Invalid input must invalidate the old candidate; OK cannot commit stale geometry.
        _viewer.CancelExtrusionPreview();
        if (!TryReadDepth(out var depth))
        {
            Status("Sonlu, sıfırdan farklı bir derinlik girin.");
            return;
        }

        var p = _sketch.Plane;
        bool ok;
        if (_sketch.TryGetExtrusionProfile(out var loop, out var polygonHoles, out var holes))
        {
            var uv = new double[loop.Count * 2];
            for (var i = 0; i < loop.Count; i++)
            {
                uv[i * 2] = loop[i].X;
                uv[i * 2 + 1] = loop[i].Y;
            }

            var polygonHoleData = new List<double>();
            foreach (var hole in polygonHoles)
            {
                polygonHoleData.Add(hole.Count);
                foreach (var point in hole)
                {
                    polygonHoleData.Add(point.X);
                    polygonHoleData.Add(point.Y);
                }
            }

            var holeData = new double[holes.Count * 3];
            for (var i = 0; i < holes.Count; i++)
            {
                var circle = holes[i];
                var center = _sketch.Point(circle.CenterPointId).Position;
                holeData[i * 3] = center.X;
                holeData[i * 3 + 1] = center.Y;
                holeData[i * 3 + 2] = circle.Radius;
            }

            ok = _viewer.PreviewExtrudeProfile(uv, polygonHoleData.ToArray(), holeData,
                p.Origin.X, p.Origin.Y, p.Origin.Z,
                p.U.X, p.U.Y, p.U.Z,
                p.V.X, p.V.Y, p.V.Z,
                p.Normal.X, p.Normal.Y, p.Normal.Z,
                depth);
        }
        else if (_sketch.TryGetSingleCircle(out var circle))
        {
            var c = _sketch.Point(circle.CenterPointId).Position;
            ok = _viewer.PreviewExtrudeCircle(c.X, c.Y, circle.Radius,
                p.Origin.X, p.Origin.Y, p.Origin.Z,
                p.U.X, p.U.Y, p.U.Z,
                p.V.X, p.V.Y, p.V.Z,
                p.Normal.X, p.Normal.Y, p.Normal.Z,
                depth);
        }
        else ok = false;

        if (!ok) Status("Ekstrüzyon önizlemesi üretilemedi; profil kapalı mı kontrol edin.");
    }

    private void ExtrudeOk_Click(object sender, RoutedEventArgs e)
    {
        if (!_extrudeCommandActive || _sketch is null || !TryReadDepth(out var committedDepth)) return;
        var beforeFeature = CaptureFeatureState();
        UpdateExtrudePreview();
        if (!_viewer.CommitExtrusion())
        {
            Status("Ekstrüzyon onaylanamadı.");
            return;
        }

        _extrudeDepth = committedDepth;
        _hasSolid = true;
        if (beforeFeature.Depth is null || Math.Abs(beforeFeature.Depth.Value - committedDepth) > 1e-9)
            _featureHistory.Record(beforeFeature, CaptureFeatureState());
        _editingExtrude = false;
        _extrudeCommandActive = false;
        ExtrudePanel.Visibility = Visibility.Collapsed;
        _viewer.ClearSketch();
        _viewer.ClearPreview();
        _viewer.SetSelectionMode(CadSelectionMode.Face);

        EnsureExtrudeNode();

        ManagerTabs.SelectedItem = FeatureManagerTab;
        Status("Ekstrüzyon1 oluşturuldu • gerçek OCCT B-Rep katı • yüz seçimi aktif.");
        RefreshCommandState();
    }

    private void ExtrudeCancel_Click(object sender, RoutedEventArgs e) => CancelExtrude();

    private void CancelExtrude()
    {
        if(_graphFeatureEdit is not null){EndGraphFeature();return;}
        _viewer.CancelExtrusionPreview();
        if (_editingExtrude)
        {
            _viewer.ShowCommittedSolid();
            _hasSolid = true;
            _editingExtrude = false;
            Status($"Ekstrüzyon1 düzenlemesi iptal edildi • önceki {_extrudeDepth:0.###} mm katı geri yüklendi.");
        }
        else
        {
            Status("Ekstrüzyon iptal edildi.");
        }

        _extrudeCommandActive = false;
        ExtrudePanel.Visibility = Visibility.Collapsed;
        ManagerTabs.SelectedItem = FeatureManagerTab;
        RefreshCommandState();
    }

    private void FeatureTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if(_partSession is not null&&SelectedGraphId is Guid id){EditGraphFeature(id);e.Handled=true;return;}
        if (_inSketch || _extrudeCommandActive) return;
        if (FeatureTree.SelectedItem is TreeViewItem sketchItem && sketchItem.Tag as string == "sketch:1")
        {
            StartSketch_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (FeatureTree.SelectedItem is not TreeViewItem item ||
            item.Tag is not string tag ||
            tag != "feature:extrude1" ||
            _sketch is null ||
            !_hasSolid)
            return;

        // Keep the last committed B-Rep alive so Cancel/Escape is lossless.
        // Only hide its presentation while the candidate replacement is previewed.
        _viewer.HideCommittedSolid();
        _editingExtrude = true;
        _depthWheel.Reset();
        _extrudeCommandActive = true;
        ExtrudeDepthTextBox.Text = _extrudeDepth.ToString("0.###", CultureInfo.CurrentCulture);
        ExtrudePanel.Visibility = Visibility.Visible;
        PropertyTitle.Text = "Ekstrüzyon1";
        PropertyInfo.Text = "Mevcut özelliği düzenliyorsunuz. Derinliği değiştirin; önizleme gerçek B-Rep katıyı yeniden oluşturur.";
        ManagerTabs.SelectedItem = PropertyManagerTab;
        UpdateExtrudePreview();
        Status($"Ekstrüzyon1 düzenleniyor • {_extrudeDepth:0.###} mm • mouse tekeri ±10 mm • ✓ ile onaylayın.");
        e.Handled = true;
    }

    private void FeatureTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if(_partSession is not null)RefreshCommandState();
        if(e.NewValue is TreeViewItem graphItem&&graphItem.Tag is Guid graphId&&
           _partSession?.Document.Features.FirstOrDefault(f=>f.Id==graphId) is FeatureNode graphNode)
        {
            switch(graphNode.Parameters)
            {
                case ReferencePlaneFeatureParameters referencePlane:
                    _selectedPlane=ReferencePlaneFrame(referencePlane,graphNode.Name);
                    _viewer.ClearSelection();
                    PropertyTitle.Text=graphNode.Name;
                    PropertyInfo.Text=$"Referans düzlem • {referencePlane.BasePlane.Name} • Ofset {referencePlane.Offset:0.###} mm";
                    Status(graphNode.Name+" seçildi • Çizim/Dik Bak/Aynala/Draft için kullanılabilir.");
                    return;
                case ReferenceAxisFeatureParameters axis:
                    PropertyTitle.Text=graphNode.Name;
                    PropertyInfo.Text=$"Referans ekseni • O({axis.Origin.X:0.###},{axis.Origin.Y:0.###},{axis.Origin.Z:0.###}) • Yön({axis.Direction.X:0.###},{axis.Direction.Y:0.###},{axis.Direction.Z:0.###})";
                    Status(graphNode.Name+" seçildi • Dairesel Dizilim ekseni olarak kullanılabilir.");
                    return;
                case ReferencePointFeatureParameters point:
                    PropertyTitle.Text=graphNode.Name;
                    PropertyInfo.Text=$"Referans noktası • X={point.Position.X:0.###}, Y={point.Position.Y:0.###}, Z={point.Position.Z:0.###}";
                    Status(graphNode.Name+" seçildi.");
                    return;
            }
        }
        if (e.NewValue is not TreeViewItem item || item.Tag is not string tag) return;
        switch (tag)
        {
            case "plane:front": _selectedPlane = StandardPlanes.Front; break;
            case "plane:top": _selectedPlane = StandardPlanes.Top; break;
            case "plane:right": _selectedPlane = StandardPlanes.Right; break;
            default: return;
        }
        _viewer.ClearSelection();
        PropertyTitle.Text = item.Header?.ToString() ?? "Düzlem";
        PropertyInfo.Text = "Çizim komutu başladığında sketch bu düzlemin gerçek 3B frame'i üzerinde oluşturulur.";
        Status($"{_selectedPlane.Name} seçildi • Çizim komutuna basın.");
    }

    private bool NormalToSelectedSupport()
    {
        if (_viewer.TryGetSelectedPlanarFace(out var face))
        {
            _viewer.SetNormalView(
                face.Normal.X,face.Normal.Y,face.Normal.Z,
                face.V.X,face.V.Y,face.V.Z);
            _viewer.FitAll();
            Status("Seçili yüzeye dik bakış • ekran yönü yüz kenarına hizalandı.");
            return true;
        }

        if(SelectedGraphNode?.Parameters is ReferencePlaneFeatureParameters referencePlane)
        {
            var plane=ReferencePlaneFrame(referencePlane,SelectedGraphNode.Name);
            _viewer.SetNormalView(plane.Normal.X,plane.Normal.Y,plane.Normal.Z,plane.V.X,plane.V.Y,plane.V.Z);
            _viewer.FitAll();Status(plane.Name+" • dik bakış");return true;
        }

        if (FeatureTree.SelectedItem is TreeViewItem item && item.Tag is string tag && tag.StartsWith("plane:",StringComparison.Ordinal))
        {
            var plane=_selectedPlane;
            _viewer.SetNormalView(
                plane.Normal.X,plane.Normal.Y,plane.Normal.Z,
                plane.V.X,plane.V.Y,plane.V.Z);
            _viewer.FitAll();
            Status(plane.Name+" • dik bakış");
            return true;
        }

        Status("Dik Bak için düzlemsel bir yüz veya Ön/Üst/Sağ Düzlem seçin.");
        return false;
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => _viewer.FitAll();
    private void NormalTo_Click(object sender,RoutedEventArgs e) => NormalToSelectedSupport();
    private void Iso_Click(object sender, RoutedEventArgs e) => _viewer.SetIsoView();
    private void Front_Click(object sender, RoutedEventArgs e) => _viewer.SetFrontView();
    private void Top_Click(object sender, RoutedEventArgs e) => _viewer.SetTopView();
    private void Right_Click(object sender, RoutedEventArgs e) => _viewer.SetRightView();

    private void CadCommandPlaceholder_Click(object sender,RoutedEventArgs e)
    {
        var name=(sender as Button)?.Content?.ToString()??"CAD komutu";
        PropertyTitle.Text=name;
        PropertyInfo.Text=name+" komutu CommandManager'da aktif. Bu komut için gerçek parametrik OCCT B-Rep motoru bağlanmadan sahte geometri üretilmez.";
        ManagerTabs.SelectedItem=PropertyManagerTab;
        Status(name+" • komut yüzeyi hazır; gerçek feature motoru geliştirme sırasına alındı.");
    }

    private void ShadedEdges_Click(object sender,RoutedEventArgs e)
    {
        _viewer.SetDisplayStyle(CadDisplayStyle.ShadedWithEdges);
        Status("Görünüm: Gölgeli + Kenarlı");
    }
    private void Shaded_Click(object sender,RoutedEventArgs e)
    {
        _viewer.SetDisplayStyle(CadDisplayStyle.Shaded);
        Status("Görünüm: Gölgeli");
    }
    private void Wireframe_Click(object sender,RoutedEventArgs e)
    {
        _viewer.SetDisplayStyle(CadDisplayStyle.Wireframe);
        Status("Görünüm: Tel Kafes");
    }

    private void FaceSelection_Click(object sender, RoutedEventArgs e)
    {
        _viewer.SetSelectionMode(CadSelectionMode.Face);
        Status("Yüz seçimi aktif • mouse üstünde OCCT pre-highlight görünür.");
    }

    private void EdgeSelection_Click(object sender, RoutedEventArgs e)
    {
        _viewer.SetSelectionMode(CadSelectionMode.Edge);
        Status("Kenar seçimi aktif • mouse üstünde OCCT pre-highlight görünür.");
    }

    private void VertexSelection_Click(object sender, RoutedEventArgs e)
    {
        _viewer.SetSelectionMode(CadSelectionMode.Vertex);
        Status("Köşe seçimi aktif • mouse üstünde OCCT pre-highlight görünür.");
    }

    private void SavePart_Click(object sender, RoutedEventArgs e)
    {
        if ((_sketch is null && _partSession is null) || _inSketch || _extrudeCommandActive)
        { Status("Kaydetmeden önce çizim/özellik düzenlemesini onaylayın veya iptal edin."); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "SrcCad Parça (*.scpart)|*.scpart", DefaultExt = ".scpart", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if(_partSession is not null)SrcCad.Core.Features.FeatureDocumentFile.Save(dialog.FileName,_partSession.Document);
            else PartFile.Save(dialog.FileName,CaptureFeatureState());
            Status("Parametrik parça kaydedildi: " + dialog.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { MessageBox.Show(this,ex.Message,"Parça kaydedilemedi",MessageBoxButton.OK,MessageBoxImage.Error); }
    }

    private void OpenPart_Click(object sender, RoutedEventArgs e)
    {
        if (_inSketch || _extrudeCommandActive)
        { Status("Açmadan önce mevcut komutu onaylayın veya iptal edin."); return; }
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "SrcCad Parça (*.scpart)|*.scpart" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if(TryOpenGraphFile(dialog.FileName))return;
            var state = PartFile.Load(dialog.FileName);
            if (!ApplyFeatureState(state)) { Status("Dosya okundu fakat OCCT rebuild başarısız; mevcut belge korundu."); return; }
            ResetPartWorkbench();
            if (_sketchNode is null)
            {
                _sketchNode = new TreeViewItem { Header="Çizim1",Tag="sketch:1" };
                PartNode.Items.Add(_sketchNode);
            }
            _featureHistory.Clear(); _sketchUndo.Clear(); _sketchRedo.Clear();
            _selectedPlane = state.Plane;
            _viewer.FitAll();
            Status("Parametrik parça açıldı; ölçü ve ilişkiler düzenlenebilir: " + dialog.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { MessageBox.Show(this,ex.Message,"Parça açılamadı",MessageBoxButton.OK,MessageBoxImage.Error); }
    }

    private void NewPart_Click(object sender, RoutedEventArgs e)
    {
        ExitAssemblyMode();
        ExitDrawingMode();
        ResetPartWorkbench();
        _commandManager.Cancel();
        _sketchCommand = null;
        _commandContext = null;
        _viewer.ClearModel();
        _sketch = null;
        _sourceSketchEditBefore = null;
        ResetSketchInteraction();
        _sketchUndo.Clear();
        _sketchRedo.Clear();
        _extrudeDepth = 20.0;
        _inSketch = false;
        _smartDimensionMode = false;
        ResetSmartDimensionTargets();
        ClearSketchSelection(false);
        _hasSolid = false;
        _editingExtrude = false;
        _featureHistory.Clear();
        _extrudeCommandActive = false;
        ExtrudePanel.Visibility = Visibility.Collapsed;
        SketchTab.IsEnabled = false;
        CommandTabs.SelectedItem = FeaturesTab;

        if (_sketchNode is not null) PartNode.Items.Remove(_sketchNode);
        if (_extrudeNode is not null) PartNode.Items.Remove(_extrudeNode);
        _sketchNode = null;
        _extrudeNode = null;
        FrontPlaneNode.IsSelected = true;
        ManagerTabs.SelectedItem = FeatureManagerTab;
        RefreshCommandState();
        Status("Yeni Parça • bir düzlem seçin ve Çizim'e basın.");
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshCommandState()
    {
        StartSketchButton.IsEnabled = !_inSketch && !_extrudeCommandActive;
        StartSketchButton.Content = "Çizim";
        CancelSourceSketchEditButton.Visibility = _sourceSketchEditBefore is null && _graphSketchEdit is null ? Visibility.Collapsed : Visibility.Visible;
        NewProfileSketchButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&(_hasSolid||_partSession is not null);
        CutButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid&&_partSession is not null&&CutProfileCandidate() is not null;
        FilletButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        ChamferButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        HoleButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        RevolveButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&(_sketch is not null||_partSession is not null);
        RevolveCutButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        SweepButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_partSession is not null;
        LoftButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_partSession is not null;
        ShellButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        DraftButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        RibButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid&&_partSession is not null&&RibSketchCandidate() is not null;
        MirrorButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        LinearPatternButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        CircularPatternButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid;
        ReferenceGeometryButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&(_sketch is not null||_partSession is not null);
        BaseFlangeButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&!_hasSolid&&(_sketch is not null||_partSession is not null);
        ConvertToSheetMetalButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid&&_partSession is not null&&
            !_partSession.Document.Features.Any(n=>n.Kind is SrcCad.Core.Features.FeatureKind.SheetMetalBase or SrcCad.Core.Features.FeatureKind.SheetMetalConvert);
        EdgeFlangeButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid&&_partSession is not null&&
            _partSession.Document.Features.Any(n=>n.Kind is SrcCad.Core.Features.FeatureKind.SheetMetalBase or SrcCad.Core.Features.FeatureKind.SheetMetalConvert);
        SheetCutButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid&&_partSession is not null&&
            _partSession.Document.Features.Any(n=>n.Kind is SrcCad.Core.Features.FeatureKind.SheetMetalBase or SrcCad.Core.Features.FeatureKind.SheetMetalConvert)&&CutProfileCandidate() is not null;
        EndCapButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&_hasSolid&&_partSession is not null;
        if(_partSession is not null)
        {
            ExtrudeButton.IsEnabled=!_inSketch&&!_extrudeCommandActive&&!_hasSolid&&SelectedGraphId is Guid sketchId&&SelectedGraphNode?.Kind==SrcCad.Core.Features.FeatureKind.Sketch;
            return;
        }
        ExtrudeButton.IsEnabled = !_inSketch && !_hasSolid && _sketch is not null &&
                                  (_sketch.TryGetExtrusionProfile(out _, out _) || _sketch.TryGetSingleCircle(out _));
    }

    private void Status(string text) => StatusText.Text = text;
}

