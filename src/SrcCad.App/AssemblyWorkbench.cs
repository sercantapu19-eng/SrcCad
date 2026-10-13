using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SrcCad.Core.Assembly;
using SrcCad.Core.Features;
using SrcCad.Core.Geometry;

namespace SrcCad.App;

public partial class MainWindow
{
    private AssemblyDocument? _assemblyDocument;
    private string? _assemblyPath;
    private readonly Dictionary<Guid,string> _assemblyBodies=new();
    private TreeViewItem? _assemblyRoot;
    private Guid? _assemblyEditingComponentId;
    private Guid? _assemblyEditingMateId;
    private readonly Stack<AssemblyDocument> _assemblyUndo=new();
    private readonly Stack<AssemblyDocument> _assemblyRedo=new();

    private bool InAssemblyMode=>_assemblyDocument is not null;

    private void SetAssemblyDocument(AssemblyDocument next,bool record=true)
    {
        var safe=AssemblyDocumentFile.Clone(next);
        if(record&&_assemblyDocument is not null)
        {
            _assemblyUndo.Push(AssemblyDocumentFile.Clone(_assemblyDocument));
            _assemblyRedo.Clear();
        }
        _assemblyDocument=safe;
    }

    private void ResetAssemblyHistory()
    {
        _assemblyUndo.Clear();_assemblyRedo.Clear();
    }

    private void RestoreAssemblyDocument(AssemblyDocument document)
    {
        _assemblyDocument=AssemblyDocumentFile.Clone(document);
        _assemblyBodies.Clear();
        foreach(var component in _assemblyDocument.Components)
        {
            try{_assemblyBodies[component.Id]=RebuildAssemblyComponent(component.SourcePath);}
            catch(Exception ex){Status(component.Name+" geri yüklenemedi: "+ex.Message);}
        }
        RefreshAssemblyTree();RenderAssembly();
    }

    internal void UndoAssembly()
    {
        if(_assemblyDocument is null||_assemblyUndo.Count==0){Status("Montajda geri alınacak işlem yok.");return;}
        _assemblyRedo.Push(AssemblyDocumentFile.Clone(_assemblyDocument));
        RestoreAssemblyDocument(_assemblyUndo.Pop());
        Status("Montaj işlemi geri alındı.");
    }

    internal void RedoAssembly()
    {
        if(_assemblyDocument is null||_assemblyRedo.Count==0){Status("Montajda yinelenecek işlem yok.");return;}
        _assemblyUndo.Push(AssemblyDocumentFile.Clone(_assemblyDocument));
        RestoreAssemblyDocument(_assemblyRedo.Pop());
        Status("Montaj işlemi yinelendi.");
    }

    private void ExitAssemblyMode()
    {
        if(_assemblyRoot is not null)FeatureTree.Items.Remove(_assemblyRoot);
        _assemblyRoot=null;
        _assemblyDocument=null;
        _assemblyPath=null;
        _assemblyBodies.Clear();
        ResetAssemblyHistory();
        _assemblyEditingComponentId=null;
        _assemblyEditingMateId=null;
        PartNode.Visibility=Visibility.Visible;
        AssemblyMovePanel.Visibility=Visibility.Collapsed;
        AssemblyMatePanel.Visibility=Visibility.Collapsed;
    }

    private string RebuildAssemblyComponent(string sourcePath)
    {
        var document=FeatureDocumentFile.Load(sourcePath);
        var session=new FeatureSession<PartFeatureOutput>(document,new PartFeatureEvaluator(new OcctPartGeometryKernel()));
        return LastBody(document,session.Output)??throw new InvalidDataException("Parça katı B-Rep içermiyor.");
    }

    private void RefreshAssemblyTree(Guid? selected=null)
    {
        if(_assemblyDocument is null)return;
        if(_assemblyRoot is not null)FeatureTree.Items.Remove(_assemblyRoot);
        PartNode.Visibility=Visibility.Collapsed;
        _assemblyRoot=new TreeViewItem{Header="Montaj1",IsExpanded=true,Tag="assembly-root"};
        foreach(var component in _assemblyDocument.Components)
        {
            var t=component.Transform;
            var header=$"{component.Name}{(component.Fixed?" [Sabit]":"")}{(component.Suppressed?" [Baskılı]":"")}";
            var item=new TreeViewItem
            {
                Header=header,
                Tag=component.Id,
                ToolTip=$"X={t.Translation.X:0.###} Y={t.Translation.Y:0.###} Z={t.Translation.Z:0.###} • Rx={t.RotationDegrees.X:0.###} Ry={t.RotationDegrees.Y:0.###} Rz={t.RotationDegrees.Z:0.###}"
            };
            var menu=new ContextMenu();
            var move=new MenuItem{Header="Taşı / Döndür"};move.Click+=(_,_)=>BeginAssemblyMove(component.Id);menu.Items.Add(move);
            var fix=new MenuItem{Header=component.Fixed?"Sabitlemeyi Kaldır":"Sabitle"};fix.Click+=(_,_)=>ToggleAssemblyFixed(component.Id);menu.Items.Add(fix);
            var suppress=new MenuItem{Header=component.Suppressed?"Baskılamayı Kaldır":"Baskıla"};suppress.Click+=(_,_)=>ToggleAssemblySuppressed(component.Id);menu.Items.Add(suppress);
            var del=new MenuItem{Header="Bileşeni Sil"};del.Click+=(_,_)=>RemoveAssemblyComponent(component.Id);menu.Items.Add(del);
            item.ContextMenu=menu;
            _assemblyRoot.Items.Add(item);
            if(selected==component.Id)item.IsSelected=true;
        }
        foreach(var mate in _assemblyDocument.Mates)
        {
            var item=new TreeViewItem
            {
                Header=$"İlişki: {mate.Name} ({mate.Type}){(mate.Suppressed?" [Baskılı]":"")}",
                Tag="assembly-mate:"+mate.Id
            };
            var menu=new ContextMenu();
            var edit=new MenuItem{Header="İlişkiyi Düzenle"};
            edit.Click+=(_,_)=>BeginAssemblyMate(mate.Id);menu.Items.Add(edit);
            var suppress=new MenuItem{Header=mate.Suppressed?"Baskılamayı Kaldır":"Baskıla"};
            suppress.Click+=(_,_)=>ToggleAssemblyMateSuppressed(mate.Id);menu.Items.Add(suppress);
            var del=new MenuItem{Header="İlişkiyi Sil"};
            del.Click+=(_,_)=>RemoveAssemblyMate(mate.Id);menu.Items.Add(del);
            item.ContextMenu=menu;
            _assemblyRoot.Items.Add(item);
        }
        FeatureTree.Items.Add(_assemblyRoot);
        ManagerTabs.SelectedItem=FeatureManagerTab;
    }

    private static double[] AssemblyTransformValues(RigidTransform t)
        => new[]{t.Translation.X,t.Translation.Y,t.Translation.Z,t.RotationDegrees.X,t.RotationDegrees.Y,t.RotationDegrees.Z};

    private string ResolvedAssemblyBody(AssemblyComponent component)
    {
        if(!_assemblyBodies.TryGetValue(component.Id,out var source))
            throw new InvalidOperationException("Bileşen gövdesi yüklenmedi: "+component.Name);
        var transformed=SrcCad.Kernel.Occt.OcctFeatureKernel.Transform(source,AssemblyTransformValues(component.Transform));
        if(component.MirrorPlane is not PlaneFrame mirror)return transformed;
        return SrcCad.Kernel.Occt.OcctFeatureKernel.Mirror(transformed,
            new[]{mirror.Origin.X,mirror.Origin.Y,mirror.Origin.Z,mirror.U.X,mirror.U.Y,mirror.U.Z,mirror.Normal.X,mirror.Normal.Y,mirror.Normal.Z},false);
    }

    private void RenderAssembly(bool fit=true)
    {
        if(_assemblyDocument is null)return;
        _viewer.ClearModel();
        foreach(var component in _assemblyDocument.Components.Where(c=>!c.Suppressed))
        {
            try
            {
                var body=ResolvedAssemblyBody(component);
                _viewer.AddAssemblyBody(body,0,0,0,0,0,0);
            }
            catch(Exception ex){Status(component.Name+" görüntülenemedi: "+ex.Message);}
        }
        _viewer.SetSelectionMode(SrcCad.Kernel.Occt.CadSelectionMode.Face);
        if(fit)_viewer.FitAll();
    }

    private void NewAssembly_Click(object sender,RoutedEventArgs e)
    {
        NewPart_Click(sender,e);
        _assemblyDocument=new AssemblyDocument(Guid.NewGuid(),Array.Empty<AssemblyComponent>(),Array.Empty<AssemblyMate>());
        ResetAssemblyHistory();
        _assemblyPath=null;
        PartNode.Visibility=Visibility.Collapsed;
        RefreshAssemblyTree();
        CommandTabs.SelectedIndex=4;
        Status("Yeni Montaj • Bileşen Ekle ile .scpart parçalarını ekleyin.");
    }

    private void AddAssemblyComponent_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null)NewAssembly_Click(sender,e);
        if(_assemblyDocument is null)return;
        var dialog=new OpenFileDialog
        {
            Title="SrcCad Parçası Ekle",
            Filter="SrcCad Parça (*.scpart)|*.scpart|Tüm Dosyalar (*.*)|*.*",
            Multiselect=true
        };
        if(dialog.ShowDialog(this)!=true)return;
        Guid? last=null;
        foreach(var file in dialog.FileNames)
        {
            try
            {
                var body=RebuildAssemblyComponent(file);
                var id=Guid.NewGuid();
                var component=new AssemblyComponent(id,Path.GetFileNameWithoutExtension(file),Path.GetFullPath(file),RigidTransform.Identity);
                SetAssemblyDocument(_assemblyDocument.AddComponent(component));
                _assemblyBodies[id]=body;
                last=id;
            }
            catch(Exception ex)
            {
                MessageBox.Show(this,$"{Path.GetFileName(file)} eklenemedi:\n{ex.Message}","SrcCad Montaj",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }
        RefreshAssemblyTree(last);
        RenderAssembly();
        Status($"{_assemblyDocument.Components.Length} bileşenli montaj.");
    }

    private void SaveAssembly_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null){Status("Kaydedilecek montaj yok.");return;}
        var path=_assemblyPath;
        if(string.IsNullOrWhiteSpace(path))
        {
            var dialog=new SaveFileDialog{Title="Montajı Kaydet",Filter="SrcCad Montaj (*.scasm)|*.scasm",DefaultExt=".scasm",AddExtension=true};
            if(dialog.ShowDialog(this)!=true)return;
            path=dialog.FileName;
        }
        AssemblyDocumentFile.Save(path!,_assemblyDocument);
        _assemblyPath=path;
        Status("Montaj kaydedildi: "+Path.GetFileName(path));
    }

    private void OpenAssembly_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="SrcCad Montaj Aç",Filter="SrcCad Montaj (*.scasm)|*.scasm"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            NewAssembly_Click(sender,e);
            _assemblyDocument=AssemblyDocumentFile.Load(dialog.FileName);
            ResetAssemblyHistory();
            _assemblyPath=dialog.FileName;
            _assemblyBodies.Clear();
            foreach(var component in _assemblyDocument.Components.Where(c=>!c.Suppressed))
                _assemblyBodies[component.Id]=RebuildAssemblyComponent(component.SourcePath);
            RefreshAssemblyTree();
            RenderAssembly();
            Status("Montaj açıldı: "+Path.GetFileName(dialog.FileName));
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,ex.Message,"Montaj açılamadı",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    private AssemblyComponent? SelectedAssemblyComponent()
    {
        if(_assemblyDocument is null||(FeatureTree.SelectedItem as TreeViewItem)?.Tag is not Guid id)return null;
        return _assemblyDocument.Components.FirstOrDefault(c=>c.Id==id);
    }

    private void BeginAssemblyMove(Guid? id=null)
    {
        if(_assemblyDocument is null)return;
        var component=id is Guid explicitId?_assemblyDocument.Components.FirstOrDefault(c=>c.Id==explicitId):SelectedAssemblyComponent();
        if(component is null){Status("Taşımak için montaj ağacından bileşen seçin.");return;}
        _assemblyEditingComponentId=component.Id;
        var t=component.Transform;
        AssemblyMoveX.Text=t.Translation.X.ToString("G17",CultureInfo.CurrentCulture);
        AssemblyMoveY.Text=t.Translation.Y.ToString("G17",CultureInfo.CurrentCulture);
        AssemblyMoveZ.Text=t.Translation.Z.ToString("G17",CultureInfo.CurrentCulture);
        AssemblyRotateX.Text=t.RotationDegrees.X.ToString("G17",CultureInfo.CurrentCulture);
        AssemblyRotateY.Text=t.RotationDegrees.Y.ToString("G17",CultureInfo.CurrentCulture);
        AssemblyRotateZ.Text=t.RotationDegrees.Z.ToString("G17",CultureInfo.CurrentCulture);
        AssemblyMovePanel.Visibility=Visibility.Visible;
        AssemblyMatePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text="Bileşen Taşı / Döndür";
        PropertyInfo.Text=component.Name+" • mm / derece • gerçek OCCT gövdesi anında yeniden konumlandırılır.";
        ManagerTabs.SelectedItem=PropertyManagerTab;
    }

    private static bool TryAssemblyNumber(string text,out double value)
        => double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out value)||
           double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value);

    private void AssemblyMoveOk_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null||_assemblyEditingComponentId is not Guid id)return;
        if(!TryAssemblyNumber(AssemblyMoveX.Text,out var x)||!TryAssemblyNumber(AssemblyMoveY.Text,out var y)||
           !TryAssemblyNumber(AssemblyMoveZ.Text,out var z)||!TryAssemblyNumber(AssemblyRotateX.Text,out var rx)||
           !TryAssemblyNumber(AssemblyRotateY.Text,out var ry)||!TryAssemblyNumber(AssemblyRotateZ.Text,out var rz))
        {Status("Montaj konum değerleri geçersiz.");return;}
        var component=_assemblyDocument.Component(id);
        var updated=component with{Transform=new RigidTransform(new Vector3d(x,y,z),new Vector3d(rx,ry,rz)).Validate()};
        SetAssemblyDocument(_assemblyDocument with{Components=_assemblyDocument.Components.Select(c=>c.Id==id?updated:c).ToArray()});
        AssemblyMovePanel.Visibility=Visibility.Collapsed;
        RefreshAssemblyTree(id);RenderAssembly(false);Status(component.Name+" konumu güncellendi.");
    }

    private void AssemblyMoveCancel_Click(object sender,RoutedEventArgs e)
    {
        AssemblyMovePanel.Visibility=Visibility.Collapsed;_assemblyEditingComponentId=null;ManagerTabs.SelectedItem=FeatureManagerTab;
    }

    private void ToggleAssemblyFixed(Guid id)
    {
        if(_assemblyDocument is null)return;
        var component=_assemblyDocument.Component(id);
        SetAssemblyDocument(_assemblyDocument with{Components=_assemblyDocument.Components.Select(c=>c.Id==id?c with{Fixed=!c.Fixed}:c).ToArray()});
        RefreshAssemblyTree(id);
    }

    private void ToggleAssemblySuppressed(Guid id)
    {
        if(_assemblyDocument is null)return;
        var component=_assemblyDocument.Component(id);
        SetAssemblyDocument(_assemblyDocument with
        {
            Components=_assemblyDocument.Components.Select(c=>c.Id==id?c with{Suppressed=!component.Suppressed}:c).ToArray()
        });
        RefreshAssemblyTree(id);RenderAssembly(false);
        Status(component.Suppressed?component.Name+" yeniden etkinleştirildi.":component.Name+" baskılandı.");
    }

    private void ToggleAssemblyMateSuppressed(Guid id)
    {
        if(_assemblyDocument is null)return;
        var mate=_assemblyDocument.Mates.FirstOrDefault(m=>m.Id==id);
        if(mate is null)return;
        SetAssemblyDocument(_assemblyDocument with
        {
            Mates=_assemblyDocument.Mates.Select(m=>m.Id==id?m with{Suppressed=!m.Suppressed}:m).ToArray()
        });
        RefreshAssemblyTree();
        Status(mate.Suppressed?mate.Name+" yeniden etkinleştirildi.":mate.Name+" baskılandı.");
    }

    private void RemoveAssemblyMate(Guid id)
    {
        if(_assemblyDocument is null)return;
        var mate=_assemblyDocument.Mates.FirstOrDefault(m=>m.Id==id);
        if(mate is null)return;
        SetAssemblyDocument(_assemblyDocument with{Mates=_assemblyDocument.Mates.Where(m=>m.Id!=id).ToArray()});
        RefreshAssemblyTree();
        Status(mate.Name+" silindi.");
    }

    private void RemoveAssemblyComponent(Guid id)
    {
        if(_assemblyDocument is null)return;
        SetAssemblyDocument(_assemblyDocument.RemoveComponent(id));
        _assemblyBodies.Remove(id);RefreshAssemblyTree();RenderAssembly();Status("Bileşen montajdan kaldırıldı.");
    }

    internal bool DeleteSelectedAssemblyTreeItem()
    {
        if(_assemblyDocument is null||FeatureTree.SelectedItem is not TreeViewItem item)return false;
        if(item.Tag is Guid componentId&&_assemblyDocument.Components.Any(c=>c.Id==componentId))
        {
            RemoveAssemblyComponent(componentId);return true;
        }
        if(item.Tag is string tag&&tag.StartsWith("assembly-mate:",StringComparison.Ordinal)&&
           Guid.TryParse(tag["assembly-mate:".Length..],out var mateId))
        {
            RemoveAssemblyMate(mateId);return true;
        }
        return false;
    }

    private void AssemblyMove_Click(object sender,RoutedEventArgs e)=>BeginAssemblyMove();

    private void AssemblyMate_Click(object sender,RoutedEventArgs e)=>BeginAssemblyMate(null);

    private void BeginAssemblyMate(Guid? mateId)
    {
        if(_assemblyDocument is null||_assemblyDocument.Components.Length<2)
        {Status("Montaj ilişkisi için en az iki bileşen gerekiyor.");return;}
        _assemblyEditingMateId=mateId;
        var existing=mateId is Guid id?_assemblyDocument.Mates.FirstOrDefault(m=>m.Id==id):null;

        AssemblyMateA.Items.Clear();AssemblyMateB.Items.Clear();AssemblyMateType.Items.Clear();
        AssemblyMateAReference.Items.Clear();AssemblyMateBReference.Items.Clear();
        foreach(var component in _assemblyDocument.Components)
        {
            var aItem=new ComboBoxItem{Content=component.Name,Tag=component.Id};
            var bItem=new ComboBoxItem{Content=component.Name,Tag=component.Id};
            AssemblyMateA.Items.Add(aItem);AssemblyMateB.Items.Add(bItem);
            if(existing?.A.ComponentId==component.Id)AssemblyMateA.SelectedItem=aItem;
            if(existing?.B.ComponentId==component.Id)AssemblyMateB.SelectedItem=bItem;
        }
        foreach(var reference in AssemblyReferenceCatalog.Standard)
        {
            var aRef=new ComboBoxItem{Content=reference.Name,Tag=new AssemblyReference(Guid.Empty,reference.Kind,reference.Semantic)};
            var bRef=new ComboBoxItem{Content=reference.Name,Tag=new AssemblyReference(Guid.Empty,reference.Kind,reference.Semantic)};
            AssemblyMateAReference.Items.Add(aRef);AssemblyMateBReference.Items.Add(bRef);
            if(existing is not null&&existing.A.Kind==reference.Kind&&existing.A.SemanticName==reference.Semantic)
                AssemblyMateAReference.SelectedItem=aRef;
            if(existing is not null&&existing.B.Kind==reference.Kind&&existing.B.SemanticName==reference.Semantic)
                AssemblyMateBReference.SelectedItem=bRef;
        }
        foreach(var type in new[]{MateType.Coincident,MateType.Concentric,MateType.Parallel,MateType.Perpendicular,MateType.Distance,MateType.Angle})
        {
            var item=new ComboBoxItem{Content=type.ToString(),Tag=type};AssemblyMateType.Items.Add(item);
            if(existing?.Type==type)AssemblyMateType.SelectedItem=item;
        }
        if(AssemblyMateA.SelectedItem is null)AssemblyMateA.SelectedIndex=0;
        if(AssemblyMateB.SelectedItem is null)AssemblyMateB.SelectedIndex=Math.Min(1,AssemblyMateB.Items.Count-1);
        if(AssemblyMateAReference.SelectedItem is null)AssemblyMateAReference.SelectedIndex=0;
        if(AssemblyMateBReference.SelectedItem is null)AssemblyMateBReference.SelectedIndex=0;
        if(AssemblyMateType.SelectedItem is null)AssemblyMateType.SelectedIndex=0;
        AssemblyMateValue.Text=(existing?.Value??0).ToString("G17",CultureInfo.CurrentCulture);
        AssemblyMatePanel.Visibility=Visibility.Visible;AssemblyMovePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=existing is null?"Montaj İlişkisi":"Montaj İlişkisini Düzenle";
        PropertyInfo.Text="İlişki .scasm içinde kalıcıdır; ✓ tek Undo/Redo transaction olarak uygulanır.";
        ManagerTabs.SelectedItem=PropertyManagerTab;
    }

    private void AssemblyMateOk_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null||
           (AssemblyMateA.SelectedItem as ComboBoxItem)?.Tag is not Guid aId||
           (AssemblyMateB.SelectedItem as ComboBoxItem)?.Tag is not Guid bId||aId==bId||
           (AssemblyMateAReference.SelectedItem as ComboBoxItem)?.Tag is not AssemblyReference aTemplate||
           (AssemblyMateBReference.SelectedItem as ComboBoxItem)?.Tag is not AssemblyReference bTemplate||
           (AssemblyMateType.SelectedItem as ComboBoxItem)?.Tag is not MateType type||
           !TryAssemblyNumber(AssemblyMateValue.Text,out var value))
        {Status("Geçerli iki bileşen, referans ve ilişki değeri seçin.");return;}

        var previous=_assemblyEditingMateId is Guid editId?_assemblyDocument.Mates.FirstOrDefault(m=>m.Id==editId):null;
        var mate=new AssemblyMate(previous?.Id??Guid.NewGuid(),previous?.Name??"İlişki"+(_assemblyDocument.Mates.Length+1),type,
            aTemplate with{ComponentId=aId},
            bTemplate with{ComponentId=bId},value,previous?.Suppressed??false);

        try
        {
            var withoutEdited=previous is null
                ?_assemblyDocument
                :_assemblyDocument with{Mates=_assemblyDocument.Mates.Where(m=>m.Id!=previous.Id).ToArray()};
            var solved=AssemblyMateSolver.Apply(withoutEdited,mate);
            var mates=previous is null
                ?solved.Mates.Append(mate).ToArray()
                :_assemblyDocument.Mates.Select(m=>m.Id==mate.Id?mate:m).ToArray();
            SetAssemblyDocument(solved with{Mates=mates});
        }
        catch(Exception ex)
        {
            Status("Montaj ilişkisi çözülemedi: "+ex.Message);return;
        }

        _assemblyEditingMateId=null;
        AssemblyMatePanel.Visibility=Visibility.Collapsed;RefreshAssemblyTree(bId);RenderAssembly(false);
        Status(mate.Name+(previous is null?" uygulandı.":" güncellendi."));
    }

    private void AssemblyMateCancel_Click(object sender,RoutedEventArgs e)
    {
        AssemblyMatePanel.Visibility=Visibility.Collapsed;_assemblyEditingMateId=null;ManagerTabs.SelectedItem=FeatureManagerTab;
    }

    private void AssemblyExplode_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null)return;
        _viewer.ClearModel();
        var index=0;
        foreach(var component in _assemblyDocument.Components.Where(c=>!c.Suppressed))
        {
            if(!_assemblyBodies.TryGetValue(component.Id,out var body))continue;
            var t=component.Transform;var extra=40.0*index++;
            _viewer.AddAssemblyBody(body,t.Translation.X+extra,t.Translation.Y,t.Translation.Z,
                t.RotationDegrees.X,t.RotationDegrees.Y,t.RotationDegrees.Z);
        }
        _viewer.FitAll();Status("Patlatılmış görünüm • montaj transformları değiştirilmedi.");
    }

    private void AssemblyResetView_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null)return;RenderAssembly();Status("Montaj normal görünüm.");
    }

    private void AssemblyLinearPattern_Click(object sender,RoutedEventArgs e)=>BeginAssemblyPattern(false);
    private void AssemblyCircularPattern_Click(object sender,RoutedEventArgs e)=>BeginAssemblyPattern(true);

    private void BeginAssemblyPattern(bool circular)
    {
        var seed=SelectedAssemblyComponent();
        if(_assemblyDocument is null||seed is null){Status("Dizilim için montaj ağacından bileşen seçin.");return;}
        _assemblyEditingComponentId=seed.Id;
        AssemblyPatternTitle.Text=circular?"Dairesel Bileşen Dizilimi":"Doğrusal Bileşen Dizilimi";
        AssemblyPatternCount.Text=circular?"4":"3";
        AssemblyPatternValue.Text=circular?"360":"50";
        AssemblyPatternValueLabel.Text=circular?"Toplam Açı (°)":"Aralık (mm)";
        AssemblyPatternCircular.IsChecked=circular;
        AssemblyPatternPanel.Visibility=Visibility.Visible;
        AssemblyMovePanel.Visibility=Visibility.Collapsed;AssemblyMatePanel.Visibility=Visibility.Collapsed;
        PropertyTitle.Text=AssemblyPatternTitle.Text;PropertyInfo.Text=seed.Name+" çoğaltılacak; yeni bileşen örnekleri .scasm içinde ayrı instance olarak saklanır.";
        ManagerTabs.SelectedItem=PropertyManagerTab;
    }

    private void AssemblyPatternOk_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null||_assemblyEditingComponentId is not Guid id||
           !_assemblyDocument.Components.Any(c=>c.Id==id)||
           !int.TryParse(AssemblyPatternCount.Text,out var count)||count<2||count>200||
           !TryAssemblyNumber(AssemblyPatternValue.Text,out var value)||value<=0)
        {Status("Dizilim adet/değer bilgisi geçersiz.");return;}
        var seed=_assemblyDocument.Component(id);
        var list=_assemblyDocument.Components.ToList();
        var circular=AssemblyPatternCircular.IsChecked==true;
        for(var i=1;i<count;i++)
        {
            var transform=seed.Transform;
            if(circular)
            {
                var step=Math.Abs(value-360)<1e-9?value/count:value/(count-1);
                transform=transform with{RotationDegrees=new Vector3d(transform.RotationDegrees.X,transform.RotationDegrees.Y,transform.RotationDegrees.Z+step*i)};
            }
            else transform=transform with{Translation=transform.Translation+new Vector3d(value*i,0,0)};
            var clone=seed with{Id=Guid.NewGuid(),Name=seed.Name+"_D"+(i+1),Transform=transform,Fixed=false};
            list.Add(clone);
            _assemblyBodies[clone.Id]=_assemblyBodies[seed.Id];
        }
        SetAssemblyDocument(_assemblyDocument with{Components=list.ToArray()});
        AssemblyPatternPanel.Visibility=Visibility.Collapsed;RefreshAssemblyTree();RenderAssembly();
        Status((circular?"Dairesel":"Doğrusal")+" bileşen dizilimi oluşturuldu.");
    }

    private void AssemblyPatternCancel_Click(object sender,RoutedEventArgs e)
    {
        AssemblyPatternPanel.Visibility=Visibility.Collapsed;_assemblyEditingComponentId=null;ManagerTabs.SelectedItem=FeatureManagerTab;
    }

    private void AssemblyMirror_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null)return;
        var seed=SelectedAssemblyComponent();
        if(seed is null){Status("Aynalamak için montaj ağacından bileşen seçin.");return;}
        var clone=seed with
        {
            Id=Guid.NewGuid(),Name=seed.Name+"_Ayna",Fixed=false,MirrorPlane=_selectedPlane
        };
        SetAssemblyDocument(_assemblyDocument.AddComponent(clone));
        _assemblyBodies[clone.Id]=_assemblyBodies[seed.Id];
        RefreshAssemblyTree(clone.Id);RenderAssembly();
        Status(seed.Name+" • "+_selectedPlane.Name+" düzlemine göre aynalandı.");
    }

    private void AssemblyInterference_Click(object sender,RoutedEventArgs e)
    {
        if(_assemblyDocument is null||_assemblyDocument.Components.Count(c=>!c.Suppressed)<2)
        {Status("Çakışma kontrolü için en az iki görünür bileşen gerekiyor.");return;}
        var visible=_assemblyDocument.Components.Where(c=>!c.Suppressed).ToArray();
        var collisions=new List<string>();
        for(var i=0;i<visible.Length;i++)for(var j=i+1;j<visible.Length;j++)
        {
            var a=visible[i];var b=visible[j];
            try
            {
                var av=ResolvedAssemblyBody(a);var bv=ResolvedAssemblyBody(b);
                var volume=SrcCad.Kernel.Occt.OcctFeatureKernel.CommonVolume(av,new double[6],bv,new double[6]);
                if(volume>1e-7)collisions.Add($"{a.Name} ↔ {b.Name}: {volume:0.###} mm³");
            }
            catch(Exception ex){collisions.Add($"{a.Name} ↔ {b.Name}: kontrol hatası ({ex.Message})");}
        }
        PropertyTitle.Text="Çakışma Kontrolü";
        PropertyInfo.Text=collisions.Count==0?"Çakışma bulunmadı.":string.Join("\n",collisions);
        ManagerTabs.SelectedItem=PropertyManagerTab;
        Status(collisions.Count==0?"Montaj çakışma kontrolü: temiz.":$"{collisions.Count} çakışma/uyarı bulundu.");
    }
}
