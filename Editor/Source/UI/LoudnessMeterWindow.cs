using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Gp200.Replica;

internal sealed class LoudnessQueueItem
{
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

    // Section A: Pedal
    readonly TextBlock pedalStatus = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#10B981") };

    // Section B: Audio Input
    readonly ComboBox input = new() { MinWidth = 280, Margin = new Thickness(0, 0, 0, 4) };
    readonly ProgressBar inputVuMeter = new() { Height = 8, Minimum = -60, Maximum = 0, Value = -60, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock inputVuLabel = new() { Text = UiText.Language == "es" ? "Nivel de entrada: —" : "Input Level: —", FontSize = 11, Foreground = BrushHex("#94A3B8") };
    readonly TextBlock diagBadge = new() { Text = "", FontSize = 11, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 2, 0, 4) };

    // Section C: Working Slot & Reference
    readonly ComboBox workingSlotCombo = new() { MinWidth = 140 };
    readonly ComboBox referencePresetCombo = new() { MinWidth = 220, IsEnabled = false };
    readonly CheckBox matchToReferenceCheck = new() { Content = UiText.Language == "es" ? "Igualar todos a un preset de referencia" : "Match all to a reference preset", Foreground = BrushHex("#F1F5F9"), Margin = new Thickness(0, 6, 0, 4) };
    readonly TextBlock refStatus = new() { FontSize = 11, Foreground = BrushHex("#F59E0B"), Text = UiText.Language == "es" ? "Objetivos por defecto: Normal (-16 LUFS) · Lead (-13 LUFS)" : "Default targets: Normal (-16 LUFS) · Lead (-13 LUFS)" };

    // Meter Displays
    readonly TextBlock momentary = new() { FontSize = 28, FontWeight = FontWeights.Bold, Foreground = BrushHex("#38BDF8"), HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock integrated = new() { FontSize = 36, FontWeight = FontWeights.Bold, Foreground = BrushHex("#10B981"), HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock peak = new() { FontSize = 12, Foreground = BrushHex("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4) };
    readonly ProgressBar momentaryBar = new() { Height = 10, Minimum = -60, Maximum = 0, Value = -60, Margin = new Thickness(0, 4, 0, 6) };

    // Section D: Batch Queue
    readonly ListBox queueListBox = new() { MinHeight = 110, MaxHeight = 180, Margin = new Thickness(0, 6, 0, 6) };
    readonly List<LoudnessQueueItem> queueItems = [];
    readonly Button addActiveBtn = new() { Content = UiText.Language == "es" ? "+ Añadir preset activo" : "+ Add active preset", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button addFileBtn = new() { Content = UiText.Language == "es" ? "+ Cargar archivo .prst" : "+ Load .prst file", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button startBatchBtn = new() { Content = UiText.Language == "es" ? "⚡ Iniciar nivelación automática" : "⚡ Start auto-matching", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button saveBatchBtn = new() { Content = UiText.Language == "es" ? "💾 Guardar en GP-200" : "💾 Save to GP-200", Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = false };

    // Bottom controls
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = BrushHex("#94A3B8"), FontSize = 12 };
    readonly Button startStop = new() { MinWidth = 130, Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button autoLevelBtn = new() { MinWidth = 200, Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = false };
    readonly Button closeBtn = new() { Content = UiText.Language == "es" ? "Cerrar" : "Close", MinWidth = 80, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };

    readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Func<PresetDocument?>? getPreset;
    readonly Action<int>? applyVolume;
    readonly AudioCaptureEngine engine = new();
    LoudnessMeter? meter;
    bool closing;
    bool batchRunning;

    internal LoudnessMeterWindow(Func<PresetDocument?>? getPreset = null, Action<int>? applyVolume = null, IEnumerable<PresetDocument>? initialPresets = null)
    {
        this.getPreset = getPreset;
        this.applyVolume = applyVolume;

        DarkUiTheme.Apply(this);
        Title = UiText.Language == "es" ? "Nivelación de Volumen y Medidor BS.1770 LUFS · GP-200" : "Volume Matcher & BS.1770 LUFS · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Width = 720;
        Height = 720;
        MinWidth = 600;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Initialize 256 working slots (default 58-D = slot 231)
        for (int i = 0; i < 256; i++)
        {
            workingSlotCombo.Items.Add($"{BankService.Position(i)} (Slot {i + 1})");
        }
        workingSlotCombo.SelectedIndex = 231; // 58-D

        // Button styles
        startStop.Content = UiText.Language == "es" ? "Detener medición" : "Stop measurement";
        startStop.Background = BrushHex("#2563EB");
        startStop.Foreground = Brushes.White;

        autoLevelBtn.Content = UiText.Language == "es" ? "⚡ Auto-Nivelar activo a -14 LUFS" : "⚡ Auto-Level active to -14 LUFS";
        autoLevelBtn.Background = BrushHex("#10B981");
        autoLevelBtn.Foreground = Brushes.White;
        autoLevelBtn.FontWeight = FontWeights.Bold;

        startBatchBtn.Background = BrushHex("#10B981");
        startBatchBtn.Foreground = Brushes.White;
        startBatchBtn.FontWeight = FontWeights.Bold;
        startBatchBtn.Padding = new Thickness(10, 4, 10, 4);

        saveBatchBtn.Background = BrushHex("#0284C7");
        saveBatchBtn.Foreground = Brushes.White;
        saveBatchBtn.FontWeight = FontWeights.Bold;
        saveBatchBtn.Padding = new Thickness(10, 4, 10, 4);

        addActiveBtn.Background = BrushHex("#334155");
        addActiveBtn.Foreground = Brushes.White;
        addActiveBtn.Padding = new Thickness(8, 4, 8, 4);

        addFileBtn.Background = BrushHex("#334155");
        addFileBtn.Foreground = Brushes.White;
        addFileBtn.Padding = new Thickness(8, 4, 8, 4);

        closeBtn.Background = BrushHex("#334155");
        closeBtn.Foreground = BrushHex("#F8FAFC");

        var root = new DockPanel { Margin = new Thickness(16) };

        // Bottom Actions
        var bottomBar = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bottomBar, Dock.Bottom);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(startStop);
        actions.Children.Add(autoLevelBtn);
        actions.Children.Add(closeBtn);
        bottomBar.Children.Add(actions);
        bottomBar.Children.Add(status);
        root.Children.Add(bottomBar);

        // Scrollable content
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        var mainStack = new StackPanel();

        // CARD A: El Pedal
        var pedalCard = Card(UiText.Language == "es" ? "A — EL PEDAL (ESTADO GP-200)" : "A — THE PEDAL (GP-200 STATUS)");
        var pStack = new StackPanel();
        bool isConn = DeviceSession.Current.Connected;
        pedalStatus.Text = isConn
            ? (UiText.Language == "es" ? "● GP-200 Conectada y Lista vía USB WinMM" : "● GP-200 Connected via USB WinMM")
            : (UiText.Language == "es" ? "○ GP-200 Desconectada" : "○ GP-200 Disconnected");
        pedalStatus.Foreground = isConn ? BrushHex("#10B981") : BrushHex("#F59E0B");
        pStack.Children.Add(pedalStatus);
        pedalCard.Child = pStack;
        mainStack.Children.Add(pedalCard);

        // CARD B: Entrada de Audio
        var inputCard = Card(UiText.Language == "es" ? "B — ENTRADA DE AUDIO USB Y MONITOREO" : "B — USB AUDIO INPUT & MONITORING");
        var inStack = new StackPanel();
        inStack.Children.Add(input);
        inStack.Children.Add(diagBadge);
        inStack.Children.Add(inputVuLabel);
        inStack.Children.Add(inputVuMeter);
        inputCard.Child = inStack;
        mainStack.Children.Add(inputCard);

        // CARD C: Slot de Trabajo y Referencia
        var workCard = Card(UiText.Language == "es" ? "C — SLOT DE TRABAJO (TEST DRIVE & AUDICIÓN)" : "C — WORKING SLOT (AUDITION & TEST DRIVE)");
        var wGrid = new Grid();
        wGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        wGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var wLabel = new TextBlock
        {
            Text = UiText.Language == "es" ? "Working Slot (Inyección Temporal):" : "Working Slot (Temporary Injection):",
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#F1F5F9"),
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
            Foreground = BrushHex("#94A3B8"),
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
                ? "Selecciona un preset de la cola: se medirá como patrón; Normal = referencia, Lead = referencia + 3 LUFS."
                : "Choose a queued preset to measure as the baseline; Normal = reference, Lead = reference + 3 LUFS.";
            refStatus.Foreground = BrushHex("#10B981");
        };
        matchToReferenceCheck.Unchecked += (_, _) =>
        {
            referencePresetCombo.IsEnabled = false;
            refStatus.Text = UiText.Language == "es"
                ? "Objetivos estándar: Normal (-16 LUFS) · Lead (-13 LUFS)"
                : "Standard targets: Normal (-16 LUFS) · Lead (-13 LUFS)";
            refStatus.Foreground = BrushHex("#F59E0B");
        };
        workCard.Child = wStack;
        mainStack.Children.Add(workCard);

        // CARD: Medición LUFS en Vivo
        var meterCard = Card(UiText.Language == "es" ? "MEDIDOR BS.1770 EN VIVO (LUFS / dBFS)" : "LIVE BS.1770 METER (LUFS / dBFS)");
        var mStack = new StackPanel();
        mStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "SONORIDAD INTEGRADA" : "INTEGRATED LOUDNESS",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#94A3B8"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        mStack.Children.Add(integrated);
        mStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Momentánea (400 ms):" : "Momentary (400 ms):",
            FontSize = 11,
            Foreground = BrushHex("#94A3B8"),
            Margin = new Thickness(0, 6, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        mStack.Children.Add(momentary);
        mStack.Children.Add(momentaryBar);
        mStack.Children.Add(peak);
        meterCard.Child = mStack;
        mainStack.Children.Add(meterCard);

        // CARD D: Cola de Presets por Lotes
        var batchCard = Card(UiText.Language == "es" ? "D — COLA DE PRESETS A NIVELAR (MATCH VOLUMES BATCH)" : "D — BATCH PRESET QUEUE (MATCH VOLUMES)");
        var bStack = new StackPanel();

        var bToolBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        bToolBar.Children.Add(addActiveBtn);
        bToolBar.Children.Add(new Border { Width = 6 });
        bToolBar.Children.Add(addFileBtn);
        bToolBar.Children.Add(new Border { Width = 6 });
        bToolBar.Children.Add(startBatchBtn);
        bToolBar.Children.Add(new Border { Width = 6 });
        bToolBar.Children.Add(saveBatchBtn);
        bStack.Children.Add(bToolBar);

        bStack.Children.Add(queueListBox);
        batchCard.Child = bStack;
        mainStack.Children.Add(batchCard);

        scroll.Content = mainStack;
        root.Children.Add(scroll);
        Content = root;
        if (initialPresets != null)
            AddPresetsToQueue(initialPresets);

        // Engine wiring
        engine.StatusChanged += text => Dispatcher.BeginInvoke(() => { diagBadge.Text = text; });
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
                engine.Start(input.SelectedIndex);
            }
        };

        startStop.Click += (_, _) =>
        {
            if (engine.IsCapturing)
            {
                engine.Stop();
                startStop.Content = UiText.Language == "es" ? "Reanudar medición" : "Resume measurement";
                startStop.Background = BrushHex("#10B981");
            }
            else
            {
                if (input.SelectedIndex >= 0) engine.Start(input.SelectedIndex);
                startStop.Content = UiText.Language == "es" ? "Detener medición" : "Stop measurement";
                startStop.Background = BrushHex("#2563EB");
            }
        };

        autoLevelBtn.Click += (_, _) => AutoLevelActivePreset();
        addActiveBtn.Click += (_, _) => AddActivePresetToQueue();
        addFileBtn.Click += (_, _) => AddFileToQueue();
        startBatchBtn.Click += async (_, _) => await StartBatchMatchingAsync();
        saveBatchBtn.Click += async (_, _) => await SaveBatchToPedalAsync();
        closeBtn.Click += (_, _) => Close();

        displayTimer.Tick += DisplayTimer_Tick;
        displayTimer.Start();

        Loaded += (_, _) =>
        {
            if (input.SelectedIndex >= 0) engine.Start(input.SelectedIndex);
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
            status.Foreground = BrushHex("#EF4444");
        }
    }

    void RefreshQueueUi()
    {
        queueListBox.Items.Clear();
        foreach (var item in queueItems)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameBlock = new TextBlock { Text = item.Name, Foreground = BrushHex("#F8FAFC"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(nameBlock);

            var typeCombo = new ComboBox { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            typeCombo.Items.Add("Normal (-16 LUFS)");
            typeCombo.Items.Add("Lead (-13 LUFS)");
            typeCombo.SelectedIndex = item.IsLead ? 1 : 0;
            typeCombo.SelectionChanged += (_, _) => { item.IsLead = typeCombo.SelectedIndex == 1; };
            Grid.SetColumn(typeCombo, 1);
            row.Children.Add(typeCombo);

            var targetSlot = new ComboBox
            {
                ItemsSource = SlotPositions,
                SelectedIndex = item.TargetSlot is >= 0 and < 256 ? item.TargetSlot : -1,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            targetSlot.SelectionChanged += (_, _) => item.TargetSlot = targetSlot.SelectedIndex;
            Grid.SetColumn(targetSlot, 2);
            row.Children.Add(targetSlot);

            var volBlock = new TextBlock { Text = $"Vol: {item.AdjustedVolume} (Orig {item.OriginalVolume})", Foreground = BrushHex("#38BDF8"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(volBlock, 3);
            row.Children.Add(volBlock);

            var statBlock = new TextBlock { Text = item.Status, Foreground = BrushHex("#94A3B8"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(statBlock, 4);
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

    async Task StartBatchMatchingAsync()
    {
        if (batchRunning || queueItems.Count == 0) return;
        if (!DeviceSession.Current.Connected)
        {
            status.Text = UiText.Language == "es" ? "⚠️ Conecta la GP-200 por USB para realizar la nivelación en vivo." : "⚠️ Connect GP-200 via USB.";
            status.Foreground = BrushHex("#F59E0B");
            return;
        }
        if (CatalogRuntime.Current.IsMx42)
        {
            status.Text = UiText.Language == "es"
                ? "La nivelación temporal por slot solo está habilitada para el perfil Stock; no se enviaron datos."
                : "Temporary-slot matching is only supported for the Stock profile; no data was written.";
            status.Foreground = BrushHex("#F59E0B");
            return;
        }

        int workSlot = workingSlotCombo.SelectedIndex >= 0 ? workingSlotCombo.SelectedIndex : 231;
        if (UiDialogs.Show(this,
            UiText.Language == "es"
                ? $"Se usará {BankService.Position(workSlot)} como slot de trabajo. Se respaldará y restaurará al finalizar. Rasguea durante cada medición. ¿Continuar?"
                : $"{BankService.Position(workSlot)} will be used as the working slot. It will be backed up and restored afterwards. Strum during each measurement. Continue?",
            UiText.Language == "es" ? "Iniciar nivelación en vivo" : "Start live matching",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        batchRunning = true;
        startBatchBtn.IsEnabled = false;
        saveBatchBtn.IsEnabled = false;
        var bank = new BankService();
        PresetDocument? restoreWorkSlot = null;
        PresetDocument? activeBefore = null;
        bool workSlotTouched = false;
        string? failure = null;
        try
        {
            activeBefore = await bank.ActiveAsync();
            var storedWorkSlot = await bank.ReadAsync(workSlot);
            restoreWorkSlot = activeBefore.Index == workSlot ? activeBefore : storedWorkSlot;
            if (activeBefore.Index == workSlot && !BankService.Equivalent(activeBefore, storedWorkSlot))
            {
                string backupDirectory = Path.Combine(AppPaths.Backups, $"loudness-active-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(backupDirectory);
                File.WriteAllBytes(Path.Combine(backupDirectory, "active-unsaved.prst"), activeBefore.ToBytes());
                File.WriteAllBytes(Path.Combine(backupDirectory, $"{BankService.Position(workSlot)}-stored.prst"), storedWorkSlot.ToBytes());
            }

            status.Text = UiText.Language == "es"
                ? $"Iniciando nivelación de {queueItems.Count} presets en {BankService.Position(workSlot)}… Rasguea durante la medición."
                : $"Starting matching {queueItems.Count} presets in {BankService.Position(workSlot)}… Strum during the measurement.";
            status.Foreground = BrushHex("#38BDF8");

            LoudnessQueueItem? referenceItem = null;
            double? referenceLufs = null;
            if (matchToReferenceCheck.IsChecked == true)
            {
                referenceItem = referencePresetCombo.SelectedItem as LoudnessQueueItem
                    ?? throw new InvalidOperationException(UiText.Language == "es"
                        ? "Elige un preset de referencia de la cola."
                        : "Choose a reference preset from the queue.");
                referenceItem.Status = UiText.Language == "es" ? "Midiendo referencia…" : "Measuring reference…";
                RefreshQueueUi();
                workSlotTouched = true;
                referenceLufs = await MeasurePresetAsync(bank, workSlot, referenceItem.Document);
                referenceItem.Status = $"{referenceLufs:F1} LUFS · referencia";
                RefreshQueueUi();
            }

            for (int i = 0; i < queueItems.Count; i++)
            {
                var item = queueItems[i];
                item.Status = UiText.Language == "es" ? "Midiendo…" : "Measuring…";
                RefreshQueueUi();

                double measured;
                if (ReferenceEquals(item, referenceItem) && referenceLufs.HasValue)
                    measured = referenceLufs.Value;
                else
                {
                    workSlotTouched = true;
                    measured = await MeasurePresetAsync(bank, workSlot, item.Document);
                }

                double target = item.IsLead ? (referenceLufs ?? -16.0) + 3.0 : referenceLufs ?? -16.0;
                var match = LoudnessMatcher.MatchPresetVolume(item.Document, measured, target);
                item.Document = match.AdjustedPreset;
                item.AdjustedVolume = match.AdjustedVolume;
                item.Status = match.Summary;
                RefreshQueueUi();
            }
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }
        finally
        {
            if (workSlotTouched && restoreWorkSlot != null)
            {
                try
                {
                    await bank.ReplaceAsync(workSlot, restoreWorkSlot);
                    if (activeBefore != null)
                        await bank.SelectAsync(activeBefore.Index, reload: true);
                }
                catch (Exception error)
                {
                    failure = failure == null
                        ? "No se pudo restaurar el slot de trabajo: " + error.Message
                        : failure + Environment.NewLine + "No se pudo restaurar el slot de trabajo: " + error.Message;
                }
            }
            batchRunning = false;
            startBatchBtn.IsEnabled = true;
        }

        if (failure != null)
        {
            status.Text = (UiText.Language == "es" ? "Error en nivelación: " : "Matching failed: ") + failure;
            status.Foreground = BrushHex("#EF4444");
            RefreshQueueUi();
            return;
        }

        status.Text = UiText.Language == "es"
            ? "✓ Mediciones completadas y slot de trabajo restaurado. Revisa destinos; pulsa Guardar para escribir los resultados."
            : "✓ Measurements complete and working slot restored. Review target slots, then click Save to write results.";
        status.Foreground = BrushHex("#10B981");
        saveBatchBtn.IsEnabled = true;
    }

    async Task<double> MeasurePresetAsync(BankService bank, int workSlot, PresetDocument document)
    {
        await bank.ReplaceAsync(workSlot, document);
        await bank.SelectAsync(workSlot, reload: true);
        meter = null;
        status.Text = UiText.Language == "es"
            ? $"Rasguea ahora; midiendo {document.Name} durante 4 segundos…"
            : $"Strum now; measuring {document.Name} for 4 seconds…";
        await Task.Delay(TimeSpan.FromSeconds(4));
        var snapshot = meter?.Snapshot();
        if (snapshot?.IntegratedLufs is not double measured || !double.IsFinite(measured) || measured <= -60)
            throw new InvalidOperationException(UiText.Language == "es"
                ? "No se obtuvo una medición LUFS válida. Comprueba la entrada de audio y vuelve a intentarlo; no se ajustó este preset."
                : "No valid LUFS measurement was captured. Check the audio input and retry; this preset was not adjusted.");
        return measured;
    }

    async Task SaveBatchToPedalAsync()
    {
        if (!DeviceSession.Current.Connected)
        {
            status.Text = UiText.Language == "es" ? "Conecta la GP-200 antes de guardar." : "Connect the GP-200 before saving.";
            status.Foreground = BrushHex("#F59E0B");
            return;
        }
        int[] slots = queueItems.Select(item => item.TargetSlot).ToArray();
        if (queueItems.Count == 0 || slots.Any(slot => slot is < 0 or > 255) || slots.Distinct().Count() != slots.Length)
        {
            status.Text = UiText.Language == "es"
                ? "Asigna un slot de destino distinto y válido a cada preset."
                : "Assign a distinct, valid target slot to every preset.";
            status.Foreground = BrushHex("#F59E0B");
            return;
        }
        if (CatalogRuntime.Current.IsMx42)
        {
            status.Text = UiText.Language == "es"
                ? "La escritura de presets de esta cola en el perfil Custom no está soportada por la transacción revisada."
                : "Writing these queued presets to the Custom profile is not supported by the reviewed transaction.";
            status.Foreground = BrushHex("#F59E0B");
            return;
        }
        string targetPositions = string.Join(", ", slots.Select(BankService.Position));
        if (UiDialogs.Show(this,
            UiText.Language == "es"
                ? $"Se reemplazarán los slots {targetPositions}. BankService creará backups y verificará cada escritura; la operación no es atómica. ¿Continuar?"
                : $"Slots {targetPositions} will be replaced. BankService will back them up and verify each write; the batch is not atomic. Continue?",
            UiText.Language == "es" ? "Guardar presets nivelados" : "Save matched presets",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        saveBatchBtn.IsEnabled = false;
        var bank = new BankService();
        int saved = 0;
        try
        {
            status.Text = UiText.Language == "es" ? "Guardando presets nivelados en la GP-200…" : "Saving leveled presets to GP-200…";
            foreach (var item in queueItems)
            {
                await bank.ReplaceAsync(item.TargetSlot, item.Document);
                saved++;
            }
            status.Text = UiText.Language == "es" ? $"✓ {saved} presets guardados y verificados en sus slots de GP-200." : $"✓ {saved} presets saved and verified on GP-200.";
            status.Foreground = BrushHex("#10B981");
        }
        catch (Exception ex)
        {
            status.Text = "Error al guardar: " + ex.Message;
            status.Foreground = BrushHex("#EF4444");
        }
        finally
        {
            saveBatchBtn.IsEnabled = saved < queueItems.Count;
        }
    }

    void AutoLevelActivePreset()
    {
        var snap = meter?.Snapshot();
        if (snap?.IntegratedLufs is not double measured || !double.IsFinite(measured))
        {
            status.Text = UiText.Language == "es" ? "Toca tu guitarra unos segundos para acumular lectura integrada." : "Strum your guitar to accumulate integrated reading.";
            status.Foreground = BrushHex("#F59E0B");
            return;
        }

        var current = getPreset?.Invoke();
        if (current == null) return;
        int currentVol = current.Volume;
        double deltaDb = -14.0 - measured;
        int newVol = LoudnessMatcher.CalculateAdjustedVolume(currentVol, deltaDb);

        applyVolume?.Invoke(newVol);
        status.Text = UiText.Language == "es"
            ? $"✓ Volumen ajustado de {currentVol} a {newVol} (Δ {deltaDb:+0.0;-0.0} dB hacia -14 LUFS)."
            : $"✓ Volume adjusted from {currentVol} to {newVol} (Δ {deltaDb:+0.0;-0.0} dB toward -14 LUFS).";
        status.Foreground = BrushHex("#10B981");
    }

    void ProcessSamples(float[] samples, int sampleRate, double peakDbfs, double rmsDbfs)
    {
        if (closing) return;
        if (meter == null) meter = new LoudnessMeter(sampleRate, 1);

        for (int i = 0; i < samples.Length; i++)
        {
            meter.ProcessFrame([samples[i]]);
        }

        Dispatcher.BeginInvoke(() =>
        {
            inputVuMeter.Value = Math.Clamp(peakDbfs, -60, 0);
            inputVuLabel.Text = $"Nivel pico: {peakDbfs:F1} dBFS";
        });
    }

    void DisplayTimer_Tick(object? sender, EventArgs e)
    {
        if (closing || meter == null) return;
        var snapshot = meter.Snapshot();

        if (snapshot.MomentaryLufs is double m && double.IsFinite(m))
        {
            momentary.Text = $"{m:F1} LUFS";
            momentaryBar.Value = Math.Clamp(m, -60, 0);
        }
        else
        {
            momentary.Text = "— LUFS";
            momentaryBar.Value = -60;
        }

        if (snapshot.IntegratedLufs is double i && double.IsFinite(i))
        {
            integrated.Text = $"{i:F1} LUFS";
            double diff = i - (-16.0);
            integrated.Foreground = Math.Abs(diff) <= 1.0 ? BrushHex("#10B981") : (diff < 0 ? BrushHex("#F59E0B") : BrushHex("#EF4444"));
            autoLevelBtn.IsEnabled = true;
        }
        else
        {
            integrated.Text = "— LUFS";
            integrated.Foreground = BrushHex("#10B981");
            autoLevelBtn.IsEnabled = false;
        }

        if (snapshot.SamplePeakDbfs is double tp && double.IsFinite(tp))
            peak.Text = $"True Peak: {tp:F1} dBFS";
        else
            peak.Text = "True Peak: — dBFS";
    }

    static Border Card(string title)
    {
        var b = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#38BDF8"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        b.Child = sp;
        return b;
    }

    static Brush BrushHex(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}
