using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gp200.Replica;

internal sealed class FootswitchSettingsWindow : Window
{
    readonly ComboBox activeModeCombo = new() { MinWidth = 160 };
    readonly TabControl modeTabs = new();
    readonly TextBlock status = new() { Foreground = BrushHex("#38BDF8"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    DeviceSettings? currentSettings;
    bool updatingUi;

    internal FootswitchSettingsWindow()
    {
        DarkUiTheme.Apply(this);
        Title = UiText.Language == "es" ? "Configuración Global de Footswitches · GP-200" : "Global Footswitch Settings · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Width = 840;
        Height = 640;
        MinWidth = 740;
        MinHeight = 540;
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
            Background = BrushHex("#334155"),
            Foreground = BrushHex("#F8FAFC")
        };
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        bottomBar.Children.Add(closeBtn);
        bottomBar.Children.Add(status);
        root.Children.Add(bottomBar);

        // Top Toolbar
        var topBar = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(topBar, Dock.Top);

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var modeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        modeStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Modo activo del pedal:" : "Active pedal mode:",
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#F1F5F9"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        activeModeCombo.Items.Add(UiText.Language == "es" ? "Patch Mode (Presets)" : "Patch Mode (Presets)");
        activeModeCombo.Items.Add(UiText.Language == "es" ? "Stomp Mode (CTRL)" : "Stomp Mode (CTRL)");
        activeModeCombo.Items.Add(UiText.Language == "es" ? "User Mode (Plantilla)" : "User Mode (Custom)");
        activeModeCombo.SelectedIndex = 0;
        activeModeCombo.SelectionChanged += async (_, _) =>
        {
            if (updatingUi || activeModeCombo.SelectedIndex < 0) return;
            await ChangeActivePedalMode(activeModeCombo.SelectedIndex);
        };
        modeStack.Children.Add(activeModeCombo);
        Grid.SetColumn(modeStack, 0);
        topGrid.Children.Add(modeStack);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var readBtn = ActionBtn(UiText.Language == "es" ? "📥 Leer del pedal" : "📥 Read from pedal", async () => await ReadSettingsAsync());
        var exportBtn = ActionBtn(UiText.Language == "es" ? "💾 Descargar copia" : "💾 Download backup", ExportSettings);
        var importBtn = ActionBtn(UiText.Language == "es" ? "📂 Restaurar..." : "📂 Restore...", async () => await ImportSettingsAsync());
        var resetBtn = ActionBtn(UiText.Language == "es" ? "🔄 Restablecer a fábrica" : "🔄 Reset to factory", async () => await ResetCurrentModeAsync());

        actions.Children.Add(readBtn);
        actions.Children.Add(exportBtn);
        actions.Children.Add(importBtn);
        actions.Children.Add(resetBtn);
        Grid.SetColumn(actions, 2);
        topGrid.Children.Add(actions);

        topBar.Child = topGrid;
        root.Children.Add(topBar);

        // Main TabControl for modes (Patch, Stomp, User)
        modeTabs.Background = BrushHex("#0F172A");
        modeTabs.BorderThickness = new Thickness(0);
        root.Children.Add(modeTabs);

        Content = root;

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
        status.Foreground = BrushHex("#38BDF8");

        try
        {
            currentSettings = await DeviceSettings.ReadAsync();
            updatingUi = true;
            try
            {
                int currentMode = currentSettings.Global(24); // 0=Preset, 1=Stomp, 2=User
                activeModeCombo.SelectedIndex = Math.Clamp(currentMode, 0, 2);
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
            ? ["Modo Patch (Presets)", "Modo Stomp (CTRL 1-8)", "Modo User (Personalizado)"]
            : ["Patch Mode", "Stomp Mode", "User Mode"];

        for (int m = 0; m < 3; m++)
        {
            var tab = new TabItem
            {
                Header = modeNames[m],
                Foreground = BrushHex("#F8FAFC"),
                Background = BrushHex("#1E293B"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            tab.Content = BuildModeContent(m);
            modeTabs.Items.Add(tab);
        }
        modeTabs.SelectedIndex = Math.Clamp(activeModeCombo.SelectedIndex, 0, 2);
    }

    UIElement BuildModeContent(int templateIndex)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        // Left Table: FS1 to FS8
        var leftCard = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        var leftStack = new StackPanel();
        leftStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "PULSADORES FS 1 A FS 8 (Pulsación Corta y Mantener)" : "FOOTSWITCHES FS1 TO FS8 (Short Press & Hold)",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#38BDF8"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        // Header row
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        headerGrid.Children.Add(new TextBlock { Text = "FS", Foreground = BrushHex("#94A3B8"), FontWeight = FontWeights.SemiBold });
        var hTap = new TextBlock { Text = UiText.Language == "es" ? "Pulsación corta (TAP)" : "Short press (TAP)", Foreground = BrushHex("#94A3B8"), FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(hTap, 1);
        headerGrid.Children.Add(hTap);
        var hHold = new TextBlock { Text = UiText.Language == "es" ? "Mantener (HOLD)" : "Hold press (HOLD)", Foreground = BrushHex("#94A3B8"), FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(hHold, 3);
        headerGrid.Children.Add(hHold);
        leftStack.Children.Add(headerGrid);

        for (int i = 0; i < 8; i++)
        {
            int fsNum = i + 1;
            int tapSlot = i * 2;
            int holdSlot = i * 2 + 1;

            int currentTap = currentSettings?.Footswitch(templateIndex, tapSlot) ?? 0;
            int currentHold = currentSettings?.Footswitch(templateIndex, holdSlot) ?? 0;

            var rowGrid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border
            {
                Background = BrushHex("#0284C7"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock { Text = $"FS {fsNum}", FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontSize = 11 };
            rowGrid.Children.Add(badge);

            var tapCombo = new ComboBox { ItemsSource = DeviceSettings.Actions, SelectedIndex = Math.Clamp(currentTap, 0, DeviceSettings.Actions.Length - 1), FontSize = 12 };
            int capturedTapSlot = tapSlot;
            tapCombo.SelectionChanged += async (_, _) =>
            {
                if (updatingUi || tapCombo.SelectedIndex < 0) return;
                await ApplyFootswitchChange(templateIndex, capturedTapSlot, tapCombo.SelectedIndex, $"FS {fsNum} (TAP)");
            };
            Grid.SetColumn(tapCombo, 1);
            rowGrid.Children.Add(tapCombo);

            var holdCombo = new ComboBox { ItemsSource = DeviceSettings.Actions, SelectedIndex = Math.Clamp(currentHold, 0, DeviceSettings.Actions.Length - 1), FontSize = 12 };
            int capturedHoldSlot = holdSlot;
            holdCombo.SelectionChanged += async (_, _) =>
            {
                if (updatingUi || holdCombo.SelectedIndex < 0) return;
                await ApplyFootswitchChange(templateIndex, capturedHoldSlot, holdCombo.SelectedIndex, $"FS {fsNum} (HOLD)");
            };
            Grid.SetColumn(holdCombo, 3);
            rowGrid.Children.Add(holdCombo);

            leftStack.Children.Add(rowGrid);
        }
        leftCard.Child = leftStack;
        Grid.SetColumn(leftCard, 0);
        grid.Children.Add(leftCard);

        // Right Table: Combinaciones de 2 pulsadores
        var rightCard = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
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
            Foreground = BrushHex("#38BDF8"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        string[] comboNames = ["FS1 + FS5", "FS5 + FS6", "FS6 + FS7", "FS7 + FS8"];
        for (int c = 0; c < comboNames.Length; c++)
        {
            int slot = 16 + c;
            int currentAction = currentSettings?.Footswitch(templateIndex, slot) ?? 0;

            var cRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            cRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            cRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock { Text = comboNames[c], Foreground = BrushHex("#F1F5F9"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            cRow.Children.Add(label);

            var actionCombo = new ComboBox { ItemsSource = DeviceSettings.Actions, SelectedIndex = Math.Clamp(currentAction, 0, DeviceSettings.Actions.Length - 1), FontSize = 12 };
            int capturedSlot = slot;
            string capturedName = comboNames[c];
            actionCombo.SelectionChanged += async (_, _) =>
            {
                if (updatingUi || actionCombo.SelectedIndex < 0) return;
                await ApplyFootswitchChange(templateIndex, capturedSlot, actionCombo.SelectedIndex, capturedName);
            };
            Grid.SetColumn(actionCombo, 1);
            cRow.Children.Add(actionCombo);

            rightStack.Children.Add(cRow);
        }

        var hintBox = new TextBlock
        {
            Text = UiText.Language == "es"
                ? "💡 Cada cambio se transmite en tiempo real a la GP-200 vía SysEx y se verifica con lectura de retorno."
                : "💡 Changes are transmitted in real time to the GP-200 via SysEx with verification readback.",
            FontSize = 11,
            Foreground = BrushHex("#94A3B8"),
            Margin = new Thickness(0, 16, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        rightStack.Children.Add(hintBox);

        rightCard.Child = rightStack;
        Grid.SetColumn(rightCard, 2);
        grid.Children.Add(rightCard);

        return grid;
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
            string[] names = ["Patch Mode", "Stomp Mode", "User Mode"];
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
        int mode = modeTabs.SelectedIndex;
        string[] names = ["Patch", "Stomp", "User"];
        string modeName = names[Math.Clamp(mode, 0, 2)];

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
            Background = BrushHex("#2563EB"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    static Brush BrushHex(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}

