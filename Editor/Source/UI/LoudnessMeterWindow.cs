using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Gp200.Replica;

internal sealed class LoudnessQueueItem
{
    public bool IsSelected { get; set; } = true;
    public PresetDocument Document { get; set; }
    public string Name => Document.Name;
    public bool IsLead { get; set; }
    public int OriginalVolume => Document.Volume;
    public int AdjustedVolume { get; set; }
    public string Status { get; set; } = "En espera";
    public int TargetSlot { get; set; }

    public LoudnessQueueItem(PresetDocument doc, int targetSlot = -1)
    {
        Document = doc;
        AdjustedVolume = doc.Volume;
        TargetSlot = targetSlot >= 0 ? targetSlot : doc.Index;
    }
}

internal sealed class LoudnessMeterWindow : Window
{
    static readonly string[] SlotPositions = Enumerable.Range(0, 256).Select(BankService.Position).ToArray();

    // Section A: Pedal Status
    readonly TextBlock pedalStatus = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = DarkUiTheme.SuccessBrush };

    // Section B: Audio Input
    readonly Border signalLed = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    readonly ComboBox input = new() { MinWidth = 280, Margin = new Thickness(0, 0, 0, 4) };
    readonly ProgressBar inputVuMeter = new() { Height = 8, Minimum = -60, Maximum = 0, Value = -60, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock inputVuLabel = new() { Text = UiText.Language == "es" ? "Nivel de entrada: —" : "Input Level: —", FontSize = 11, Foreground = DarkUiTheme.AuraTextMutedBrush };

    // Section C: Working Slot & Reference
    readonly ComboBox workingSlotCombo = new() { MinWidth = 140 };
    readonly ComboBox referencePresetCombo = new() { MinWidth = 220, IsEnabled = false };
    readonly CheckBox matchToReferenceCheck = new() { Content = UiText.Language == "es" ? "Igualar todos a un preset de referencia" : "Match all to a reference preset", Foreground = DarkUiTheme.AuraTextMainBrush, Margin = new Thickness(0, 6, 0, 4) };
    readonly TextBlock refStatus = new() { FontSize = 11, Foreground = DarkUiTheme.WarningBrush, Text = UiText.Language == "es" ? "Objetivos estándar: Normal (-14 LUFS) · Lead (-12 LUFS)" : "Standard targets: Normal (-14 LUFS) · Lead (-12 LUFS)" };

    // Meter Displays
    readonly TextBlock momentary = new() { FontSize = 28, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock integrated = new() { FontSize = 36, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.SuccessBrush, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock peak = new() { FontSize = 12, Foreground = DarkUiTheme.AuraTextMutedBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4) };
    readonly ProgressBar momentaryBar = new() { Height = 10, Minimum = -60, Maximum = 0, Value = -60, Margin = new Thickness(0, 4, 0, 6) };

    // Section D: Batch Queue
    readonly ListBox queueListBox = new() { MinHeight = 110, MaxHeight = 180, Margin = new Thickness(0, 6, 0, 6) };
    readonly List<LoudnessQueueItem> queueItems = [];
    readonly Button addActiveBtn = new() { Content = UiText.Language == "es" ? "+ Añadir preset activo" : "+ Add active preset", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button addFileBtn = new() { Content = UiText.Language == "es" ? "+ Cargar archivo .prst" : "+ Load .prst file", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button startBatchBtn = new() { Content = UiText.Language == "es" ? "⚡ Nivelar en Vivo (Tiempo Real)" : "⚡ Match Active Live", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button selectiveSaveBtn = new() { Content = UiText.Language == "es" ? "💾 Nivelar Presets Seleccionados en GP-200" : "💾 Level Selected in GP-200", Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = true };

    // Bottom controls
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = DarkUiTheme.AuraTextMutedBrush, FontSize = 12 };
    readonly Button startStop = new() { MinWidth = 130, Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button autoLevelBtn = new() { MinWidth = 220, Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = false };
    readonly Button closeBtn = new() { Content = UiText.Language == "es" ? "Cerrar" : "Close", MinWidth = 80, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };

    readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Func<PresetDocument?>? getPreset;
    readonly Action<int>? applyVolume;
    readonly AudioCaptureEngine engine = new();
    LoudnessMeter? meter;
    bool closing;

    internal LoudnessMeterWindow(Func<PresetDocument?>? getPreset = null, Action<int>? applyVolume = null, IEnumerable<PresetDocument>? initialPresets = null)
    {
        this.getPreset = getPreset;
        this.applyVolume = applyVolume;

        EditorWindow.StyleDialog(this, false);
        Title = UiText.Language == "es" ? "Nivelación de Volumen y Medidor BS.1770 LUFS · GP-200" : "Volume Matcher & BS.1770 LUFS · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 750;
        Height = 740;
        MinWidth = 640;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Initialize 256 working slots (default 58-D = slot 231)
        for (int i = 0; i < 256; i++)
        {
            workingSlotCombo.Items.Add($"{BankService.Position(i)} (Slot {i + 1})");
        }
        workingSlotCombo.SelectedIndex = 231; // 58-D

        startStop.Content = UiText.Language == "es" ? "Detener medición" : "Stop measurement";
        startStop.Background = DarkUiTheme.HighlightBrush;
        startStop.Foreground = Brushes.White;

        autoLevelBtn.Content = UiText.Language == "es" ? "⚡ Igualar Preset Activo en Vivo" : "⚡ Match Active Live (SysEx)";
        autoLevelBtn.Background = DarkUiTheme.SuccessBrush;
        autoLevelBtn.Foreground = Brushes.White;
        autoLevelBtn.FontWeight = FontWeights.Bold;

        startBatchBtn.Background = DarkUiTheme.HighlightBrush;
        startBatchBtn.Foreground = Brushes.White;
        startBatchBtn.FontWeight = FontWeights.Bold;
        startBatchBtn.Padding = new Thickness(10, 4, 10, 4);

        selectiveSaveBtn.Background = DarkUiTheme.SuccessBrush;
        selectiveSaveBtn.Foreground = Brushes.White;
        selectiveSaveBtn.FontWeight = FontWeights.Bold;
        selectiveSaveBtn.Padding = new Thickness(10, 4, 10, 4);

        addActiveBtn.Background = DarkUiTheme.AuraBgCardBrush;
        addActiveBtn.Foreground = Brushes.White;
        addActiveBtn.Padding = new Thickness(8, 4, 8, 4);

        addFileBtn.Background = DarkUiTheme.AuraBgCardBrush;
        addFileBtn.Foreground = Brushes.White;
        addFileBtn.Padding = new Thickness(8, 4, 8, 4);

        closeBtn.Background = DarkUiTheme.AuraBgCardBrush;
        closeBtn.Foreground = DarkUiTheme.AuraTextMainBrush;

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerBar = new AuraWindowHeaderBar(this, Title, rootGrid, UiText.Language == "es" ? "NORMALIZADOR BS.1770" : "BS.1770 NORMALIZER");
        Grid.SetRow(headerBar, 0);
        rootGrid.Children.Add(headerBar);

        var innerDock = new DockPanel { Margin = new Thickness(16) };
        Grid.SetRow(innerDock, 1);
        rootGrid.Children.Add(innerDock);

        // Bottom Actions
        var bottomBar = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bottomBar, Dock.Bottom);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(startStop);
        actions.Children.Add(autoLevelBtn);
        actions.Children.Add(closeBtn);
        bottomBar.Children.Add(actions);
        bottomBar.Children.Add(status);
        innerDock.Children.Add(bottomBar);

        // Scrollable content
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        var mainStack = new StackPanel();

        // CARD A: El Pedal
        var pedalCard = Card(UiText.Language == "es" ? "A — ESTADO DE CONEXIÓN GP-200" : "A — GP-200 CONNECTION STATUS");
        var pStack = new StackPanel();
        bool isConn = DeviceSession.Current.Connected;
        pedalStatus.Text = isConn
            ? (UiText.Language == "es" ? "● GP-200 Conectada y Lista para Envío SysEx en Vivo" : "● GP-200 Connected via USB WinMM")
            : (UiText.Language == "es" ? "○ GP-200 Desconectada" : "○ GP-200 Disconnected");
        pedalStatus.Foreground = isConn ? DarkUiTheme.SuccessBrush : DarkUiTheme.WarningBrush;
        pStack.Children.Add(pedalStatus);
        pedalCard.Child = pStack;
        mainStack.Children.Add(pedalCard);

        // CARD B: Entrada de Audio (Clean UI with LED)
        var inputCard = Card(UiText.Language == "es" ? "B — ENTRADA DE AUDIO (ASIO / WASAPI < 5ms)" : "B — AUDIO INPUT (ASIO / WASAPI < 5ms)");
        var inStack = new StackPanel();
        var inRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        inRow.Children.Add(signalLed);
        inRow.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Dispositivo de captura de audio:" : "Audio capture device:",
            FontWeight = FontWeights.SemiBold,
            Foreground = DarkUiTheme.AuraCyanPrimaryBrush
        });
        inStack.Children.Add(inRow);
        inStack.Children.Add(input);
        inStack.Children.Add(inputVuLabel);
        inStack.Children.Add(inputVuMeter);
        inputCard.Child = inStack;
        mainStack.Children.Add(inputCard);

        // CARD C: Slot de Trabajo y Referencia
        var workCard = Card(UiText.Language == "es" ? "C — CONFIGURACIÓN DE OBJETIVOS LUFS" : "C — TARGET LUFS CONFIGURATION");
        var wGrid = new Grid();
        wGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        wGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var wLabel = new TextBlock
        {
            Text = UiText.Language == "es" ? "Slot de Referencia / Trabajo:" : "Working / Reference Slot:",
            FontWeight = FontWeights.SemiBold,
            Foreground = DarkUiTheme.AuraTextMainBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        wGrid.Children.Add(wLabel);
        Grid.SetColumn(workingSlotCombo, 2);
        wGrid.Children.Add(workingSlotCombo);

        var wStack = new StackPanel();
        wStack.Children.Add(wGrid);
        wStack.Children.Add(matchToReferenceCheck);
        var referenceRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        referenceRow.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Preset de referencia:" : "Reference preset:",
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        referenceRow.Children.Add(referencePresetCombo);
        wStack.Children.Add(referenceRow);
        wStack.Children.Add(refStatus);
        matchToReferenceCheck.Checked += (_, _) =>
        {
            referencePresetCombo.IsEnabled = true;
            refStatus.Text = UiText.Language == "es"
                ? "Selecciona un preset de referencia: Normal = referencia, Lead = referencia + 2.5 LUFS."
                : "Choose a baseline reference preset; Normal = reference, Lead = reference + 2.5 LUFS.";
            refStatus.Foreground = DarkUiTheme.SuccessBrush;
        };
        matchToReferenceCheck.Unchecked += (_, _) =>
        {
            referencePresetCombo.IsEnabled = false;
            refStatus.Text = UiText.Language == "es"
                ? "Objetivos estándar: Normal (-14 LUFS) · Lead (-12 LUFS)"
                : "Standard targets: Normal (-14 LUFS) · Lead (-12 LUFS)";
            refStatus.Foreground = DarkUiTheme.WarningBrush;
        };
        workCard.Child = wStack;
        mainStack.Children.Add(workCard);

        // CARD: Medición LUFS en Vivo
        var meterCard = Card(UiText.Language == "es" ? "D — MEDIDOR BS.1770 EN VIVO (LUFS / dBFS)" : "D — LIVE BS.1770 METER (LUFS / dBFS)");
        var mStack = new StackPanel();
        mStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "SONORIDAD INTEGRADA (LUFS)" : "INTEGRATED LOUDNESS (LUFS)",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        mStack.Children.Add(integrated);
        mStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Momentánea (400 ms):" : "Momentary (400 ms):",
            FontSize = 11,
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            Margin = new Thickness(0, 6, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        mStack.Children.Add(momentary);
        mStack.Children.Add(momentaryBar);
        mStack.Children.Add(peak);
        meterCard.Child = mStack;
        mainStack.Children.Add(meterCard);

        // CARD E: Nivelación Selectiva de Presets
        var batchCard = Card(UiText.Language == "es" ? "E — NIVELACIÓN SELECTIVA DE BANCOS / SETLIST" : "E — SELECTIVE BANK / SETLIST LEVELING");
        var bStack = new StackPanel();

        var bToolBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        bToolBar.Children.Add(addActiveBtn);
        bToolBar.Children.Add(addFileBtn);
        bToolBar.Children.Add(startBatchBtn);
        bToolBar.Children.Add(selectiveSaveBtn);
        bStack.Children.Add(bToolBar);

        bStack.Children.Add(queueListBox);
        batchCard.Child = bStack;
        mainStack.Children.Add(batchCard);

        scroll.Content = mainStack;
        innerDock.Children.Add(scroll);
        Content = rootGrid;

        if (initialPresets != null)
            AddPresetsToQueue(initialPresets);

        // Engine wiring
        engine.StatusChanged += text => Dispatcher.BeginInvoke(() =>
        {
            status.Text = text;
            status.Foreground = text.Contains("Error", StringComparison.OrdinalIgnoreCase) ? DarkUiTheme.DangerBrush :
                text.Contains("fallback", StringComparison.OrdinalIgnoreCase) ? DarkUiTheme.WarningBrush : DarkUiTheme.AuraTextMutedBrush;
        });
        engine.SamplesAvailable += ProcessSamples;

        input.ItemsSource = engine.Devices.Select(d => d.Name).ToArray();
        if (engine.SelectedIndex >= 0 && engine.SelectedIndex < engine.Devices.Count)
        {
            input.SelectedIndex = engine.SelectedIndex;
        }

        input.SelectionChanged += (_, _) =>
        {
            if (input.SelectedIndex >= 0 && input.SelectedIndex < engine.Devices.Count)
            {
                ResetMeasurements();
                SetCaptureState(engine.Start(input.SelectedIndex));
            }
        };

        startStop.Click += (_, _) =>
        {
            if (engine.IsCapturing)
            {
                engine.Stop();
                ResetMeasurements();
                SetCaptureState(false);
            }
            else
            {
                if (input.SelectedIndex >= 0) SetCaptureState(engine.Start(input.SelectedIndex));
            }
        };

        autoLevelBtn.Click += async (_, _) => await AutoLevelActivePresetAsync();
        startBatchBtn.Click += async (_, _) => await AutoLevelActivePresetAsync();
        addActiveBtn.Click += (_, _) => AddActivePresetToQueue();
        addFileBtn.Click += (_, _) => AddFileToQueue();
        selectiveSaveBtn.Click += async (_, _) => await SaveSelectedPresetsToPedalAsync();
        closeBtn.Click += (_, _) => Close();

        displayTimer.Tick += DisplayTimer_Tick;
        displayTimer.Start();

        Loaded += (_, _) =>
        {
            if (input.SelectedIndex >= 0) SetCaptureState(engine.Start(input.SelectedIndex));
            AddActivePresetToQueue();
        };

        Closing += (_, _) =>
        {
            closing = true;
            displayTimer.Stop();
            engine.Stop();
            engine.Dispose();
        };
    }

    static Border Card(string title)
    {
        var border = new Border
        {
            Background = DarkUiTheme.AuraBgPanelBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        return border;
    }

    void SetCaptureState(bool started)
    {
        startStop.Content = started
            ? (UiText.Language == "es" ? "Detener medición" : "Stop measurement")
            : (UiText.Language == "es" ? "Iniciar medición" : "Start measurement");
        startStop.Background = started ? DarkUiTheme.SuccessBrush : DarkUiTheme.HighlightBrush;
    }

    void ResetMeasurements()
    {
        meter = null;
        inputVuMeter.Value = -60;
        momentary.Text = "— LUFS";
        momentaryBar.Value = -60;
        integrated.Text = "— LUFS";
        peak.Text = "True Peak: — dBFS";
        signalLed.Background = Brushes.DimGray;
        autoLevelBtn.IsEnabled = false;
    }

    void AddActivePresetToQueue()
    {
        var current = getPreset?.Invoke();
        if (current == null) return;
        if (queueItems.Any(i => i.Document.Name == current.Name && i.TargetSlot == current.Index)) return;
        AddPresetsToQueue([current]);
    }

    internal void AddPresetsToQueue(IEnumerable<PresetDocument> presets)
    {
        foreach (var preset in presets)
            queueItems.Add(new LoudnessQueueItem(new PresetDocument(preset.ToBytes()), preset.Index));
        RefreshQueueUi();
    }

    void AddFileToQueue()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "GP-200 Presets (*.prst)|*.prst|All Files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true) return;
        var failures = new List<string>();
        foreach (var file in dlg.FileNames)
        {
            try
            {
                var doc = new PresetDocument(File.ReadAllBytes(file));
                queueItems.Add(new LoudnessQueueItem(doc, -1));
            }
            catch (Exception error)
            {
                failures.Add($"{Path.GetFileName(file)}: {error.Message}");
            }
        }
        RefreshQueueUi();
        if (failures.Count > 0)
        {
            status.Text = string.Join(Environment.NewLine, failures);
            status.Foreground = DarkUiTheme.DangerBrush;
        }
    }

    void RefreshQueueUi()
    {
        queueListBox.Items.Clear();
        foreach (var item in queueItems)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var check = new CheckBox
            {
                IsChecked = item.IsSelected,
                VerticalAlignment = VerticalAlignment.Center
            };
            check.Checked += (_, _) => item.IsSelected = true;
            check.Unchecked += (_, _) => item.IsSelected = false;
            row.Children.Add(check);

            var nameBlock = new TextBlock { Text = item.Name, Foreground = DarkUiTheme.AuraTextMainBrush, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameBlock, 1);
            row.Children.Add(nameBlock);

            var typeCombo = new ComboBox { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            typeCombo.Items.Add("Normal (-14 LUFS)");
            typeCombo.Items.Add("Lead (-12 LUFS)");
            typeCombo.SelectedIndex = item.IsLead ? 1 : 0;
            typeCombo.SelectionChanged += (_, _) => { item.IsLead = typeCombo.SelectedIndex == 1; };
            Grid.SetColumn(typeCombo, 2);
            row.Children.Add(typeCombo);

            var targetSlot = new ComboBox
            {
                ItemsSource = SlotPositions,
                SelectedIndex = item.TargetSlot is >= 0 and < 256 ? item.TargetSlot : -1,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            targetSlot.SelectionChanged += (_, _) => item.TargetSlot = targetSlot.SelectedIndex;
            Grid.SetColumn(targetSlot, 3);
            row.Children.Add(targetSlot);

            var volBlock = new TextBlock { Text = $"Vol: {item.AdjustedVolume} (Orig {item.OriginalVolume})", Foreground = DarkUiTheme.AuraCyanPrimaryBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(volBlock, 4);
            row.Children.Add(volBlock);

            var statBlock = new TextBlock { Text = item.Status, Foreground = DarkUiTheme.AuraTextMutedBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(statBlock, 5);
            row.Children.Add(statBlock);

            queueListBox.Items.Add(row);
        }

        var previousReference = referencePresetCombo.SelectedItem as LoudnessQueueItem;
        referencePresetCombo.DisplayMemberPath = nameof(LoudnessQueueItem.Name);
        referencePresetCombo.ItemsSource = queueItems.ToArray();
        referencePresetCombo.SelectedItem = previousReference != null && queueItems.Contains(previousReference)
            ? previousReference
            : queueItems.FirstOrDefault();
        referencePresetCombo.IsEnabled = matchToReferenceCheck.IsChecked == true && queueItems.Count > 0;
    }

    private async Task AutoLevelActivePresetAsync()
    {
        var snap = meter?.Snapshot();
        if (snap?.IntegratedLufs is not double measured || !double.IsFinite(measured))
        {
            status.Text = UiText.Language == "es" ? "⚠️ No hay medición integrada suficiente. Toca la guitarra unos segundos." : "⚠️ Insufficient integrated measurement.";
            status.Foreground = DarkUiTheme.WarningBrush;
            return;
        }

        var doc = getPreset?.Invoke();
        if (doc == null) return;

        double target = matchToReferenceCheck.IsChecked == true && referencePresetCombo.SelectedItem is LoudnessQueueItem refItem
            ? -14.0
            : -14.0;

        var result = LoudnessMatcher.MatchPresetVolume(doc, measured, target);
        applyVolume?.Invoke(result.AdjustedVolume);

        // Immediate real-time SysEx update to pedal output
        bool liveSynced = await LoudnessMatcher.ApplyLiveVolumeToPedalAsync(result.AdjustedPreset, result.AdjustedVolume);

        status.Text = result.Summary + (liveSynced ? (UiText.Language == "es" ? " (✓ Sincronizado en vivo con GP-200)" : " (✓ Live synced to GP-200)") : "");
        status.Foreground = DarkUiTheme.SuccessBrush;
    }

    private async Task SaveSelectedPresetsToPedalAsync()
    {
        var selectedItems = queueItems.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            status.Text = UiText.Language == "es" ? "Marca con la casilla al menos un preset para nivelar." : "Check at least one preset to level.";
            status.Foreground = DarkUiTheme.WarningBrush;
            return;
        }

        if (!ReadOnlyMidi.IsDeviceAvailable())
        {
            status.Text = UiText.Language == "es" ? "Conecta la GP-200 por USB antes de escribir en Flash." : "Connect GP-200 via USB before saving.";
            status.Foreground = DarkUiTheme.WarningBrush;
            return;
        }

        string slotsList = string.Join(", ", selectedItems.Select(i => BankService.Position(i.TargetSlot)));
        if (UiDialogs.Show(this,
            UiText.Language == "es"
                ? $"Se escribirán únicamente los slots seleccionados: {slotsList}. Los demás presets de la pedalera no se modificarán. ¿Continuar?"
                : $"Only the selected slots will be written: {slotsList}. Other presets will remain untouched. Continue?",
            UiText.Language == "es" ? "Nivelación Selectiva" : "Selective Leveling",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        selectiveSaveBtn.IsEnabled = false;
        var bankService = new BankService();
        int done = 0;

        try
        {
            foreach (var item in selectedItems)
            {
                status.Text = UiText.Language == "es"
                    ? $"Guardando {item.Name} en slot {BankService.Position(item.TargetSlot)}…"
                    : $"Saving {item.Name} to slot {BankService.Position(item.TargetSlot)}…";

                var adjustedDoc = new PresetDocument(item.Document.ToBytes());
                adjustedDoc.SetGeneral(adjustedDoc.Name, item.AdjustedVolume, adjustedDoc.Tempo);
                await bankService.ReplaceAsync(item.TargetSlot, adjustedDoc);
                item.Status = "✓ Guardado";
                done++;
                RefreshQueueUi();
            }

            status.Text = UiText.Language == "es"
                ? $"✓ {done} presets seleccionados guardados quirúrgicamente en la GP-200."
                : $"✓ {done} selected presets saved surgically to GP-200.";
            status.Foreground = DarkUiTheme.SuccessBrush;
        }
        catch (Exception ex)
        {
            status.Text = (UiText.Language == "es" ? "Error al guardar: " : "Save error: ") + ex.Message;
            status.Foreground = DarkUiTheme.DangerBrush;
        }
        finally
        {
            selectiveSaveBtn.IsEnabled = true;
        }
    }

    private void ProcessSamples(float[] block, int sampleRate, double peakDbfs, double rmsDbfs)
    {
        if (closing || !engine.IsCapturing) return;

        Dispatcher.BeginInvoke(() =>
        {
            inputVuMeter.Value = Math.Clamp(peakDbfs, -60, 0);
            inputVuLabel.Text = $"{peakDbfs:+0.0;-0.0;0} dBFS (RMS: {rmsDbfs:+0.0;-0.0;0} dBFS)";
            signalLed.Background = peakDbfs > -45.0 ? DarkUiTheme.SuccessBrush : Brushes.DimGray;
        });

        if (meter == null || meter.SampleRate != sampleRate)
        {
            meter = new LoudnessMeter(sampleRate, 1);
        }

        foreach (var sample in block)
        {
            meter.ProcessFrame([sample]);
        }
    }

    private void DisplayTimer_Tick(object? sender, EventArgs e)
    {
        if (meter == null) return;
        var snap = meter.Snapshot();

        if (snap.MomentaryLufs.HasValue && double.IsFinite(snap.MomentaryLufs.Value))
        {
            double m = snap.MomentaryLufs.Value;
            momentary.Text = $"{m:F1} LUFS";
            momentaryBar.Value = Math.Clamp(m, -60, 0);
        }

        if (snap.IntegratedLufs.HasValue && double.IsFinite(snap.IntegratedLufs.Value))
        {
            double integ = snap.IntegratedLufs.Value;
            integrated.Text = $"{integ:F1} LUFS";
            autoLevelBtn.IsEnabled = true;
        }

        if (snap.MaxTruePeakDbfs.HasValue && double.IsFinite(snap.MaxTruePeakDbfs.Value))
        {
            peak.Text = $"True Peak: {snap.MaxTruePeakDbfs.Value:+0.0;-0.0;0} dBFS";
        }
    }
}
