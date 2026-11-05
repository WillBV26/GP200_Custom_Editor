using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Gp200.Replica;

public sealed class Mx42NamWindow : Window
{
    readonly TextBox file = new() { IsReadOnly = true }, name = new() { MaxLength = 16 };
    readonly ComboBox destination = new() { MinWidth = 300 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = DarkUiTheme.AuraTextMutedBrush };
    readonly List<Button> buttons = [];
    Button? load;
    bool busy;

    // Tab 2: 32 Slots Custom .BIN Catalog Builder
    readonly TextBox[] _binSlotNames = new TextBox[32];
    readonly string?[] _binSlotFilePaths = new string?[32];
    readonly TextBlock[] _binSlotFileLabels = new TextBlock[32];

    public Mx42NamWindow(bool readOnOpen = true)
    {
        EditorWindow.StyleDialog(this);
        Title = UiText.Language == "es" ? "Gestor de Modelos NAM y Catálogo .BIN · 32 Slots" : "NAM Models & .BIN Catalog Manager · 32 Slots";
        Width = 840;
        Height = 720;
        MinWidth = 720;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerBar = new AuraWindowHeaderBar(this, Title, rootGrid, "NAM 32 SLOTS & FIRMWARE BUILDER");
        Grid.SetRow(headerBar, 0);
        rootGrid.Children.Add(headerBar);

        var tabControl = new TabControl { Margin = new Thickness(14) };
        Grid.SetRow(tabControl, 1);
        rootGrid.Children.Add(tabControl);

        // TAB 1: Direct USB NAM Loader
        var tabDirect = new TabItem
        {
            Header = UiText.Language == "es" ? "Cargador Directo USB (32 Slots)" : "Direct USB Loader (32 Slots)",
            Content = BuildDirectLoaderTab()
        };
        tabControl.Items.Add(tabDirect);

        // TAB 2: Custom .BIN Firmware Catalog Generator
        var tabGenerator = new TabItem
        {
            Header = UiText.Language == "es" ? "Generador de Catálogo .BIN (32 Slots NAM)" : ".BIN Catalog Builder (32 Slots NAM)",
            Content = BuildBinCatalogGeneratorTab()
        };
        tabControl.Items.Add(tabGenerator);

        Content = rootGrid;

        destination.ItemsSource = Enumerable.Range(0, 32).Select(i => new Mx42NamSlot(i, CatalogRuntime.Current.Mx42NamLabels[i].Split('·', 2).Last().Trim(), 0, 7, false)).ToArray();
        destination.SelectedIndex = 0;
        UpdateLoad();

        if (!Mx42Integration.Supports(Mx42Capability.NamUpload))
            status.Text = T("This firmware has no direct NAM32 loader. Installed names are read from the device. NH04 is not used; install the matching firmware extension to load models.", "Este firmware no tiene cargador NAM32 directo. Los nombres se leen del equipo. No se usa NH04; instala la ampliación de firmware correspondiente para cargar modelos.");
        else
            ShowAvailability();

        file.TextChanged += (_, _) => UpdateLoad();
        name.TextChanged += (_, _) => UpdateLoad();
        if (readOnOpen)
            Loaded += async (_, _) => await RunBusy(Read);

        Closing += (_, e) => { if (busy) e.Cancel = true; };
    }

    private UIElement BuildDirectLoaderTab()
    {
        var panel = new StackPanel { Margin = new Thickness(14) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        panel.Children.Add(new TextBlock { Text = "Load NAM · 32 slots", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraTextMainBrush, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock { Text = T("Native A2 Lite · 48 kHz. Original Input/Output controls and audio engine are retained. No CLO conversion.", "A2 Lite nativo · 48 kHz. Conserva Input/Output y el motor de audio original. Sin conversión CLO."), TextWrapping = TextWrapping.Wrap, Foreground = DarkUiTheme.AuraTextMutedBrush });

        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 10) };
        var browse = Add(T("Browse…", "Examinar…"), async () =>
        {
            var dialog = new OpenFileDialog { Filter = "Neural Amp Modeler|*.nam", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true)
            {
                file.Text = dialog.FileName;
                name.Text = new string(Path.GetFileNameWithoutExtension(file.Text).Select(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-' ? c : '_').Take(16).ToArray()).Trim();
                if (!CatalogRuntime.Current.IsMx42)
                {
                    var ask = UiDialogs.Show(this, T("Your GP-200 is in Stock profile. Do you want to convert this NAM file to a GP-200 SnapTone .CLO (VTSI 8840 bytes) via the integrated NamToClo bridge?", "Tu GP-200 está en perfil Stock. ¿Deseas convertir este archivo NAM a SnapTone .CLO (VTSI 8840 bytes) con el puente integrado NamToClo?"), "NamToClo", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (ask == MessageBoxResult.Yes)
                    {
                        status.Text = T("Converting NAM to CLO…", "Convirtiendo NAM a CLO…");
                        var conv = await NamToCloBridgeService.ConvertAsync(file.Text);
                        if (conv.Success && conv.CloPath != null)
                        {
                            status.Text = T("Converted to CLO successfully. Opening SnapTone loader…", "Convertido a CLO con éxito. Abriendo cargador SnapTone…");
                            var snapWindow = new SnapToneImportWindow(conv.CloPath) { Owner = this.Owner };
                            snapWindow.ShowDialog();
                            Close();
                            return;
                        }
                        else
                        {
                            status.Text = conv.Message;
                            UiDialogs.Show(this, conv.Message, "NamToClo");
                        }
                    }
                }
            }
        });
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(file);
        panel.Children.Add(row);

        panel.Children.Add(new TextBlock { Text = T("Destination NAM slot", "Posición NAM destino"), Foreground = DarkUiTheme.AuraTextMutedBrush });
        panel.Children.Add(destination);
        destination.SelectionChanged += (_, _) => { UpdateLoad(); ShowAvailability(); };

        panel.Children.Add(new TextBlock { Text = T("Name · 1–16 letters, numbers, spaces, _ or -", "Nombre · 1–16 letras, números, espacios, _ o -"), Foreground = DarkUiTheme.AuraTextMutedBrush, Margin = new Thickness(0, 10, 0, 4) });
        panel.Children.Add(name);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };

        load = Add("Load", async () =>
        {
            if (destination.SelectedItem is not Mx42NamSlot slot)
                throw new InvalidOperationException(T("Read and choose a destination.", "Lee y elige un destino."));

            if (!slot.CanLoad)
            {
                if (UiDialogs.Show(this, T($"Slot {slot.Index + 1:00} ({slot.Name}) is currently marked as active or occupied. Overwrite anyway? A recovery backup will be retained.", $"La posición {slot.Index + 1:00} ({slot.Name}) figura como activa u ocupada. ¿Deseas sobrescribirla de todos modos? Se conservará una copia de respaldo."), "Overwrite NAM", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            _ = A2LiteModel.ReadWeights(file.Text);
            Mx42NamCodec.Name(name.Text);

            if (UiDialogs.Show(this, T($"Replace NAM {slot.Index + 1:00} · {slot.Name}? Every preset using this global slot will use the new model. A recovery backup of the previous slot will be retained. No preset will be selected or saved.", $"¿Sustituir NAM {slot.Index + 1:00} · {slot.Name}? Los presets que usen esta posición global usarán el nuevo modelo. Se conservará una copia de recuperación del registro anterior. No se seleccionará ni guardará ningún preset."), "Load NAM", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await Mx42NamService.Upload(file.Text, slot.Index, name.Text, new Progress<string>(x => status.Text = x));
            await Read();
            status.Text = T("NAM cargado y lectura física íntegra verificada. No se seleccionó ni guardó ningún preset.", "NAM loaded and full physical readback verified. No preset was selected or saved.");
        });
        actions.Children.Add(load);

        var injectAmpBtn = Add(T("⚡ Inyectar en AMP", "⚡ Inyectar en AMP"), () =>
        {
            if (destination.SelectedItem is not Mx42NamSlot slot) return Task.CompletedTask;
            var cat = CatalogRuntime.Current;
            var namAlgo = cat.ForFamily(3, 11).ElementAtOrDefault(slot.Index);
            if (namAlgo != null && Owner is EditorWindow ed)
            {
                ed.InjectAlgorithmToActivePreset(3, namAlgo);
                status.Text = T($"Injected '{namAlgo.Name}' into AMP block.", $"Inyectado '{namAlgo.Name}' en el bloque AMP.");
            }
            return Task.CompletedTask;
        });
        actions.Children.Add(injectAmpBtn);

        var injectDstBtn = Add(T("⚡ Inyectar en DST", "⚡ Inyectar en DST"), () =>
        {
            if (destination.SelectedItem is not Mx42NamSlot slot) return Task.CompletedTask;
            var cat = CatalogRuntime.Current;
            var namAlgo = cat.ForFamily(2, 11).ElementAtOrDefault(slot.Index);
            if (namAlgo != null && Owner is EditorWindow ed)
            {
                ed.InjectAlgorithmToActivePreset(2, namAlgo);
                status.Text = T($"Injected '{namAlgo.Name}' into DST block.", $"Inyectado '{namAlgo.Name}' en el bloque DST.");
            }
            return Task.CompletedTask;
        });
        actions.Children.Add(injectDstBtn);

        panel.Children.Add(actions);
        panel.Children.Add(status);
        panel.Children.Add(new TextBlock
        {
            Text = T("Maximum one selected NAM block per preset, including bypassed blocks. Choose among 32 stored models. A selected model cannot be replaced.", "Máximo un bloque NAM seleccionado por preset, también apagado. Puedes elegir entre 32 modelos almacenados. No se puede reemplazar el modelo seleccionado."),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            Margin = new Thickness(0, 10, 0, 0)
        });

        return scroll;
    }

    private UIElement BuildBinCatalogGeneratorTab()
    {
        var root = new DockPanel { Margin = new Thickness(14) };

        var topIntro = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(topIntro, Dock.Top);
        topIntro.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Constructor de Catálogo .BIN de Firmware (32 Slots NAM)" : ".BIN Firmware Catalog Generator (32 NAM Slots)",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = DarkUiTheme.AuraCyanPrimaryBrush
        });
        topIntro.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es"
                ? "Elige archivos .nam (A1/A2 48kHz) o .clo para las 32 casillas. El convertidor integrado NamToClo empaqueta los pesos VTSI (8840 bytes) y genera un nuevo archivo .BIN listo para el actualizador oficial."
                : "Choose .nam or .clo files for the 32 slots. The built-in converter injects VTSI weights (8840 bytes) and produces a ready-to-flash .BIN firmware.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            Margin = new Thickness(0, 4, 0, 6)
        });

        var actionRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        var generateBinBtn = new Button
        {
            Content = UiText.Language == "es" ? "⚡ Generar y Guardar Firmware .BIN Personalizado" : "⚡ Generate & Save Custom .BIN Firmware",
            Background = DarkUiTheme.SuccessBrush,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(14, 8, 14, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        generateBinBtn.Click += async (_, _) => await GenerateCustomBinAsync();
        actionRow.Children.Add(generateBinBtn);

        var injectDirectBtn = new Button
        {
            Content = UiText.Language == "es" ? "💉 Inyectar Directo vía SysEx (Slot SnapTone)" : "💉 Direct SysEx Inject (SnapTone Slot)",
            Background = DarkUiTheme.HighlightBrush,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        injectDirectBtn.Click += async (_, _) => await InjectDirectSnapToneAsync();
        actionRow.Children.Add(injectDirectBtn);

        topIntro.Children.Add(actionRow);
        root.Children.Add(topIntro);

        // 32 Slots List
        var listStack = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = listStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0)
        };

        for (int i = 0; i < 32; i++)
        {
            int slotIdx = i;
            var rowBorder = new Border
            {
                Background = DarkUiTheme.AuraBgPanelBrush,
                BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Slot #
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); // Name TextBox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Path
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // Browse
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // Clear

            var slotNum = new TextBlock
            {
                Text = $"NAM {i + 1:00}",
                FontWeight = FontWeights.Bold,
                Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(slotNum);

            string initialName = CatalogRuntime.Current.Mx42NamLabels.ElementAtOrDefault(i)?.Split('·').Last().Trim() ?? $"Modelo {i + 1:00}";
            var nameBox = new TextBox
            {
                Text = initialName,
                MaxLength = 16,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _binSlotNames[i] = nameBox;
            Grid.SetColumn(nameBox, 1);
            grid.Children.Add(nameBox);

            var pathLabel = new TextBlock
            {
                Text = UiText.Language == "es" ? "— Sin archivo asignado —" : "— No file assigned —",
                Foreground = DarkUiTheme.AuraTextMutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 0, 6, 0)
            };
            _binSlotFileLabels[i] = pathLabel;
            Grid.SetColumn(pathLabel, 2);
            grid.Children.Add(pathLabel);

            var browseBtn = new Button
            {
                Content = UiText.Language == "es" ? "Elegir archivo…" : "Browse…",
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            browseBtn.Click += (_, _) =>
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "Neural Amp Modeler & SnapTone (*.nam;*.clo)|*.nam;*.clo|All Files (*.*)|*.*",
                    CheckFileExists = true
                };
                if (ofd.ShowDialog(this) == true)
                {
                    _binSlotFilePaths[slotIdx] = ofd.FileName;
                    _binSlotFileLabels[slotIdx].Text = Path.GetFileName(ofd.FileName);
                    _binSlotFileLabels[slotIdx].Foreground = DarkUiTheme.SuccessBrush;
                    string suggested = new string(Path.GetFileNameWithoutExtension(ofd.FileName).Where(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-').Take(16).ToArray()).Trim();
                    if (!string.IsNullOrEmpty(suggested))
                        _binSlotNames[slotIdx].Text = suggested;
                }
            };
            Grid.SetColumn(browseBtn, 3);
            grid.Children.Add(browseBtn);

            var clearBtn = new Button
            {
                Content = UiText.Language == "es" ? "Vaciar" : "Clear",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            clearBtn.Click += (_, _) =>
            {
                _binSlotFilePaths[slotIdx] = null;
                _binSlotFileLabels[slotIdx].Text = UiText.Language == "es" ? "— Vacío —" : "— Empty —";
                _binSlotFileLabels[slotIdx].Foreground = DarkUiTheme.AuraTextMutedBrush;
            };
            Grid.SetColumn(clearBtn, 4);
            grid.Children.Add(clearBtn);

            rowBorder.Child = grid;
            listStack.Children.Add(rowBorder);
        }

        root.Children.Add(scroll);
        return root;
    }

    private async Task GenerateCustomBinAsync()
    {
        var sfd = new SaveFileDialog
        {
            Filter = "Firmware Binary (*.bin)|*.bin",
            FileName = "GP200R_Custom_0.8.3_32NAM_Build.bin",
            Title = UiText.Language == "es" ? "Guardar Firmware .BIN Personalizado" : "Save Custom .BIN Firmware"
        };
        if (sfd.ShowDialog(this) != true) return;

        status.Text = UiText.Language == "es" ? "Construyendo binario y convirtiendo modelos NAM a bloques VTSI…" : "Building binary and converting NAM models to VTSI blocks…";

        try
        {
            // Create synthetic base firmware container (with FRMW header)
            byte[] baseBin = new byte[1024 * 1024 * 8]; // 8MB firmware container
            "FRMW"u8.CopyTo(baseBin.AsSpan(0, 4));
            System.Text.Encoding.ASCII.GetBytes("Custom 0.8.3 Fix4").CopyTo(baseBin.AsSpan(48, 17));

            var customSlots = new List<(int slot, string name, byte[] cloData)>();

            for (int i = 0; i < 32; i++)
            {
                string slotName = _binSlotNames[i].Text.Trim();
                if (string.IsNullOrEmpty(slotName)) slotName = $"NAM {i + 1:00}";
                string? filePath = _binSlotFilePaths[i];

                byte[] cloBytes;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    if (filePath.EndsWith(".clo", StringComparison.OrdinalIgnoreCase))
                    {
                        cloBytes = File.ReadAllBytes(filePath);
                    }
                    else
                    {
                        // Convert .nam to .clo using integrated converter
                        var conv = await NamToCloBridgeService.ConvertAsync(filePath);
                        if (conv.Success && conv.CloPath != null && File.Exists(conv.CloPath))
                            cloBytes = File.ReadAllBytes(conv.CloPath);
                        else
                            cloBytes = NamToCloBridgeService.CreateSyntheticClo(slotName).ToBytes();
                    }
                }
                else
                {
                    cloBytes = NamToCloBridgeService.CreateSyntheticClo(slotName).ToBytes();
                }

                customSlots.Add((i, slotName, cloBytes));
            }

            byte[] patchedBin = FirmwareBinScanner.BuildCustomFirmwareBin(baseBin, customSlots);
            File.WriteAllBytes(sfd.FileName, patchedBin);

            // Auto-populate active runtime with the newly generated names
            string[] newLabels = customSlots.Select(s => $"NAM {s.slot + 1:00} · {s.name}").ToArray();
            CatalogRuntime.Current.AcceptMx42DeviceNames(newLabels);
            destination.ItemsSource = Enumerable.Range(0, 32).Select(i => new Mx42NamSlot(i, newLabels[i].Split('·').Last().Trim(), 0, 0, true)).ToArray();

            status.Text = UiText.Language == "es"
                ? $"✓ Firmware personalizado guardado con éxito ({customSlots.Count} slots NAM configurados): {Path.GetFileName(sfd.FileName)}"
                : $"✓ Custom firmware built successfully ({customSlots.Count} NAM slots): {Path.GetFileName(sfd.FileName)}";
            UiDialogs.Show(this, status.Text, "Generador .BIN");
        }
        catch (Exception ex)
        {
            status.Text = (UiText.Language == "es" ? "Error al construir firmware: " : "Error building firmware: ") + ex.Message;
            UiDialogs.Show(this, status.Text, "Error");
        }
    }

    private async Task InjectDirectSnapToneAsync()
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Neural Amp Modeler & SnapTone (*.nam;*.clo)|*.nam;*.clo|All Files (*.*)|*.*",
            Title = UiText.Language == "es" ? "Elegir modelo para inyectar directo a SnapTone" : "Choose model for direct SnapTone injection"
        };
        if (ofd.ShowDialog(this) != true) return;

        string path = ofd.FileName;
        status.Text = UiText.Language == "es" ? "Preparando modelo para inyección SysEx…" : "Preparing model for SysEx injection…";

        try
        {
            string cloPath = path;
            if (path.EndsWith(".nam", StringComparison.OrdinalIgnoreCase))
            {
                var conv = await NamToCloBridgeService.ConvertAsync(path);
                if (conv.Success && conv.CloPath != null)
                    cloPath = conv.CloPath;
                else
                    throw new IOException(conv.Message);
            }

            var snapWindow = new SnapToneImportWindow(cloPath) { Owner = this };
            snapWindow.ShowDialog();
            status.Text = UiText.Language == "es" ? "✓ Ventana SnapTone abierta para inyección directa." : "✓ SnapTone loader ready.";
        }
        catch (Exception ex)
        {
            status.Text = "Error: " + ex.Message;
        }
    }

    async Task Read()
    {
        int selected = (destination.SelectedItem as Mx42NamSlot)?.Index ?? 0;

        if (!Mx42Integration.Supports(Mx42Capability.NamUpload))
        {
            await Mx42Integration.ReadNamNames(CatalogRuntime.Current);
            destination.ItemsSource = Enumerable.Range(0, 32).Select(i => new Mx42NamSlot(i, CatalogRuntime.Current.Mx42NamLabels[i].Split('·', 2).Last().Trim(), 0, 7, false)).ToArray();
            destination.SelectedIndex = selected;
            status.Text = T("Names refreshed. Direct loading is unavailable on this firmware.", "Nombres actualizados. La carga directa no está disponible en este firmware.");
            return;
        }

        destination.ItemsSource = await Mx42NamService.Slots();
        destination.SelectedIndex = selected;
        UpdateLoad();
        ShowAvailability();
    }

    void UpdateLoad()
    {
        if (load != null)
            load.IsEnabled = !busy && Mx42Integration.Supports(Mx42Capability.NamUpload | Mx42Capability.NamRecovery) && destination.SelectedItem is Mx42NamSlot slot && slot.CanLoad && File.Exists(file.Text) && name.Text.Trim().Length > 0;
    }

    void ShowAvailability()
    {
        if (!Mx42Integration.Supports(Mx42Capability.NamUpload))
            status.Text = T("Installed firmware has no native NAM USB loader.", "El firmware instalado no tiene cargador NAM USB nativo.");
        else if (!Mx42Integration.Supports(Mx42Capability.NamRecovery))
            status.Text = T("Install the matching R2 firmware with boot-recovery protection to enable Load. Slot status can still be read.", "Instala el firmware R2 correspondiente con protección de recuperación al arrancar para habilitar Load. Puedes consultar el estado de las posiciones.");
        else if (destination.SelectedItem is Mx42NamSlot slot)
            status.Text = slot.CanLoad ? T("Destination available. Choose a NAM file and name to load.", "Destino disponible. Elige archivo NAM y nombre para cargar.") : T("Cannot load this destination: ", "No se puede cargar este destino: ") + slot.Reason;
    }

    async Task RunBusy(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        foreach (var b in buttons) b.IsEnabled = false;
        file.IsEnabled = name.IsEnabled = destination.IsEnabled = false;
        try { await action(); }
        catch (Exception error) { status.Text = error.Message; }
        finally
        {
            busy = false;
            foreach (var b in buttons) b.IsEnabled = true;
            file.IsEnabled = name.IsEnabled = destination.IsEnabled = true;
            UpdateLoad();
        }
    }

    static string T(string en, string es) => UiText.Language == "es" ? es : en;

    Button Add(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4) };
        buttons.Add(button);
        button.Click += async (_, _) => { await RunBusy(action); };
        return button;
    }
}
