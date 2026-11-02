using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Gp200.Replica;

public sealed class LibraryEntry:System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    string displayLabel="";
    public string DisplayLabel=>displayLabel;
    internal void RefreshDisplayLabel(){string next=ToString();if(next==displayLabel)return;displayLabel=next;PropertyChanged?.Invoke(this,new(nameof(DisplayLabel)));}
    public required string Path {get;init;}
    public required byte[] Original {get;set;}
    public required PresetDocument Document {get;set;}
    public byte[]? EditingBaseline {get;set;}
    public Stack<byte[]> Undo {get;}=new();
    public Stack<byte[]> Redo {get;}=new();
    internal bool AcceptStoredSnapshot(PresetDocument stored,Catalog catalog)
    {
        if(ScenesDirty||DeviceSlot!=stored.Index||!PresetComparison.EquivalentFunctional(Document,stored,catalog))return false;
        Original=stored.ToBytes();EditingBaseline=stored.ToBytes();return true;
    }
    public bool DeviceSnapshot {get;init;}
    public int? DeviceSlot {get;init;}
    public bool ScenesDirty {get;private set;}
    public void MarkSceneCaptured()=>ScenesDirty=true;
    public void ConfirmSceneStore()=>ScenesDirty=false;
    public void DiscardSceneChanges()=>ScenesDirty=false;
    public bool Dirty=>ScenesDirty||PresetDirty;
    bool PresetDirty=>Undo.Count==0&&Document.ToBytes().AsSpan().SequenceEqual(DeviceSlot.HasValue?EditingBaseline??Original:Original)?false:DeviceSlot.HasValue?
        !PresetComparison.EquivalentStoredSettings(Document,new PresetDocument(EditingBaseline??Original),SharedCatalog)||Undo.Count>0&&!PresetComparison.EquivalentFunctional(Document,new PresetDocument(EditingBaseline??Original),SharedCatalog):
        !Document.ToBytes().SequenceEqual(Original);
    static Catalog SharedCatalog=>CatalogRuntime.Current;
    public string LibraryName=>DeviceSlot.HasValue?new PresetDocument(Original).Name:Document.Name;
    public override string ToString()=>$"{BankService.Position(DeviceSlot??Document.Index)}   {LibraryName}{(Dirty?DeviceSlot.HasValue?UiText.T("  • en edición"):"  •":"")}";
}

public sealed partial class EditorWindow:Window
{
    static readonly Brush BackgroundBrush=Brush("#10151C"),PanelBrush=Brush("#19212C"),TextBrush=Brush("#EAF0F7"),Muted=Brush("#91A1B4"),Accent=Brush("#70E0BA");
    readonly Catalog catalog=CatalogRuntime.Current;
    readonly List<LibraryEntry> entries=[];
    PresetCollections? collections;
    readonly ListBox library=new();
    readonly TextBox search=new();
    readonly TextBlock title=new(),subtitle=new(),status=new();
    readonly StackPanel chain=new(){Orientation=Orientation.Horizontal};
    readonly StackPanel parameterPanel=new();
    readonly TabControl tabs=new();
    readonly Grid details=new();
    readonly ContentControl presetSummary=new();
    readonly TextBox name=new(),volume=new(),tempo=new();
    LibraryEntry? current;
    int selectedModule=3;
    bool updating;
    LiveEditing? live;
    LibraryEntry? liveEntry;
    bool liveBusy;
    readonly LiveEditCoordinator liveEdits=new();
    PresetDocument? pendingLive {get=>liveEdits.Pending;set=>liveEdits.Pending=value;}
    PresetDocument? desiredLive {get=>liveEdits.Desired;set=>liveEdits.Desired=value;}
    bool livePump=>liveEdits.Running;
    bool polling=false;
    bool devicePolling;
    int consecutivePollFailures;
    const int MaxConsecutivePollFailures=3;
    bool deviceSnapshotUpdating;
    readonly DevicePollSchedule devicePollSchedule=new();
    bool sliderGesture;
    bool chainGesture;
    bool renderQueued;
    readonly System.Windows.Threading.DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly Button liveButton=new(){Content=UiText.T("Conectar · edición en vivo")};

    bool autoConnect;
    bool devicePanelOpen;
    TunerWindow? tunerWindow;
    LoudnessMeterWindow? loudnessMeterWindow;
    DateTime nextConnect;
    public EditorWindow(string[] paths,bool autoConnect=true)
    {
        this.autoConnect=autoConnect;
        Loaded+=async(_,_)=>{if(AppPreferences.ProfileSelectionRequired){status.Text=AppPreferences.LoadWarning??"Select device profile.";UiDialogs.Show(this,status.Text,"Device profile");}else await TryAutoConnect();};
        Title=UiText.T("GP200 Replica · Editor local");Width=1380;Height=930;MinWidth=1080;MinHeight=760;
        Background=BackgroundBrush;Foreground=TextBrush;FontFamily=new FontFamily("Segoe UI");FontSize=14;
        Resources.Add(typeof(Button),ButtonStyle());
        Resources.Add(typeof(TextBox),InputStyle());
        Resources.Add(typeof(ComboBox),ComboStyle());
        Resources.Add(typeof(TabItem),TabStyle());
        var root=BuildModernShell();
        Content=root;
        void ApplyScale(double scale){root.LayoutTransform=new ScaleTransform(scale,scale);Width=Math.Min(1380*scale,SystemParameters.WorkArea.Width);Height=Math.Min(930*scale,SystemParameters.WorkArea.Height);}
        applyInterfaceScale=ApplyScale;
        ApplyScale(AppPreferences.Load().ScalePercent/100.0);
        refreshTimer.Tick+=async(_,_)=>{if(live==null)await TryAutoConnect();else PollDevice();UpdateModernConnection();if(renderQueued&&!sliderGesture&&!liveBusy&&!livePump)Render();};refreshTimer.Start();Closed+=(_,_)=>refreshTimer.Stop();
        Closing+=(_,eventArgs)=>{if(liveBusy||loadingLibrary){eventArgs.Cancel=true;return;}if(entries.Any(e=>e.Dirty)&&UiDialogs.Show(this,UiText.T("Hay cambios locales sin exportar. ¿Cerrar?"),UiText.T("Cambios locales"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)eventArgs.Cancel=true;};
        PreviewKeyDown+=(sender,e)=>
        {
            if(System.Windows.Input.Keyboard.FocusedElement is TextBox)return;
            bool ctrl=(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Control)!=0;
            if(ctrl)
            {
                if(e.Key==System.Windows.Input.Key.Left)
                {
                    e.Handled=true;
                    NavigatePreset(-1);
                }
                else if(e.Key==System.Windows.Input.Key.Right)
                {
                    e.Handled=true;
                    NavigatePreset(1);
                }
                else if(e.Key==System.Windows.Input.Key.S)
                {
                    e.Handled=true;
                    _=SaveDevice();
                }
                else if(e.Key==System.Windows.Input.Key.Z)
                {
                    e.Handled=true;
                    History(false);
                }
                else if(e.Key==System.Windows.Input.Key.Y)
                {
                    e.Handled=true;
                    History(true);
                }
            }
        };
        foreach(string path in paths)Load(path);
        RefreshLibrary();if(entries.Count>0){library.SelectedItem=entries[0];status.Text=UiText.T($"{entries.Count} presets abiertos. Los originales se conservan.");}else RenderEmpty();
    }
    static Brush Brush(string color)=>(Brush)new BrushConverter().ConvertFromString(color)!;
    AlgorithmInfo DisplayAlgorithm(int module,uint code)=>catalog.Find(module,code)??new(PresetDocument.Modules[module],code,
        UiText.T($"Código de efecto no catalogado: 0x{code:X8}"),[],null){ParametersKnown=false};
    static TextBlock Text(string value,double size=14,FontWeight? weight=null,Brush? brush=null)=>new(){Text=value,FontSize=size,FontWeight=weight??FontWeights.Normal,Foreground=brush??TextBrush,TextWrapping=TextWrapping.Wrap};
    static Style ButtonStyle()
    {
        var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#273647")));style.Setters.Add(new Setter(Control.ForegroundProperty,TextBrush));style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(15,10,15,10)));style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,8,0)));style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left));style.Setters.Add(new Setter(Control.CursorProperty,System.Windows.Input.Cursors.Hand));return style;
    }
    static Style InputStyle()
    {
        var style=new Style(typeof(TextBox));style.Setters.Add(new Setter(Control.BackgroundProperty,BackgroundBrush));style.Setters.Add(new Setter(Control.ForegroundProperty,TextBrush));style.Setters.Add(new Setter(Control.BorderBrushProperty,Brush("#354456")));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,7,9,7)));style.Setters.Add(new Setter(Control.FontSizeProperty,14d));return style;
    }
    static Style ComboStyle()
    {
        return (Style)DarkUiTheme.GetDictionary()[typeof(ComboBox)];
    }
    static Style TabStyle()
    {
        var style=new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(18,12,18,12)));
        style.Setters.Add(new Setter(Control.ForegroundProperty,Muted));
        style.Setters.Add(new Setter(Control.BackgroundProperty,BackgroundBrush));
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.ContentSourceProperty,"Header");border.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(TabItem)){VisualTree=border}));
        var selected=new Trigger{Property=TabItem.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(Control.BackgroundProperty,PanelBrush));selected.Setters.Add(new Setter(Control.ForegroundProperty,Accent));style.Triggers.Add(selected);return style;
    }
    Button Button(string label,Action action)
    {
        var button=new Button{Content=label};button.Click+=(_,_)=>Guard(action);return button;
    }
    void Guard(Action action)
    {
        try{action();}catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("GP200 Replica"),MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    void Open()
    {
        var dialog=new OpenFileDialog{Filter=UiText.Filter("Presets GP-200 and ZIP archives|*.prst;*.zip"),Multiselect=true};
        if(dialog.ShowDialog(this)!=true)return;
        var failures=new List<string>();
        foreach(string path in dialog.FileNames)
        {
            try
            {
                if(Path.GetExtension(path).Equals(".zip",StringComparison.OrdinalIgnoreCase))
                {
                    var presets=PresetArchive.Read(path);
                    foreach(var preset in presets)LoadBytes($"{Path.GetFullPath(path)}!{preset.Name}",preset.Bytes);
                }
                else Load(path);
            }
            catch(Exception e){failures.Add($"{Path.GetFileName(path)}: {e.Message}");}
        }
        librarySource.SelectedIndex=0;RefreshLibrary();if(entries.Any(e=>!e.DeviceSlot.HasValue))library.SelectedItem=entries.Last(e=>!e.DeviceSlot.HasValue);
        status.Text=failures.Count==0?UiText.T($"{entries.Count} presets abiertos. Sin cambios en el equipo."):string.Join(" | ",failures);
    }
    void Load(string path)
    {
        path=Path.GetFullPath(path);if(entries.Any(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path)))return;
        LoadBytes(path,File.ReadAllBytes(path));
    }
    void LoadBytes(string path,byte[] data)
    {
        if(entries.Any(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path)))return;
        entries.Add(new(){Path=path,Original=data,Document=new(data)});
    }
    void OpenSnapshot(PresetDocument document)
    {
        var entry=entries.FirstOrDefault(e=>e.DeviceSlot==document.Index);
        if(entry==null){entry=new LibraryEntry{Path=$"GP-200 · {BankService.Position(document.Index)}",Original=document.ToBytes(),Document=document,DeviceSnapshot=true,DeviceSlot=document.Index};entries.Add(entry);}else entry.Document=document;
        if(librarySource.Items.Count>0)librarySource.SelectedIndex=1;
        entry.EditingBaseline??=document.ToBytes();current=entry;RefreshLibrary();library.SelectedItem=entry;Render();
    }
    void OpenDevicePanel(Func<Window> create)
    {
        if(liveBusy||livePump)throw new InvalidOperationException(UiText.T("Espera a que se confirme la edición pendiente."));
        bool patchRequested=false;Window? window=null;
        devicePanelOpen=true;try{window=create();if(window is SettingsWindow settings){settings.PatchSettingsRequested+=()=>patchRequested=true;}if(window is SnapToneImportWindow snap)snap.CatalogRead+=catalog.UpdateSnapToneNames;window.Owner=this;window.ShowDialog();}finally{devicePanelOpen=false;}
        if(window is SettingsWindow settingsWindow&&live!=null&&settingsWindow.CurrentState is DeviceSettings settingsState)live.AcceptOperatingSettings(settingsState);
        if(window is not SettingsWindow)Render();
        if(patchRequested)_=OpenPatchAfterSettings();else if(autoConnect)_=TryAutoConnect();
    }
    void OpenAiPresetStudio()
    {
        if(liveBusy||livePump||loadingLibrary)throw new InvalidOperationException(UiText.T("Espera a que termine la operación actual antes de abrir el estudio de IA."));
        var knownDevicePresets=entries.Where(entry=>entry.DeviceSlot.HasValue).Select(entry=>entry.Document).ToArray();
        devicePanelOpen=true;
        try
        {
            var window=new AiPresetWindow(catalog,current?.Document,knownDevicePresets,AddAiPreset,ReadFullBankForAi,ReadScenesForAi,
                (presets, owner) => OpenLoudnessMeter(presets, owner)){Owner=this};
            window.ShowDialog();
        }
        finally{devicePanelOpen=false;}
    }
    async Task<IReadOnlyList<PresetDocument>> ReadFullBankForAi(IProgress<int> progress,CancellationToken cancellationToken)
    {
        if(live==null)throw new InvalidOperationException(UiText.T("Conecta el GP-200 antes de leer los 256 presets."));
        if(liveBusy||livePump||loadingLibrary)throw new InvalidOperationException(UiText.T("Espera a que termine la operación actual."));
        liveBusy=true;
        try
        {
            var bank=await new BankService().ReadLibraryAsync(progress,cancellationToken);
            if(bank.Count!=256||!bank.Select(item=>item.Slot).SequenceEqual(Enumerable.Range(0,256)))
                throw new InvalidDataException(UiText.T("No se pudieron leer todos los presets del pedal."));
            return bank.Select(item=>item.Document).ToArray();
        }
        finally{liveBusy=false;}
    }
    async Task<Mx42SceneSnapshot> ReadScenesForAi(CancellationToken cancellationToken)
    {
        if(live==null||liveEntry==null||current!=liveEntry||!liveEntry.DeviceSnapshot||liveEntry.Dirty)
            throw new InvalidOperationException(UiText.T("Scene generation requires the unchanged active GP-200 preset. Select its live device entry first."));
        if(!catalog.IsMx42||!Mx42Integration.Supports(Mx42Capability.SceneSnapshot))
            throw new NotSupportedException(UiText.T("The connected Custom firmware does not expose the reviewed Scene snapshot capability."));
        await DeviceMidi.OperationGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot=await Mx42SceneUsb.Read();
            if(snapshot.Slot!=liveEntry.DeviceSlot)throw new IOException(UiText.T("The live Scene bank belongs to a different device slot."));
            return snapshot;
        }
        finally{DeviceMidi.OperationGate.Release();}
    }
    void AddAiPreset(AiGeneratedPreset generated)
    {
        string path=$"AI generated · {generated.Name}.prst";
        var existing=entries.FirstOrDefault(entry=>StringComparer.OrdinalIgnoreCase.Equals(entry.Path,path));
        if(existing!=null)
        {
            path=$"AI generated · {generated.Name} · {DateTime.Now:yyyyMMdd-HHmmss}.prst";
        }
        var document=new PresetDocument(generated.Document.ToBytes());
        var entry=new LibraryEntry{Path=path,Original=document.ToBytes(),Document=document};
        entries.Add(entry);librarySource.SelectedIndex=0;current=entry;RefreshLibrary();library.SelectedItem=entry;Render();
    }
    async Task SaveDevice(int? destination=null)
    {
        var entry=current;
        if(liveBusy||livePump){status.Text=UiText.T("Espera a que termine la edición pendiente.");return;}
        if(live==null||entry==null||entry!=liveEntry){status.Text=UiText.T("Conecta primero la edición en vivo del preset activo. Para importar una copia local usa Importar.");return;}
        liveBusy=true;
        try
        {
            int target=destination??entry.Document.Index;
            var service=new BankService();var saved=await service.SaveActiveAsync(target);
            if(target==entry.Document.Index)
            {entry.ConfirmSceneStore();entry.Original=saved.ToBytes();entry.EditingBaseline=saved.ToBytes();entry.Document=saved;live?.AcceptSnapshot(saved);}
            else
            {
                var stored=entries.FirstOrDefault(e=>e.DeviceSlot==target);
                if(stored==null){stored=new LibraryEntry{Path=$"GP-200 · {BankService.Position(target)}",Original=saved.ToBytes(),Document=saved,DeviceSnapshot=true,DeviceSlot=target};entries.Add(stored);}
                stored.Original=saved.ToBytes();stored.EditingBaseline=saved.ToBytes();stored.Document=saved;stored.Undo.Clear();stored.Redo.Clear();stored.ConfirmSceneStore();
            }
            RefreshLibrary();Render();status.Text=UiText.T("Guardado y verificado en GP-200. ")+BankService.Position(target)+UiText.T(". Copia anterior: ")+service.LastBackupDirectory;
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Guardar GP-200"));}
        finally{liveBusy=false;}
    }
    async void PollDevice()
    {
        if(live==null||liveEntry==null||polling||sceneStatusPolling||liveBusy||livePump||devicePanelOpen||pendingLive!=null||sliderGesture||chainGesture||loadingLibrary||devicePolling||!devicePollSchedule.Due(DateTime.UtcNow))return;
        // Passive keep-alive: verifica en <0.05ms si el hardware sigue conectado físicamente sin enviar SysEx
        if(!ReadOnlyMidi.IsDeviceAvailable())
        {
            DeviceSession.Current.Reset();
            consecutivePollFailures=0;
            live=null;liveEntry=null;
            liveButton.Content=UiText.T("Conectar · edición en vivo");
            Title=UiText.T("GP200 Replica · Editor local");
            status.Text=UiText.Language=="es"?"Dispositivo desconectado.":"Device disconnected.";
            UpdateModernConnection();
            return;
        }
        var session=live;var entry=liveEntry;devicePolling=true;
        try
        {
            var actual=await session.ReadCurrentForPollingAsync();
            if(live!=session||liveEntry!=entry)return;
            var updated=LiveEditCoordinator.MergeLocalChain(actual,entry.Document);
            bool changed=!updated.ToBytes().AsSpan().SequenceEqual(entry.Document.ToBytes());
            consecutivePollFailures=0;
            if(changed)
            {
                entry.Document=updated;RefreshLibrary();
                if(current==entry)UpdateLiveSliders(updated);
            }
            session.AcceptSnapshot(actual);
            devicePollSchedule.Complete(DateTime.UtcNow,changed);
        }
        catch(Exception error)
        {
            if(live==session)
            {
                consecutivePollFailures++;
                if(consecutivePollFailures>=MaxConsecutivePollFailures)
                {
                    consecutivePollFailures=0;
                    DeviceSession.Current.Reset();
                    live=null;liveEntry=null;liveButton.Content=UiText.T("Conectar · edición en vivo");Title=UiText.T("GP200 Replica · Editor local");status.Text=error.Message;
                }
                else
                {
                    status.Text=UiText.T("Temporary device read failure; retaining the connection for retry (")
                        +consecutivePollFailures+"/"+MaxConsecutivePollFailures+"): "+error.Message;
                }
            }
            devicePollSchedule.Complete(DateTime.UtcNow,false);
        }
        finally{devicePolling=false;}
    }

    void UpdateLiveSliders(PresetDocument document)
    {
        deviceSnapshotUpdating=true;
        try
        {
            foreach(var tab in tabs.Items.OfType<TabItem>())
            {
                if(tab.Content is not DependencyObject root)continue;
                foreach(var node in LogicalDescendants(root))
                {
                    if(node is System.Windows.Controls.Primitives.RangeBase slider&&slider.Tag is string tag)
                    {
                        double? value=tag switch
                        {
                            "preset-volume"=>document.Volume,
                            "preset-tempo"=>document.Tempo,
                            "preset-pan"=>document.Pan,
                            _=>KnobValue(tag,document)
                        };
                        if(value is double next&&next>=slider.Minimum&&next<=slider.Maximum)slider.Value=next;
                    }
                    else if(node is TextBox input&&!input.IsKeyboardFocusWithin&&input.Tag is string textTag&&textTag.StartsWith("parameter-",StringComparison.Ordinal)&&int.TryParse(textTag.AsSpan(10),out int slot)&&slot is >=0 and <15)
                        input.Text=document.GetEffect(selectedModule).Parameters[slot].ToString("R",CultureInfo.InvariantCulture);
                }
            }
        }
        finally{deviceSnapshotUpdating=false;}
    }

    static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        yield return root;
        foreach(object child in LogicalTreeHelper.GetChildren(root))
            if(child is DependencyObject dependency)
                foreach(var descendant in LogicalDescendants(dependency))yield return descendant;
    }

    static double? KnobValue(string tag,PresetDocument document)
    {
        var parts=tag.Split('-');
        return parts.Length==3&&parts[0]=="knob"&&int.TryParse(parts[1],out int module)&&module is >=0 and <11&&int.TryParse(parts[2],out int slot)&&slot is >=0 and <15
            ?document.GetEffect(module).Parameters[slot]
            :null;
    }
    void RefreshLibrary()
    {
        if(pressedDevicePreset!=null)return;
        updating=true;
        try
        {
            RefreshCollectionChoices();
            UpdateCollectionActions();
            var rows=VisibleEntries().ToList();foreach(var row in rows)row.RefreshDisplayLabel();
            rows=rows.Where(e=>e.DisplayLabel.Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToList();
            library.DisplayMemberPath=nameof(LibraryEntry.DisplayLabel);
            if(library.ItemsSource is not List<LibraryEntry> old||!old.SequenceEqual(rows))library.ItemsSource=rows;
            if(current!=null&&library.Items.Contains(current))library.SelectedItem=current;
        }
        finally{updating=false;}
    }
    void SelectEntry(){if(updating)return;var selected=library.SelectedItem as LibraryEntry;if(selected==null)return;UpdateCollectionActions();if(selected.DeviceSlot.HasValue)return;current=selected;Render();}
    void RenderEmpty(){title.Text=UiText.T("Un espacio para tu sonido");subtitle.Text=UiText.T("Abre tus presets para explorar bloques, parámetros y asignaciones.");tabs.Items.Clear();chain.Children.Clear();RenderChainControls();}
    void Render()
    {
        if(chainGesture){renderQueued=true;return;}
        if(current==null)return;renderQueued=false;
        int tab=tabs.SelectedIndex;var doc=current.Document;title.Text=doc.Name+(current.Dirty?"  •":"");
        subtitle.Text=UiText.T($"{(current==liveEntry&&live!=null?UiText.T("EN VIVO · "):"LOCAL · ")}{doc.Index/4+1:00}-{(char)('A'+doc.Index%4)}    ·    {doc.Tempo} BPM    ·    {Path.GetFileName(current.Path)}");
        chain.Children.Clear();
        double available=details.ActualWidth>0?details.ActualWidth:Width-410;
        double blockWidth=Math.Max(66,(available-12*22-20)/11);
        int chainPosition=0;
        bool parallel=catalog.IsMx42&&ChainRouting.Enabled(doc.FxLoopMode);
        if(parallel)chain.Children.Add(BuildParallelChain(doc,available));
        foreach(byte module in parallel?Array.Empty<byte>():doc.Chain)
        {
            AddGap(chainPosition++);
            if(module>=11){chain.Children.Add(Text(UiText.T($"Módulo {module}")));continue;}
            var effect=doc.GetEffect(module);var algorithm=catalog.Find(module,effect.Code);var displayAlgorithm=algorithm??DisplayAlgorithm(module,effect.Code);
            var text=new StackPanel();var icon=ChainVisual(doc,module,68);
            if(icon!=null){icon.HorizontalAlignment=HorizontalAlignment.Stretch;icon.Width=blockWidth-6;icon.Margin=new Thickness(0);text.Children.Add(icon);}else text.Children.Add(Text(EffectLabel(doc,module),12,FontWeights.Bold,effect.Enabled?Accent:Muted));
            var button=new Button{Content=text,Width=blockWidth,Height=82,Margin=new Thickness(0),Background=module==selectedModule?Brush("#1A4B4E"):Brush("#151E28"),Padding=new Thickness(2),Cursor=System.Windows.Input.Cursors.Hand};
            ToolTipService.SetShowOnDisabled(button,true);
            button.ToolTip=UiText.T($"{EffectLabel(doc,module)} · {displayAlgorithm.Name}\nArrastra el bloque a otra posición.");
            button.AllowDrop=true;Point dragStart=default;
            button.PreviewMouseLeftButtonDown+=(_,args)=>dragStart=args.GetPosition(button);
            button.PreviewMouseMove+=(_,args)=>
            {
                if(args.LeftButton!=System.Windows.Input.MouseButtonState.Pressed)return;
                var point=args.GetPosition(button);if(Math.Abs(point.X-dragStart.X)+Math.Abs(point.Y-dragStart.Y)<8)return;
                StartChainDrag(button,new DataObject("GP200.Module",module));
            };
            int blockPosition=chainPosition-1;
            ConfigureChainDrop(button,args=>blockPosition+(args.GetPosition(button).X>button.ActualWidth/2?1:0));
            button.Click+=(_,_)=>{selectedModule=module;algorithmFilter="";Render();tabs.SelectedIndex=0;};
            if(ChainRouting.Enabled(doc.FxLoopMode))
            {
                var menu=new ContextMenu();
                int currentGroup=blockPosition<doc.RoutingRaw[0]?0:blockPosition<ChainRouting.Boundary(doc)?1:blockPosition<doc.RoutingRaw[1]?2:3;
                void AddGroupItem(string header,int targetGroup)
                {
                    var item=new MenuItem{Header=header,IsChecked=currentGroup==targetGroup,Cursor=System.Windows.Input.Cursors.Hand};
                    item.Click+=(_,_)=>MoveToExplicitGroup(module,targetGroup);
                    menu.Items.Add(item);
                }
                AddGroupItem(ChainText("Move to IN (Input)","Mover a IN (Entrada)"),0);
                AddGroupItem(ChainText("Move to BRANCH A","Mover a RAMA A"),1);
                AddGroupItem(ChainText("Move to BRANCH B","Mover a RAMA B"),2);
                AddGroupItem(ChainText("Move to OUT (Output)","Mover a OUT (Salida)"),3);
                button.ContextMenu=menu;
            }
            var block=new StackPanel{Tag=$"chain-module-{module}"};
            if(ChainRouting.Enabled(doc.FxLoopMode))
            {
                string group=blockPosition<doc.RoutingRaw[0]?"IN":blockPosition<ChainRouting.Boundary(doc)?"RAMA A":blockPosition<doc.RoutingRaw[1]?"RAMA B":"OUT";
                Brush groupColor=group=="RAMA A"?Accent:group=="RAMA B"?Brush("#BD9EFF"):Muted;
                var groupBadge=new Border
                {
                    Background=group=="RAMA A"?Brush("#1A3835"):group=="RAMA B"?Brush("#2C1E45"):Brush("#1A232E"),
                    CornerRadius=new CornerRadius(3),
                    Padding=new Thickness(4,1,4,1),
                    Margin=new Thickness(2,0,2,2),
                    HorizontalAlignment=HorizontalAlignment.Center,
                    Cursor=System.Windows.Input.Cursors.Hand,
                    ToolTip=ChainText("Click to cycle group (IN -> A -> B -> OUT)","Clic para cambiar de rama (IN -> A -> B -> OUT)"),
                    Child=Text(group,9,FontWeights.Bold,groupColor)
                };
                ToolTipService.SetShowOnDisabled(groupBadge,true);
                groupBadge.PreviewMouseLeftButtonDown+=(s,e)=>
                {
                    e.Handled=true;
                    CycleBlockGroup(module);
                };
                block.Children.Add(groupBadge);
            }
            var bypass=new CheckBox{Style=(Style)Resources["ChainToggle"],Content=EffectLabel(doc,module),IsChecked=algorithm?.IsNone==true?false:effect.Enabled,IsEnabled=algorithm?.IsNone!=true,Foreground=TextBrush,Margin=new Thickness(5,2,0,5),Cursor=System.Windows.Input.Cursors.Hand};
            ToolTipService.SetShowOnDisabled(bypass,true);
            bypass.Click+=(_,_)=>Guard(()=>Edit(d=>{var e=d.GetEffect(module);d.SetEffect(module,bypass.IsChecked==true,e.Parameters);}));block.Children.Add(bypass);block.Children.Add(button);
            var modelText=new FrameworkElementFactory(typeof(TextBlock));
            modelText.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Name"));
            modelText.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
            var availableAlgorithms=catalog.ForEffect(module,effect.Code);
            if(algorithm==null)availableAlgorithms=[displayAlgorithm,..availableAlgorithms];
            var choices=new ComboBox{Tag=$"chain-model-{module}",ItemsSource=availableAlgorithms,ItemTemplate=new DataTemplate{VisualTree=modelText},SelectedItem=displayAlgorithm,Width=blockWidth,MinHeight=43,Margin=new Thickness(0,5,0,0),ToolTip=displayAlgorithm.Name,Cursor=System.Windows.Input.Cursors.Hand};
            ToolTipService.SetShowOnDisabled(choices,true);
            choices.SelectionChanged+=(_,_)=>{if(ReferenceEquals(block.Parent,chain)&&choices.SelectedItem is AlgorithmInfo choice&&current!=null){selectedModule=module;algorithmFilter="";var editable=current==liveEntry&&desiredLive!=null?desiredLive:current.Document;if(editable.GetEffect(module).Code!=choice.Code)Guard(()=>Edit(d=>d.SetAlgorithm(module,choice)));if(!sliderGesture)Render();else renderQueued=true;}};block.Children.Add(choices);chain.Children.Add(block);
        }
        if(!parallel)AddGap(chainPosition);
        void AddGap(int position)
        {
            var gap=new StackPanel{Tag=$"chain-gap-{position}",MinWidth=18,MinHeight=96,VerticalAlignment=VerticalAlignment.Center,Background=Brushes.Transparent,AllowDrop=true,Margin=new Thickness(0,0,4,0)};
            foreach(bool returnNode in new[]{false,true})
                if(doc.RoutingRaw[returnNode?1:0]==position)
                {
                    var node=new Border{Tag=returnNode?"chain-return":"chain-send",Width=18,Height=22,CornerRadius=new CornerRadius(9),Background=Brush(returnNode?"#8750D9":"#3177DC"),Margin=new Thickness(0,2,0,2),Cursor=System.Windows.Input.Cursors.SizeAll,Child=new TextBlock{Text=returnNode?"↓":"↑",FontSize=17,FontWeight=FontWeights.Bold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
                    node.ToolTip=(returnNode?"Return":"Send")+" · "+UiText.T("Arrastra este nodo a otro hueco de la cadena.");Point nodeStart=default;
                    node.PreviewMouseLeftButtonDown+=(_,args)=>nodeStart=args.GetPosition(node);
                    node.MouseMove+=(_,args)=>{if(args.LeftButton==System.Windows.Input.MouseButtonState.Pressed&&(args.GetPosition(node)-nodeStart).Length>5)StartChainDrag(node,new DataObject("GP200.Loop",returnNode));};gap.Children.Add(node);
                }
            if(catalog.IsMx42&&ChainRouting.Enabled(doc.FxLoopMode)&&ChainRouting.Boundary(doc)==position)
            {
                var split=new Border{Tag="chain-split",Width=18,Height=22,CornerRadius=new CornerRadius(5),Background=Brush("#137F77"),Cursor=System.Windows.Input.Cursors.SizeAll,Child=new TextBlock{Text="◆",Foreground=TextBrush,HorizontalAlignment=HorizontalAlignment.Center},ToolTip="Split A/B · drag between Send and Return"};Point start=default;
                split.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(split);split.MouseMove+=(_,e)=>{if(e.LeftButton==System.Windows.Input.MouseButtonState.Pressed&&(e.GetPosition(split)-start).Length>5)StartChainDrag(split,new DataObject("GP200.Split",true));};gap.Children.Add(split);
            }
            ConfigureChainDrop(gap,_=>position);chain.Children.Add(gap);
        }
        UpdateModernConnection();
        tabs.Items.Clear();AddTab(UiText.T("Bloque"),EffectPanel());
        tabs.SelectedIndex=tab>=0&&tab<tabs.Items.Count?tab:0;
        RenderWorkspacePanels();
        RenderChainControls();
    }
    void AddTab(string title,UIElement content)=>tabs.Items.Add(new TabItem{Header=UiText.T(title),Content=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=content,Padding=new Thickness(16)}});
    StackPanel Section(string title,string description)
    {
        var p=new StackPanel();p.Children.Add(Text(title,22,FontWeights.SemiBold));var descriptionLabel=Text(description,13,FontWeights.Normal,Muted);descriptionLabel.Margin=new Thickness(0,7,0,22);p.Children.Add(descriptionLabel);return p;
    }
    void Labelled(StackPanel host,string label,Control control)
    {
        var row=new Grid{Margin=new Thickness(0,0,0,14)};row.ColumnDefinitions.Add(new(){Width=new GridLength(175)});row.ColumnDefinitions.Add(new(){Width=new GridLength(260)});
        var title=Text(label);title.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(title);Grid.SetColumn(control,1);row.Children.Add(control);host.Children.Add(row);
    }
    UIElement General()
    {
        var doc=current!.Document;var p=Section(UiText.T("Tu preset"),current==liveEntry&&live!=null?UiText.T("Edición en tiempo real del preset activo. Guardar en GP-200 conserva los cambios en esta posición."):UiText.T("Edita una copia local. Exporta el resultado a un archivo nuevo cuando quieras conservarlo."));
        var n=new TextBox{Text=doc.Name,MaxLength=16,Tag="preset-name"};var v=new TextBox{Text=doc.Volume.ToString()};var t=new TextBox{Text=doc.Tempo.ToString()};
        Labelled(p,UiText.T("Nombre"),n);Labelled(p,UiText.T("Volumen · 0–100"),v);Labelled(p,"Tempo · BPM",t);
        var volumeSlider=new Slider{Minimum=0,Maximum=100,Value=doc.Volume,TickFrequency=1,IsSnapToTickEnabled=true,Width=260,HorizontalAlignment=HorizontalAlignment.Left,Tag="preset-volume"};
        AddDelayedSlider(volumeSlider,value=>Edit(d=>d.SetGeneral(d.Name,(int)value,d.Tempo)),suppress:()=>deviceSnapshotUpdating);Labelled(p,UiText.T("Volumen en tiempo real"),volumeSlider);
        var tempoSlider=new Slider{Minimum=40,Maximum=250,Value=doc.Tempo,TickFrequency=1,IsSnapToTickEnabled=true,Width=260,HorizontalAlignment=HorizontalAlignment.Left,Tag="preset-tempo"};
        AddDelayedSlider(tempoSlider,value=>Edit(d=>d.SetGeneral(d.Name,d.Volume,(int)value)),suppress:()=>deviceSnapshotUpdating);Labelled(p,UiText.T("Tempo en tiempo real"),tempoSlider);
        var panSlider=new Slider{Minimum=-100,Maximum=100,Value=doc.Pan,TickFrequency=1,IsSnapToTickEnabled=true,Width=260,HorizontalAlignment=HorizontalAlignment.Left,Tag="preset-pan"};
        AddDelayedSlider(panSlider,value=>Edit(d=>d.SetPatchOptions(pan:(int)value)),suppress:()=>deviceSnapshotUpdating);Labelled(p,UiText.T("Pan · −100…100"),panSlider);
        var apply=Button(current==liveEntry&&live!=null?UiText.T("Aplicar a GP-200"):UiText.T("Aplicar al archivo local"),()=>Edit(d=>d.SetGeneral(n.Text,int.Parse(v.Text),int.Parse(t.Text))));apply.Tag="apply-general";p.Children.Add(apply);apply.Visibility=Visibility.Collapsed;
        AutoText(n,()=>Edit(d=>d.SetGeneral(n.Text,d.Volume,d.Tempo)));
        AutoText(v,()=>Edit(d=>d.SetGeneral(d.Name,int.Parse(v.Text),d.Tempo)));
        AutoText(t,()=>Edit(d=>d.SetGeneral(d.Name,d.Volume,int.Parse(t.Text))));
        var metadata=new StackPanel{Margin=new Thickness(0,28,0,0)};
        var author=new TextBox{Text=doc.Author,MaxLength=16};var notes=new TextBox{Text=doc.Notes,MaxLength=40};
        Labelled(metadata,UiText.T("Autor"),author);Labelled(metadata,UiText.T("Notas"),notes);
        AutoText(author,()=>Edit(d=>d.SetMetadata(author.Text,d.Notes)));AutoText(notes,()=>Edit(d=>d.SetMetadata(d.Author,notes.Text)));p.Children.Add(metadata);
        var style=new ComboBox{ItemsSource=Enumerable.Range(0,20).Select(i=>UiText.T($"Tipo {i+1:00}")).ToArray(),SelectedIndex=doc.PatchStyle};Labelled(metadata,UiText.T("Tipo de preset"),style);
        style.SelectionChanged+=(_,_)=>Guard(()=>Edit(d=>d.SetPatchStyle(style.SelectedIndex)));
        var move=new StackPanel{Margin=new Thickness(0,30,0,0)};move.Children.Add(Text(UiText.T("Orden de bloques"),18,FontWeights.SemiBold));
        var hint=Text(UiText.T("Arrastra un bloque en la cadena superior hasta otro bloque o hueco. También puedes seleccionarlo y usar estas flechas."),13,FontWeights.Normal,Muted);hint.Margin=new Thickness(0,8,0,14);move.Children.Add(hint);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(Button(UiText.T("← Mover bloque"),()=>Move(-1)));buttons.Children.Add(Button(UiText.T("Mover bloque →"),()=>Move(1)));move.Children.Add(buttons);p.Children.Add(move);
        var loop=new StackPanel{Margin=new Thickness(0,28,0,0)};loop.Children.Add(Text(UiText.T("Nodos de FX loop"),18,FontWeights.SemiBold));
        var send=new TextBox{Text=doc.RoutingRaw[0].ToString()};var ret=new TextBox{Text=doc.RoutingRaw[1].ToString()};
        Labelled(loop,UiText.T("Send · hueco 0–11"),send);Labelled(loop,UiText.T("Return · hueco 0–11"),ret);
        var loopButtons=new StackPanel{Orientation=Orientation.Horizontal};loopButtons.Children.Add(Button(UiText.T("Mover send"),()=>Edit(d=>d.SetLoopNode(false,int.Parse(send.Text)))));loopButtons.Children.Add(Button(UiText.T("Mover return"),()=>Edit(d=>d.SetLoopNode(true,int.Parse(ret.Text)))));loopButtons.Visibility=Visibility.Collapsed;loop.Children.Add(loopButtons);AutoText(send,()=>Edit(d=>d.SetLoopNode(false,int.Parse(send.Text))));AutoText(ret,()=>Edit(d=>d.SetLoopNode(true,int.Parse(ret.Text))));
        var sendLevel=new TextBox{Text=doc.FxSendLevel.ToString()};var returnLevel=new TextBox{Text=doc.FxReturnLevel.ToString()};
        var mode=new ComboBox{ItemsSource=new[]{"Parallel","Series"},SelectedIndex=doc.FxLoopMode};
        Labelled(loop,"Send Level · 0–100",sendLevel);Labelled(loop,"Return Level · 0–100",returnLevel);Labelled(loop,UiText.T("FX loop · modo"),mode);
        AutoText(sendLevel,()=>Edit(d=>d.SetPatchOptions(sendLevel:int.Parse(sendLevel.Text))));AutoText(returnLevel,()=>Edit(d=>d.SetPatchOptions(returnLevel:int.Parse(returnLevel.Text))));mode.SelectionChanged+=(_,_)=>Guard(()=>Edit(d=>d.SetPatchOptions(loopMode:mode.SelectedIndex)));
        p.Children.Add(loop);return p;
    }
    void Move(int direction)
    {
        byte[] order=current!.Document.Chain;int position=Array.IndexOf(order,(byte)selectedModule),target=position+direction;
        if(position<0||target<0||target>=order.Length)return;(order[position],order[target])=(order[target],order[position]);Edit(d=>d.SetChain(order));
    }
    UIElement EffectPanel()
    {
        var effect=current!.Document.GetEffect(selectedModule);var catalogued=catalog.Find(selectedModule,effect.Code);var algorithm=catalogued??DisplayAlgorithm(selectedModule,effect.Code);
        var p=new StackPanel();p.Children.Add(Text($"{EffectLabel(current.Document,selectedModule)} / {algorithm.Name}",21,FontWeights.SemiBold));
        if(algorithm.ParametersKnown!=true)
        {
            p.Children.Add(Text(UiText.T("El pedal contiene este código, pero no hay metadatos de parámetros validados. Se conservan los valores originales y no se exponen controles de edición."),12,FontWeights.Normal,Muted));
            string values=string.Join("  ",effect.Parameters.Select((value,index)=>$"P{index+1}: {value:R}"));
            p.Children.Add(Text(UiText.T("Valores sin interpretar: ")+values,11,FontWeights.Normal,Muted));
        }
        if(algorithm?.IsNone==true)
        {
            var empty=ChainVisual(current.Document,selectedModule,120);empty.Margin=new Thickness(0,18,0,18);p.Children.Add(empty);
            p.Children.Add(Text("Choose a family and effect to add this block.",12,FontWeights.Normal,Muted));
        }
        else if(catalogued!=null&&EffectArtwork.Load(EffectArtwork.EffectPath(catalogued),190) is Image artwork)
        {
            artwork.Width=double.NaN;artwork.MaxWidth=330;artwork.Height=155;artwork.Stretch=Stretch.Uniform;artwork.HorizontalAlignment=HorizontalAlignment.Center;artwork.Margin=new Thickness(0,12,0,14);artwork.ToolTip=catalogued.Name;p.Children.Add(artwork);
        }
        int module=selectedModule;
        var inputs=new TextBox[15];var visible=new System.Windows.Controls.Primitives.UniformGrid{Columns=3};
        visible.SizeChanged+=(_,e)=>visible.Columns=e.NewSize.Width<240?1:e.NewSize.Width<360?2:3;
        for(int i=0;i<15;i++)
        {
            var parameter=algorithm?.Parameters.FirstOrDefault(x=>x.Slot==i);
            inputs[i]=new TextBox{Text=effect.Parameters[i].ToString("R",CultureInfo.InvariantCulture),Tag=$"parameter-{i}",ToolTip=parameter==null?UiText.T("Parámetro conservado sin etiqueta"):UiText.T($"{parameter.Kind} · rango visual: {parameter.Range} {parameter.Unit}")};
            if(parameter==null)continue; // Preserve unknown values in the document without exposing internal controls.
            var card=new StackPanel{Margin=new Thickness(4,0,4,9),IsEnabled=!(catalog.IsMx42&&current==liveEntry&&live!=null&&module==10&&i==12)};
            var label=Text(parameter.Name,12);label.TextAlignment=TextAlignment.Center;label.Margin=new Thickness(0,0,0,2);card.Children.Add(label);
            int slot=i;
            card.ToolTip=card.IsEnabled?"Double-click to restore the default value.":"MX42 legacy USB receiver reserves this word in slot 11. Read-only control.";
            card.PreviewMouseLeftButtonDown+=(_,args)=>
            {
                if(args.ClickCount!=2)return;
                args.Handled=true;
                Guard(()=>Edit(d=>
                {
                    var active=d.GetEffect(module);
                    float value=!parameter.Power&&parameter.Bind>=0&&parameter.Bind<15&&active.Parameters[parameter.Bind]>0?4:Catalog.DefaultValue(parameter.Default);
                    d.SetParameter(module,slot,value,catalog);
                }));
            };
            if(parameter.Kind=="Switch")
            {
                var toggle=new CheckBox{Content=parameter.Name,IsChecked=effect.Parameters[i]>0,Foreground=TextBrush,Margin=new Thickness(0,6,0,8)};
                toggle.Click+=(_,_)=>Guard(()=>Edit(d=>d.SetParameter(module,slot,toggle.IsChecked==true?1:0,catalog)));card.Children.Add(toggle);
            }
            else if(parameter.Kind=="Combox"&&parameter.Menus.Count>0)
            {
                var menu=new ComboBox{ItemsSource=parameter.Menus,DisplayMemberPath="Name",SelectedItem=parameter.Menus.FirstOrDefault(m=>m.Value==effect.Parameters[slot]),Margin=new Thickness(0,4,0,8)};
                menu.SelectionChanged+=(_,_)=>{if(menu.SelectedItem is ParameterMenu choice)Guard(()=>Edit(d=>d.SetParameter(module,slot,choice.Value,catalog)));};card.Children.Add(menu);
            }
            else
            {
                var (low,high)=parameter.Bounds(effect);
                if(float.IsFinite(effect.Parameters[i])&&high>low&&effect.Parameters[i]>=low&&effect.Parameters[i]<=high)
                {
                    double step=parameter.Bind>=0&&parameter.Bind<15&&effect.Parameters[parameter.Bind]>0?1:Math.Max(parameter.Number("step",1),.0001f);
                    var slider=new RotaryKnob{Tag=$"knob-{module}-{slot}",Minimum=low,Maximum=high,Value=effect.Parameters[i],TickFrequency=step,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,0,1),HorizontalAlignment=HorizontalAlignment.Center,ToolTip=parameter.Name+" · "+UiText.T("Arrastra verticalmente o usa la rueda; también puedes escribir el valor.")};
                    slider.ValueChanged+=(_,args)=>{if(!inputs[slot].IsKeyboardFocusWithin)inputs[slot].Text=((float)args.NewValue).ToString("R",CultureInfo.InvariantCulture);};
                    AddDelayedSlider(slider,value=>Edit(d=>d.SetParameter(module,slot,(float)value,catalog)),suppress:()=>deviceSnapshotUpdating);card.Children.Add(slider);
                }
            }
            inputs[i].Width=56;inputs[i].Height=25;inputs[i].Padding=new Thickness(4,2,4,2);inputs[i].FontSize=11;inputs[i].VerticalContentAlignment=VerticalAlignment.Center;inputs[i].HorizontalAlignment=HorizontalAlignment.Center;inputs[i].TextAlignment=TextAlignment.Center;
            AutoText(inputs[i],()=>Edit(d=>d.SetParameter(module,slot,ParseNumber(inputs[slot].Text),catalog)));card.Children.Add(inputs[i]);inputs[i].KeyDown+=(_,args)=>{if(args.Key==System.Windows.Input.Key.Enter)Guard(()=>Edit(d=>d.SetParameter(module,slot,ParseNumber(inputs[slot].Text),catalog)));};visible.Children.Add(card);
        }
        p.Children.Add(visible);
        var applyParameters=Button(UiText.T("Aplicar parámetros"),()=>{var values=inputs.Select(x=>ParseNumber(x.Text)).ToArray();Edit(d=>d.SetEffect(selectedModule,d.GetEffect(selectedModule).Enabled,values));});applyParameters.Tag="apply-parameters";applyParameters.Visibility=Visibility.Collapsed;p.Children.Add(applyParameters);
        return p;
    }
    void AutoText(TextBox box,Action commit,bool reusable=false,Func<bool>? suppress=null)
    {
        // Text commits on Enter or leaving the field; sliders and choices send
        // immediately. Never send an incomplete number on each keystroke.
        bool changed=false;var entry=current;
        box.TextChanged+=(_,_)=>{if(suppress?.Invoke()==true){changed=false;return;}if(box.IsKeyboardFocusWithin){changed=true;if(reusable)entry=current;}};
        void Apply(){if(!changed||current!=entry)return;changed=false;Guard(commit);}
        box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Apply();e.Handled=true;}};
        box.LostKeyboardFocus+=(_,_)=>{if(box.IsLoaded)Apply();};
    }
    static float ParseNumber(string text)=>float.Parse(text.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture);
    void AddDelayedSlider(System.Windows.Controls.Primitives.RangeBase slider,Action<double> change,bool reusable=false,Func<bool>? suppress=null)
    {
        CancellationTokenSource? pending=null;var entry=current;bool hasPending=false,gesture=false;double latest=slider.Value;
        void Flush()
        {
            pending?.Cancel();if(!hasPending)return;hasPending=false;
            if(current==entry)Guard(()=>change(latest));
        }
        void FinishGesture()
        {
            if(!gesture)return;gesture=false;sliderGesture=false;Flush();
            // A stationary first click must keep the same knob alive so WPF
            // can recognize the second click as a double-click.
            if(current==entry&&renderQueued){if(!livePump&&!liveBusy)Render();else renderQueued=true;}
        }
        slider.PreviewMouseLeftButtonDown+=(_,_)=>{gesture=true;sliderGesture=true;};
        slider.AddHandler(System.Windows.Input.Mouse.PreviewMouseUpEvent,new System.Windows.Input.MouseButtonEventHandler((_,_)=>FinishGesture()),true);
        slider.LostMouseCapture+=(_,_)=>FinishGesture();
        slider.Unloaded+=(_,_)=>{pending?.Cancel();hasPending=false;if(gesture){gesture=false;sliderGesture=false;renderQueued=true;}};
        slider.ValueChanged+=async(_,args)=>
        {
            if(suppress?.Invoke()==true){pending?.Cancel();hasPending=false;return;}
            if(reusable)entry=current;var pendingEntry=entry;
            pending?.Cancel();pending=new();var token=pending.Token;latest=args.NewValue;hasPending=true;
            try
            {
                await Task.Delay(45,token);
                if(!token.IsCancellationRequested&&current==pendingEntry&&entry==pendingEntry){hasPending=false;Guard(()=>change(latest));}
            }
            catch(OperationCanceledException){}
        };
    }
    UIElement Expressions()
    {
        var p=Section(UiText.T("Expresión"),UiText.T("Asignaciones existentes y sus extremos. Los valores internos se conservan sin suponer unidades."));
        foreach(var assignment in current!.Document.Expression)
        {
            string bank=(assignment.Slot>>4) switch{0=>"EXP A",1=>"EXP B",2=>UiText.T("EXP externo"),_=>"EXP"};
            var box=new StackPanel{Margin=new Thickness(0,0,0,22)};box.Children.Add(Text(UiText.T($"{bank} · destino {(assignment.Slot&15)+1}"),16,FontWeights.SemiBold));
            box.Children.Add(Text(UiText.T($"{ModuleName(assignment.Module)} · parámetro {assignment.Parameter}"),13,FontWeights.Normal,Muted));
            box.Children.Add(Destination(false,assignment));
            var a=new TextBox{Text=assignment.End1.ToString("R",CultureInfo.InvariantCulture)};
            var b=new TextBox{Text=assignment.End0.ToString("R",CultureInfo.InvariantCulture)};
            Labelled(box,UiText.T("Extremo 1"),a);Labelled(box,UiText.T("Extremo 0"),b);
            AutoText(a,()=>Edit(d=>d.SetExpression(assignment.Slot,ParseNumber(a.Text),ParseNumber(b.Text))));AutoText(b,()=>Edit(d=>d.SetExpression(assignment.Slot,ParseNumber(a.Text),ParseNumber(b.Text))));p.Children.Add(box);
        }
        p.Children.Add(Text(UiText.T("Acceso rápido"),18,FontWeights.SemiBold));foreach(var q in current.Document.Quick){p.Children.Add(Text(UiText.T($"Acceso {q.Slot+1}"),15,FontWeights.SemiBold));p.Children.Add(Destination(true,q));}return p;
    }
    UIElement Destination(bool quick,Assignment assignment)
    {
        var panel=new StackPanel{Margin=new Thickness(0,8,0,12)};
        var module=new ComboBox{ItemsSource=Enumerable.Range(0,11).Select(i=>EffectLabel(current!.Document,i)).Concat(new[]{UiText.T("Sin asignar")}).ToArray(),SelectedIndex=assignment.Module==255?11:assignment.Module,Width=260,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8)};
        var parameter=new ComboBox{Width=260,HorizontalAlignment=HorizontalAlignment.Left,SelectedValuePath="Tag",Margin=new Thickness(0,0,0,8)};
        void RefreshParameters(ushort selected)
        {
            var algorithm=module.SelectedIndex is >=0 and <11?catalog.Find(module.SelectedIndex,current!.Document.GetEffect(module.SelectedIndex).Code):null;
            parameter.ItemsSource=(catalog.IsMx42?(algorithm?.Parameters.Select(p=>p.Slot)??Enumerable.Empty<int>()):Enumerable.Range(0,15)).Select(i=>new ComboBoxItem{Tag=(ushort)i,Content=UiText.T($"{i} · {algorithm?.Parameters.FirstOrDefault(p=>p.Slot==i)?.Name??UiText.T("Parámetro interno")}")}).ToArray();parameter.SelectedValue=selected;
        }
        RefreshParameters(assignment.Parameter);
        bool refreshing=false;
        void CommitDestination(){if(!refreshing&&module.SelectedIndex>=0&&parameter.SelectedValue is ushort value)Guard(()=>Edit(d=>d.SetDestination(quick,assignment.Slot,module.SelectedIndex==11?(byte)255:(byte)module.SelectedIndex,value,catalog)));}
        module.SelectionChanged+=(_,_)=>{refreshing=true;RefreshParameters(0);refreshing=false;CommitDestination();};
        parameter.SelectionChanged+=(_,_)=>CommitDestination();
        panel.Children.Add(module);panel.Children.Add(parameter);
        return panel;
    }
    static string ModuleName(byte value)=>value==255?UiText.T("Sin asignar"):value<11?PresetDocument.Modules[value]:UiText.T($"Módulo {value}");
    UIElement Controls()
    {
        var p=Section(UiText.T("Asignaciones CTRL"),UiText.T("Elige los bloques afectados por cada asignación. Se conservan los bits adicionales del archivo."));
        foreach(var ctrl in current!.Document.Controls)
        {
            var block=new StackPanel{Margin=new Thickness(0,0,0,24)};block.Children.Add(Text(UiText.T($"CTRL {ctrl.Slot+1}"),17,FontWeights.SemiBold));
            var flow=new WrapPanel{Margin=new Thickness(0,10,0,12)};
            var document=current.Document;
            for(int visual=0;visual<document.Chain.Length;visual++)
            {
                int owner=CtrlAssignmentView.OwnerAt(document,visual);
                var check=new CheckBox{Content=CtrlAssignmentView.CardLabel(catalog,document,visual,owner),Tag=owner,IsChecked=CtrlAssignmentView.IsAssigned(ctrl.Mask,owner),Foreground=TextBrush,Margin=new Thickness(0,0,16,10)};
                check.Click+=(_,_)=>Guard(()=>Edit(d=>CtrlAssignmentView.SetAssigned(d,ctrl.Slot,owner,check.IsChecked==true)));
                flow.Children.Add(check);
            }
            block.Children.Add(flow);p.Children.Add(block);
        }
        return p;
    }
    UIElement Comparison()
    {
        var p=Section(UiText.T("Cambios locales"),UiText.T("Comparación con el archivo abierto originalmente."));var original=new PresetDocument(current!.Original);var doc=current.Document;
        var lines=new List<string>();if(original.Name!=doc.Name)lines.Add(UiText.T($"Nombre: {original.Name} → {doc.Name}"));if(original.Volume!=doc.Volume)lines.Add(UiText.T($"Volumen: {original.Volume} → {doc.Volume}"));if(original.Tempo!=doc.Tempo)lines.Add($"Tempo: {original.Tempo} → {doc.Tempo}");
        if(!original.Chain.SequenceEqual(doc.Chain))lines.Add(UiText.T("Orden de bloques modificado"));
        if(original.Author!=doc.Author)lines.Add(UiText.T($"Autor: {original.Author} → {doc.Author}"));if(original.Notes!=doc.Notes)lines.Add(UiText.T($"Notas: {original.Notes} → {doc.Notes}"));
        if(!original.RoutingRaw.SequenceEqual(doc.RoutingRaw))lines.Add($"Send/return: {original.RoutingRaw[0]}/{original.RoutingRaw[1]} → {doc.RoutingRaw[0]}/{doc.RoutingRaw[1]}");
        for(int i=0;i<11;i++){var a=original.GetEffect(i);var b=doc.GetEffect(i);if(a.Enabled!=b.Enabled)lines.Add($"{PresetDocument.Modules[i]}: {(b.Enabled?UiText.T("activado"):UiText.T("desactivado"))}");for(int n=0;n<15;n++)if(BitConverter.SingleToInt32Bits(a.Parameters[n])!=BitConverter.SingleToInt32Bits(b.Parameters[n]))lines.Add(UiText.T($"{PresetDocument.Modules[i]} · parámetro {n+1}: {a.Parameters[n]:R} → {b.Parameters[n]:R}"));}
        byte[] bytes=doc.ToBytes();int count=Enumerable.Range(0,bytes.Length-4).Count(i=>bytes[i]!=current.Original[i]);
        if(count==0)lines.Add(UiText.T("Sin cambios."));else lines.Add(UiText.T($"\n{count} bytes de contenido modificados. Checksum actualizado al exportar."));
        if(lines.Count==1&&count>0)lines.Insert(0,UiText.T("Cambios en asignaciones u otros campos conocidos."));
        foreach(string line in lines){var item=Text(line);item.Margin=new Thickness(0,0,0,9);p.Children.Add(item);}return p;
    }
    UIElement Info()
    {
        var p=Section(UiText.T("Detalles del archivo"),UiText.T("Los datos desconocidos se preservan. El tamaño no se utiliza para decidir si un preset es de fábrica o de usuario."));
        p.Children.Add(Text(current!.Path,13,FontWeights.Normal,Muted));p.Children.Add(Text(UiText.T($"\n{current.Original.Length} bytes · {current.Document.Records.Count} registros · checksum válido")));
        if(current.DeviceSnapshot)p.Children.Add(Text(UiText.T("\nArchivo local generado desde una lectura MIDI. No es una exportación realizada por el editor oficial."),13,FontWeights.Normal,Muted));
        p.Children.Add(Text(UiText.T($"\nSend/return: bytes conservados {Convert.ToHexString(current.Document.RoutingRaw)}"),13,FontWeights.Normal,Muted));
        p.Children.Add(Text(UiText.T("\nEditor Stock / MX42. Las operaciones en vivo se verifican por lectura. Guardar conserva el preset y sus Scenes en la GP-200."),13,FontWeights.Normal,Muted));
        return p;
    }
    void Edit(Action<PresetDocument> action)
    {
        if(current==null||loadingLibrary)return;byte[] before=(current==liveEntry&&desiredLive!=null?desiredLive:current.Document).ToBytes();var candidate=new PresetDocument(before);action(candidate);Mx42NamLimit.Validate(candidate);if(catalog.IsMx42&&current==liveEntry&&live!=null)Mx42Policy.RequireTransition(new PresetDocument(before),candidate);
        if(candidate.ToBytes().SequenceEqual(before))return;
        if(current==liveEntry&&live!=null)
        {
            if(!Mx42ChainUsb.Available&&Mx42ChainUsb.Requires(new PresetDocument(before),candidate))
            {
                current.Undo.Push(current.Document.ToBytes());current.Redo.Clear();current.Document=candidate;RefreshLibrary();if(!sliderGesture)Render();else renderQueued=true;
                status.Text=UiText.Language=="es"?"Ruteo Dual A/B aplicado en el editor. Pulsa 'Guardar en GP-200' o 'Sincronizar Cadena' para escribirlo en el pedal.":"Dual A/B routing applied in the editor. Click 'Save to GP-200' or 'Sync Chain' to write it to the pedal.";
                return;
            }
            QueueLive(candidate);return;
        }
        current.Undo.Push(before);current.Redo.Clear();current.Document=candidate;RefreshLibrary();if(!sliderGesture)Render();else renderQueued=true;status.Text=UiText.T("Cambio aplicado a la copia local. Puedes deshacerlo o exportar otro archivo.");
    }
    void History(bool redo)
    {
        if(current==null)return;var from=redo?current.Redo:current.Undo;var to=redo?current.Undo:current.Redo;if(from.Count==0)return;
        if(current==liveEntry&&live!=null){_=CommitLive(current,new PresetDocument(from.Peek()),redo);return;}
        Mx42NamLimit.Validate(new PresetDocument(from.Peek()));
        to.Push(current.Document.ToBytes());current.Document=new(from.Pop());RefreshLibrary();Render();status.Text=redo?UiText.T("Cambio rehecho."):UiText.T("Cambio deshecho.");
    }
    async Task TryAutoConnect()
    {
        if(AppPreferences.ProfileSelectionRequired||!autoConnect||devicePanelOpen||live!=null||liveBusy||livePump||DateTime.UtcNow<nextConnect)return;
        nextConnect=DateTime.UtcNow.AddSeconds(3);
        if(ReadOnlyMidi.IsDeviceAvailable())await ToggleLive(true);
    }
    async Task ToggleLive(bool automatic=false)
    {
        if(liveBusy)return;
        if(live!=null){autoConnect=false;DeviceSession.Current.Reset();live=null;liveEntry=null;consecutivePollFailures=0;liveButton.Content=UiText.T("Conectar · edición en vivo");Title=UiText.T("GP200 Replica · Editor local");status.Text=UiText.T("Edición en vivo desconectada. Los cambios del preset activo permanecen en la pedalera; no se han guardado en memoria.");return;}
        consecutivePollFailures=0;
        liveBusy=true;status.Text=UiText.Language=="es"?"Conectando GP-200…":"Connecting GP-200…";
        try
        {
            live=await LiveEditing.ConnectAsync();consecutivePollFailures=0;autoConnect=true;OpenSnapshot(live.Baseline);liveEntry=current;liveEntry!.Undo.Clear();liveEntry.Redo.Clear();
            Render();
            liveButton.Content=UiText.T("Desconectar edición en vivo");Title=UiText.T("GP200 Replica · GP-200 conectada");
            status.Text=UiText.Language=="es"?"● Conectado en vivo · GP-200 lista":"● Live connected · GP-200 ready";
            _ = DiscoverConnectedDeviceDataAsync();
        }
        catch(Exception error){live=null;liveEntry=null;status.Text=error.Message;if(!automatic)UiDialogs.Show(this,error.Message,UiText.T("Conexión GP-200"));}
        finally{liveBusy=false;}
    }
    async Task DiscoverConnectedDeviceDataAsync()
    {
        var connected=live;if(connected==null)return;
        try
        {
            var irSlots=await DeviceMidi.ReadCatalogAsync(0x1009);
            if(live!=connected||!IsVisible)return;
            catalog.UpdateUserIrNames(IrService.Catalog(irSlots));
            var snapToneSlots=await DeviceMidi.ReadCatalogAsync(0x1012);
            if(live!=connected||!IsVisible)return;
            catalog.UpdateSnapToneNames(SnapToneSlots.Catalog(snapToneSlots));
        }
        catch(Exception error)
        {
            if(live==connected&&IsVisible)status.Text=(UiText.Language=="es"?"No se pudo completar la detección del dispositivo: ":"Device discovery could not be completed: ")+error.Message;
        }
    }
    void QueueLive(PresetDocument candidate)
    {
        devicePollSchedule.Activity(DateTime.UtcNow);
        liveEdits.Validate(live!,candidate);liveEdits.Queue(candidate);if(!livePump)_=PumpLive();
    }
    async Task PumpLive()
    {
        liveBusy=true;var entry=liveEntry!;
        try
        {
            await liveEdits.RunAsync(live!,entry,
                ()=>status.Text=UiText.T("Enviando edición en tiempo real…"),
                ()=>status.Text=ChainRouting.Extended(entry.Document.FxLoopMode)&&!Mx42ChainUsb.Available
                    ?(UiText.Language=="es"?"Ruteo Dual A/B activo en el editor. Pulsa 'Sincronizar Cadena en Pedal' o 'Guardar' para escribirlo en la GP-200.":"Dual A/B chain active in editor. Click 'Sync Chain to GP-200' or 'Save' to write it to hardware.")
                    :(UiText.Language=="es"?"● Conectado en vivo · GP-200 lista":"● Live connected · GP-200 ready"));
            desiredLive=null;RefreshLibrary();if(current==entry){if(!sliderGesture)Render();else renderQueued=true;}
        }
        catch(Exception error){pendingLive=desiredLive=null;live=null;liveEntry=null;liveButton.Content=UiText.T("Conectar · edición en vivo");Title=UiText.T("GP200 Replica · Editor local");status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Edición interrumpida · vuelve a conectar"));}
        finally{liveBusy=false;}
    }
    async Task CommitLive(LibraryEntry entry,PresetDocument candidate,bool? redo)
    {
        if(liveBusy)return;
        // Validate the whole change before sending anything.
        try{liveEdits.Validate(live!,candidate);}catch(Exception e){status.Text=e.Message;UiDialogs.Show(this,e.Message,UiText.T("Edición en vivo"));return;}
        liveBusy=true;status.Text=UiText.Language=="es"?"Enviando a GP-200…":"Sending to GP-200…";
        try
        {
            await liveEdits.CommitAsync(live!,entry,candidate,redo);RefreshLibrary();Render();
            status.Text=ChainRouting.Extended(entry.Document.FxLoopMode)&&!Mx42ChainUsb.Available
                ?(UiText.Language=="es"?"Ruteo Dual A/B activo en el editor. Pulsa 'Sincronizar Cadena en Pedal' o 'Guardar' para escribirlo en la GP-200.":"Dual A/B chain active in editor. Click 'Sync Chain to GP-200' or 'Save' to write it to hardware.")
                :(UiText.Language=="es"?"● Conectado en vivo · GP-200 lista":"● Live connected · GP-200 ready");
        }
        catch(Exception error)
        {
            live=null;liveEntry=null;liveButton.Content=UiText.T("Conectar · edición en vivo");Title=UiText.T("GP200 Replica · Editor local");
            status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Edición interrumpida · vuelve a conectar"));
        }
        finally{liveBusy=false;}
    }
    async Task SyncChainToDevice()
    {
        if(liveBusy||livePump||loadingLibrary){status.Text=UiText.T("Espera a que termine la operación actual.");return;}
        if(live==null||current==null){status.Text=UiText.Language=="es"?"Conecta la GP-200 para sincronizar la cadena.":"Connect GP-200 to sync chain.";return;}
        int targetSlot=current.DeviceSlot??current.Document.Index;
        liveBusy=true;
        status.Text=UiText.Language=="es"?"Sincronizando cadena dual en la GP-200…":"Syncing dual chain to GP-200…";
        try
        {
            var service=new BankService();
            var saved=await service.ReplaceAsync(targetSlot,current.Document);
            current.Original=saved.ToBytes();
            current.EditingBaseline=saved.ToBytes();
            current.Document=saved;
            current.ConfirmSceneStore();
            if(liveEntry?.DeviceSlot==targetSlot||current==liveEntry)
            {
                var actual=await service.SelectAsync(targetSlot,reload:true);
                live.AcceptSnapshot(actual);
                liveEntry!.Document=actual;
            }
            RefreshLibrary();Render();
            status.Text=(UiText.Language=="es"?"Cadena dual sincronizada y activa en GP-200: ":"Dual chain synced and active on GP-200: ")+BankService.Position(targetSlot);
        }
        catch(Exception ex)
        {
            status.Text=ex.Message;
            UiDialogs.Show(this,ex.Message,UiText.Language=="es"?"Sincronizar Cadena":"Sync Chain");
        }
        finally{liveBusy=false;}
    }
    async void Export()
    {
        if(current==null)return;
        try{Mx42NamLimit.Validate(current.Document);}catch(Exception e){UiDialogs.Show(this,e.Message,"Export");return;}
        if(CatalogRuntime.Current.IsMx42&&(!catalog.IsMx42||Mx42Integration.Supports(Mx42Capability.SceneSnapshot|Mx42Capability.SceneRestore|Mx42Capability.PresetAdmission))&&current.DeviceSlot is int slot)
        {
            if(liveBusy||livePump||loadingLibrary)return;
            var sceneDialog=new SaveFileDialog
            {
                Filter=UiText.Filter("Custom preset with Scenes|*.gpscene|GP-200 preset (*.prst)|*.prst|ZIP archives (*.zip)|*.zip"),
                FileName=BankService.Position(slot),
                AddExtension=false,
                OverwritePrompt=false
            };
            if(sceneDialog.ShowDialog(this)!=true)return;
            liveBusy=true;
            try
            {
                string extension=sceneDialog.FilterIndex switch{2=>".prst",3=>".zip",_=>".gpscene"};
                string exportPath=Path.ChangeExtension(sceneDialog.FileName,extension);
                if(File.Exists(exportPath)){status.Text=UiText.T("Elige un nombre nuevo para conservar los archivos existentes.");return;}
                if(extension==".gpscene")
                {
                    var bundle=await new Mx42PresetTransactions("export").ExportAsync(slot,true);
                    bundle.Save(exportPath);
                    status.Text="Preset and Scenes exported: "+exportPath;
                }
                else
                {
                    byte[] snapshotBytes=current.Document.ToBytes();
                    if(extension==".zip")PresetArchive.Write(exportPath,[(BankService.Position(slot)+".prst",snapshotBytes)]);
                    else using(var stream=new FileStream(exportPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(snapshotBytes);
                    status.Text="Preset exported without Scenes: "+exportPath;
                }
            }
            catch(Exception e){status.Text=e.Message;UiDialogs.Show(this,e.Message,"Export Scenes");}
            finally{liveBusy=false;}return;
        }
        string baseName=current.DeviceSnapshot?string.Concat(current.Document.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)):Path.GetFileNameWithoutExtension(current.Path);
        var dialog=new SaveFileDialog{Filter=UiText.Filter("GP-200 preset (*.prst)|*.prst|ZIP archives (*.zip)|*.zip"),FileName=baseName+"-replica",OverwritePrompt=false,AddExtension=false};
        if(dialog.ShowDialog(this)!=true)return;
        string targetExtension=dialog.FilterIndex==2?".zip":".prst";
        string targetPath=Path.ChangeExtension(dialog.FileName,targetExtension);
        if(File.Exists(targetPath)){status.Text=UiText.T("Elige un nombre nuevo para conservar los archivos existentes.");return;}
        byte[] bytes=current.Document.ToBytes();
        if(targetExtension==".zip")
            PresetArchive.Write(targetPath,[(baseName+".prst",bytes)]);
        else using(var stream=new FileStream(targetPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(bytes);
        status.Text=catalog.IsMx42?"MX42 current snapshot exported WITHOUT SC10/Scenes/BASE bank: "+targetPath:UiText.T($"Copia exportada: {targetPath}");
    }
}
