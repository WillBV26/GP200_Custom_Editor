using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gp200.Replica;

internal sealed class FootswitchSettingsWindow : Window
{
    readonly ComboBox activeModeCombo = new() { MinWidth = 190 };
    readonly TabControl modeTabs = new();
    readonly TextBlock status = new() { Foreground = BrushHex("#00E5FF"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    DeviceSettings? currentSettings;
    bool updatingUi;
    int pageMode = 0; // 0 = Single FS, 1 = Dual FS

    // Colores oficiales de anillo LED de la GP-200R
    private static readonly Color[] LedRingColors =
    [
        Color.FromRgb(239, 68, 68),   // Rojo (Red)
        Color.FromRgb(59, 130, 246),  // Azul (Blue)
        Color.FromRgb(16, 185, 129),  // Verde (Green)
        Color.FromRgb(245, 158, 11),  // Amarillo (Yellow)
        Color.FromRgb(0, 229, 255),   // Cian (Cyan)
        Color.FromRgb(248, 250, 252), // Blanco (White)
        Color.FromRgb(71, 85, 105)    // Apagado / Off
    ];

    internal FootswitchSettingsWindow()
    {
        EditorWindow.StyleDialog(this);
        Title = UiText.Language == "es" ? "Configuración Global de Footswitches (4 Modos) · GP-200" : "Global Footswitch Settings (4 Modes) · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 920;
        Height = 700;
        MinWidth = 800;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(16) };

        // Bottom status & Close button
        var bottomBar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bottomBar, Dock.Bottom);

        var closeBtn = new Button
        {
            Content = UiText.Language == "es" ? "Cerrar" : "Close",
            MinWidth = 90,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = BrushHex("#142036"),
            Foreground = BrushHex("#F8FAFC"),
            BorderBrush = BrushHex("#3300E5FF")
        };
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        bottomBar.Children.Add(closeBtn);
        bottomBar.Children.Add(status);
        root.Children.Add(bottomBar);

        // Top Toolbar
        var topBar = new Border
        {
            Background = BrushHex("#0D1524"),
            BorderBrush = BrushHex("#3300E5FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(topBar, Dock.Top);

        var topGrid = new Grid();
        topGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        topGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var modeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        modeStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Modo activo del pedal:" : "Active pedal mode:",
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#F8FAFC"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        // 4 Modos Oficiales de la GP-200 / GP-200R: Patch, Stomp, Scenes, User
        activeModeCombo.Items.Add(UiText.Language == "es" ? "Modo Patch (Presets)" : "Patch Mode (Presets)");
        activeModeCombo.Items.Add(UiText.Language == "es" ? "Modo Stomp (CTRL 1-8)" : "Stomp Mode (CTRL 1-8)");
        activeModeCombo.Items.Add(UiText.Language == "es" ? "Modo Scenes (Escenas A-D)" : "Scenes Mode (Scenes A-D)");
        activeModeCombo.Items.Add(UiText.Language == "es" ? "Modo User (Personalizado)" : "User Mode (Custom)");
        activeModeCombo.SelectedIndex = 0;
        activeModeCombo.SelectionChanged += async (_, _) =>
        {
            if (updatingUi || activeModeCombo.SelectedIndex < 0) return;
            await ChangeActivePedalMode(activeModeCombo.SelectedIndex);
        };
        modeStack.Children.Add(activeModeCombo);
        Grid.SetRow(modeStack, 0);
        topGrid.Children.Add(modeStack);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin=new Thickness(0,8,0,0) };
        var readBtn = ActionBtn(UiText.Language == "es" ? "📥 Leer del pedal" : "📥 Read from pedal", async () => await ReadSettingsAsync());
        var exportBtn = ActionBtn(UiText.Language == "es" ? "💾 Descargar copia" : "💾 Download backup", ExportSettings);
        var importBtn = ActionBtn(UiText.Language == "es" ? "📂 Restaurar..." : "📂 Restore...", async () => await ImportSettingsAsync());
        var resetBtn = ActionBtn(UiText.Language == "es" ? "🔄 Restablecer a fábrica" : "🔄 Reset to factory", async () => await ResetCurrentModeAsync());

        actions.Children.Add(readBtn);
        actions.Children.Add(exportBtn);
        actions.Children.Add(importBtn);
        actions.Children.Add(resetBtn);
        Grid.SetRow(actions, 1);
        topGrid.Children.Add(actions);

        topBar.Child = topGrid;
        root.Children.Add(topBar);

        // Main TabControl for modes (Patch, Stomp, Scenes, User)
        modeTabs.Background = BrushHex("#070B12");
        modeTabs.BorderThickness = new Thickness(0);
        root.Children.Add(new ScrollViewer
        {
            Content=modeTabs,
            VerticalScrollBarVisibility=ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            CanContentScroll=false,
            PanningMode=PanningMode.Both
        });

        var windowRoot = new Grid();
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var headerBar = new AuraWindowHeaderBar(this, Title, root, "FOOTSWITCH MATRIX");
        Grid.SetRow(headerBar, 0);
        windowRoot.Children.Add(headerBar);
        Grid.SetRow(root, 1);
        windowRoot.Children.Add(root);
        Content = windowRoot;

        Loaded += async (_, _) => await ReadSettingsAsync();
    }

    async Task ReadSettingsAsync()
    {
        if (!DeviceSession.Current.Connected)
        {
            status.Text = UiText.Language == "es"
                ? "⚠️ GP-200 no conectada por USB. Conecta el pedal para leer o escribir footswitches."
                : "⚠️ GP-200 not connected via USB.";
            status.Foreground = BrushHex("#F59E0B");
            return;
        }

        status.Text = UiText.Language == "es" ? "Leyendo configuración de footswitches desde la GP-200…" : "Reading footswitch configuration from GP-200…";
        status.Foreground = BrushHex("#00E5FF");

        try
        {
            currentSettings = await DeviceSettings.ReadAsync();
            updatingUi = true;
            try
            {
                int currentMode = currentSettings.Global(24); // 0=Patch, 1=Stomp, 2=Scenes, 3=User
                activeModeCombo.SelectedIndex = Math.Clamp(currentMode, 0, 3);
                PopulateModeTabs();
            }
            finally
            {
                updatingUi = false;
            }
            status.Text = UiText.Language == "es" ? "✓ Asignaciones leídas de la GP-200 correctamente." : "✓ Footswitch assignments loaded from GP-200.";
            status.Foreground = BrushHex("#10B981");
        }
        catch (Exception ex)
        {
            status.Text = (UiText.Language == "es" ? "Error al leer GP-200: " : "Error reading GP-200: ") + ex.Message;
            status.Foreground = BrushHex("#EF4444");
        }
    }

    void PopulateModeTabs()
    {
        modeTabs.Items.Clear();
        string[] modeNames = UiText.Language == "es"
            ? ["Modo Patch (Presets)", "Modo Stomp (CTRL 1-8)", "Modo Scenes (Escenas A-D)", "Modo User (Personalizado)"]
            : ["Patch Mode", "Stomp Mode", "Scenes Mode", "User Mode"];

        for (int m = 0; m < 4; m++)
        {
            var tab = new TabItem
            {
                Header = modeNames[m],
                Foreground = BrushHex("#F8FAFC"),
                Background = BrushHex("#0D1524"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            tab.Content = BuildPhysicalModeContent(m);
            modeTabs.Items.Add(tab);
        }
        modeTabs.SelectedIndex = Math.Clamp(activeModeCombo.SelectedIndex, 0, 3);
    }

    UIElement BuildPhysicalModeContent(int templateIndex)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, CanContentScroll=false, PanningMode=PanningMode.Both };
        var mainGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 240 });
        var splitter=new GridSplitter
        {
            Width=12,
            HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Stretch,
            ResizeDirection=GridResizeDirection.Columns,
            ResizeBehavior=GridResizeBehavior.PreviousAndNext,
            ShowsPreview=true,
            Background=BrushHex("#24374A"),
            Cursor=System.Windows.Input.Cursors.SizeWE,
            ToolTip=UiText.Language=="es"?"Arrastra para cambiar el ancho de las columnas":"Drag to resize the columns"
        };
        ToolTipService.SetShowOnDisabled(splitter,true);
        Grid.SetColumn(splitter,1);
        mainGrid.Children.Add(splitter);

        // Left Panel: Physical 2x4 Layout (FS 1-4 top, FS 5-8 bottom)
        var leftCard = new Border
        {
            Background = BrushHex("#0D1524"),
            BorderBrush = BrushHex("#3300E5FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        var leftStack = new StackPanel();

        // Header
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var title = new TextBlock
        {
            Text = UiText.Language == "es" ? "DISPOSICIÓN FÍSICA DE PULSADORES (PANTALLA GP-200R)" : "PHYSICAL FOOTSWITCH LAYOUT (GP-200R SCREEN)",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#00E5FF")
        };
        titleRow.Children.Add(title);
        leftStack.Children.Add(titleRow);

        // 2x4 Grid for FS 1-4 and FS 5-8
        var fsGrid = new Grid();
        for (int c = 0; c < 4; c++) fsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: FS 1 - FS 4
        fsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: FS 5 - FS 8

        for (int i = 0; i < 8; i++)
        {
            int fsNum = i + 1;
            int row = i < 4 ? 0 : 1;
            int col = i % 4;

            int tapSlot = i * 2;
            int holdSlot = i * 2 + 1;

            int currentTap = currentSettings?.Footswitch(Math.Min(templateIndex, 2), tapSlot) ?? 0;
            int currentHold = currentSettings?.Footswitch(Math.Min(templateIndex, 2), holdSlot) ?? 0;

            // En modo Scenes, asignar por defecto Scene A-D a FS 1-4
            if (templateIndex == 2 && currentTap == 0)
            {
                currentTap = i < 4 ? (i + 1) : 0;
            }

            var card = BuildFootswitchCard(fsNum, templateIndex, tapSlot, holdSlot, currentTap, currentHold, i);
            Grid.SetRow(card, row);
            Grid.SetColumn(card, col);
            fsGrid.Children.Add(card);
        }
        leftStack.Children.Add(fsGrid);

        // Page Selector [ Single FS / Dual FS ]
        var pageBorder = new Border
        {
            Background = BrushHex("#142036"),
            BorderBrush = BrushHex("#3300E5FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 14, 0, 0)
        };
        var pageStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        pageStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Página activa (Page): " : "Active Page: ",
            Foreground = BrushHex("#94A3B8"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        var singleBtn = new RadioButton
        {
            Content = "Single FS (FS 1-4)",
            IsChecked = pageMode == 0,
            Foreground = BrushHex("#F8FAFC"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        singleBtn.Checked += (_, _) => { pageMode = 0; status.Text = "Page: Single FS (FS 1-4)"; };

        var dualBtn = new RadioButton
        {
            Content = "Dual FS (FS 1-8)",
            IsChecked = pageMode == 1,
            Foreground = BrushHex("#F8FAFC"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        dualBtn.Checked += (_, _) => { pageMode = 1; status.Text = "Page: Dual FS (FS 1-8)"; };

        pageStack.Children.Add(singleBtn);
        pageStack.Children.Add(dualBtn);
        pageBorder.Child = pageStack;
        leftStack.Children.Add(pageBorder);

        leftCard.Child = leftStack;
        Grid.SetColumn(leftCard, 0);
        mainGrid.Children.Add(leftCard);

        // Right Panel: 2-Footswitch Combinations
        var rightCard = new Border
        {
            Background = BrushHex("#0D1524"),
            BorderBrush = BrushHex("#3300E5FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        var rightStack = new StackPanel();
        rightStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "COMBINACIONES DE 2 PULSADORES" : "2-FOOTSWITCH COMBINATIONS",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#00E5FF"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        string[] comboNames = ["FS1 + FS5", "FS2 + FS6", "FS3 + FS7", "FS4 + FS8"];
        for (int c = 0; c < comboNames.Length; c++)
        {
            int slot = 16 + c;
            int currentAction = currentSettings?.Footswitch(Math.Min(templateIndex, 2), slot) ?? 0;

            var cRow = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            cRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            cRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = comboNames[c],
                Foreground = BrushHex("#F8FAFC"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            cRow.Children.Add(label);

            var actionCombo = new ComboBox
            {
                ItemsSource = DeviceSettings.Actions,
                SelectedIndex = Math.Clamp(currentAction, 0, DeviceSettings.Actions.Length - 1),
                FontSize = 11
            };
            int capturedSlot = slot;
            string capturedName = comboNames[c];
            actionCombo.SelectionChanged += async (_, _) =>
            {
                if (updatingUi || actionCombo.SelectedIndex < 0) return;
                await ApplyFootswitchChange(Math.Min(templateIndex, 2), capturedSlot, actionCombo.SelectedIndex, capturedName);
            };
            Grid.SetColumn(actionCombo, 1);
            cRow.Children.Add(actionCombo);

            rightStack.Children.Add(cRow);
        }

        var hintBox = new TextBlock
        {
            Text = UiText.Language == "es"
                ? "💡 Cada ajuste se envía vía SysEx a la memoria no volátil del pedal y se mantiene activo al desconectar el cable USB."
                : "💡 All settings are sent via SysEx to pedal EEPROM and persist when unplugging USB.",
            FontSize = 11,
            Foreground = BrushHex("#94A3B8"),
            Margin = new Thickness(0, 16, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        rightStack.Children.Add(hintBox);

        rightCard.Child = rightStack;
        Grid.SetColumn(rightCard, 2);
        mainGrid.Children.Add(rightCard);

        scroll.Content = mainGrid;
        return scroll;
    }

    FrameworkElement BuildFootswitchCard(int fsNum, int templateIndex, int tapSlot, int holdSlot, int currentTap, int currentHold, int ledIndex)
    {
        var card = new Border
        {
            Background = BrushHex("#142036"),
            BorderBrush = BrushHex("#3300E5FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(4)
        };

        var stack = new StackPanel();

        // Top Row: LED Ring + Label
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        Color ledColor = LedRingColors[ledIndex % LedRingColors.Length];

        var ledRing = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = new SolidColorBrush(ledColor),
            Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            StrokeThickness = 1.5,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(ledRing, Dock.Left);
        header.Children.Add(ledRing);

        var numBlock = new TextBlock
        {
            Text = $"FS {fsNum}",
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#00E5FF"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(numBlock);
        stack.Children.Add(header);

        // Tap Combo
        var tapLabel = new TextBlock { Text = UiText.Language == "es" ? "Tap (Pulsación):" : "Tap (Short):", Foreground = BrushHex("#94A3B8"), FontSize = 10, Margin = new Thickness(0, 2, 0, 2) };
        stack.Children.Add(tapLabel);

        var tapCombo = new ComboBox { ItemsSource = DeviceSettings.Actions, SelectedIndex = Math.Clamp(currentTap, 0, DeviceSettings.Actions.Length - 1), FontSize = 11 };
        int capturedTapSlot = tapSlot;
        tapCombo.SelectionChanged += async (_, _) =>
        {
            if (updatingUi || tapCombo.SelectedIndex < 0) return;
            await ApplyFootswitchChange(Math.Min(templateIndex, 2), capturedTapSlot, tapCombo.SelectedIndex, $"FS {fsNum} (TAP)");
        };
        stack.Children.Add(tapCombo);

        // Hold Combo
        var holdLabel = new TextBlock { Text = UiText.Language == "es" ? "Hold (Mantener):" : "Hold (Long):", Foreground = BrushHex("#94A3B8"), FontSize = 10, Margin = new Thickness(0, 6, 0, 2) };
        stack.Children.Add(holdLabel);

        var holdCombo = new ComboBox { ItemsSource = DeviceSettings.Actions, SelectedIndex = Math.Clamp(currentHold, 0, DeviceSettings.Actions.Length - 1), FontSize = 11 };
        int capturedHoldSlot = holdSlot;
        holdCombo.SelectionChanged += async (_, _) =>
        {
            if (updatingUi || holdCombo.SelectedIndex < 0) return;
            await ApplyFootswitchChange(Math.Min(templateIndex, 2), capturedHoldSlot, holdCombo.SelectedIndex, $"FS {fsNum} (HOLD)");
        };
        stack.Children.Add(holdCombo);

        card.Child = stack;
        return card;
    }

    async Task ApplyFootswitchChange(int template, int slot, int action, string label)
    {
        if (!DeviceSession.Current.Connected) return;
        try
        {
            var change = DeviceSettings.SetFootswitch(template, slot, action);
            currentSettings = await DeviceSettings.ApplyAsync(change);
            status.Text = UiText.Language == "es"
                ? $"✓ Asignación guardada: {label} -> {DeviceSettings.Actions[action]}"
                : $"✓ Saved: {label} -> {DeviceSettings.Actions[action]}";
            status.Foreground = BrushHex("#10B981");
        }
        catch (Exception ex)
        {
            status.Text = (UiText.Language == "es" ? "Error al guardar asignación: " : "Error saving assignment: ") + ex.Message;
            status.Foreground = BrushHex("#EF4444");
        }
    }

    async Task ChangeActivePedalMode(int modeIndex)
    {
        if (!DeviceSession.Current.Connected) return;
        try
        {
            var change = DeviceSettings.SetGlobal(24, modeIndex);
            currentSettings = await DeviceSettings.ApplyAsync(change);
            string[] names = ["Patch Mode", "Stomp Mode", "Scenes Mode", "User Mode"];
            status.Text = UiText.Language == "es"
                ? $"✓ Modo activo cambiado a {names[modeIndex]} en la GP-200."
                : $"✓ Active mode switched to {names[modeIndex]} on GP-200.";
            status.Foreground = BrushHex("#10B981");
            modeTabs.SelectedIndex = modeIndex;
        }
        catch (Exception ex)
        {
            status.Text = (UiText.Language == "es" ? "Error al cambiar modo: " : "Error switching mode: ") + ex.Message;
            status.Foreground = BrushHex("#EF4444");
        }
    }

    async Task ResetCurrentModeAsync()
    {
        if (!DeviceSession.Current.Connected) return;
        int mode = Math.Min(modeTabs.SelectedIndex, 2);
        string[] names = ["Patch", "Stomp", "Scenes", "User"];
        string modeName = names[Math.Clamp(modeTabs.SelectedIndex, 0, 3)];

        if (UiDialogs.Show(this,
            UiText.Language == "es" ? $"¿Restablecer asignaciones de fábrica para el modo {modeName}?" : $"Reset factory assignments for {modeName} mode?",
            UiText.Language == "es" ? "Restablecer modo a fábrica" : "Reset mode to factory",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            currentSettings = await DeviceSettings.ResetFootswitchAsync(mode);
            updatingUi = true;
            try
            {
                PopulateModeTabs();
            }
            finally
            {
                updatingUi = false;
            }
            status.Text = UiText.Language == "es" ? $"✓ Modo {modeName} restablecido a valores de fábrica." : $"✓ {modeName} mode reset to factory.";
            status.Foreground = BrushHex("#10B981");
        }
        catch (Exception ex)
        {
            status.Text = (UiText.Language == "es" ? "Error al resetear: " : "Error resetting: ") + ex.Message;
            status.Foreground = BrushHex("#EF4444");
        }
    }

    void ExportSettings()
    {
        if (currentSettings == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            FileName = $"GP200_Footswitches_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var dump = new Dictionary<string, object>();
            for (int t = 0; t < 3; t++)
            {
                var fsList = new List<object>();
                for (int slot = 0; slot < 20; slot++)
                {
                    int a = currentSettings.Footswitch(t, slot);
                    fsList.Add(new { slot, action = a, actionName = a < DeviceSettings.Actions.Length ? DeviceSettings.Actions[a] : "Unknown" });
                }
                dump[$"template_{t}"] = fsList;
            }
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = UiText.Language == "es" ? "✓ Copia guardada en disco." : "✓ Backup saved to disk.";
            status.Foreground = BrushHex("#10B981");
        }
        catch (Exception ex)
        {
            status.Text = "Error al exportar: " + ex.Message;
            status.Foreground = BrushHex("#EF4444");
        }
    }

    async Task ImportSettingsAsync()
    {
        if (!DeviceSession.Current.Connected) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            string json = File.ReadAllText(dlg.FileName);
            using var doc = JsonDocument.Parse(json);
            int applied = 0;

            for (int t = 0; t < 3; t++)
            {
                if (doc.RootElement.TryGetProperty($"template_{t}", out var arr))
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        int slot = item.GetProperty("slot").GetInt32();
                        int action = item.GetProperty("action").GetInt32();
                        var change = DeviceSettings.SetFootswitch(t, slot, action);
                        currentSettings = await DeviceSettings.ApplyAsync(change);
                        applied++;
                    }
                }
            }

            updatingUi = true;
            try
            {
                PopulateModeTabs();
            }
            finally
            {
                updatingUi = false;
            }
            status.Text = UiText.Language == "es" ? $"✓ Se restauraron {applied} asignaciones con éxito." : $"✓ Restored {applied} assignments.";
            status.Foreground = BrushHex("#10B981");
        }
        catch (Exception ex)
        {
            status.Text = "Error al importar: " + ex.Message;
            status.Foreground = BrushHex("#EF4444");
        }
    }

    static Button ActionBtn(string text, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            Background = BrushHex("#0077B6"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    static Brush BrushHex(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}
