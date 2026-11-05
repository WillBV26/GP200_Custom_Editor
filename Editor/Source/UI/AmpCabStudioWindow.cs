using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>
/// Dedicated "Amp & Cab Room" Studio for GP-200.
/// Provides an expanded, tactile control room for Amp Head, Pre/Post EQ, Noise Gate,
/// and Cabinet/Microphone placement with live bidirectional editing.
/// Fully dynamic: automatically inspects algorithm parameters for ANY amp/cab/gate/eq model,
/// supports morphed universal blocks, and prevents parameter slot mismatch crashes.
/// </summary>
public sealed class AmpCabStudioWindow : Window
{
    readonly EditorWindow editor;
    readonly Catalog catalog;
    readonly Func<PresetDocument?> getDocument;
    readonly Action<Action<PresetDocument>> edit;

    // Active module slots in chain
    int activeAmpModule = 3;
    int activeCabModule = 5;
    int activeNrModule = 4;
    int activeEqModule = 6;

    // AMP Controls
    readonly ComboBox ampSelector = new() { MinWidth = 240, Margin = new Thickness(0, 0, 0, 8) };
    readonly CheckBox ampBypass = new() { Content = UiText.Language == "es" ? "Amp Activado" : "Amp Active", IsChecked = true, Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand };
    readonly WrapPanel ampKnobsHost = new() { Margin = new Thickness(0, 10, 0, 10) };

    // CAB Controls
    readonly ComboBox cabSelector = new() { MinWidth = 240, Margin = new Thickness(0, 0, 0, 8) };
    readonly CheckBox cabBypass = new() { Content = UiText.Language == "es" ? "Cab Activado" : "Cab Active", IsChecked = true, Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand };
    readonly WrapPanel cabKnobsHost = new() { Margin = new Thickness(0, 10, 0, 10) };

    // NR (Noise Gate) Controls
    readonly ComboBox nrSelector = new() { MinWidth = 200, Margin = new Thickness(0, 0, 0, 8) };
    readonly CheckBox nrBypass = new() { Content = UiText.Language == "es" ? "Noise Gate Activado" : "Noise Gate Active", IsChecked = true, Margin = new Thickness(0, 0, 0, 8), Cursor = System.Windows.Input.Cursors.Hand };
    readonly WrapPanel nrKnobsHost = new() { Margin = new Thickness(0, 4, 0, 10) };

    // EQ Controls
    readonly ComboBox eqSelector = new() { MinWidth = 200, Margin = new Thickness(0, 0, 0, 8) };
    readonly CheckBox eqBypass = new() { Content = UiText.Language == "es" ? "EQ Activado" : "EQ Active", IsChecked = true, Margin = new Thickness(0, 0, 0, 8), Cursor = System.Windows.Input.Cursors.Hand };
    readonly Grid eqBarsHost = new() { Margin = new Thickness(0, 6, 0, 8) };

    uint lastAmpCode = 0xFFFFFFFF;
    uint lastCabCode = 0xFFFFFFFF;
    uint lastNrCode = 0xFFFFFFFF;
    uint lastEqCode = 0xFFFFFFFF;

    readonly Dictionary<int, Slider> ampSliders = new();
    readonly Dictionary<int, Slider> cabSliders = new();
    readonly Dictionary<int, Slider> nrSliders = new();
    readonly Dictionary<int, Slider> eqSliders = new();

    bool updatingUi;

    public AmpCabStudioWindow(EditorWindow editor, Catalog catalog, Func<PresetDocument?> getDocument, Action<Action<PresetDocument>> edit)
    {
        this.editor = editor;
        this.catalog = catalog;
        this.getDocument = getDocument;
        this.edit = edit;

        EditorWindow.StyleDialog(this);
        Title = UiText.Language == "es" ? "Estudio de Amplificador y Cabina (Amp & Cab Room) · GP-200" : "Amp & Cab Room Studio · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 1180;
        Height = 780;
        MinWidth = 920;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var studioLayout = BuildStudioLayout();
        var windowRoot = new Grid();
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var headerBar = new AuraWindowHeaderBar(this, Title, studioLayout, "AMP & CAB ROOM");
        Grid.SetRow(headerBar, 0);
        windowRoot.Children.Add(headerBar);
        Grid.SetRow(studioLayout, 1);
        windowRoot.Children.Add(studioLayout);
        Content = windowRoot;

        PopulateModels();
        SyncFromDocument();
        FirmwareBinScanner.NamCatalogUpdated += _ => Dispatcher.BeginInvoke(PopulateModels);
    }

    UIElement BuildStudioLayout()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var header = new Border
        {
            Background = DarkUiTheme.CardBrush,
            BorderBrush = DarkUiTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var headerStack = new DockPanel();
        var titleText = new TextBlock
        {
            Text = UiText.Language == "es" ? "🎛️ AMP & CAB ROOM STUDIO" : "🎛️ AMP & CAB ROOM STUDIO",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = DarkUiTheme.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var syncStatus = new TextBlock
        {
            Text = UiText.Language == "es" ? "● Sincronizado en tiempo real con el preset activo" : "● Live sync with active preset",
            FontSize = 11,
            Foreground = BrushHex("#10B981"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(syncStatus, Dock.Right);
        headerStack.Children.Add(syncStatus);
        headerStack.Children.Add(titleText);
        header.Child = headerStack;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Body: 3 columns (Amp Head, EQ & Dynamics, Cab & Mic)
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star), MinWidth = 320 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star), MinWidth = 200 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star), MinWidth = 320 });

        var ampCard = CreateSectionCard(UiText.Language == "es" ? "CABEZAL DE AMPLIFICADOR" : "AMPLIFIER HEAD", BuildAmpPanel());
        ampCard.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(ampCard, 0);
        body.Children.Add(ampCard);

        var eqCard = CreateSectionCard(UiText.Language == "es" ? "EQ & NOISE GATE" : "EQ & NOISE GATE", BuildEqPanel());
        eqCard.Margin = new Thickness(4, 0, 4, 0);
        Grid.SetColumn(eqCard, 1);
        body.Children.Add(eqCard);

        var cabCard = CreateSectionCard(UiText.Language == "es" ? "CABINA, MICRÓFONO & ESPACIO" : "CABINET & MICROPHONE", BuildCabPanel());
        cabCard.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(cabCard, 2);
        body.Children.Add(cabCard);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // Footer Bar
        var footer = new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var footerRow = new DockPanel();
        var closeBtn = new Button
        {
            Content = UiText.Language == "es" ? "Cerrar Estudio" : "Close Studio",
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        footerRow.Children.Add(closeBtn);
        var hint = new TextBlock
        {
            Text = UiText.Language == "es" ? "Las perillas se envían a la GP-200 dinámicamente según los controles de cada modelo." : "Knobs are synced dynamically to the GP-200 according to each model's controls.",
            FontSize = 11,
            Foreground = DarkUiTheme.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        footerRow.Children.Add(hint);
        footer.Child = footerRow;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    UIElement BuildAmpPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Modelo de Amplificador:" : "Amplifier Model:", FontSize = 11, Foreground = DarkUiTheme.MutedBrush, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(ampSelector);
        sp.Children.Add(ampBypass);
        sp.Children.Add(ampKnobsHost);

        ampSelector.SelectionChanged += (_, _) =>
        {
            if (updatingUi || ampSelector.SelectedItem is not AlgorithmInfo algo) return;
            try
            {
                edit(d => d.SetAlgorithm(activeAmpModule, algo));
                SyncFromDocument();
            }
            catch {}
        };
        ampBypass.Click += (_, _) =>
        {
            if (updatingUi) return;
            try
            {
                edit(d =>
                {
                    var ef = d.GetEffect(activeAmpModule);
                    d.SetEffect(activeAmpModule, ampBypass.IsChecked == true, ef.Parameters);
                });
            }
            catch {}
        };

        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, PanningMode = PanningMode.Both };
    }

    UIElement BuildEqPanel()
    {
        var sp = new StackPanel();

        // Noise gate
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "NOISE GATE (Trim)" : "NOISE GATE (Trim)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AccentBrush, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Modelo de Noise Gate:" : "Noise Gate Model:", FontSize = 11, Foreground = DarkUiTheme.MutedBrush, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(nrSelector);
        sp.Children.Add(nrBypass);
        sp.Children.Add(nrKnobsHost);

        nrSelector.SelectionChanged += (_, _) =>
        {
            if (updatingUi || nrSelector.SelectedItem is not AlgorithmInfo algo) return;
            try
            {
                lastNrCode = 0xFFFFFFFF;
                edit(d => d.SetAlgorithm(activeNrModule, algo));
                SyncFromDocument();
            }
            catch {}
        };

        nrBypass.Click += (_, _) =>
        {
            if (updatingUi) return;
            try
            {
                edit(d =>
                {
                    var ef = d.GetEffect(activeNrModule);
                    d.SetEffect(activeNrModule, nrBypass.IsChecked == true, ef.Parameters);
                });
            }
            catch {}
        };

        // 5-band / parametric EQ
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "CURVA TONAL (EQ)" : "TONE SHAPING (EQ)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AccentBrush, Margin = new Thickness(0, 14, 0, 4) });
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Modelo de Ecualizador:" : "Equalizer Model:", FontSize = 11, Foreground = DarkUiTheme.MutedBrush, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(eqSelector);
        sp.Children.Add(eqBypass);
        sp.Children.Add(eqBarsHost);

        eqSelector.SelectionChanged += (_, _) =>
        {
            if (updatingUi || eqSelector.SelectedItem is not AlgorithmInfo algo) return;
            try
            {
                lastEqCode = 0xFFFFFFFF;
                edit(d => d.SetAlgorithm(activeEqModule, algo));
                SyncFromDocument();
            }
            catch {}
        };

        eqBypass.Click += (_, _) =>
        {
            if (updatingUi) return;
            try
            {
                edit(d =>
                {
                    var ef = d.GetEffect(activeEqModule);
                    d.SetEffect(activeEqModule, eqBypass.IsChecked == true, ef.Parameters);
                });
            }
            catch {}
        };

        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, PanningMode = PanningMode.Both };
    }

    UIElement BuildCabPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Cabina / Respuesta de Impulso (IR):" : "Cabinet / Impulse Response (IR):", FontSize = 11, Foreground = DarkUiTheme.MutedBrush, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(cabSelector);
        sp.Children.Add(cabBypass);
        sp.Children.Add(cabKnobsHost);

        cabSelector.SelectionChanged += (_, _) =>
        {
            if (updatingUi || cabSelector.SelectedItem is not AlgorithmInfo algo) return;
            try
            {
                lastCabCode = 0xFFFFFFFF;
                edit(d => d.SetAlgorithm(activeCabModule, algo));
                SyncFromDocument();
            }
            catch {}
        };
        cabBypass.Click += (_, _) =>
        {
            if (updatingUi) return;
            try
            {
                edit(d =>
                {
                    var ef = d.GetEffect(activeCabModule);
                    d.SetEffect(activeCabModule, cabBypass.IsChecked == true, ef.Parameters);
                });
            }
            catch {}
        };

        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, PanningMode = PanningMode.Both };
    }

    void PopulateModels()
    {
        var ampAlgos = catalog.ForModule(activeAmpModule).Where(a => !a.IsNone).ToList();
        ampSelector.ItemsSource = ampAlgos;
        ampSelector.DisplayMemberPath = "Name";

        var cabAlgos = catalog.ForModule(activeCabModule).Where(a => !a.IsNone).ToList();
        cabSelector.ItemsSource = cabAlgos;
        cabSelector.DisplayMemberPath = "Name";

        var nrAlgos = catalog.ForModule(activeNrModule).Where(a => !a.IsNone).ToList();
        nrSelector.ItemsSource = nrAlgos;
        nrSelector.DisplayMemberPath = "Name";

        var eqAlgos = catalog.ForModule(activeEqModule).Where(a => !a.IsNone).ToList();
        eqSelector.ItemsSource = eqAlgos;
        eqSelector.DisplayMemberPath = "Name";
    }

    int FindModuleOfFamily(PresetDocument doc, int familyId, int fallbackSlot)
    {
        for (int i = 0; i < doc.Chain.Length; i++)
        {
            byte m = doc.Chain[i];
            if (m < 11)
            {
                var id = catalog.Identity(m, doc.GetEffect(m).Code);
                if (id.EffectiveFamily == familyId)
                    return m;
            }
        }
        return fallbackSlot;
    }

    internal void SyncFromDocument()
    {
        var doc = getDocument();
        if (doc == null) return;
        updatingUi = true;
        try
        {
            // Dynamically resolve actual modules in chain
            activeAmpModule = FindModuleOfFamily(doc, 3, 3);
            activeCabModule = FindModuleOfFamily(doc, 5, 5);
            activeNrModule = FindModuleOfFamily(doc, 4, 4);
            activeEqModule = FindModuleOfFamily(doc, 6, 6);

            // AMP (activeAmpModule)
            var ampEffect = doc.GetEffect(activeAmpModule);
            ampBypass.IsChecked = ampEffect.Enabled;
            var currentAmpAlgo = catalog.Find(activeAmpModule, ampEffect.Code);
            if (currentAmpAlgo != null) ampSelector.SelectedItem = currentAmpAlgo;

            if (ampEffect.Code != lastAmpCode)
            {
                ampKnobsHost.Children.Clear();
                ampSliders.Clear();
                lastAmpCode = ampEffect.Code;
                if (currentAmpAlgo != null && currentAmpAlgo.Parameters.Count > 0)
                {
                    foreach (var param in currentAmpAlgo.Parameters)
                    {
                        if (param.Slot < 0 || param.Slot >= 15) continue;
                        var (min, max) = param.Bounds(ampEffect);
                        float currentVal = ampEffect.Parameters.Length > param.Slot ? ampEffect.Parameters[param.Slot] : Catalog.DefaultValue(param.Default);
                        currentVal = Math.Clamp(currentVal, min, max);

                        var slider = CreateKnobSlider(min, max, currentVal);
                        int capturedSlot = param.Slot;
                        slider.ValueChanged += (_, e) =>
                        {
                            if (updatingUi) return;
                            try
                            {
                                edit(d => d.SetParameter(activeAmpModule, capturedSlot, (float)e.NewValue, catalog));
                            }
                            catch {}
                        };

                        ampSliders[capturedSlot] = slider;
                        ampKnobsHost.Children.Add(KnobTile(param.Name.ToUpperInvariant(), slider, param.Unit));
                    }
                }
            }
            else if (currentAmpAlgo != null)
            {
                foreach (var param in currentAmpAlgo.Parameters)
                {
                    if (ampSliders.TryGetValue(param.Slot, out var slider))
                    {
                        var (min, max) = param.Bounds(ampEffect);
                        float currentVal = ampEffect.Parameters.Length > param.Slot ? ampEffect.Parameters[param.Slot] : Catalog.DefaultValue(param.Default);
                        slider.Value = Math.Clamp(currentVal, min, max);
                    }
                }
            }

            // CAB (activeCabModule)
            var cabEffect = doc.GetEffect(activeCabModule);
            cabBypass.IsChecked = cabEffect.Enabled;
            var currentCabAlgo = catalog.Find(activeCabModule, cabEffect.Code);
            if (currentCabAlgo != null) cabSelector.SelectedItem = currentCabAlgo;

            if (cabEffect.Code != lastCabCode)
            {
                cabKnobsHost.Children.Clear();
                cabSliders.Clear();
                lastCabCode = cabEffect.Code;
                if (currentCabAlgo != null && currentCabAlgo.Parameters.Count > 0)
                {
                    foreach (var param in currentCabAlgo.Parameters)
                    {
                        if (param.Slot < 0 || param.Slot >= 15) continue;
                        var (min, max) = param.Bounds(cabEffect);
                        float currentVal = cabEffect.Parameters.Length > param.Slot ? cabEffect.Parameters[param.Slot] : Catalog.DefaultValue(param.Default);
                        currentVal = Math.Clamp(currentVal, min, max);

                        var slider = CreateKnobSlider(min, max, currentVal);
                        int capturedSlot = param.Slot;
                        slider.ValueChanged += (_, e) =>
                        {
                            if (updatingUi) return;
                            try
                            {
                                edit(d => d.SetParameter(activeCabModule, capturedSlot, (float)e.NewValue, catalog));
                            }
                            catch {}
                        };

                        cabSliders[capturedSlot] = slider;
                        cabKnobsHost.Children.Add(KnobTile(param.Name.ToUpperInvariant(), slider, param.Unit));
                    }
                }
            }
            else if (currentCabAlgo != null)
            {
                foreach (var param in currentCabAlgo.Parameters)
                {
                    if (cabSliders.TryGetValue(param.Slot, out var slider))
                    {
                        var (min, max) = param.Bounds(cabEffect);
                        float currentVal = cabEffect.Parameters.Length > param.Slot ? cabEffect.Parameters[param.Slot] : Catalog.DefaultValue(param.Default);
                        slider.Value = Math.Clamp(currentVal, min, max);
                    }
                }
            }

            // NR (activeNrModule)
            var nrEffect = doc.GetEffect(activeNrModule);
            nrBypass.IsChecked = nrEffect.Enabled;
            var currentNrAlgo = catalog.Find(activeNrModule, nrEffect.Code);
            if (currentNrAlgo != null) nrSelector.SelectedItem = currentNrAlgo;

            if (nrEffect.Code != lastNrCode)
            {
                nrKnobsHost.Children.Clear();
                nrSliders.Clear();
                lastNrCode = nrEffect.Code;
                if (currentNrAlgo != null && currentNrAlgo.Parameters.Count > 0)
                {
                    foreach (var param in currentNrAlgo.Parameters)
                    {
                        if (param.Slot < 0 || param.Slot >= 15) continue;
                        var (min, max) = param.Bounds(nrEffect);
                        float currentVal = nrEffect.Parameters.Length > param.Slot ? nrEffect.Parameters[param.Slot] : Catalog.DefaultValue(param.Default);
                        currentVal = Math.Clamp(currentVal, min, max);

                        var slider = CreateKnobSlider(min, max, currentVal);
                        int capturedSlot = param.Slot;
                        slider.ValueChanged += (_, e) =>
                        {
                            if (updatingUi) return;
                            try
                            {
                                edit(d => d.SetParameter(activeNrModule, capturedSlot, (float)e.NewValue, catalog));
                            }
                            catch {}
                        };

                        nrSliders[capturedSlot] = slider;
                        nrKnobsHost.Children.Add(KnobTile(param.Name.ToUpperInvariant(), slider, param.Unit));
                    }
                }
            }
            else if (currentNrAlgo != null)
            {
                foreach (var param in currentNrAlgo.Parameters)
                {
                    if (nrSliders.TryGetValue(param.Slot, out var slider))
                    {
                        var (min, max) = param.Bounds(nrEffect);
                        float currentVal = nrEffect.Parameters.Length > param.Slot ? nrEffect.Parameters[param.Slot] : Catalog.DefaultValue(param.Default);
                        slider.Value = Math.Clamp(currentVal, min, max);
                    }
                }
            }

            // EQ (activeEqModule)
            var eqEffect = doc.GetEffect(activeEqModule);
            eqBypass.IsChecked = eqEffect.Enabled;
            var currentEqAlgo = catalog.Find(activeEqModule, eqEffect.Code);
            if (currentEqAlgo != null) eqSelector.SelectedItem = currentEqAlgo;

            if (eqEffect.Code != lastEqCode)
            {
                eqBarsHost.Children.Clear();
                eqBarsHost.ColumnDefinitions.Clear();
                eqSliders.Clear();
                lastEqCode = eqEffect.Code;

                if (currentEqAlgo != null && currentEqAlgo.Parameters.Count > 0)
                {
                    int count = Math.Min(currentEqAlgo.Parameters.Count, 10);
                    for (int i = 0; i < count; i++)
                        eqBarsHost.ColumnDefinitions.Add(new ColumnDefinition());

                    for (int col = 0; col < count; col++)
                    {
                        var param = currentEqAlgo.Parameters[col];
                        if (param.Slot < 0 || param.Slot >= 15) continue;
                        var (min, max) = param.Bounds(eqEffect);
                        float currentVal = eqEffect.Parameters.Length > param.Slot ? eqEffect.Parameters[param.Slot] : Catalog.DefaultValue(param.Default);
                        currentVal = Math.Clamp(currentVal, min, max);

                        var slider = CreateEqSlider(min, max, currentVal);
                        int capturedSlot = param.Slot;
                        slider.ValueChanged += (_, e) =>
                        {
                            if (updatingUi) return;
                            try
                            {
                                edit(d => d.SetParameter(activeEqModule, capturedSlot, (float)e.NewValue, catalog));
                            }
                            catch {}
                        };

                        eqSliders[capturedSlot] = slider;
                        eqBarsHost.Children.Add(EqBandColumn(param.Name, slider, col));
                    }
                }
            }
            else if (currentEqAlgo != null)
            {
                foreach (var param in currentEqAlgo.Parameters)
                {
                    if (eqSliders.TryGetValue(param.Slot, out var slider))
                    {
                        var (min, max) = param.Bounds(eqEffect);
                        float currentVal = eqEffect.Parameters.Length > param.Slot ? eqEffect.Parameters[param.Slot] : Catalog.DefaultValue(param.Default);
                        slider.Value = Math.Clamp(currentVal, min, max);
                    }
                }
            }
        }
        catch {}
        finally
        {
            updatingUi = false;
        }
    }

    static Border CreateSectionCard(string header, UIElement content)
    {
        var border = new Border
        {
            Background = DarkUiTheme.CardBrush,
            BorderBrush = DarkUiTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };
        var sp = new Grid();
        sp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sp.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock
        {
            Text = header,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = DarkUiTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(title, 0);
        sp.Children.Add(title);

        Grid.SetRow((FrameworkElement)content, 1);
        sp.Children.Add(content);
        border.Child = sp;
        return border;
    }

    static Border KnobTile(string label, Slider slider, string unit = "")
    {
        var b = new Border
        {
            Background = DarkUiTheme.InputBrush,
            BorderBrush = DarkUiTheme.InputBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 8, 8),
            Width = 140
        };
        var sp = new StackPanel();
        var valText = new TextBlock { Text = $"{(int)slider.Value} {unit}".Trim(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AccentBrush, HorizontalAlignment = HorizontalAlignment.Right };
        var top = new DockPanel();
        DockPanel.SetDock(valText, Dock.Right);
        top.Children.Add(valText);
        top.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DarkUiTheme.MutedBrush });
        sp.Children.Add(top);

        slider.ValueChanged += (_, e) => valText.Text = $"{(int)e.NewValue} {unit}".Trim();
        sp.Children.Add(slider);
        b.Child = sp;
        return b;
    }

    static UIElement EqBandColumn(string label, Slider slider, int col)
    {
        var sp = new StackPanel { Margin = new Thickness(4) };
        Grid.SetColumn(sp, col);
        var valText = new TextBlock { Text = $"{(int)slider.Value:+0;-0;0} dB", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center };
        slider.ValueChanged += (_, e) => valText.Text = $"{(int)e.NewValue:+0;-0;0} dB";
        slider.Orientation = Orientation.Vertical;
        slider.Height = 130;
        slider.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(valText);
        sp.Children.Add(slider);
        sp.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = DarkUiTheme.MutedBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        return sp;
    }

    static Slider CreateKnobSlider(double min, double max, double def) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(def, min, max),
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
        Cursor = System.Windows.Input.Cursors.Hand,
        Margin = new Thickness(0, 4, 0, 0)
    };

    static Slider CreateEqSlider(double min = -12, double max = 12, double def = 0) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(def, min, max),
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    static SolidColorBrush BrushHex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
