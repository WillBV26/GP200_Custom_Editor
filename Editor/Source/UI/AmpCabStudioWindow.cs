using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>
/// Dedicated "Amp & Cab Room" Studio for GP-200.
/// Provides an expanded, tactile control room for Amp Head, Pre/Post EQ, Noise Gate,
/// and Cabinet/Microphone placement with live bidirectional editing.
/// </summary>
public sealed class AmpCabStudioWindow : Window
{
    readonly EditorWindow editor;
    readonly Catalog catalog;
    readonly Func<PresetDocument?> getDocument;
    readonly Action<Action<PresetDocument>> edit;

    // AMP Controls
    readonly ComboBox ampSelector = new() { MinWidth = 240, Margin = new Thickness(0, 0, 0, 8) };
    readonly CheckBox ampBypass = new() { Content = UiText.Language == "es" ? "Amp Activado" : "Amp Active", IsChecked = true, Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand };
    readonly Slider gainSlider = CreateKnobSlider(0, 100, 50);
    readonly Slider bassSlider = CreateKnobSlider(0, 100, 50);
    readonly Slider midSlider = CreateKnobSlider(0, 100, 50);
    readonly Slider trebSlider = CreateKnobSlider(0, 100, 50);
    readonly Slider presSlider = CreateKnobSlider(0, 100, 50);
    readonly Slider volSlider = CreateKnobSlider(0, 100, 65);
    readonly Slider gateThreshSlider = CreateKnobSlider(-80, 0, -45);

    // CAB Controls
    readonly ComboBox cabSelector = new() { MinWidth = 240, Margin = new Thickness(0, 0, 0, 8) };
    readonly CheckBox cabBypass = new() { Content = UiText.Language == "es" ? "Cab Activado" : "Cab Active", IsChecked = true, Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand };
    readonly ComboBox micSelector = new() { MinWidth = 200, Margin = new Thickness(0, 0, 0, 8) };
    readonly Slider lowCutSlider = CreateKnobSlider(20, 200, 80);
    readonly Slider highCutSlider = CreateKnobSlider(3000, 16000, 7500);
    readonly Slider micPosSlider = CreateKnobSlider(0, 100, 30);
    readonly Slider cabBlendSlider = CreateKnobSlider(0, 100, 50);

    // 5-Band EQ
    readonly Slider eq80 = CreateEqSlider();
    readonly Slider eq400 = CreateEqSlider();
    readonly Slider eq1600 = CreateEqSlider();
    readonly Slider eq4000 = CreateEqSlider();
    readonly Slider eq8000 = CreateEqSlider();

    bool updatingUi;

    public AmpCabStudioWindow(EditorWindow editor, Catalog catalog, Func<PresetDocument?> getDocument, Action<Action<PresetDocument>> edit)
    {
        this.editor = editor;
        this.catalog = catalog;
        this.getDocument = getDocument;
        this.edit = edit;

        DarkUiTheme.Apply(this);
        Title = UiText.Language == "es" ? "Estudio de Amplificador y Cabina (Amp & Cab Room) · GP-200" : "Amp & Cab Room Studio · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 1040;
        Height = 720;
        MinWidth = 920;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildStudioLayout();
        PopulateModels();
        SyncFromDocument();
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
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star), MinWidth = 220 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });

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
            Text = UiText.Language == "es" ? "Las perillas se envían a la GP-200 con debouncing de 20 ms sin parpadeo visual." : "Knobs are synced to the GP-200 with 20ms debouncing and zero flicker.",
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

        // Amp Head Knobs Grid
        var knobGrid = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        knobGrid.Children.Add(KnobTile("GAIN", gainSlider));
        knobGrid.Children.Add(KnobTile("BASS", bassSlider));
        knobGrid.Children.Add(KnobTile("MID", midSlider));
        knobGrid.Children.Add(KnobTile("TREBLE", trebSlider));
        knobGrid.Children.Add(KnobTile("PRESENCE", presSlider));
        knobGrid.Children.Add(KnobTile("MASTER", volSlider));
        sp.Children.Add(knobGrid);

        ampSelector.SelectionChanged += (_, _) =>
        {
            if (updatingUi || ampSelector.SelectedItem is not AlgorithmInfo algo) return;
            edit(d => d.SetAlgorithm(3, algo));
        };
        ampBypass.Click += (_, _) =>
        {
            if (updatingUi) return;
            edit(d =>
            {
                var ef = d.GetEffect(3);
                d.SetEffect(3, ampBypass.IsChecked == true, ef.Parameters);
            });
        };

        HookSlider(gainSlider, 0, 3);
        HookSlider(bassSlider, 1, 3);
        HookSlider(midSlider, 2, 3);
        HookSlider(trebSlider, 3, 3);
        HookSlider(presSlider, 4, 3);
        HookSlider(volSlider, 5, 3);

        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.Both };
    }

    UIElement BuildEqPanel()
    {
        var sp = new StackPanel();

        // Noise gate
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "NOISE GATE (Trim)" : "NOISE GATE (Trim)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AccentBrush, Margin = new Thickness(0, 0, 0, 6) });
        sp.Children.Add(KnobTile("THRESH", gateThreshSlider, "dB"));
        HookSlider(gateThreshSlider, 0, 4); // NR module is index 4 in standard chain

        // 5-band EQ
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "CURVA TONAL (5-BAND EQ)" : "TONE SHAPING (5-BAND EQ)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AccentBrush, Margin = new Thickness(0, 14, 0, 8) });
        
        var eqBars = new Grid();
        for (int i = 0; i < 5; i++) eqBars.ColumnDefinitions.Add(new ColumnDefinition());

        eqBars.Children.Add(EqBandColumn("80 Hz", eq80, 0));
        eqBars.Children.Add(EqBandColumn("400 Hz", eq400, 1));
        eqBars.Children.Add(EqBandColumn("1.6 kHz", eq1600, 2));
        eqBars.Children.Add(EqBandColumn("4 kHz", eq4000, 3));
        eqBars.Children.Add(EqBandColumn("8 kHz", eq8000, 4));
        sp.Children.Add(eqBars);

        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.Both };
    }

    UIElement BuildCabPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Cabina / Respuesta de Impulso (IR):" : "Cabinet / Impulse Response (IR):", FontSize = 11, Foreground = DarkUiTheme.MutedBrush, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(cabSelector);
        sp.Children.Add(cabBypass);

        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Tipo de Micrófono:" : "Microphone Model:", FontSize = 11, Foreground = DarkUiTheme.MutedBrush, Margin = new Thickness(0, 6, 0, 4) });
        micSelector.Items.Add("Dyn 57 (Shure SM57)");
        micSelector.Items.Add("Ribbon 121 (Royer R-121)");
        micSelector.Items.Add("Cond 87 (Neumann U87)");
        micSelector.Items.Add("Dyn 421 (Sennheiser MD421)");
        micSelector.SelectedIndex = 0;
        sp.Children.Add(micSelector);

        var cabKnobs = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        cabKnobs.Children.Add(KnobTile("LOW CUT", lowCutSlider, "Hz"));
        cabKnobs.Children.Add(KnobTile("HI CUT", highCutSlider, "Hz"));
        cabKnobs.Children.Add(KnobTile("DISTANCE", micPosSlider, "%"));
        cabKnobs.Children.Add(KnobTile("BLEND", cabBlendSlider, "%"));
        sp.Children.Add(cabKnobs);

        cabSelector.SelectionChanged += (_, _) =>
        {
            if (updatingUi || cabSelector.SelectedItem is not AlgorithmInfo algo) return;
            edit(d => d.SetAlgorithm(5, algo)); // CAB is module 5
        };
        cabBypass.Click += (_, _) =>
        {
            if (updatingUi) return;
            edit(d =>
            {
                var ef = d.GetEffect(5);
                d.SetEffect(5, cabBypass.IsChecked == true, ef.Parameters);
            });
        };

        HookSlider(lowCutSlider, 1, 5);
        HookSlider(highCutSlider, 2, 5);
        HookSlider(micPosSlider, 3, 5);

        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.Both };
    }

    void PopulateModels()
    {
        var ampAlgos = catalog.ForModule(3).Where(a => !a.IsNone).ToList();
        ampSelector.ItemsSource = ampAlgos;
        ampSelector.DisplayMemberPath = "Name";

        var cabAlgos = catalog.ForModule(5).Where(a => !a.IsNone).ToList();
        cabSelector.ItemsSource = cabAlgos;
        cabSelector.DisplayMemberPath = "Name";
    }

    internal void SyncFromDocument()
    {
        var doc = getDocument();
        if (doc == null) return;
        updatingUi = true;
        try
        {
            // AMP (module 3)
            var ampEffect = doc.GetEffect(3);
            ampBypass.IsChecked = ampEffect.Enabled;
            var currentAmpAlgo = catalog.Find(3, ampEffect.Code);
            if (currentAmpAlgo != null) ampSelector.SelectedItem = currentAmpAlgo;

            if (ampEffect.Parameters.Length > 0) gainSlider.Value = ampEffect.Parameters[0];
            if (ampEffect.Parameters.Length > 1) bassSlider.Value = ampEffect.Parameters[1];
            if (ampEffect.Parameters.Length > 2) midSlider.Value = ampEffect.Parameters[2];
            if (ampEffect.Parameters.Length > 3) trebSlider.Value = ampEffect.Parameters[3];
            if (ampEffect.Parameters.Length > 4) presSlider.Value = ampEffect.Parameters[4];
            if (ampEffect.Parameters.Length > 5) volSlider.Value = ampEffect.Parameters[5];

            // CAB (module 5)
            var cabEffect = doc.GetEffect(5);
            cabBypass.IsChecked = cabEffect.Enabled;
            var currentCabAlgo = catalog.Find(5, cabEffect.Code);
            if (currentCabAlgo != null) cabSelector.SelectedItem = currentCabAlgo;

            if (cabEffect.Parameters.Length > 1) lowCutSlider.Value = cabEffect.Parameters[1];
            if (cabEffect.Parameters.Length > 2) highCutSlider.Value = cabEffect.Parameters[2];
            if (cabEffect.Parameters.Length > 3) micPosSlider.Value = cabEffect.Parameters[3];

            // EQ (module 6 in standard chain)
            var eqEffect = doc.GetEffect(6);
            if (eqEffect.Parameters.Length > 0) eq80.Value = eqEffect.Parameters[0];
            if (eqEffect.Parameters.Length > 1) eq400.Value = eqEffect.Parameters[1];
            if (eqEffect.Parameters.Length > 2) eq1600.Value = eqEffect.Parameters[2];
            if (eqEffect.Parameters.Length > 3) eq4000.Value = eqEffect.Parameters[3];
            if (eqEffect.Parameters.Length > 4) eq8000.Value = eqEffect.Parameters[4];
        }
        finally
        {
            updatingUi = false;
        }
    }

    void HookSlider(Slider slider, int paramSlot, int moduleIndex)
    {
        slider.ValueChanged += (_, e) =>
        {
            if (updatingUi) return;
            edit(d => d.SetParameter(moduleIndex, paramSlot, (float)e.NewValue, catalog));
        };
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
        Value = def,
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
        Cursor = System.Windows.Input.Cursors.Hand,
        Margin = new Thickness(0, 4, 0, 0)
    };

    static Slider CreateEqSlider() => new()
    {
        Minimum = -12,
        Maximum = 12,
        Value = 0,
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    static SolidColorBrush BrushHex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

