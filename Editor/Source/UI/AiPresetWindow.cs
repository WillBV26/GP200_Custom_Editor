using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace Gp200.Replica;

internal sealed class AiPresetWindow : Window
{
    readonly Catalog catalog;
    readonly PresetDocument? current;
    readonly IReadOnlyList<PresetDocument> deviceLibrary;
    readonly Action<AiGeneratedPreset> addPreset;
    readonly Action<IReadOnlyList<PresetDocument>, Window> openVolumeMatcher;
    readonly Func<IProgress<int>, CancellationToken, Task<IReadOnlyList<PresetDocument>>>? readFullBankAsync;
    readonly Func<CancellationToken, Task<Mx42SceneSnapshot>>? readSceneSnapshotAsync;

    // Navigation & Provider Settings
    readonly ComboBox provider = new() { MinWidth = 130 };
    readonly ComboBox model = new()
    {
        MinWidth = 165,
        IsEditable = false,
        IsEnabled = false,
        ToolTip = UiText.Language == "es"
            ? "Los modelos aparecen después de validar la clave con el proveedor."
            : "Models appear after the provider validates the API key."
    };
    readonly PasswordBox apiKey = new() { MinWidth = 165 };
    readonly Button verifyAiProviderBtn = new()
    {
        Content = UiText.Language == "es" ? "Validar clave y detectar modelos" : "Verify key & load models",
        Cursor = System.Windows.Input.Cursors.Hand,
        Padding = new Thickness(8, 4, 8, 4)
    };
    readonly CheckBox rememberKeyCheck = new() { Content = UiText.Language == "es" ? "Recordar clave en este equipo" : "Remember key on this computer", Foreground = BrushHex("#F1F5F9"), Margin = new Thickness(0, 4, 0, 4), MaxWidth = 210 };
    readonly CheckBox webSearch = new() { Content = UiText.Language == "es" ? "Búsqueda web" : "Web search", IsChecked = false, Visibility = Visibility.Collapsed, Foreground = BrushHex("#F1F5F9"), Margin = new Thickness(0, 4, 0, 4), MaxWidth = 250 };
    readonly TextBlock apiKeyEnvStatus = new() { FontSize = 11, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 2, 0, 4) };
    CancellationTokenSource? modelDiscoveryRequest;
    string? verifiedCredentialFingerprint;
    AiProvider? verifiedProvider;
    bool providerDiscoveryInProgress;

    // Mode Tabs
    readonly TabControl modeTabControl = new();
    
    // TAB 1: New Song controls
    readonly TextBox requestText = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 65, MaxHeight = 110, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly WrapPanel structurePillsPanel = new();
    readonly ToggleButton structureAutoDetect = new() { Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 3, 8, 3) };
    readonly Dictionary<string, ToggleButton> structurePills = new(StringComparer.OrdinalIgnoreCase);
    readonly TextBlock structureSummaryText = new() { FontSize = 11, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 4, 0, 6), TextWrapping = TextWrapping.Wrap };
    readonly CheckBox aiChoosesSetSizeCheck = new()
    {
        Content = UiText.Language == "es" ? "La IA elige la cantidad (hasta 4 presets)" : "Let AI choose the set size (up to 4 presets)",
        Foreground = BrushHex("#F1F5F9"),
        Margin = new Thickness(0, 3, 0, 3),
        ToolTip = UiText.Language == "es"
            ? "Disponible con Detectar automáticamente. La IA elige entre 1 y 4 propuestas."
            : "Available with Auto-detect. The AI chooses between one and four proposals."
    };
    readonly ComboBox pickups = new() { MinWidth = 160 };
    readonly ComboBox chainTopology = new() { MinWidth = 180 };
    readonly ComboBox performanceMode = new() { MinWidth = 180 };
    readonly TextBox songLinkBox = new() { MinWidth = 240 };
    readonly TextBox audioFileBox = new() { IsReadOnly = true, MinWidth = 220 };
    readonly Button browseAudioBtn = new() { Content = UiText.Language == "es" ? "Examinar Audio…" : "Browse Audio…", Margin = new Thickness(6, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
    readonly TextBlock audioReportDisplay = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 2, 0, 4) };
    readonly CheckBox sendAudioToAiCheck = new()
    {
        Content = UiText.Language == "es" ? "Autorizar envío explícito del audio a Gemini" : "Explicitly authorize sending audio to Gemini",
        IsChecked = false,
        IsEnabled = false,
        Foreground = BrushHex("#F1F5F9"),
        Margin = new Thickness(0, 2, 0, 4),
        ToolTip = UiText.Language == "es"
            ? "Desmarcado por defecto. El análisis local no envía audio. Al marcarlo, se pedirá confirmación antes de cada envío a Gemini."
            : "Unchecked by default. Local analysis never sends audio. Checking this prompts for confirmation before each Gemini upload."
    };
    byte[]? loadedAudioBytes;
    string? loadedAudioMime;
    AudioToneReport? loadedAudioReport;

    // TAB 2: Refine controls
    readonly TextBlock refineTargetInfo = new() { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 0, 0, 6) };
    readonly TextBox refineRequestText = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 65, MaxHeight = 110, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly Button openRefinePresetBtn = new() { Content = UiText.Language == "es" ? "Abrir preset .prst…" : "Open .prst preset…", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button liveOptimizeBtn = new() { Content = UiText.Language == "es" ? "🎸 Optimizar para directo" : "🎸 Optimize for live", Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.Bold };
    readonly Button primaryAiActionBtn = new() { Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.Bold, MinHeight = 38, Padding = new Thickness(16, 8, 16, 8) };
    readonly Button cancelAiRequestBtn = new() { Content = UiText.Language == "es" ? "Cancelar" : "Cancel", Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = false, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };

    // My Presets (Generated proposals) & Injection Slot
    readonly ListBox myPresetsList = new() { MinHeight = 150 };
    readonly ComboBox injectionSlotCombo = new() { MinWidth = 120 };
    readonly CheckBox autoInjectCheck = new() { Content = UiText.Language == "es" ? "Inyectar automáticamente al seleccionar" : "Auto-inject on select", IsChecked = false, Foreground = BrushHex("#F1F5F9"), Margin = new Thickness(0, 2, 0, 2) };
    readonly CheckBox listenToPedalCheck = new() { Content = UiText.Language == "es" ? "Escuchar al pedal" : "Listen to pedal", IsChecked = false, Foreground = BrushHex("#94A3B8"), Margin = new Thickness(0, 2, 0, 2) };
    readonly TextBlock injectionStatus = new() { FontSize = 11, Foreground = BrushHex("#10B981"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly Button injectSelectedBtn = new() { Content = UiText.Language == "es" ? "⚡ Inyectar preset seleccionado" : "⚡ Inject selected preset", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button addToEditorBtn = new() { Content = UiText.Language == "es" ? "Añadir al editor" : "Add to editor", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button matchVolumesBtn = new() { Content = UiText.Language == "es" ? "Igualar volúmenes" : "Match volumes", Cursor = System.Windows.Input.Cursors.Hand };

    // 5 Detail Tabs on the Right
    readonly TabControl detailTabs = new();
    
    // 1. Result tab
    readonly TextBlock resHeader = new() { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = BrushHex("#F8FAFC") };
    readonly TextBlock resDescription = new() { FontSize = 11, Foreground = BrushHex("#94A3B8"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
    readonly WrapPanel resChainFlow = new() { Margin = new Thickness(0, 4, 0, 8) };
    readonly StackPanel resSources = new() { Margin = new Thickness(0, 2, 0, 6) };
    readonly StackPanel resChanges = new() { Margin = new Thickness(0, 2, 0, 6) };
    readonly TextBlock resCtrlSummary = new() { FontSize = 11, Foreground = BrushHex("#38BDF8"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
    readonly TextBlock resNotes = new() { FontSize = 11, Foreground = BrushHex("#E2E8F0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
    readonly TextBox quickTweakBox = new() { MinHeight = 32, TextWrapping = TextWrapping.Wrap };
    readonly Button quickTweakBtn = new() { Content = UiText.Language == "es" ? "Retocar" : "Tweak", Cursor = System.Windows.Input.Cursors.Hand };

    // 2. Pedal tab
    readonly TextBlock pedalTabStatus = new() { FontSize = 12, FontWeight = FontWeights.Bold };
    readonly ComboBox pedalTargetSlotCombo = new() { MinWidth = 130 };
    readonly Button sendToSlotBtn = new() { Content = UiText.Language == "es" ? "⚡ Enviar este preset al slot" : "⚡ Send to slot", Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.Bold };
    readonly Button readActiveFromPedalBtn = new() { Content = UiText.Language == "es" ? "Leer patch actual del pedal" : "Read active pedal patch", Cursor = System.Windows.Input.Cursors.Hand };
    readonly TextBox pedalResponseLog = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 80, MaxHeight = 130, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    // 3. CTRL 8x11 Matrix tab
    readonly Grid ctrlMatrixGrid = new();
    readonly TextBlock ctrlLiveSummary = new() { FontSize = 11, Foreground = BrushHex("#38BDF8"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    readonly Button sendCtrlBtn = new() { Content = UiText.Language == "es" ? "⚡ Enviar asignaciones CTRL al pedal" : "⚡ Send CTRL assignments to pedal", Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.Bold };

    // 4. Volume Match tab
    readonly TextBlock volCurrentLufs = new() { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = BrushHex("#10B981") };
    readonly Button autoLevelCurrentBtn = new() { Content = UiText.Language == "es" ? "Medir e igualar este preset" : "Measure and match this preset", Cursor = System.Windows.Input.Cursors.Hand };

    // 5. Set List tab
    readonly StackPanel setListTable = new();
    readonly ComboBox setListStartSlot = new() { MinWidth = 110 };
    readonly Button autoAssignSlotsBtn = new() { Content = UiText.Language == "es" ? "Auto-asignar consecutivos" : "Auto-assign consecutive", Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button uploadAllToPedalBtn = new() { Content = UiText.Language == "es" ? "⚡ Subir seleccionados a la GP-200" : "⚡ Upload selected to GP-200", Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.Bold };
    readonly Button exportZipBtn = new() { Content = UiText.Language == "es" ? "Exportar Set List ZIP" : "Export Set List ZIP", Cursor = System.Windows.Input.Cursors.Hand };

    // Global state
    readonly List<AiGeneratedPreset> generated = [];
    readonly HashSet<int> favoriteIndices = [];
    readonly HashSet<int> selectedForSetList = [];
    readonly Dictionary<int, int> setListSlotAssignments = [];
    readonly List<ComboBox> setListTargetCombos = [];
    CancellationTokenSource? activeRequest;
    bool updatingUi;
    bool injectingPreset;
    bool aiRequestInProgress;
    PresetDocument? refineFilePreset;
    string? refineFilePath;
    bool pollingPedal;
    bool resettingInjectionSlot;
    int approvedInjectionSlot = 231;
    PresetDocument? pedalReadPreset;
    readonly DispatcherTimer pedalListenTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    internal AiPresetWindow(Catalog catalog, PresetDocument? current, IReadOnlyList<PresetDocument> deviceLibrary, Action<AiGeneratedPreset> addPreset,
        Func<IProgress<int>, CancellationToken, Task<IReadOnlyList<PresetDocument>>>? readFullBankAsync,
        Func<CancellationToken, Task<Mx42SceneSnapshot>>? readSceneSnapshotAsync,
        Action<IReadOnlyList<PresetDocument>, Window> openVolumeMatcher)
    {
        this.catalog = catalog;
        this.current = current;
        this.deviceLibrary = deviceLibrary;
        this.addPreset = addPreset;
        this.openVolumeMatcher = openVolumeMatcher;
        this.readFullBankAsync = readFullBankAsync;
        this.readSceneSnapshotAsync = readSceneSnapshotAsync;

        EditorWindow.StyleDialog(this);
        Title = UiText.Language == "es" ? "GP-200 Studio · IA Studio, Inyección en Slots y Matriz CTRL" : "GP-200 Studio · AI Studio, Slot Injection & CTRL Matrix";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 1180;
        Height = 780;
        MinWidth = 980;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Init 256 slot combos
        for (int i = 0; i < 256; i++)
        {
            string pos = BankService.Position(i);
            injectionSlotCombo.Items.Add(pos);
            pedalTargetSlotCombo.Items.Add(pos);
            setListStartSlot.Items.Add(pos);
        }
        injectionSlotCombo.SelectedIndex = 231; // 58-D default
        pedalTargetSlotCombo.SelectedIndex = current?.Index ?? 231;
        setListStartSlot.SelectedIndex = 236; // 60-A default

        // Pickups options
        pickups.Items.Add(UiText.Language == "es" ? "Sin especificar (la IA decide)" : "Unspecified (AI decides)");
        pickups.Items.Add("Humbucker");
        pickups.Items.Add("Single-coil");
        pickups.Items.Add(UiText.Language == "es" ? "Activas (EMG, etc.)" : "Active (EMG, etc.)");
        pickups.Items.Add("P90");
        pickups.SelectedIndex = 0;

        // Structure pills
        structureAutoDetect.Content = UiText.Language == "es" ? "Detectar automáticamente" : "Auto-detect";
        structureAutoDetect.IsChecked = true;
        structureAutoDetect.Background = BrushHex("#2563EB");
        structureAutoDetect.Foreground = Brushes.White;
        structureAutoDetect.BorderBrush = BrushHex("#334155");
        structureAutoDetect.Checked += (_, _) =>
        {
            structureAutoDetect.Background = BrushHex("#2563EB");
            structureAutoDetect.Foreground = Brushes.White;
        };
        structureAutoDetect.Unchecked += (_, _) =>
        {
            structureAutoDetect.Background = BrushHex("#1E293B");
            structureAutoDetect.Foreground = BrushHex("#CBD5E1");
        };
        structureAutoDetect.Checked += (_, _) =>
        {
            foreach (var pill in structurePills.Values)
                pill.IsChecked = false;
            UpdateStructureText();
        };
        foreach (string section in new[] { "Clean", "Crunch", "Dist", "Lead" })
        {
            var pill = new ToggleButton
            {
                Content = section.ToLowerInvariant(),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(8, 3, 8, 3),
                Background = BrushHex("#1E293B"),
                Foreground = BrushHex("#CBD5E1"),
                BorderBrush = BrushHex("#334155"),
                ToolTip = UiText.Language == "es" ? $"Añadir o quitar la sección {section}." : $"Toggle the {section} section."
            };
            pill.Checked += (_, _) =>
            {
                pill.Background = BrushHex("#0284C7");
                pill.Foreground = Brushes.White;
                structureAutoDetect.IsChecked = false;
                UpdateStructureText();
            };
            pill.Unchecked += (_, _) =>
            {
                pill.Background = BrushHex("#1E293B");
                pill.Foreground = BrushHex("#CBD5E1");
                if (structurePills.Values.All(item => item.IsChecked != true))
                    structureAutoDetect.IsChecked = true;
                UpdateStructureText();
            };
            structurePills.Add(section, pill);
        }
        aiChoosesSetSizeCheck.Checked += (_, _) =>
        {
            structureAutoDetect.IsChecked = true;
            UpdateStructureText();
        };
        aiChoosesSetSizeCheck.Unchecked += (_, _) => UpdateStructureText();
        UpdateStructureText();

        // Topology options
        chainTopology.Items.Add(UiText.Language == "es" ? "Cadena normal (Serie)" : "Standard Series");
        chainTopology.Items.Add(UiText.Language == "es" ? "Dual Chain A/B (Paralelo)" : "Dual Chain A/B (Parallel)");
        chainTopology.SelectedIndex = 0;

        performanceMode.Items.Add(UiText.Language == "es" ? "Presets independientes (Patch/Stomp)" : "Direct presets (Patch/Stomp)");
        performanceMode.Items.Add(UiText.Language == "es" ? "Stomp (CTRL por preset)" : "Stomp (per-preset CTRL)");
        performanceMode.Items.Add(UiText.Language == "es" ? "User (CTRL por preset)" : "User (per-preset CTRL)");
        performanceMode.Items.Add(UiText.Language == "es" ? "Banco de 4 Escenas (A/B/C/D)" : "4-Scene Bank (A/B/C/D)");
        performanceMode.SelectedIndex = 0;

        // Button styles
        liveOptimizeBtn.Background = BrushHex("#10B981");
        liveOptimizeBtn.Foreground = Brushes.White;
        liveOptimizeBtn.Padding = new Thickness(12, 6, 12, 6);
        primaryAiActionBtn.Background = BrushHex("#059669");
        primaryAiActionBtn.Foreground = Brushes.White;
        primaryAiActionBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        primaryAiActionBtn.ToolTip = UiText.Language == "es"
            ? "Envía tu descripción al proveedor de IA seleccionado y genera o refina presets reales."
            : "Send your description to the selected AI provider to generate or refine real presets.";
        cancelAiRequestBtn.Background = BrushHex("#7F1D1D");
        cancelAiRequestBtn.Foreground = Brushes.White;
        openRefinePresetBtn.Background = BrushHex("#334155");
        openRefinePresetBtn.Foreground = Brushes.White;
        openRefinePresetBtn.Padding = new Thickness(9, 5, 9, 5);

        sendToSlotBtn.Background = BrushHex("#0284C7");
        sendToSlotBtn.Foreground = Brushes.White;
        sendToSlotBtn.Padding = new Thickness(10, 4, 10, 4);

        sendCtrlBtn.Background = BrushHex("#2563EB");
        sendCtrlBtn.Foreground = Brushes.White;
        sendCtrlBtn.Padding = new Thickness(10, 4, 10, 4);

        uploadAllToPedalBtn.Background = BrushHex("#059669");
        uploadAllToPedalBtn.Foreground = Brushes.White;
        uploadAllToPedalBtn.Padding = new Thickness(12, 6, 12, 6);

        exportZipBtn.Background = BrushHex("#334155");
        exportZipBtn.Foreground = Brushes.White;
        exportZipBtn.Padding = new Thickness(10, 4, 10, 4);

        autoAssignSlotsBtn.Background = BrushHex("#334155");
        autoAssignSlotsBtn.Foreground = Brushes.White;
        autoAssignSlotsBtn.Padding = new Thickness(8, 4, 8, 4);

        quickTweakBtn.Background = BrushHex("#2563EB");
        quickTweakBtn.Foreground = Brushes.White;
        quickTweakBtn.Padding = new Thickness(8, 4, 8, 4);

        readActiveFromPedalBtn.Background = BrushHex("#334155");
        readActiveFromPedalBtn.Foreground = Brushes.White;
        readActiveFromPedalBtn.Padding = new Thickness(8, 4, 8, 4);

        autoLevelCurrentBtn.Background = BrushHex("#10B981");
        autoLevelCurrentBtn.Foreground = Brushes.White;
        autoLevelCurrentBtn.Padding = new Thickness(8, 4, 8, 4);

        BuildUi();
        InitProviders();
        injectSelectedBtn.Click += async (_, _) => await InjectSelectedPresetAsync();
        addToEditorBtn.Click += (_, _) => AddSelectedPresetToEditor();
        matchVolumesBtn.Click += (_, _) => OpenVolumeMatcher();
        listenToPedalCheck.Checked += (_, _) => pedalListenTimer.Start();
        listenToPedalCheck.Unchecked += (_, _) => pedalListenTimer.Stop();
        primaryAiActionBtn.Click += async (_, _) =>
        {
            if (modeTabControl.SelectedIndex == 1)
                await RefineActivePresetAsync(false);
            else
                await GenerateNewSongAsync();
        };
        cancelAiRequestBtn.Click += (_, _) => activeRequest?.Cancel();
        modeTabControl.SelectionChanged += (_, _) => UpdatePrimaryAiAction();
        pedalListenTimer.Tick += async (_, _) => await PollPedalAsync();
        Closed += (_, _) =>
        {
            pedalListenTimer.Stop();
            activeRequest?.Cancel();
            modelDiscoveryRequest?.Cancel();
        };
        UpdatePrimaryAiAction();
        injectionSlotCombo.SelectionChanged += (_, _) =>
        {
            if (resettingInjectionSlot || injectionSlotCombo.SelectedIndex < 0)
                return;
            int requestedSlot = injectionSlotCombo.SelectedIndex;
            if (autoInjectCheck.IsChecked == true && requestedSlot != approvedInjectionSlot
                && UiDialogs.Show(this,
                    UiText.Language == "es"
                        ? $"La inyección automática también podrá reemplazar {BankService.Position(requestedSlot)}. Se realizará un backup antes de cada escritura. ¿Autorizar este slot?"
                        : $"Automatic injection will also be allowed to replace {BankService.Position(requestedSlot)}. A backup will be made before every write. Allow this slot?",
                    UiText.Language == "es" ? "Autorizar slot de prueba" : "Authorize test slot",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                resettingInjectionSlot = true;
                injectionSlotCombo.SelectedIndex = approvedInjectionSlot;
                resettingInjectionSlot = false;
                return;
            }
            approvedInjectionSlot = requestedSlot;
        };
        autoInjectCheck.Checked += (_, _) =>
        {
            int slot = injectionSlotCombo.SelectedIndex >= 0 ? injectionSlotCombo.SelectedIndex : 231;
            if (UiDialogs.Show(this,
                UiText.Language == "es"
                    ? $"La selección automática reemplazará {BankService.Position(slot)}. Se creará un backup y se verificará cada escritura. ¿Autorizar?"
                    : $"Automatic selection will replace {BankService.Position(slot)}. A backup and readback will be performed for each write. Allow?",
                UiText.Language == "es" ? "Autorizar escritura automática" : "Allow automatic writes",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                autoInjectCheck.IsChecked = false;
            else
                approvedInjectionSlot = slot;
        };
    }

    void BuildUi()
    {
        var mainGrid = new Grid { Margin = new Thickness(14) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header / Provider
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Workspace
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom status

        // 1. TOP HEADER & PROVIDER SETTINGS
        var headerBorder = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var hGrid = new Grid();

        var provStack = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        provStack.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Proveedor:" : "Provider:", Foreground = BrushHex("#94A3B8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        provStack.Children.Add(provider);
        provStack.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Modelo:" : "Model:", Foreground = BrushHex("#94A3B8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
        provStack.Children.Add(model);
        provStack.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Clave API:" : "API Key:", Foreground = BrushHex("#94A3B8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
        provStack.Children.Add(apiKey);
        provStack.Children.Add(verifyAiProviderBtn);
        provStack.Children.Add(rememberKeyCheck);
        provStack.Children.Add(webSearch);
        hGrid.Children.Add(provStack);

        var topStack = new StackPanel();
        topStack.Children.Add(hGrid);
        topStack.Children.Add(apiKeyEnvStatus);
        headerBorder.Child = topStack;
        mainGrid.Children.Add(headerBorder);

        // 2. MAIN WORKSPACE (2 COLUMNS: LEFT = Generation & My Presets, RIGHT = 5 Detail Tabs)
        var workGrid = new Grid();
        workGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(11, GridUnitType.Star) });
        workGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        workGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13, GridUnitType.Star) });
        Grid.SetRow(workGrid, 1);
        mainGrid.Children.Add(workGrid);

        // LEFT COLUMN
        var leftDock = new DockPanel();
        
        // Lower: My Presets (Mis presets)
        var myPresetsCard = Card(UiText.Language == "es" ? "MIS PRESETS GENERADOS / EN TRABAJO" : "MY PRESETS");
        myPresetsCard.MaxHeight = 280;
        DockPanel.SetDock(myPresetsCard, Dock.Bottom);

        var myPresetsStack = new StackPanel();
        var injectBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        injectBar.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "⚡ Slot de prueba:" : "⚡ Test Slot:", Foreground = BrushHex("#38BDF8"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        injectBar.Children.Add(injectionSlotCombo);
        injectBar.Children.Add(new Border { Width = 10 });
        injectBar.Children.Add(autoInjectCheck);
        injectBar.Children.Add(new Border { Width = 10 });
        injectBar.Children.Add(listenToPedalCheck);
        myPresetsStack.Children.Add(injectBar);
        myPresetsList.MinHeight = 110;
        myPresetsList.MaxHeight = 180;
        myPresetsStack.Children.Add(myPresetsList);
        injectSelectedBtn.Background = BrushHex("#2563EB");
        injectSelectedBtn.Foreground = Brushes.White;
        injectSelectedBtn.Padding = new Thickness(8, 4, 8, 4);
        var presetActions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        presetActions.Children.Add(injectSelectedBtn);
        myPresetsStack.Children.Add(presetActions);
        myPresetsStack.Children.Add(injectionStatus);
        SetCardContent(myPresetsCard, myPresetsStack);
        leftDock.Children.Add(myPresetsCard);

        // Upper: Studio Tabs (New song vs Refine)
        var studioCard = Card(UiText.Language == "es" ? "ESTUDIO DE TONO IA (GP-200 STUDIO)" : "AI TONE STUDIO");
        var sStack = new Grid();
        sStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        modeTabControl.Background = BrushHex("#0F172A");
        modeTabControl.BorderThickness = new Thickness(0);
        modeTabControl.ItemContainerStyle = CreateStudioTabItemStyle();

        // TAB 1: New Song
        var tabNew = new TabItem { Header = UiText.Language == "es" ? "🎵 Nueva canción / sonido" : "🎵 New song", FontWeight = FontWeights.Bold };
        tabNew.Content = BuildNewSongTab();
        modeTabControl.Items.Add(tabNew);

        // TAB 2: Refine
        var tabRefine = new TabItem { Header = UiText.Language == "es" ? "✏️ Refinar un preset existente" : "✏️ Refine preset", FontWeight = FontWeights.Bold };
        tabRefine.Content = BuildRefineTab();
        modeTabControl.Items.Add(tabRefine);

        Grid.SetRow(modeTabControl, 0);
        sStack.Children.Add(modeTabControl);
        var actionRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionRow.Children.Add(primaryAiActionBtn);
        Grid.SetColumn(cancelAiRequestBtn, 1);
        actionRow.Children.Add(cancelAiRequestBtn);
        Grid.SetRow(actionRow, 1);
        sStack.Children.Add(actionRow);
        SetCardContent(studioCard, sStack);
        leftDock.Children.Add(studioCard);

        Grid.SetColumn(leftDock, 0);
        workGrid.Children.Add(leftDock);

        // RIGHT COLUMN: 5 Detail Tabs (Result, Pedal, CTRL, Volume, Set list)
        var rightCard = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };
        detailTabs.Background = BrushHex("#0F172A");
        detailTabs.BorderThickness = new Thickness(0);
        detailTabs.ItemContainerStyle = CreateStudioTabItemStyle();

        detailTabs.Items.Add(new TabItem { Header = "Result", FontWeight = FontWeights.SemiBold, Content = BuildResultTab() });
        detailTabs.Items.Add(new TabItem { Header = "Pedal", FontWeight = FontWeights.SemiBold, Content = BuildPedalTab() });
        detailTabs.Items.Add(new TabItem { Header = "CTRL", FontWeight = FontWeights.SemiBold, Content = BuildCtrlTab() });
        detailTabs.Items.Add(new TabItem { Header = "Volume", FontWeight = FontWeights.SemiBold, Content = BuildVolumeTab() });
        detailTabs.Items.Add(new TabItem { Header = "Set list", FontWeight = FontWeights.SemiBold, Content = BuildSetListTab() });

        rightCard.Child = detailTabs;
        Grid.SetColumn(rightCard, 2);
        workGrid.Children.Add(rightCard);

        var windowRoot = new Grid();
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var headerBar = new AuraWindowHeaderBar(this, Title, mainGrid, "AI PRESET STUDIO");
        Grid.SetRow(headerBar, 0);
        windowRoot.Children.Add(headerBar);
        Grid.SetRow(mainGrid, 1);
        windowRoot.Children.Add(mainGrid);
        Content = windowRoot;

        // Wire selections
        myPresetsList.SelectionChanged += (_, _) => OnPresetSelected();
    }

    UIElement BuildNewSongTab()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Describe el sonido, artista, canción o guitarra:" : "Describe the sound, artist, song, or guitar:", FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#94A3B8"), Margin = new Thickness(0, 0, 0, 3) });
        requestText.Text = "";
        requestText.MinHeight = 90;
        requestText.ToolTip = UiText.Language == "es"
            ? "Describe el tono que quieres. Los botones inferiores son ejemplos de solicitudes, no presets cargados."
            : "Describe the tone you want. The chips below are prompt examples, not loaded presets.";
        sp.Children.Add(requestText);
        sp.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es"
                ? "La propuesta la genera el proveedor de IA seleccionado; los botones son ejemplos de texto, no presets descargados de la biblioteca."
                : "The selected AI provider generates the proposal; the chips are prompt examples, not presets loaded from the library.",
            FontSize = 10,
            Foreground = BrushHex("#94A3B8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 5)
        });

        // Clickable Chips
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        chips.Children.Add(Chip("Master of Puppets", "Master of Puppets - Metallica, album rhythm tone with punchy low end"));
        chips.Children.Add(Chip("Nothing Else Matters", "Nothing Else Matters - Metallica, clean intro with warm chorus to heavy solo"));
        chips.Children.Add(Chip("Gracia Más Grande (Worship)", "Gracia Más Grande - Jonathan & Sarah Jerez, warm worship ambient overdrive and dotted-8th delay"));
        chips.Children.Add(Chip("Comfortably Numb", "Comfortably Numb - Pink Floyd, smooth sustaining lead solo with delay and Hiwatt warmth"));
        sp.Children.Add(chips);

        // Structure & Pickups
        var midGrid = new Grid();
        midGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        midGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        midGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var strStack = new StackPanel();
        strStack.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Estructura:" : "Structure:", FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#94A3B8"), Margin = new Thickness(0, 0, 0, 2) });
        structurePillsPanel.Children.Clear();
        structurePillsPanel.Children.Add(structureAutoDetect);
        foreach (var pill in structurePills.Values)
            structurePillsPanel.Children.Add(pill);
        strStack.Children.Add(structurePillsPanel);
        strStack.Children.Add(aiChoosesSetSizeCheck);
        strStack.Children.Add(structureSummaryText);
        midGrid.Children.Add(strStack);

        var pickStack = new StackPanel();
        pickStack.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Pastillas de tu guitarra:" : "Your guitar pickups:", FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#94A3B8"), Margin = new Thickness(0, 0, 0, 2) });
        pickStack.Children.Add(pickups);
        pickStack.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Topología:" : "Topology:", FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#94A3B8"), Margin = new Thickness(0, 6, 0, 2) });
        pickStack.Children.Add(chainTopology);
        Grid.SetColumn(pickStack, 2);
        midGrid.Children.Add(pickStack);
        sp.Children.Add(midGrid);

        // Banner Informativo: Optimización de Tokens y Cuota
        var optBanner = new Border
        {
            Background = BrushHex("#081220"),
            BorderBrush = BrushHex("#00E5FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 10, 0, 4)
        };
        var optStack = new StackPanel();
        optStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "⚡ MODO ULTRA-RÁPIDO Y OPTIMIZADO PARA CUOTA GRATUITA" : "⚡ ULTRA-FAST & QUOTA-OPTIMIZED MODE",
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#00E5FF"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        });
        optStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es"
                ? "El envío de datos se ha optimizado a <1.200 tokens (reducción del 99%) eliminando enlaces y archivos pesados para evitar el error de cuota (HTTP 429). Escribe el nombre de la banda, canción o estilo directamente en la descripción para un resultado perfecto."
                : "Payload optimized to <1,200 tokens (99% reduction) eliminating heavy links and audio to prevent HTTP 429 rate limits. Specify artist, song or style directly in the sound description.",
            Foreground = BrushHex("#94A3B8"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap
        });
        optBanner.Child = optStack;
        sp.Children.Add(optBanner);

        return ScrollTab(sp);
    }

    UIElement BuildRefineTab()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        openRefinePresetBtn.Click += (_, _) => OpenRefinePreset();
        sp.Children.Add(openRefinePresetBtn);
        UpdateRefineTargetInfo();
        sp.Children.Add(refineTargetInfo);

        sp.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "¿Qué quieres cambiar en este preset?" : "What do you want to change in this preset?",
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#94A3B8"),
            Margin = new Thickness(0, 4, 0, 3)
        });
        refineRequestText.Text = "";
        refineRequestText.ToolTip = UiText.Language == "es"
            ? "Escribe los cambios que quieres. Los botones inferiores son ejemplos."
            : "Describe the changes you want. The chips below are examples.";
        sp.Children.Add(refineRequestText);

        // Quick Refine Chips
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
        chips.Children.Add(RefineChip("Demasiada reverb, añade un flanger", "Demasiada reverb, reduce el mix y añade un flanger sutil"));
        chips.Children.Add(RefineChip("Falta ganancia y brillo", "Falta ganancia y el tono suena apagado en los agudos"));
        chips.Children.Add(RefineChip("Añade delay corchea con punto", "Añade un delay en corchea con punto (dotted-eighth)"));
        chips.Children.Add(RefineChip("Convertir a Dual Chain A/B", "Conviértelo en Dual Chain A/B con dos amplificadores complementarios"));
        sp.Children.Add(chips);

        var btnRow = new WrapPanel();
        liveOptimizeBtn.Click += async (_, _) => await RefineActivePresetAsync(true);
        btnRow.Children.Add(liveOptimizeBtn);
        sp.Children.Add(btnRow);

        return ScrollTab(sp);
    }

    UIElement BuildResultTab()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        sp.Children.Add(resHeader);
        sp.Children.Add(resDescription);

        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Cambios reales (comparación local):" : "Actual changes (local comparison):", FontWeight = FontWeights.Bold, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 4, 0, 2) });
        sp.Children.Add(resChanges);
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Cadena de Señal:" : "Signal Chain:", FontWeight = FontWeights.Bold, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 4, 0, 2) });
        sp.Children.Add(resChainFlow);
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Fuentes de búsqueda:" : "Research sources:", FontWeight = FontWeights.Bold, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 4, 0, 2) });
        sp.Children.Add(resSources);

        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Resumen CTRL:" : "CTRL Summary:", FontWeight = FontWeights.Bold, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 4, 0, 2) });
        sp.Children.Add(resCtrlSummary);

        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Notas de Interpretación:" : "Performance Notes:", FontWeight = FontWeights.Bold, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 4, 0, 2) });
        sp.Children.Add(resNotes);

        // Quick Touch-up
        var tGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        quickTweakBox.Text = UiText.Language == "es" ? "Pedir retoque a la IA (ej. más graves, menos delay)..." : "Ask AI for touchup...";
        tGrid.Children.Add(quickTweakBox);
        Grid.SetColumn(quickTweakBtn, 2);
        quickTweakBtn.Click += async (_, _) => await TweakCurrentPresetAsync();
        tGrid.Children.Add(quickTweakBtn);
        sp.Children.Add(tGrid);

        var resultActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        addToEditorBtn.Background = BrushHex("#334155");
        addToEditorBtn.Foreground = Brushes.White;
        addToEditorBtn.Padding = new Thickness(8, 4, 8, 4);
        matchVolumesBtn.Background = BrushHex("#10B981");
        matchVolumesBtn.Foreground = Brushes.White;
        matchVolumesBtn.Padding = new Thickness(8, 4, 8, 4);
        resultActions.Children.Add(addToEditorBtn);
        sp.Children.Add(resultActions);

        return ScrollTab(sp);
    }

    UIElement BuildPedalTab()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        bool isConn = DeviceSession.Current.Connected;
        pedalTabStatus.Text = isConn ? "● GP-200 Conectada vía USB" : "○ GP-200 Desconectada";
        pedalTabStatus.Foreground = isConn ? BrushHex("#10B981") : BrushHex("#F59E0B");
        sp.Children.Add(pedalTabStatus);

        var sRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        sRow.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Slot Destino en GP-200:" : "Target Slot:", VerticalAlignment = VerticalAlignment.Center, Foreground = BrushHex("#94A3B8"), Margin = new Thickness(0, 0, 8, 0) });
        sRow.Children.Add(pedalTargetSlotCombo);
        sRow.Children.Add(new Border { Width = 10 });
        sendToSlotBtn.Click += async (_, _) => await SendSelectedToTargetSlotAsync();
        sRow.Children.Add(sendToSlotBtn);
        sp.Children.Add(sRow);

        readActiveFromPedalBtn.Click += async (_, _) => await ReadActiveFromPedalAsync();
        sp.Children.Add(readActiveFromPedalBtn);

        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Registro / Respuesta del Pedal:" : "Pedal Response Log:", FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#94A3B8"), Margin = new Thickness(0, 12, 0, 4) });
        sp.Children.Add(pedalResponseLog);

        return ScrollTab(sp);
    }

    UIElement BuildCtrlTab()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        sp.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "MATRIZ DE ASIGNACIÓN CTRL 8×11 (FOOTSWITCHES)" : "CTRL 8×11 FOOTSWITCH MATRIX",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#38BDF8"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        // Initialize 8x11 grid
        ctrlMatrixGrid.Children.Clear();
        ctrlMatrixGrid.RowDefinitions.Clear();
        ctrlMatrixGrid.ColumnDefinitions.Clear();

        // 1 header col + 11 module cols
        ctrlMatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        for (int c = 0; c < 11; c++)
            ctrlMatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 1 header row + 8 CTRL rows
        for (int r = 0; r < 9; r++)
            ctrlMatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header labels
        for (int c = 0; c < 11; c++)
        {
            var h = new TextBlock
            {
                Text = PresetDocument.Modules[c],
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = BrushHex("#94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(h, 0);
            Grid.SetColumn(h, c + 1);
            ctrlMatrixGrid.Children.Add(h);
        }

        for (int r = 0; r < 8; r++)
        {
            int slot = r;
            var rLabel = new TextBlock
            {
                Text = $"CTRL {slot + 1}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushHex("#F1F5F9"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(rLabel, r + 1);
            Grid.SetColumn(rLabel, 0);
            ctrlMatrixGrid.Children.Add(rLabel);

            for (int c = 0; c < 11; c++)
            {
                int mod = c;
                var cb = new CheckBox
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2)
                };
                cb.Click += (_, _) => OnCtrlCellToggled(slot, mod, cb.IsChecked == true);
                Grid.SetRow(cb, r + 1);
                Grid.SetColumn(cb, c + 1);
                ctrlMatrixGrid.Children.Add(cb);
            }
        }

        var scroll = new ScrollViewer { Content = ctrlMatrixGrid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        sp.Children.Add(scroll);
        sp.Children.Add(ctrlLiveSummary);

        sendCtrlBtn.Click += async (_, _) => await SendCtrlAssignmentsToPedalAsync();
        sp.Children.Add(sendCtrlBtn);

        return ScrollTab(sp);
    }

    UIElement BuildVolumeTab()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        sp.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "NIVELACIÓN DE VOLUMEN (MATCH VOLUME)" : "VOLUME MATCHING", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = BrushHex("#38BDF8") });
        sp.Children.Add(volCurrentLufs);

        autoLevelCurrentBtn.Click += (_, _) => OpenVolumeMatcher(true);
        sp.Children.Add(autoLevelCurrentBtn);

        var hint = new TextBlock
        {
            Text = UiText.Language == "es"
                ? "El nivel se mide desde la entrada de audio seleccionada; no se estima con un incremento fijo."
                : "Levels are measured from the selected audio input; no fixed-volume guess is used.",
            FontSize = 11,
            Foreground = BrushHex("#94A3B8"),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        sp.Children.Add(hint);
        matchVolumesBtn.Background = BrushHex("#10B981");
        matchVolumesBtn.Foreground = Brushes.White;
        matchVolumesBtn.Padding = new Thickness(10, 5, 10, 5);
        sp.Children.Add(matchVolumesBtn);

        return ScrollTab(sp);
    }

    UIElement BuildSetListTab()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        sp.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "GESTIÓN DE SET LIST Y SUBIDA MASIVA A SLOTS" : "SET LIST & BATCH SLOT UPLOAD",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#38BDF8"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var assignBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        assignBar.Children.Add(new TextBlock { Text = UiText.Language == "es" ? "Slot inicial:" : "Start slot:", VerticalAlignment = VerticalAlignment.Center, Foreground = BrushHex("#94A3B8"), Margin = new Thickness(0, 0, 6, 0) });
        assignBar.Children.Add(setListStartSlot);
        assignBar.Children.Add(new Border { Width = 8 });
        autoAssignSlotsBtn.Click += (_, _) => AutoAssignSetListSlots();
        assignBar.Children.Add(autoAssignSlotsBtn);
        sp.Children.Add(assignBar);

        var scrollTable = new ScrollViewer { Content = setListTable, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        sp.Children.Add(scrollTable);

        var actionRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        uploadAllToPedalBtn.Click += async (_, _) => await UploadSetListToPedalAsync();
        exportZipBtn.Click += (_, _) => ExportSetListZip();
        actionRow.Children.Add(uploadAllToPedalBtn);
        actionRow.Children.Add(new Border { Width = 10 });
        actionRow.Children.Add(exportZipBtn);
        sp.Children.Add(actionRow);

        return ScrollTab(sp);
    }

    void InitProviders()
    {
        provider.Items.Add("Google Gemini");
        provider.Items.Add("Anthropic Claude");
        provider.Items.Add("OpenRouter");
        provider.SelectedIndex = 0;

        provider.SelectionChanged += (_, _) =>
        {
            var selectedProv = (AiProvider)Math.Clamp(provider.SelectedIndex, 0, 2);
            string? savedKey = AiApiKeyResolver.ReadSavedKey(selectedProv);
            if (!string.IsNullOrEmpty(savedKey))
            {
                if (selectedProv == AiProvider.OpenRouter && !savedKey.StartsWith("sk-or-", StringComparison.OrdinalIgnoreCase))
                    apiKey.Password = "";
                else
                    apiKey.Password = savedKey;
            }
            else
            {
                apiKey.Password = "";
            }
            InvalidateProviderConnection();
            UpdateApiKeyIndicator();
        };

        rememberKeyCheck.IsChecked = AiApiKeyResolver.IsRememberEnabled();
        rememberKeyCheck.Checked += (_, _) => SaveCurrentKeyIfRemembered();
        rememberKeyCheck.Unchecked += (_, _) => SaveCurrentKeyIfRemembered();
        apiKey.PasswordChanged += (_, _) =>
        {
            InvalidateProviderConnection();
            UpdateApiKeyIndicator();
            if (rememberKeyCheck.IsChecked == true) SaveCurrentKeyIfRemembered();
        };
        model.SelectionChanged += (_, _) => UpdatePrimaryAiAction();
        verifyAiProviderBtn.Click += async (_, _) => await VerifyProviderAndLoadModelsAsync();

        ResetModelList();
        UpdateApiKeyIndicator();
    }

    static readonly string[] DefaultGeminiModels = ["gemini-3.1-flash-lite", "gemini-2.5-flash-lite", "gemini-3.5-flash", "gemini-2.5-flash"];
    static readonly string[] DefaultAnthropicModels = ["claude-3-7-sonnet-latest", "claude-3-5-sonnet-latest", "claude-3-5-haiku-latest"];
    static readonly string[] DefaultOpenRouterModels = ["google/gemini-2.0-flash-exp:free", "meta-llama/llama-3.3-70b-instruct:free", "mistralai/mistral-7b-instruct:free"];

    void ResetModelList()
    {
        var selectedProvider = (AiProvider)Math.Clamp(provider.SelectedIndex, 0, 2);
        string[] defaultModels = selectedProvider switch
        {
            AiProvider.Gemini => DefaultGeminiModels,
            AiProvider.Anthropic => DefaultAnthropicModels,
            AiProvider.OpenRouter => DefaultOpenRouterModels,
            _ => DefaultGeminiModels
        };
        model.ItemsSource = defaultModels;
        model.SelectedIndex = 0;
        model.IsEnabled = true;
        verifiedCredentialFingerprint = null;
        verifiedProvider = null;
        UpdatePrimaryAiAction();
    }

    void InvalidateProviderConnection()
    {
        modelDiscoveryRequest?.Cancel();
        ResetModelList();
    }

    static string CredentialFingerprint(string key) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));

    bool HasVerifiedProviderAccess(AiProvider provider, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return model.IsEnabled && model.SelectedItem is string selectedModel && !string.IsNullOrWhiteSpace(selectedModel);
    }

    async Task VerifyProviderAndLoadModelsAsync()
    {
        if (providerDiscoveryInProgress)
            return;

        var selectedProvider = (AiProvider)Math.Clamp(provider.SelectedIndex, 0, 2);
        string? key = AiApiKeyResolver.ResolveKey(selectedProvider, apiKey.Password);
        if (string.IsNullOrWhiteSpace(key))
        {
            apiKeyEnvStatus.Text = UiText.Language == "es"
                ? "Introduce una clave API válida para continuar."
                : "Enter an API key to continue.";
            apiKeyEnvStatus.Foreground = BrushHex("#F59E0B");
            apiKey.Focus();
            return;
        }

        InvalidateProviderConnection();
        using var request = new CancellationTokenSource();
        modelDiscoveryRequest = request;
        providerDiscoveryInProgress = true;
        verifyAiProviderBtn.IsEnabled = false;
        verifyAiProviderBtn.Content = UiText.Language == "es" ? "Validando acceso…" : "Checking access…";
        apiKeyEnvStatus.Text = UiText.Language == "es"
            ? "Consultando al proveedor y comprobando los modelos disponibles para esta clave…"
            : "Contacting the provider and checking models available to this key…";
        apiKeyEnvStatus.Foreground = BrushHex("#38BDF8");
        UpdatePrimaryAiAction();

        try
        {
            var availableModels = await AiPresetGeneration.DiscoverModelsAsync(selectedProvider, key, request.Token);
            if (request.IsCancellationRequested || provider.SelectedIndex != (int)selectedProvider
                || CredentialFingerprint(AiApiKeyResolver.ResolveKey(selectedProvider, apiKey.Password) ?? "") != CredentialFingerprint(key))
                return;

            model.ItemsSource = availableModels;
            model.IsEnabled = true;
            model.SelectedIndex = 0;
            verifiedProvider = selectedProvider;
            verifiedCredentialFingerprint = CredentialFingerprint(key);
            apiKeyEnvStatus.Text = UiText.Language == "es"
                ? $"✓ Clave validada: {availableModels.Count} modelos accesibles."
                : $"✓ Key verified: {availableModels.Count} accessible models.";
            apiKeyEnvStatus.Foreground = BrushHex("#10B981");
            UpdatePrimaryAiAction();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ResetModelList();
            apiKeyEnvStatus.Text = (UiText.Language == "es" ? "No se pudo validar la clave ni cargar modelos: " : "Could not verify the key or load models: ") + error.Message;
            apiKeyEnvStatus.Foreground = BrushHex("#EF4444");
        }
        finally
        {
            providerDiscoveryInProgress = false;
            if (ReferenceEquals(modelDiscoveryRequest, request))
                modelDiscoveryRequest = null;
            verifyAiProviderBtn.IsEnabled = true;
            verifyAiProviderBtn.Content = UiText.Language == "es" ? "Validar clave y detectar modelos" : "Verify key & load models";
            UpdatePrimaryAiAction();
        }
    }

    void SaveCurrentKeyIfRemembered()
    {
        var prov = (AiProvider)Math.Clamp(provider.SelectedIndex, 0, 2);
        if (prov == AiProvider.OpenRouter && !string.IsNullOrWhiteSpace(apiKey.Password) && !apiKey.Password.StartsWith("sk-or-", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            AiApiKeyResolver.SaveKey(prov, apiKey.Password, rememberKeyCheck.IsChecked == true);
            UpdateApiKeyIndicator();
        }
        catch (Exception error)
        {
            apiKeyEnvStatus.Text = (UiText.Language == "es" ? "No se pudo guardar la clave: " : "Could not save key: ") + error.Message;
            apiKeyEnvStatus.Foreground = BrushHex("#EF4444");
        }
    }

    void UpdateApiKeyIndicator()
    {
        var prov = (AiProvider)Math.Clamp(provider.SelectedIndex, 0, 2);
        string? envName = AiApiKeyResolver.GetDetectedEnvVarName(prov, apiKey.Password);
        if (!string.IsNullOrEmpty(envName))
        {
            apiKeyEnvStatus.Text = UiText.Language == "es"
                ? $"Credencial disponible ({envName}); valida la conexión para cargar modelos."
                : $"Credential available ({envName}); verify access to load models.";
            apiKeyEnvStatus.Foreground = BrushHex("#38BDF8");
        }
        else if (!string.IsNullOrWhiteSpace(apiKey.Password))
        {
            apiKeyEnvStatus.Text = UiText.Language == "es"
                ? "Clave introducida; valida la conexión para comprobar el acceso."
                : "Key entered; verify access to check the connection.";
            apiKeyEnvStatus.Foreground = BrushHex("#38BDF8");
        }
        else
        {
            string environmentName = prov switch
            {
                AiProvider.Gemini => "GEMINI_API_KEY",
                AiProvider.Anthropic => "ANTHROPIC_API_KEY",
                AiProvider.OpenRouter => "OPENROUTER_API_KEY",
                _ => "GP200_AI_API_KEY"
            };
            apiKeyEnvStatus.Text = UiText.Language == "es"
                ? $"Introduce una clave API de {provider.SelectedItem} o configúrala en $env:{environmentName}."
                : $"Enter an API key for {provider.SelectedItem} or set $env:{environmentName}.";
            apiKeyEnvStatus.Foreground = BrushHex("#94A3B8");
        }
    }

    void UpdateStructureText()
    {
        bool aiChoosesSetSize = aiChoosesSetSizeCheck.IsChecked == true;
        structureAutoDetect.IsEnabled = !aiChoosesSetSize;
        foreach (var pill in structurePills.Values)
            pill.IsEnabled = !aiChoosesSetSize;

        if (aiChoosesSetSize)
        {
            structureSummaryText.Text = UiText.Language == "es"
                ? "La IA elegirá de 1 a 4 presets y las secciones musicales más útiles."
                : "The AI will choose one to four presets and the most useful musical sections.";
            return;
        }

        var selected = new[] { "Clean", "Crunch", "Dist", "Lead" }
            .Where(section => structurePills.TryGetValue(section, out var pill) && pill.IsChecked == true)
            .ToArray();
        if (selected.Length == 0)
        {
            structureSummaryText.Text = UiText.Language == "es"
                ? "3 a 9 presets: La IA decide 1, 2 o 3 secciones (3 variantes por sección)."
                : "3 to 9 presets: AI decides 1, 2 or 3 sections (3 variations each).";
        }
        else
        {
            int count = selected.Length * 3;
            structureSummaryText.Text = UiText.Language == "es"
                ? $"{count} presets: 3 variantes por sección, en el orden {string.Join(", ", selected)}."
                : $"{count} presets: 3 variations per section, in order: {string.Join(", ", selected)}.";
        }
    }

    void UpdatePrimaryAiAction()
    {
        bool refining = modeTabControl.SelectedIndex == 1;
        if (aiRequestInProgress)
            primaryAiActionBtn.Content = refining
                ? (UiText.Language == "es" ? "⏳ Refinando con IA…" : "⏳ Refining with AI…")
                : (UiText.Language == "es" ? "⏳ Generando con IA…" : "⏳ Generating with AI…");
        else
            primaryAiActionBtn.Content = refining
                ? (UiText.Language == "es" ? "✨ Refinar preset con IA" : "✨ Refine preset with AI")
                : (UiText.Language == "es" ? "✨ Generar presets con IA" : "✨ Generate presets with AI");

        var selectedProvider = (AiProvider)Math.Clamp(provider.SelectedIndex, 0, 2);
        string? key = AiApiKeyResolver.ResolveKey(selectedProvider, apiKey.Password);
        bool providerReady = key is not null && HasVerifiedProviderAccess(selectedProvider, key);
        primaryAiActionBtn.IsEnabled = !aiRequestInProgress && providerReady;
        primaryAiActionBtn.ToolTip = providerReady
            ? (UiText.Language == "es"
                ? "El proveedor y el modelo están validados para la clave actual."
                : "The provider and model are verified for the current key.")
            : (UiText.Language == "es"
                ? "Introduce una clave y pulsa «Validar clave y detectar modelos»."
                : "Enter a key and click “Verify key & load models”.");
        modeTabControl.IsEnabled = !aiRequestInProgress;
        cancelAiRequestBtn.Visibility = aiRequestInProgress ? Visibility.Visible : Visibility.Collapsed;
        cancelAiRequestBtn.IsEnabled = aiRequestInProgress;
    }

    void UpdateRefineTargetInfo()
    {
        if (myPresetsList.SelectedIndex is >= 0 && myPresetsList.SelectedIndex < generated.Count)
        {
            var selected = generated[myPresetsList.SelectedIndex];
            refineTargetInfo.Text = UiText.Language == "es"
                ? $"Preset generado: {selected.Name}"
                : $"Generated preset: {selected.Name}";
        }
        else if (refineFilePreset != null)
        {
            refineTargetInfo.Text = UiText.Language == "es"
                ? $"Archivo cargado: {Path.GetFileName(refineFilePath)} · {refineFilePreset.Name}"
                : $"Loaded file: {Path.GetFileName(refineFilePath)} · {refineFilePreset.Name}";
        }
        else if (current != null)
        {
            refineTargetInfo.Text = UiText.Language == "es"
                ? $"Preset activo: [{BankService.Position(current.Index)}] {current.Name}"
                : $"Active preset: [{BankService.Position(current.Index)}] {current.Name}";
        }
        else
        {
            refineTargetInfo.Text = UiText.Language == "es"
                ? "No hay preset activo. Abre un archivo .prst o selecciona un preset generado."
                : "No active preset. Open a .prst file or select a generated preset.";
        }
    }

    void OpenRefinePreset()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = UiText.Filter("GP-200 preset (*.prst)|*.prst"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var file = new FileInfo(dialog.FileName);
            if (file.Length is <= 0 or > 4 * 1024 * 1024)
                throw new InvalidDataException("Preset file must be between 1 byte and 4 MiB.");

            var loaded = new PresetDocument(File.ReadAllBytes(file.FullName));
            refineFilePreset = loaded;
            refineFilePath = file.FullName;
            myPresetsList.SelectedIndex = -1;
            UpdateRefineTargetInfo();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            UiDialogs.Show(this, error.Message, UiText.Language == "es" ? "No se pudo abrir el preset" : "Could not open preset");
        }
    }

    async Task GenerateNewSongAsync()
    {
        string soundDescription = requestText.Text.Trim();
        if (string.IsNullOrWhiteSpace(soundDescription))
        {
            UiDialogs.Show(this,
                UiText.Language == "es" ? "Describe el sonido que quieres generar." : "Describe the sound you want to generate.",
                UiText.Language == "es" ? "Generar preset" : "Generate preset");
            return;
        }

        string songReference = songLinkBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(songReference)
            && (!Uri.TryCreate(songReference, UriKind.Absolute, out var songUri)
                || songUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(songUri.UserInfo)))
        {
            UiDialogs.Show(this,
                UiText.Language == "es"
                    ? "El enlace debe ser una URL HTTPS válida y no incluir credenciales."
                    : "The link must be a valid HTTPS URL and must not contain credentials.",
                UiText.Language == "es" ? "Enlace no válido" : "Invalid link");
            return;
        }

        var prov = (AiProvider)Math.Clamp(provider.SelectedIndex, 0, 2);
        string? key = AiApiKeyResolver.ResolveKey(prov, apiKey.Password);
        if (string.IsNullOrWhiteSpace(key))
        {
            UiDialogs.Show(this, UiText.Language == "es" ? "Por favor introduce tu clave API o configúrala en $env:GEMINI_API_KEY." : "Please enter your API Key.", "API Key");
            return;
        }
        if (!HasVerifiedProviderAccess(prov, key))
        {
            UiDialogs.Show(this,
                UiText.Language == "es"
                    ? "Valida primero la clave con el proveedor para cargar y elegir un modelo accesible."
                    : "Verify the key with the provider first to load and select an accessible model.",
                UiText.Language == "es" ? "Proveedor sin validar" : "Provider not verified");
            return;
        }

        string chosenModel = (string)model.SelectedItem!;
        string[] reqSec = new[] { "Clean", "Crunch", "Dist", "Lead" }
            .Where(section => structurePills.TryGetValue(section, out var pill) && pill.IsChecked == true)
            .ToArray();
        int maxNewSongPresets = aiChoosesSetSizeCheck.IsChecked == true ? 4 : 9;
        bool includeAudio = sendAudioToAiCheck.IsChecked == true;
        if (includeAudio)
        {
            if (prov != AiProvider.Gemini || loadedAudioBytes is not { Length: > 0 } || string.IsNullOrWhiteSpace(loadedAudioMime))
            {
                UiDialogs.Show(this,
                    UiText.Language == "es"
                        ? "El envío de audio solo está disponible para un archivo de audio compatible y el proveedor Gemini."
                        : "Audio upload requires a supported audio file and the Gemini provider.",
                    "Audio");
                return;
            }

            if (UiDialogs.Show(this,
                UiText.Language == "es"
                    ? $"Se enviará el audio «{Path.GetFileName(audioFileBox.Text)}» y, si está disponible, su análisis local a Google Gemini. ¿Confirmas este envío externo?"
                    : $"The audio “{Path.GetFileName(audioFileBox.Text)}” and, if available, its local analysis will be sent to Google Gemini. Confirm this external transfer?",
                UiText.Language == "es" ? "Confirmar envío de audio" : "Confirm audio upload",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        string desc = soundDescription + "\nGuitar pickups: " + pickups.SelectedItem;
        var topo = (AiChainTopology)Math.Clamp(chainTopology.SelectedIndex, 0, 2);
        var perf = (AiPerformanceMode)Math.Clamp(performanceMode.SelectedIndex, 0, 3);

        using var request = new CancellationTokenSource();
        activeRequest = request;
        aiRequestInProgress = true;
        UpdatePrimaryAiAction();
        SetMyPresetsStatus("⏳ Conectando con IA real y diseñando presets...", "#38BDF8");
        injectionStatus.Text = UiText.Language == "es" ? "Generando presets con IA real... Por favor espera." : "Generating presets with real AI...";
        injectionStatus.Foreground = BrushHex("#38BDF8");

        try
        {
            var service = new AiPresetGeneration(catalog);
            var proposals = await service.GenerateAsync(
                prov, chosenModel, key, AiPresetMode.NewSong, desc, current, deviceLibrary, false,
                perf, null, reqSec, false, request.Token,
                null, null, null, null, topo, maxNewSongPresets
            );

            generated.Clear();
            generated.AddRange(proposals);
            favoriteIndices.Clear();
            selectedForSetList.Clear();
            selectedForSetList.UnionWith(Enumerable.Range(0, generated.Count));
            setListSlotAssignments.Clear();
            RefreshMyPresetsUi();
            if (generated.Count > 0) myPresetsList.SelectedIndex = 0;
            injectionStatus.Text = UiText.Language == "es" ? $"✓ Generados {generated.Count} presets con éxito por la IA." : $"✓ Successfully generated {generated.Count} presets.";
            injectionStatus.Foreground = BrushHex("#10B981");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            RefreshMyPresetsUi();
            injectionStatus.Text = UiText.Language == "es" ? "Generación cancelada." : "Generation cancelled.";
            injectionStatus.Foreground = BrushHex("#F59E0B");
        }
        catch (Exception ex)
        {
            SetMyPresetsStatus($"⚠️ Error: {ex.Message}", "#EF4444");
            injectionStatus.Text = "Error: " + ex.Message;
            injectionStatus.Foreground = BrushHex("#EF4444");
            if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } || ex.Message.Contains("429"))
            {
                UiDialogs.Show(this,
                    UiText.Language == "es"
                        ? "Has alcanzado el límite de peticiones por minuto (HTTP 429) de Google AI Studio en tu cuenta gratuita.\n\nPor favor espera 20-30 segundos a que se restablezca el cupo y vuelve a pulsar el botón.\n\nRecomendación: Usa el modelo 'gemini-3.1-flash-lite' (seleccionado por defecto) para la mayor velocidad y disponibilidad."
                        : "Rate limit reached (HTTP 429) on Google AI Studio.\n\nPlease wait 20-30 seconds for the quota to replenish and click generate again.\n\nRecommended: Use 'gemini-3.1-flash-lite' for the best speed and headroom.",
                    "Cuota Temporal (HTTP 429)");
            }
            else
            {
                UiDialogs.Show(this, ex.Message, "Error IA");
            }
        }
        finally
        {
            aiRequestInProgress = false;
            if (ReferenceEquals(activeRequest, request)) activeRequest = null;
            UpdatePrimaryAiAction();
        }
    }

    async Task RefineActivePresetAsync(bool liveOptimize)
    {
        PresetDocument? target = GetRefineTarget();
        if (target == null)
        {
            UiDialogs.Show(this, "Selecciona un preset antes de refinar.", "Refinar");
            return;
        }

        var prov = (AiProvider)Math.Clamp(provider.SelectedIndex, 0, 2);
        string? key = AiApiKeyResolver.ResolveKey(prov, apiKey.Password);
        if (string.IsNullOrWhiteSpace(key))
        {
            UiDialogs.Show(this, "Introduce tu clave API.", "API Key");
            return;
        }
        if (!HasVerifiedProviderAccess(prov, key))
        {
            UiDialogs.Show(this,
                UiText.Language == "es"
                    ? "Valida primero la clave con el proveedor para cargar y elegir un modelo accesible."
                    : "Verify the key with the provider first to load and select an accessible model.",
                UiText.Language == "es" ? "Proveedor sin validar" : "Provider not verified");
            return;
        }

        string chosenModel = (string)model.SelectedItem!;
        string desc = liveOptimize ? "Optimize this preset for live performance in a band mix." : refineRequestText.Text.Trim();
        if (!liveOptimize && string.IsNullOrWhiteSpace(desc))
        {
            UiDialogs.Show(this, "Describe qué quieres cambiar en el preset.", "Refinar");
            return;
        }
        var mode = liveOptimize ? AiPresetMode.OptimizeForLive : AiPresetMode.Refine;

        using var request = new CancellationTokenSource();
        activeRequest = request;
        aiRequestInProgress = true;
        UpdatePrimaryAiAction();
        SetMyPresetsStatus("⏳ Refinando preset con IA real...", "#38BDF8");

        try
        {
            var service = new AiPresetGeneration(catalog);
            var proposals = await service.GenerateAsync(
                prov, chosenModel, key, mode, desc, target, deviceLibrary, false,
                AiPerformanceMode.Patch, null, [], false, request.Token
            );

            generated.Clear();
            generated.AddRange(proposals);
            favoriteIndices.Clear();
            selectedForSetList.Clear();
            selectedForSetList.UnionWith(Enumerable.Range(0, generated.Count));
            setListSlotAssignments.Clear();
            RefreshMyPresetsUi();
            if (generated.Count > 0) myPresetsList.SelectedIndex = 0;
            injectionStatus.Text = UiText.Language == "es" ? "✓ Preset refinado con éxito." : "✓ Preset refined successfully.";
            injectionStatus.Foreground = BrushHex("#10B981");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            RefreshMyPresetsUi();
            injectionStatus.Text = UiText.Language == "es" ? "Refinamiento cancelado." : "Refinement cancelled.";
            injectionStatus.Foreground = BrushHex("#F59E0B");
        }
        catch (Exception ex)
        {
            SetMyPresetsStatus($"⚠️ Error: {ex.Message}", "#EF4444");
            injectionStatus.Text = ex.Message;
            injectionStatus.Foreground = BrushHex("#EF4444");
            if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } || ex.Message.Contains("429"))
            {
                UiDialogs.Show(this,
                    UiText.Language == "es"
                        ? "Has alcanzado el límite de peticiones por minuto (HTTP 429) de Google AI Studio en tu cuenta gratuita.\n\nPor favor espera 20-30 segundos a que se restablezca el cupo y vuelve a pulsar el botón.\n\nRecomendación: Usa el modelo 'gemini-3.1-flash-lite' para la mayor velocidad y disponibilidad."
                        : "Rate limit reached (HTTP 429) on Google AI Studio.\n\nPlease wait 20-30 seconds for the quota to replenish and click generate again.\n\nRecommended: Use 'gemini-3.1-flash-lite' for the best speed and headroom.",
                    "Cuota Temporal (HTTP 429)");
            }
            else
            {
                UiDialogs.Show(this, ex.Message, "Error IA");
            }
        }
        finally
        {
            aiRequestInProgress = false;
            if (ReferenceEquals(activeRequest, request)) activeRequest = null;
            UpdatePrimaryAiAction();
        }
    }

    async Task TweakCurrentPresetAsync()
    {
        if (myPresetsList.SelectedIndex < 0 || myPresetsList.SelectedIndex >= generated.Count) return;
        refineRequestText.Text = quickTweakBox.Text;
        modeTabControl.SelectedIndex = 1;
        await RefineActivePresetAsync(false);
    }

    PresetDocument? GetRefineTarget()
    {
        if (myPresetsList.SelectedIndex is >= 0 && myPresetsList.SelectedIndex < generated.Count)
            return generated[myPresetsList.SelectedIndex].Document;
        if (refineFilePreset != null)
            return refineFilePreset;
        return pedalReadPreset ?? current;
    }

    void SetMyPresetsStatus(string message, string hexColor)
    {
        myPresetsList.ItemsSource = null;
        myPresetsList.Items.Clear();
        myPresetsList.Items.Add(new TextBlock
        {
            Text = message,
            Foreground = BrushHex(hexColor),
            FontSize = 11,
            Margin = new Thickness(8, 6, 8, 6),
            TextWrapping = TextWrapping.Wrap
        });
    }

    void RefreshMyPresetsUi()
    {
        myPresetsList.ItemsSource = null;
        myPresetsList.Items.Clear();
        for (int i = 0; i < generated.Count; i++)
        {
            var p = generated[i];
            int idx = i;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var favBtn = new Button
            {
                Content = favoriteIndices.Contains(idx) ? "♥" : "♡",
                Foreground = favoriteIndices.Contains(idx) ? BrushHex("#EF4444") : BrushHex("#94A3B8"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 14
            };
            favBtn.Click += (_, e) =>
            {
                e.Handled = true;
                if (favoriteIndices.Contains(idx)) favoriteIndices.Remove(idx);
                else favoriteIndices.Add(idx);
                favBtn.Content = favoriteIndices.Contains(idx) ? "♥" : "♡";
                favBtn.Foreground = favoriteIndices.Contains(idx) ? BrushHex("#EF4444") : BrushHex("#94A3B8");
                RefreshSetListUi();
            };
            row.Children.Add(favBtn);

            var secBorder = new Border
            {
                Background = BrushHex("#2563EB"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            secBorder.Child = new TextBlock { Text = p.Section ?? "Tone", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold };
            Grid.SetColumn(secBorder, 1);
            row.Children.Add(secBorder);

            var nameTxt = new TextBlock { Text = p.Name, Foreground = BrushHex("#F8FAFC"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameTxt, 2);
            row.Children.Add(nameTxt);

            myPresetsList.Items.Add(row);
        }
        RefreshSetListUi();
    }

    async void OnPresetSelected()
    {
        UpdateRefineTargetInfo();
        if (myPresetsList.SelectedIndex < 0 || myPresetsList.SelectedIndex >= generated.Count) return;
        var p = generated[myPresetsList.SelectedIndex];
        UpdatePresetDetails(p);

        // Auto-injection requires a separate, explicit opt-in.
        if (autoInjectCheck.IsChecked == true && DeviceSession.Current.Connected)
            await InjectSelectedPresetAsync(true);
    }

    void UpdatePresetDetails(AiGeneratedPreset p)
    {
        string author = string.IsNullOrWhiteSpace(p.Document.Author) ? "AI" : p.Document.Author;
        resHeader.Text = $"{p.Name} · {author} (Vol: {p.Document.Volume} · BPM: {p.Document.Tempo})";
        resDescription.Text = string.Join(" · ", new[] { p.Section, p.Document.Notes }.Where(value => !string.IsNullOrWhiteSpace(value)));
        resNotes.Text = p.Document.Notes;
        UpdatePresetChanges(p);
        resSources.Children.Clear();
        if (p.Sources is { Count: > 0 })
        {
            foreach (var source in p.Sources)
            {
                if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    continue;

                var sourceLine = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3)
                };
                var link = new Hyperlink(new Run(source.Title)) { NavigateUri = uri };
                link.RequestNavigate += (_, args) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch (Exception error)
                    {
                        UiDialogs.Show(this, error.Message, UiText.Language == "es" ? "No se pudo abrir la fuente" : "Could not open source");
                    }
                    args.Handled = true;
                };
                sourceLine.Inlines.Add(link);
                sourceLine.Inlines.Add(new Run($" — {uri.Host}") { Foreground = BrushHex("#94A3B8") });
                resSources.Children.Add(sourceLine);
            }
        }

        void UpdatePresetChanges(AiGeneratedPreset preset)
        {
            resChanges.Children.Clear();
            if (preset.RefinedFrom is not { } source)
            {
                resChanges.Children.Add(new TextBlock
                {
                    Text = UiText.Language == "es"
                        ? "Propuesta nueva: los bloques, parámetros y límites del catálogo se validaron localmente. No hay un preset anterior que comparar."
                        : "New proposal: blocks, parameters, and catalog limits were validated locally. There is no source preset to compare.",
                    Foreground = BrushHex("#94A3B8"),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            var changes = new List<string>();
            var result = preset.Document;
            if (source.Volume != result.Volume)
                changes.Add($"Patch volume: {source.Volume} → {result.Volume}");
            if (source.Tempo != result.Tempo)
                changes.Add($"Tempo: {source.Tempo} → {result.Tempo} BPM");
            if (!source.Chain.SequenceEqual(result.Chain))
            {
                string before = string.Join(" → ", source.Chain.Select(module => PresetDocument.Modules[module]));
                string after = string.Join(" → ", result.Chain.Select(module => PresetDocument.Modules[module]));
                changes.Add($"Chain: {before} → {after}");
            }
            if (!source.RoutingRaw.SequenceEqual(result.RoutingRaw))
                changes.Add(UiText.Language == "es" ? "Dual-chain routing: actualizado" : "Dual-chain routing: changed");

            for (int module = 0; module < PresetDocument.Modules.Length; module++)
            {
                var before = source.GetEffect(module);
                var after = result.GetEffect(module);
                string moduleName = PresetDocument.Modules[module];
                if (before.Code != after.Code)
                {
                    string beforeName = catalog.Find(module, before.Code)?.Name ?? $"0x{before.Code:X8}";
                    string afterName = catalog.Find(module, after.Code)?.Name ?? $"0x{after.Code:X8}";
                    changes.Add($"{moduleName} model: {beforeName} → {afterName}");
                    continue;
                }

                if (before.Enabled != after.Enabled)
                    changes.Add($"{moduleName}: {(after.Enabled ? (UiText.Language == "es" ? "activado" : "enabled") : (UiText.Language == "es" ? "desactivado" : "bypassed"))}");

                var algorithm = catalog.Find(module, after.Code);
                for (int slot = 0; slot < Math.Min(before.Parameters.Length, after.Parameters.Length); slot++)
                {
                    if (BitConverter.SingleToInt32Bits(before.Parameters[slot]) == BitConverter.SingleToInt32Bits(after.Parameters[slot]))
                        continue;
                    var parameter = algorithm?.Parameters.FirstOrDefault(item => item.Slot == slot);
                    string parameterName = parameter?.Name ?? $"P{slot}";
                    string oldValue = parameter?.Display(before.Parameters[slot], before)
                        ?? before.Parameters[slot].ToString("G", CultureInfo.InvariantCulture);
                    string newValue = parameter?.Display(after.Parameters[slot], after)
                        ?? after.Parameters[slot].ToString("G", CultureInfo.InvariantCulture);
                    changes.Add($"{moduleName} · {parameterName}: {oldValue} → {newValue}");
                }
            }

            foreach (byte slot in source.Controls.Select(control => control.Slot).Union(result.Controls.Select(control => control.Slot)).Distinct().Order())
            {
                uint beforeMask = source.Controls.FirstOrDefault(control => control.Slot == slot).Mask;
                uint afterMask = result.Controls.FirstOrDefault(control => control.Slot == slot).Mask;
                if (beforeMask != afterMask)
                    changes.Add($"CTRL {slot + 1}: 0x{beforeMask:X3} → 0x{afterMask:X3}");
            }

            string DescribeExpression(PresetDocument preset, Assignment? assignment)
            {
                if (assignment is null || assignment.Module == 255)
                    return "OFF";
                if (assignment.Module >= PresetDocument.Modules.Length)
                    return $"Module {assignment.Module} · P{assignment.Parameter} ({assignment.End0:G} → {assignment.End1:G})";
                var effect = preset.GetEffect(assignment.Module);
                var algorithm = catalog.Find(assignment.Module, effect.Code);
                string modelName = algorithm?.Name ?? $"0x{effect.Code:X8}";
                string parameterName = algorithm?.Parameters.FirstOrDefault(item => item.Slot == assignment.Parameter)?.Name
                    ?? $"P{assignment.Parameter}";
                return $"{modelName} · {parameterName} ({assignment.End0:G} → {assignment.End1:G})";
            }

            foreach (byte slot in source.Expression.Select(assignment => assignment.Slot)
                         .Union(result.Expression.Select(assignment => assignment.Slot)).Distinct().Order())
            {
                var before = source.Expression.FirstOrDefault(assignment => assignment.Slot == slot);
                var after = result.Expression.FirstOrDefault(assignment => assignment.Slot == slot);
                if (before != after)
                {
                    string label = UiText.Language == "es" ? $"Expresión {slot + 1}" : $"Expression {slot + 1}";
                    changes.Add($"{label}: {DescribeExpression(source, before)} → {DescribeExpression(result, after)}");
                }
            }

            if (source.Notes != result.Notes)
                changes.Add(UiText.Language == "es" ? "Notas de preset: actualizadas" : "Preset notes: changed");

            if (changes.Count == 0)
                changes.Add(UiText.Language == "es"
                    ? "No se detectaron cambios funcionales frente al preset de origen."
                    : "No functional changes were detected from the source preset.");

            foreach (string change in changes)
                resChanges.Children.Add(new TextBlock
                {
                    Text = "• " + change,
                    Foreground = BrushHex("#CBD5E1"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                });
        }
        if (resSources.Children.Count == 0)
        {
            resSources.Children.Add(new TextBlock
            {
                Text = UiText.Language == "es"
                    ? "El proveedor no devolvió fuentes web citables para esta propuesta."
                    : "The provider returned no citable web sources for this proposal.",
                Foreground = BrushHex("#94A3B8"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        resChainFlow.Children.Clear();
        foreach (int m in p.Document.Chain)
        {
            var eff = p.Document.GetEffect(m);
            var algo = catalog.Find(m, eff.Code);
            var b = new Border
            {
                Background = eff.Enabled ? BrushHex("#0284C7") : BrushHex("#334155"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 3, 5, 3),
                Margin = new Thickness(0, 0, 4, 4)
            };
            b.Child = new TextBlock
            {
                Text = $"{PresetDocument.Modules[m]}: {(algo?.Name ?? "—")}",
                FontSize = 10,
                Foreground = eff.Enabled ? Brushes.White : BrushHex("#94A3B8"),
                FontWeight = eff.Enabled ? FontWeights.Bold : FontWeights.Normal
            };
            resChainFlow.Children.Add(b);
        }

        // CTRL summary
        UpdateCtrlMatrixUi(p.Document);

        // 2. Volume tab
        volCurrentLufs.Text = $"Volumen asignado: {p.Document.Volume}";
        refineTargetInfo.Text = UiText.Language == "es"
            ? $"Preset para refinar: {p.Name}"
            : $"Preset to refine: {p.Name}";
    }

    async Task PollPedalAsync()
    {
        if (pollingPedal || injectingPreset || listenToPedalCheck.IsChecked != true
            || !DeviceSession.Current.Connected
            || myPresetsList.SelectedIndex < 0 || myPresetsList.SelectedIndex >= generated.Count)
            return;

        pollingPedal = true;
        try
        {
            int selectedIndex = myPresetsList.SelectedIndex;
            int slot = injectionSlotCombo.SelectedIndex;
            var actual = await new BankService().ActiveAsync();
            if (actual.Index != slot || selectedIndex != myPresetsList.SelectedIndex)
                return;
            var currentPreset = generated[selectedIndex];
            var expectedRecords = BankService.Replacement(slot, currentPreset.Document)
                .Where(record => record.Type != 1)
                .ToArray();
            var expected = PresetDocument.FromSnapshot(expectedRecords, DeviceSession.Current.Format);
            if (BankService.Equivalent(expected, actual))
                return;

            var updated = currentPreset with { Document = actual };
            generated[selectedIndex] = updated;
            UpdatePresetDetails(updated);
            injectionStatus.Text = UiText.Language == "es"
                ? "✓ Cambios detectados desde la pedalera y reflejados en el preset seleccionado."
                : "✓ Pedal changes detected and reflected in the selected preset.";
            injectionStatus.Foreground = BrushHex("#10B981");
        }
        catch (Exception error)
        {
            injectionStatus.Text = (UiText.Language == "es" ? "No se pudieron leer los cambios del pedal: " : "Could not read pedal changes: ") + error.Message;
            injectionStatus.Foreground = BrushHex("#EF4444");
        }
        finally
        {
            pollingPedal = false;
        }
    }

    async Task InjectSelectedPresetAsync(bool preAuthorized = false)
    {
        if (myPresetsList.SelectedIndex < 0 || myPresetsList.SelectedIndex >= generated.Count || injectingPreset)
            return;
        if (!DeviceSession.Current.Connected)
        {
            injectionStatus.Text = UiText.Language == "es" ? "Conecta la GP-200 antes de inyectar." : "Connect the GP-200 before injecting.";
            injectionStatus.Foreground = BrushHex("#F59E0B");
            return;
        }

        int slot = injectionSlotCombo.SelectedIndex >= 0 ? injectionSlotCombo.SelectedIndex : 231;
        var preset = generated[myPresetsList.SelectedIndex];
        if (!preAuthorized && UiDialogs.Show(this,
            UiText.Language == "es"
                ? $"Se reemplazará {BankService.Position(slot)} para probar '{preset.Name}'. El programa hará una copia previa y verificará la escritura. ¿Continuar?"
                : $"Slot {BankService.Position(slot)} will be replaced to test '{preset.Name}'. A backup will be made and the write verified. Continue?",
            UiText.Language == "es" ? "Inyectar preset en el pedal" : "Inject preset into pedal",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        injectingPreset = true;
        myPresetsList.IsEnabled = false;
        injectSelectedBtn.IsEnabled = false;
        try
        {
            var bank = new BankService();
            if (CatalogRuntime.Current.IsMx42)
            {
                if (preset.SceneBundle?.Mx42 == null)
                    throw new NotSupportedException("Writing a non-Scene AI draft to Custom firmware is not supported by the reviewed native transaction.");
                await bank.ImportSceneAsync(slot, preset.SceneBundle);
            }
            else
                await bank.ReplaceAsync(slot, preset.Document);
            await bank.SelectAsync(slot);
            injectionStatus.Text = UiText.Language == "es"
                ? $"✓ Inyectado y seleccionado en {BankService.Position(slot)}. Backup previo y lectura de retorno confirmados."
                : $"✓ Injected and selected in {BankService.Position(slot)}. Pre-write backup and readback confirmed.";
            injectionStatus.Foreground = BrushHex("#10B981");
            pedalResponseLog.Text += $"\n[{DateTime.Now:HH:mm:ss}] Preset '{preset.Name}' verificado en {BankService.Position(slot)}.";
        }
        catch (Exception error)
        {
            injectionStatus.Text = (UiText.Language == "es" ? "No se pudo inyectar: " : "Injection failed: ") + error.Message;
            injectionStatus.Foreground = BrushHex("#EF4444");
            pedalResponseLog.Text += $"\n[ERROR] {error.Message}";
        }
        finally
        {
            injectingPreset = false;
            myPresetsList.IsEnabled = true;
            injectSelectedBtn.IsEnabled = true;
        }
    }

    void AddSelectedPresetToEditor()
    {
        if (myPresetsList.SelectedIndex < 0 || myPresetsList.SelectedIndex >= generated.Count)
            return;
        addPreset(generated[myPresetsList.SelectedIndex]);
        injectionStatus.Text = UiText.Language == "es" ? "Preset añadido al editor." : "Preset added to the editor.";
        injectionStatus.Foreground = BrushHex("#10B981");
    }

    void OpenVolumeMatcher(bool selectedOnly = false)
    {
        IReadOnlyList<PresetDocument> presets;
        if (selectedOnly && myPresetsList.SelectedIndex is >= 0 && myPresetsList.SelectedIndex < generated.Count)
            presets = [generated[myPresetsList.SelectedIndex].Document];
        else
            presets = selectedForSetList.Order().Where(index => index >= 0 && index < generated.Count)
                .Select(index => generated[index].Document).ToArray();

        if (presets.Count == 0 && GetRefineTarget() is { } currentPreset)
            presets = [currentPreset];
        if (presets.Count == 0)
        {
            UiDialogs.Show(this, UiText.Language == "es" ? "Genera o selecciona presets antes de nivelar." : "Generate or select presets before matching volumes.", "Volume");
            return;
        }
        openVolumeMatcher(presets, this);
    }

    void UpdateCtrlMatrixUi(PresetDocument doc)
    {
        updatingUi = true;
        try
        {
            var ctrlList = doc.Controls;
            var summaryParts = new List<string>();

            for (int r = 0; r < 8; r++)
            {
                uint mask = 0;
                var match = ctrlList.FirstOrDefault(c => c.Slot == r);
                if (match != default) mask = match.Mask;

                var modsForSlot = new List<string>();
                for (int c = 0; c < 11; c++)
                {
                    bool isChecked = (mask & (1u << c)) != 0;
                    int row = r + 1;
                    int col = c + 1;
                    var cb = ctrlMatrixGrid.Children.OfType<CheckBox>().FirstOrDefault(x => Grid.GetRow(x) == row && Grid.GetColumn(x) == col);
                    if (cb != null) cb.IsChecked = isChecked;
                    if (isChecked) modsForSlot.Add(PresetDocument.Modules[c]);
                }

                if (modsForSlot.Count > 0)
                    summaryParts.Add($"CTRL {r + 1}: {string.Join(", ", modsForSlot)}");
            }

            string sum = summaryParts.Count > 0 ? string.Join(" · ", summaryParts) : "Sin módulos asignados a CTRL";
            resCtrlSummary.Text = sum;
            ctrlLiveSummary.Text = sum;
        }
        finally
        {
            updatingUi = false;
        }
    }

    void OnCtrlCellToggled(int slot, int module, bool isChecked)
    {
        if (updatingUi || myPresetsList.SelectedIndex < 0 || myPresetsList.SelectedIndex >= generated.Count) return;
        var p = generated[myPresetsList.SelectedIndex];
        var ctrlList = p.Document.Controls;
        uint mask = 0;
        var match = ctrlList.FirstOrDefault(c => c.Slot == slot);
        if (match != default) mask = match.Mask;

        if (isChecked) mask |= (1u << module);
        else mask &= ~(1u << module);

        p.Document.SetCtrl((byte)slot, mask);
        UpdateCtrlMatrixUi(p.Document);
    }

    async Task SendCtrlAssignmentsToPedalAsync()
    {
        if (myPresetsList.SelectedIndex < 0 || myPresetsList.SelectedIndex >= generated.Count) return;
        var p = generated[myPresetsList.SelectedIndex];
        int slot = pedalTargetSlotCombo.SelectedIndex >= 0 ? pedalTargetSlotCombo.SelectedIndex : p.Document.Index;
        await SendPresetToSlotAsync(p, slot);
    }

    async Task SendSelectedToTargetSlotAsync()
    {
        if (myPresetsList.SelectedIndex < 0 || myPresetsList.SelectedIndex >= generated.Count) return;
        var p = generated[myPresetsList.SelectedIndex];
        int slot = pedalTargetSlotCombo.SelectedIndex >= 0 ? pedalTargetSlotCombo.SelectedIndex : 231;
        sendToSlotBtn.IsEnabled = false;
        try
        {
            await SendPresetToSlotAsync(p, slot);
        }
        catch (Exception ex)
        {
            pedalResponseLog.Text += $"\n[ERROR] {ex.Message}";
        }
        finally
        {
            sendToSlotBtn.IsEnabled = true;
        }
    }

    async Task SendPresetToSlotAsync(AiGeneratedPreset preset, int slot)
    {
        if (!DeviceSession.Current.Connected)
            throw new InvalidOperationException(UiText.Language == "es" ? "Conecta la GP-200 antes de escribir." : "Connect the GP-200 before writing.");
        if (slot is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (UiDialogs.Show(this,
            UiText.Language == "es"
                ? $"Se reemplazará {BankService.Position(slot)} con '{preset.Name}'. Habrá backup previo y lectura de retorno. ¿Continuar?"
                : $"Slot {BankService.Position(slot)} will be replaced with '{preset.Name}'. A pre-write backup and readback will be performed. Continue?",
            UiText.Language == "es" ? "Enviar preset al pedal" : "Send preset to pedal",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var bank = new BankService();
        if (CatalogRuntime.Current.IsMx42)
        {
            if (preset.SceneBundle?.Mx42 == null)
                throw new NotSupportedException("This AI draft has no reviewed Scene transaction for Custom firmware.");
            await bank.ImportSceneAsync(slot, preset.SceneBundle);
        }
        else
            await bank.ReplaceAsync(slot, preset.Document);
        await bank.SelectAsync(slot);
        pedalResponseLog.Text += $"\n[{DateTime.Now:HH:mm:ss}] ✓ '{preset.Name}' verificado en {BankService.Position(slot)}.";
        UiDialogs.Show(this, $"✓ Preset guardado en slot {BankService.Position(slot)}.", "GP-200");
    }

    async Task ReadActiveFromPedalAsync()
    {
        if (!DeviceSession.Current.Connected)
        {
            pedalTabStatus.Text = UiText.Language == "es" ? "○ GP-200 Desconectada" : "○ GP-200 Disconnected";
            pedalTabStatus.Foreground = BrushHex("#F59E0B");
            return;
        }
        try
        {
            var bank = new BankService();
            var act = await bank.ActiveAsync();
            pedalReadPreset = act;
            pedalTabStatus.Text = UiText.Language == "es"
                ? $"✓ Leído [{BankService.Position(act.Index)}] {act.Name}"
                : $"✓ Read [{BankService.Position(act.Index)}] {act.Name}";
            pedalTabStatus.Foreground = BrushHex("#10B981");
            refineTargetInfo.Text = UiText.Language == "es"
                ? $"Preset leído del pedal: [{BankService.Position(act.Index)}] {act.Name}"
                : $"Preset read from pedal: [{BankService.Position(act.Index)}] {act.Name}";
            pedalResponseLog.Text += $"\n[{DateTime.Now:HH:mm:ss}] Leído patch activo: [{BankService.Position(act.Index)}] {act.Name}";
        }
        catch (Exception ex)
        {
            pedalResponseLog.Text += $"\n[ERROR] {ex.Message}";
        }
    }

    void RefreshSetListUi()
    {
        setListTable.Children.Clear();
        setListTargetCombos.Clear();

        for (int i = 0; i < generated.Count; i++)
        {
            var p = generated[i];
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var include = new CheckBox
            {
                IsChecked = selectedForSetList.Contains(i),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = UiText.Language == "es" ? "Incluir en subida/exportación" : "Include in upload/export"
            };
            int presetIndex = i;
            include.Checked += (_, _) => selectedForSetList.Add(presetIndex);
            include.Unchecked += (_, _) => selectedForSetList.Remove(presetIndex);
            row.Children.Add(include);

            var fav = new TextBlock
            {
                Text = favoriteIndices.Contains(i) ? "♥" : " ",
                Foreground = BrushHex("#EF4444"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(fav, 1);
            row.Children.Add(fav);

            var nameTxt = new TextBlock { Text = p.Name, Foreground = BrushHex("#F8FAFC"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameTxt, 2);
            row.Children.Add(nameTxt);

            var secTxt = new TextBlock { Text = p.Section ?? "Tone", Foreground = BrushHex("#38BDF8"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(secTxt, 3);
            row.Children.Add(secTxt);

            var combo = new ComboBox { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            for (int s = 0; s < 256; s++) combo.Items.Add(BankService.Position(s));
            int assignedSlot = setListSlotAssignments.TryGetValue(i, out int configured)
                ? configured
                : Math.Clamp(setListStartSlot.SelectedIndex + i, 0, 255);
            combo.SelectedIndex = assignedSlot;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex >= 0)
                    setListSlotAssignments[presetIndex] = combo.SelectedIndex;
            };
            setListTargetCombos.Add(combo);
            Grid.SetColumn(combo, 4);
            row.Children.Add(combo);

            setListTable.Children.Add(row);
        }
    }

    void AutoAssignSetListSlots()
    {
        int start = setListStartSlot.SelectedIndex >= 0 ? setListStartSlot.SelectedIndex : 236;
        for (int i = 0; i < setListTargetCombos.Count; i++)
        {
            int slot = Math.Clamp(start + i, 0, 255);
            setListSlotAssignments[i] = slot;
            setListTargetCombos[i].SelectedIndex = slot;
        }
    }

    async Task UploadSetListToPedalAsync()
    {
        int[] selected = selectedForSetList.Order().Where(index => index >= 0 && index < generated.Count).ToArray();
        if (selected.Length == 0 || !DeviceSession.Current.Connected) return;
        int[] slots = selected.Select(index => setListTargetCombos[index].SelectedIndex).ToArray();
        if (slots.Any(slot => slot is < 0 or > 255) || slots.Distinct().Count() != slots.Length)
        {
            UiDialogs.Show(this, UiText.Language == "es"
                ? "Cada preset seleccionado debe tener un slot distinto válido."
                : "Every selected preset must have a distinct, valid target slot.", "Set List");
            return;
        }
        if (CatalogRuntime.Current.IsMx42 && selected.Any(index => generated[index].SceneBundle?.Mx42 == null))
        {
            UiDialogs.Show(this,
                UiText.Language == "es"
                    ? "El perfil Custom solo permite la subida aquí de presets con un paquete de escenas revisado. No se envió ningún preset."
                    : "This Custom-profile upload only supports presets with a reviewed Scene bundle. Nothing was written.",
                "Set List");
            return;
        }
        string positions = string.Join(", ", slots.Select(BankService.Position));
        if (UiDialogs.Show(this,
            UiText.Language == "es"
                ? $"Se reemplazarán {selected.Length} slots ({positions}). Se crearán backups y se verificará cada escritura. ¿Continuar?"
                : $"{selected.Length} slots will be replaced ({positions}). Backups will be created and each write verified. Continue?",
            UiText.Language == "es" ? "Subir set list" : "Upload set list",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        uploadAllToPedalBtn.IsEnabled = false;
        var bank = new BankService();
        int uploaded = 0;
        try
        {
            for (int i = 0; i < selected.Length; i++)
            {
                int index = selected[i];
                int targetSlot = setListTargetCombos[index].SelectedIndex;
                var preset = generated[index];
                if (CatalogRuntime.Current.IsMx42)
                {
                    if (preset.SceneBundle?.Mx42 == null)
                        throw new NotSupportedException($"'{preset.Name}' is not a Scene bundle and cannot be written through the reviewed Custom transaction.");
                    await bank.ImportSceneAsync(targetSlot, preset.SceneBundle);
                }
                else
                    await bank.ReplaceAsync(targetSlot, preset.Document);
                uploaded++;
            }
            UiDialogs.Show(this, $"✓ {uploaded} presets subidos y verificados con éxito en la GP-200.", "Set List");
        }
        catch (Exception ex)
        {
            UiDialogs.Show(this, "Error al subir set list: " + ex.Message, "Error");
        }
        finally
        {
            uploadAllToPedalBtn.IsEnabled = true;
        }
    }

    void ExportSetListZip()
    {
        int[] selected = selectedForSetList.Order().Where(index => index >= 0 && index < generated.Count).ToArray();
        if (selected.Length == 0) return;
        int[] slots = selected.Select(index => index < setListTargetCombos.Count ? setListTargetCombos[index].SelectedIndex : -1).ToArray();
        if (slots.Any(slot => slot is < 0 or > 255) || slots.Distinct().Count() != slots.Length)
        {
            UiDialogs.Show(this, UiText.Language == "es"
                ? "Cada preset seleccionado debe tener un slot distinto válido."
                : "Every selected preset must have a distinct, valid target slot.", "Set List");
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "ZIP Files (*.zip)|*.zip",
            FileName = $"GP200_SetList_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            DefaultExt = ".zip"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            using var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create);
            for (int i = 0; i < selected.Length; i++)
            {
                int index = selected[i];
                var p = generated[index];
                int targetSlot = slots[i];
                string safeName = string.Concat(p.Name.Select(character =>
                    char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')).Trim('_');
                if (string.IsNullOrWhiteSpace(safeName)) safeName = "Preset";
                bool isSceneBundle = p.SceneBundle?.Mx42 != null;
                string filename = $"{BankService.Position(targetSlot)}_{safeName}{(isSceneBundle ? ".gpscene" : ".prst")}";
                var entry = zip.CreateEntry(filename);
                using var stream = entry.Open();
                if (isSceneBundle)
                {
                    p.SceneBundle!.Validate();
                    JsonSerializer.Serialize(stream, p.SceneBundle, new JsonSerializerOptions { WriteIndented = true });
                }
                else
                {
                    byte[] bytes = p.Document.ToBytes();
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            UiDialogs.Show(this, "✓ Set List exportado como ZIP con éxito.", "Exportar ZIP");
        }
        catch (Exception ex)
        {
            UiDialogs.Show(this, "Error al exportar ZIP: " + ex.Message, "Error");
        }
    }

    void BrowseAudioFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio Files (*.mp3;*.wav;*.m4a;*.mp4;*.aac)|*.mp3;*.wav;*.m4a;*.mp4;*.aac|All Files (*.*)|*.*",
            Title = "Seleccionar audio de referencia"
        };
        if (dlg.ShowDialog(this) != true) return;

        audioFileBox.Text = dlg.FileName;
        loadedAudioBytes = null;
        loadedAudioMime = null;
        loadedAudioReport = null;
        sendAudioToAiCheck.IsChecked = false;
        sendAudioToAiCheck.IsEnabled = false;

        string extension = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        string? mimeType = extension switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            _ => null
        };
        var status = new List<string>();
        try
        {
            loadedAudioReport = AudioToneAnalyzer.AnalyzeFile(dlg.FileName, 30);
            status.Add(loadedAudioReport.Summary);
        }
        catch (Exception ex)
        {
            status.Add((UiText.Language == "es" ? "El análisis local falló: " : "Local analysis failed: ") + ex.Message);
        }

        if (extension == ".mp4")
        {
            status.Add(UiText.Language == "es"
                ? "No se enviarán contenedores de vídeo a proveedores externos."
                : "Video containers are never sent to external providers.");
        }
        else if (mimeType == null)
        {
            status.Add(UiText.Language == "es"
                ? "Este formato puede analizarse localmente, pero no se admite para envío externo."
                : "This format may be analyzed locally but is not supported for external upload.");
        }
        else
        {
            try
            {
                var file = new FileInfo(dlg.FileName);
                if (file.Length > 8 * 1024 * 1024)
                {
                    status.Add(UiText.Language == "es"
                        ? "El archivo supera el límite de envío de 8 MiB; el análisis local sigue disponible."
                        : "The file exceeds the 8 MiB upload limit; local analysis remains available.");
                }
                else if (file.Length == 0)
                {
                    status.Add(UiText.Language == "es" ? "El archivo está vacío." : "The file is empty.");
                }
                else
                {
                    loadedAudioBytes = File.ReadAllBytes(dlg.FileName);
                    loadedAudioMime = mimeType;
                    sendAudioToAiCheck.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                status.Add((UiText.Language == "es" ? "No se pudo leer el archivo para enviarlo: " : "Could not read the file for upload: ") + ex.Message);
            }
        }

        audioReportDisplay.Text = string.Join(Environment.NewLine, status);
    }

    Button Chip(string label, string fullText)
    {
        var b = new Button
        {
            Content = label,
            Background = BrushHex("#1E293B"),
            Foreground = BrushHex("#38BDF8"),
            BorderBrush = BrushHex("#334155"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 4),
            Padding = new Thickness(6, 3, 6, 3),
            FontSize = 11
        };
        b.Click += (_, _) => requestText.Text = fullText;
        return b;
    }

    Button RefineChip(string label, string fullText)
    {
        var b = new Button
        {
            Content = label,
            Background = BrushHex("#1E293B"),
            Foreground = BrushHex("#38BDF8"),
            BorderBrush = BrushHex("#334155"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 4),
            Padding = new Thickness(6, 3, 6, 3),
            FontSize = 11
        };
        b.Click += (_, _) => refineRequestText.Text = fullText;
        return b;
    }

    static ScrollViewer ScrollTab(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        CanContentScroll = false,
        Background = Brushes.Transparent
    };

    static Style CreateStudioTabItemStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 5, 9, 5)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(2, 0, 2, 0)));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 32d));
        style.Setters.Add(new Setter(Control.BackgroundProperty, BrushHex("#1E293B")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, BrushHex("#CBD5E1")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BrushHex("#334155")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        var template=new ControlTemplate(typeof(TabItem));
        var chrome=new FrameworkElementFactory(typeof(Border));
        chrome.Name="TabChrome";
        chrome.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        chrome.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
        chrome.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
        chrome.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        chrome.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
        var header=new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentSourceProperty,"Header");
        header.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        header.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        header.SetValue(TextElement.ForegroundProperty,new TemplateBindingExtension(Control.ForegroundProperty));
        chrome.AppendChild(header);
        template.VisualTree=chrome;
        var selectedChrome=new Trigger{Property=TabItem.IsSelectedProperty,Value=true};
        selectedChrome.Setters.Add(new Setter(Border.BackgroundProperty,BrushHex("#2563EB"),"TabChrome"));
        selectedChrome.Setters.Add(new Setter(Border.BorderBrushProperty,BrushHex("#38BDF8"),"TabChrome"));
        selectedChrome.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));
        template.Triggers.Add(selectedChrome);
        var hoverChrome=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};
        hoverChrome.Setters.Add(new Setter(Border.BackgroundProperty,BrushHex("#334155"),"TabChrome"));
        template.Triggers.Add(hoverChrome);
        style.Setters.Add(new Setter(Control.TemplateProperty,template));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, BrushHex("#334155")));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(hover);

        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, BrushHex("#2563EB")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, BrushHex("#38BDF8")));
        style.Triggers.Add(selected);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, BrushHex("#64748B")));
        disabled.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Arrow));
        style.Triggers.Add(disabled);
        return style;
    }

    static Border Card(string title)
    {
        var b = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var cardGrid = new Grid();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#38BDF8"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        cardGrid.Children.Add(header);
        var content = new ContentControl();
        Grid.SetRow(content, 1);
        cardGrid.Children.Add(content);
        b.Child = cardGrid;
        b.Tag = content;
        return b;
    }

    static void SetCardContent(Border card, UIElement content)
    {
        if (card.Tag is not ContentControl contentHost)
            throw new InvalidOperationException("The card content host is missing.");
        contentHost.Content = content;
    }

    static Brush BrushHex(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}
