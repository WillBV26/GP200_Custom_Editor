using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace Gp200.Replica;
public sealed partial class EditorWindow
{
    readonly TextBlock connectionBadge=new(){FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0)};
    readonly TextBlock liveSyncBadge=new(){FontSize=12,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16,0,0,0)};
    readonly TextBlock dspLoadBadge=new(){FontSize=12,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(14,0,0,0)};
    readonly StackPanel patchOverview=new();
    readonly OfflineAudioPreviewService previewAudio=new();
    Button? nativeNamButton;
    Button? diLoopBtn;
    Action<double>? applyInterfaceScale;
    static Border Card(UIElement child,int padding=12)=>new(){Child=child,Padding=new Thickness(padding),Background=PanelBrush,BorderBrush=Brush("#2C3744"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9)};
    Grid BuildModernShell()
    {
        InstallModernStyles();
        var root=new Grid{Margin=new Thickness(16),Background=BackgroundBrush};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid();header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new());header.RowDefinitions.Add(new(){Height=GridLength.Auto});header.RowDefinitions.Add(new(){Height=GridLength.Auto});var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(Text("GP-200",27,FontWeights.Bold));
        connectionBadge.Cursor=System.Windows.Input.Cursors.Hand;
        ToolTipService.SetShowOnDisabled(connectionBadge,true);
        connectionBadge.PreviewMouseLeftButtonDown+=async(_,e)=>
        {
            e.Handled=true;
            if(liveBusy||livePump||loadingLibrary)return;
            autoConnect=true;
            status.Text=UiText.T("Reescaneando y conectando GP-200…");
            if(live!=null)
            {
                DeviceSession.Current.Reset();
                live=null;liveEntry=null;consecutivePollFailures=0;
            }
            await ToggleLive(false);
        };
        brand.Children.Add(connectionBadge);
        brand.Children.Add(liveSyncBadge);
        dspLoadBadge.Text = UiText.Language=="es"?"DSP: sin medir":"DSP: unmetered";
        dspLoadBadge.Foreground = Muted;
        dspLoadBadge.Cursor = System.Windows.Input.Cursors.Hand;
        dspLoadBadge.PreviewMouseLeftButtonDown += (_, _) => OpenDspTaskManager();
        ToolTipService.SetShowOnDisabled(dspLoadBadge, true);
        brand.Children.Add(dspLoadBadge);
        var tools=new WrapPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center};
        var reSyncBtn = Button(UiText.Language == "es" ? "🔄 Re-Sincronizar" : "🔄 Sync Pedal", async () => await ReSyncWithPedalAsync());
        reSyncBtn.ToolTip = UiText.Language == "es" ? "Lee el preset activo de la GP-200 y sincroniza todas las vistas en tiempo real." : "Reads active preset from GP-200 and syncs all views in real-time.";
        ToolTipService.SetShowOnDisabled(reSyncBtn, true);
        tools.Children.Add(reSyncBtn);
        var undoBtn = Button("↶", () => History(false));
        undoBtn.ToolTip = UiText.Language == "es" ? "Deshacer (Ctrl+Z)" : "Undo (Ctrl+Z)";
        undoBtn.FontSize = 14;
        undoBtn.FontWeight = FontWeights.Bold;
        undoBtn.Padding = new Thickness(10, 6, 10, 6);
        ToolTipService.SetShowOnDisabled(undoBtn, true);
        tools.Children.Add(undoBtn);

        var redoBtn = Button("↷", () => History(true));
        redoBtn.ToolTip = UiText.Language == "es" ? "Rehacer (Ctrl+Y / Ctrl+Shift+Z)" : "Redo (Ctrl+Y / Ctrl+Shift+Z)";
        redoBtn.FontSize = 14;
        redoBtn.FontWeight = FontWeights.Bold;
        redoBtn.Padding = new Thickness(10, 6, 10, 6);
        ToolTipService.SetShowOnDisabled(redoBtn, true);
        tools.Children.Add(redoBtn);
        var optCommitBtn = Button(UiText.Language == "es" ? "⚡ Optimizar DSP y Grabar" : "⚡ Optimize & Commit DSP", async () => await OptimizeAndCommitDspAsync());
        optCommitBtn.ToolTip = UiText.Language == "es" ? "Ejecuta las 7 etapas de optimización y persiste el resultado en la memoria Flash del pedal." : "Runs 7-stage optimization and commits to pedal Flash.";
        ToolTipService.SetShowOnDisabled(optCommitBtn, true);
        tools.Children.Add(optCommitBtn);

        var stageBtn = Button(UiText.Language == "es" ? "🎸 Modo Escenario" : "🎸 Stage Mode", () => new StageModeWindow(this).Show());
        stageBtn.ToolTip = UiText.Language == "es" ? "Vista de alto contraste a pantalla completa para escenario con afinador y pulsadores gigantes." : "Full-screen high-contrast stage view with tuner and footswitches.";
        ToolTipService.SetShowOnDisabled(stageBtn, true);
        tools.Children.Add(stageBtn);

        Button? loopBtn = null;
        loopBtn = Button("▶ DI Loop", () =>
        {
            if (previewAudio.IsPlaying)
            {
                previewAudio.StopLoop();
                if (loopBtn != null) loopBtn.Content = "▶ DI Loop";
                status.Text = UiText.Language == "es" ? "Preescucha DI Loop detenida." : "DI Loop preview stopped.";
            }
            else if (current != null)
            {
                previewAudio.PlayLoop(current.Document);
                if (loopBtn != null) loopBtn.Content = "⏹ Stop Loop";
                status.Text = UiText.Language == "es" ? "Reproduciendo DI Loop con emulación de amplificador y cabina en la laptop." : "Playing DI Loop with software tone processing.";
            }
            else
            {
                status.Text = UiText.Language == "es" ? "Carga un preset para preescuchar con el reproductor DI." : "Load a preset to audition.";
            }
        });
        diLoopBtn = loopBtn;
        diLoopBtn.ToolTip = UiText.Language == "es" ? "Preescucha offline de tonos e IRs con reproductor de guitarra DI integrado." : "Offline DI guitar loop player and tone preview.";
        ToolTipService.SetShowOnDisabled(diLoopBtn, true);
        tools.Children.Add(diLoopBtn);

        tools.Children.Add(Button(UiText.T("AI presets"),OpenAiPresetStudio));
        tools.Children.Add(Button(UiText.T("Desktop tuner"),OpenDesktopTuner));
        tools.Children.Add(Button(UiText.T("Loudness meter"),()=>OpenLoudnessMeter()));
        var ampCabBtn=Button("Amp & Cab Room 🎛️",OpenAmpCabStudio);
        ampCabBtn.ToolTip=UiText.Language=="es"?"Estudio dedicado de cabezal, EQ de 5 bandas, noise gate y microfonía de cabina":"Dedicated Amp Head, 5-band EQ, Noise Gate and Cabinet Studio";
        ToolTipService.SetShowOnDisabled(ampCabBtn,true);tools.Children.Add(ampCabBtn);
        var jamStudioBtn=Button("GP-200 Stem & Jam Studio 🎧",OpenJamStudio);
        jamStudioBtn.ToolTip=UiText.Language=="es"?"Estudio multipista tipo BandLab: Separación de 6 Stems con StemKit (Guitarra, Voz, Piano, Bajo, Batería, Otros), cabeceras con Mute/Solo, grabación en vivo y automatización SysEx":"Multi-track BandLab Studio: 6-stem StemKit separation, mute/solo headers, live recording, and timeline preset triggers";
        ToolTipService.SetShowOnDisabled(jamStudioBtn,true);tools.Children.Add(jamStudioBtn);
        var irBtn=Button("Load IR",()=>OpenDevicePanel(()=>new IrImportWindow(()=>DeviceMidi.ReadCatalogAsync(0x1009),async record=>{var response=await DeviceMidi.SendIrAsync(record);if(response.CleanupErrors.Count!=0)throw new IOException(UiText.T("El driver MIDI no se liberó correctamente."));})));
        irBtn.ToolTip=UiText.Language=="es"?"Cargar respuesta de impulso (.wav, 44.1 kHz / 48 kHz, 16/24-bit)":"Load Impulse Response (.wav, 44.1 kHz / 48 kHz, 16/24-bit)";
        ToolTipService.SetShowOnDisabled(irBtn,true);tools.Children.Add(irBtn);
        var snapBtn=Button("Load SnapTone",()=>OpenDevicePanel(()=>new SnapToneImportWindow()));
        snapBtn.ToolTip=UiText.Language=="es"?"Cargar modelo SnapTone / Tone Match (.clo con cabecera VTSI, 8840 bytes exactos)":"Load SnapTone / Tone Match model (.clo with VTSI header, exact 8840 bytes)";
        ToolTipService.SetShowOnDisabled(snapBtn,true);tools.Children.Add(snapBtn);
        nativeNamButton=Button("Load NAM",()=>{OpenDevicePanel(()=>new Mx42NamWindow());});nativeNamButton.Content="Load NAM";nativeNamButton.Visibility=Visibility.Visible;
        nativeNamButton.ToolTip=UiText.Language=="es"?"Cargar modelo Neural Amp Modeler (.nam)":"Load Neural Amp Modeler model (.nam)";
        ToolTipService.SetShowOnDisabled(nativeNamButton,true);tools.Children.Add(nativeNamButton);
        var settingsBtn=Button("Settings",()=>OpenDevicePanel(()=>
        {
            var settings=new SettingsWindow();
            settings.ScaleChanged+=scale=>applyInterfaceScale?.Invoke(scale);
            return settings;
        }));
        ToolTipService.SetShowOnDisabled(settingsBtn,true);tools.Children.Add(settingsBtn);

        // Global Zoom Controls
        Grid.SetRow(brand,0);Grid.SetColumn(brand,0);header.Children.Add(brand);
        SetupChainControls();
        sceneToolbar.Toggle.Style=(Style)FindResource("ChainToggle");sceneToolbar.Toggle.FontSize=12;
        sceneToolbar.Execute=ExecuteToolbarScene;sceneToolbar.Failed=e=>{status.Text=e.Message;UiDialogs.Show(this,e.Message,"Scenes");};
        sceneToolbar.Session(catalog.IsMx42,false,null);
        Grid.SetRow(tools,1);Grid.SetColumnSpan(tools,2);header.Children.Add(tools);
        var top=Card(header,14);top.Margin=new Thickness(0,0,0,12);root.Children.Add(top);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(4,GridUnitType.Star),MinWidth=480});body.ColumnDefinitions.Add(new(){Width=new GridLength(10)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=302});
        var bodySplitter=new GridSplitter{Width=10,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,ShowsPreview=true,Background=Brush("#354456"),Cursor=System.Windows.Input.Cursors.SizeWE,ToolTip=UiText.Language=="es"?"Arrastra para cambiar el ancho del panel de presets":"Drag to resize the preset panel"};
        ToolTipService.SetShowOnDisabled(bodySplitter,true);Grid.SetColumn(bodySplitter,1);body.Children.Add(bodySplitter);
        details.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star),MinHeight=220});details.RowDefinitions.Add(new(){Height=new GridLength(8)});details.RowDefinitions.Add(new(){Height=new GridLength(1.15,GridUnitType.Star),MinHeight=220});
        var detailsSplitter=new GridSplitter{Height=8,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext,ShowsPreview=true,Background=Brush("#354456"),Cursor=System.Windows.Input.Cursors.SizeNS,ToolTip=UiText.Language=="es"?"Arrastra para cambiar el alto del editor y la cadena":"Drag to resize the editor and signal-chain panels"};
        ToolTipService.SetShowOnDisabled(detailsSplitter,true);Grid.SetRow(detailsSplitter,1);details.Children.Add(detailsSplitter);
        var workspace=new Grid();workspace.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star),MinWidth=300,MaxWidth=510});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(8)});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=180});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(8)});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(210),MinWidth=170});
        GridSplitter WorkspaceSplitter()=>new(){Width=8,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,ShowsPreview=true,Background=Brush("#354456"),Cursor=System.Windows.Input.Cursors.SizeWE,ToolTip=UiText.Language=="es"?"Arrastra para cambiar el tamaño de este panel":"Drag to resize this panel"};
        // Keep the controls compact; distribute extra width to readable names.
        tabs.Background=PanelBrush;tabs.Foreground=TextBrush;tabs.BorderThickness=new Thickness(0);
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetBinding(ContentPresenter.ContentProperty,new System.Windows.Data.Binding("SelectedContent"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});tabs.Template=new ControlTemplate(typeof(TabControl)){VisualTree=presenter};
        var effect=Card(tabs,0);effect.Margin=new Thickness(0,0,10,0);workspace.Children.Add(effect);
        var effectSplitter=WorkspaceSplitter();Grid.SetColumn(effectSplitter,1);workspace.Children.Add(effectSplitter);
        var algorithms=Card(algorithmHost,8);algorithms.Margin=new Thickness(0,0,10,0);Grid.SetColumn(algorithms,2);workspace.Children.Add(algorithms);
        var algorithmSplitter=WorkspaceSplitter();Grid.SetColumn(algorithmSplitter,3);workspace.Children.Add(algorithmSplitter);
        var presetColumn=new DockPanel();var currentInfo=new StackPanel();title.FontSize=17;title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.Wrap;subtitle.FontSize=10;subtitle.Foreground=Muted;subtitle.TextWrapping=TextWrapping.Wrap;subtitle.Margin=new Thickness(0,5,0,10);currentInfo.Children.Add(title);currentInfo.Children.Add(subtitle);currentInfo.Children.Add(presetSummary);
        var infoCard=Card(currentInfo,12);infoCard.Margin=new Thickness(0,0,0,10);DockPanel.SetDock(infoCard,Dock.Top);presetColumn.Children.Add(infoCard);
        var patch=new StackPanel();var patchButton=Button("Patch Setting  ⚙",OpenPatchSettings);patchButton.Margin=new Thickness(0,0,0,12);patch.Children.Add(patchButton);patch.Children.Add(patchOverview);presetColumn.Children.Add(Card(patch,12));Grid.SetColumn(presetColumn,4);workspace.Children.Add(presetColumn);Grid.SetRow(workspace,0);details.Children.Add(workspace);
        var chainHost=new ScrollViewer{Content=chain,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var chainContents=new StackPanel();
        
        var chainTopBar = new Border
        {
            Background = Brush("#0D1526"),
            BorderBrush = Brush("#1E3A5F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var chainTopPanel = new DockPanel();
        enableChain.Content = UiText.Language == "es" ? "⚡ Ruteo Dual Paralelo A/B (Split / Mix)" : "⚡ Dual Chain Routing A/B (Split / Mix)";
        DockPanel.SetDock(enableChain, Dock.Left);
        chainTopPanel.Children.Add(enableChain);

        var addPedalTopBtn = new Button
        {
            Content = UiText.Language == "es" ? "➕ Añadir Pedal" : "➕ Add Pedal",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(12, 0, 0, 0),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = DarkUiTheme.Brush("#142238"),
            BorderBrush = DarkUiTheme.AuraCyanPrimaryBrush,
            ToolTip = UiText.Language == "es" ? "Explorar catálogo y añadir o activar cualquier pedal en la cadena" : "Browse catalog and add or enable any pedal in the chain"
        };
        addPedalTopBtn.Click += (_, _) => OpenAddPedalDialog();
        DockPanel.SetDock(addPedalTopBtn, Dock.Left);
        chainTopPanel.Children.Add(addPedalTopBtn);

        var dspPerfBtn = new Button
        {
            Content = UiText.Language == "es" ? "📊 Monitor DSP (Task Manager)" : "📊 DSP Task Manager",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = DarkUiTheme.Brush("#142238"),
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            ToolTip = UiText.Language == "es" ? "Ver ciclos de hardware, consumo de cada pedal y optimizar sin pérdida de sonido" : "View hardware cycles, per-pedal load and optimize tone-safely"
        };
        dspPerfBtn.Click += (_, _) => OpenDspTaskManager();
        DockPanel.SetDock(dspPerfBtn, Dock.Right);
        chainTopPanel.Children.Add(dspPerfBtn);

        var chainModeLabel = new TextBlock
        {
            Text = UiText.Language == "es" ? "Línea Serie / Dual A/B (Arrastra o usa ◀ ⇄ ▶ para mover o cambiar pedales)" : "Series / Dual A/B (Drag or use ◀ ⇄ ▶ to reorder or morph)",
            Foreground = Muted,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        chainTopPanel.Children.Add(chainModeLabel);
        chainTopBar.Child = chainTopPanel;

        chainContents.Children.Add(chainTopBar);
        chainContents.Children.Add(chainOptions);
        chainContents.Children.Add(chainHost);
        var chainCard=Card(chainContents,8);
        chainCard.Margin=new Thickness(0,12,0,0);
        Grid.SetRow(chainCard,2);
        details.Children.Add(chainCard);
        body.Children.Add(details);
        chainHost.SizeChanged+=(_,e)=>ResizeChain(e.NewSize.Width);
        var sidebar=Card(BuildWorkspaceSidebar(),0);Grid.SetColumn(sidebar,2);body.Children.Add(sidebar);Grid.SetRow(body,1);root.Children.Add(body);
        status.Foreground=Muted;status.FontSize=11;status.Margin=new Thickness(4,10,0,0);status.Text=UiText.T("Abre tus presets para explorar bloques, parámetros y asignaciones.");Grid.SetRow(status,2);root.Children.Add(status);UpdateModernConnection();return root;
    }
    void UpdateModernConnection()
    {
        connectionBadge.Text=(live!=null?"●  GP-200 USB":"○  GP-200 USB")+" · "+(catalog.IsMx42?"Custom":"Stock")+(catalog.IsMx42&&live!=null?" · live":"");
        connectionBadge.Foreground=live!=null?Accent:Muted;
        connectionBadge.Cursor=System.Windows.Input.Cursors.Hand;
        ToolTipService.SetShowOnDisabled(connectionBadge,true);
        connectionBadge.ToolTip=live!=null
            ?(UiText.Language=="es"?"Conectado. Clic para reconectar/reescanear en caliente.":$"{catalog.ProfileStatus}\nConnected. Click to reconnect / rescan USB in hot-plug.")
            :(UiText.Language=="es"?"Desconectado. Clic para conectar/reescanear GP-200 en caliente.\n\n" + ReadOnlyMidi.GetPedalDiagnosticDetails():$"{catalog.ProfileStatus}\nDisconnected. Click to connect / rescan GP-200 in hot-plug.\n\n" + ReadOnlyMidi.GetPedalDiagnosticDetails());
        if(nativeNamButton!=null){nativeNamButton.Content="Load NAM";nativeNamButton.Visibility=Visibility.Visible;}
        if(live!=null)
        {
            liveSyncBadge.Text=UiText.Language=="es"?"⚡ LIVE SYNC: ACTIVO":"⚡ LIVE SYNC: ON";
            liveSyncBadge.Foreground=DarkUiTheme.AuraCyanPrimaryBrush;
            liveSyncBadge.ToolTip=UiText.Language=="es"?"Sincronización bidireccional en vivo activa (<25ms)":"Bidirectional real-time live sync active (<25ms)";
        }
        else
        {
            liveSyncBadge.Text=UiText.Language=="es"?"⚡ LIVE SYNC: PAUSA":"⚡ LIVE SYNC: PAUSED";
            liveSyncBadge.Foreground=Muted;
            liveSyncBadge.ToolTip=UiText.Language=="es"?"GP-200 no conectada por USB":"GP-200 not connected via USB";
        }
        LivePedalSyncCoordinator.Instance.NotifyStatusChanged(live!=null);
        UpdateDspLoadDisplay();
        UpdateChainInteraction();
    }
    void ResizeChain(double width)
    {
        foreach(var ribbon in chain.Children.OfType<ParallelChainRibbon>())ribbon.Resize(width);
        double blockWidth=Math.Max(66,(width-12*22-4)/11);
        foreach(var block in chain.Children.OfType<StackPanel>())
        {
            if(!block.Children.OfType<ComboBox>().Any())continue;
            foreach(var selector in block.Children.OfType<ComboBox>())selector.Width=blockWidth;
            foreach(var button in block.Children.OfType<Button>())
            {
                button.Width=blockWidth;
                if(button.Content is StackPanel content)
                    foreach(var icon in content.Children.OfType<Image>())icon.Width=blockWidth-6;
            }
        }
    }
    void ScrollActivePresetToTop()
    {
        var entry=current;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
        {
            if(entry==null||current!=entry||!library.Items.Contains(entry))return;
            library.ScrollIntoView(entry);library.UpdateLayout();
            ScrollViewer? FindScroll(DependencyObject node)
            {
                if(node is ScrollViewer scroll)return scroll;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                    if(FindScroll(VisualTreeHelper.GetChild(node,i)) is ScrollViewer found)return found;
                return null;
            }
            var viewer=FindScroll(library);if(viewer==null)return;
            if(viewer.CanContentScroll)viewer.ScrollToVerticalOffset(library.Items.IndexOf(entry));
            else if(library.ItemContainerGenerator.ContainerFromItem(entry) is FrameworkElement item)
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset+item.TranslatePoint(new Point(),viewer).Y);
        }));
    }
    void InstallModernStyles()
    {
        StyleDialog(this);
    }
    internal static void StyleDialog(Window window,bool wrapContent=true)
    {
        DarkUiTheme.Apply(window);
        if(window is not EditorWindow)CompactDialogStyles(window);
        window.PreviewMouseWheel+=RouteMouseWheelToScrollViewer;
        if(window is EditorWindow)return;
        window.Loaded+=(_,_) =>
        {
            double scale=AppPreferences.Load().ScalePercent/100.0;
            if(window.Content is not UIElement content)return;
            if(Math.Abs(scale-1.0)>0.001&&content is FrameworkElement element)
                element.LayoutTransform=new ScaleTransform(scale,scale);
            if(!wrapContent)
            {
                if(window.SizeToContent!=SizeToContent.Manual)
                {
                    double width=window.ActualWidth,height=window.ActualHeight;
                    window.SizeToContent=SizeToContent.Manual;
                    window.Width=Math.Max(width,window.MinWidth);
                    window.Height=Math.Max(height,window.MinHeight);
                }
                window.ResizeMode=ResizeMode.CanResizeWithGrip;
                return;
            }
            var scroll=new ScrollViewer
            {
                Content=content,
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
                CanContentScroll=false,
                PanningMode=PanningMode.Both
            };
            window.Content=scroll;
            void FitScrollViewport()
            {
                scroll.MaxHeight=Math.Max(120,window.ActualHeight-SystemParameters.WindowCaptionHeight-24);
                scroll.MaxWidth=Math.Max(180,window.ActualWidth-16);
            }
            window.SizeChanged+=(_,_)=>FitScrollViewport();
            FitScrollViewport();
            if(window.SizeToContent!=SizeToContent.Manual)
            {
                double width=window.ActualWidth,height=window.ActualHeight;
                window.SizeToContent=SizeToContent.Manual;
                window.Width=Math.Max(width,window.MinWidth);
                window.Height=Math.Max(height,window.MinHeight);
            }
            window.ResizeMode=ResizeMode.CanResizeWithGrip;
        };
    }
    static void RouteMouseWheelToScrollViewer(object sender,System.Windows.Input.MouseWheelEventArgs e)
    {
        DependencyObject? node=e.OriginalSource as DependencyObject;
        while(node!=null)
        {
            if(node is ComboBox or Slider or ScrollBar)return;
            if(node is ScrollViewer scroll)
            {
                bool shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
                if(shift && scroll.ScrollableWidth > 0)
                {
                    scroll.ScrollToHorizontalOffset(Math.Clamp(scroll.HorizontalOffset - e.Delta/120.0*60, 0, scroll.ScrollableWidth));
                    e.Handled = true;
                    return;
                }
                if(scroll.ScrollableHeight > 0)
                {
                    scroll.ScrollToVerticalOffset(Math.Clamp(scroll.VerticalOffset - e.Delta/120.0*60, 0, scroll.ScrollableHeight));
                    e.Handled = true;
                    return;
                }
                if(scroll.ScrollableWidth > 0)
                {
                    scroll.ScrollToHorizontalOffset(Math.Clamp(scroll.HorizontalOffset - e.Delta/120.0*60, 0, scroll.ScrollableWidth));
                    e.Handled = true;
                    return;
                }
            }
            node=node is Visual or Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
        }
    }
    internal static ContextMenu DarkContextMenu(Window owner)
    {
        var menu = new ContextMenu();
        var darkTheme = DarkUiTheme.GetDictionary();
        foreach (object key in darkTheme.Keys) menu.Resources[key] = darkTheme[key];
        return menu;
    }
}
