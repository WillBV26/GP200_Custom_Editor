using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>
/// Modern AuraCode pedal catalog browser for adding pedals to the chain,
/// replacing/morphing existing blocks, or inserting pedals at any position in single/dual chain.
/// </summary>
public sealed class AddPedalDialog : Window
{
    readonly PresetDocument _document;
    readonly Catalog _catalog;
    readonly int? _targetPosition;
    readonly byte? _replaceModule;
    readonly int? _targetGroup; // 0=IN, 1=RAMA A, 2=RAMA B, 3=OUT
    readonly TextBox _searchBox = new();
    readonly StackPanel _itemsHost = new();
    readonly List<PedalBrowserItem> _allItems = [];
    int _selectedCategoryIndex = 0;

    public AlgorithmInfo? SelectedAlgorithm { get; private set; }
    public byte SelectedModule { get; private set; }
    public bool Confirmed { get; private set; }

    static readonly string[] Categories =
    [
        "Todos",
        "🎸 PRE (Comp / Boost)",
        "🎛️ WAH (Filtro)",
        "🔊 DST (Overdrive / Fuzz)",
        "⚡ AMP (Amplificador)",
        "🛡️ NR (Puerta de Ruido)",
        "📢 CAB (Gabinete / IR)",
        "🎚️ EQ (Ecualizador)",
        "🌀 MOD (Modulación)",
        "⏱️ DLY (Delay)",
        "🌌 RVB (Reverb)",
        "🎚️ VOL (Volumen)"
    ];

    record PedalBrowserItem(
        byte Module,
        AlgorithmInfo Algo,
        string CategoryName,
        string Description,
        double EstimatedDsp);

    public AddPedalDialog(
        PresetDocument document,
        Catalog catalog,
        int? targetPosition = null,
        byte? replaceModule = null,
        int? targetGroup = null)
    {
        _document = document;
        _catalog = catalog;
        _targetPosition = targetPosition;
        _replaceModule = replaceModule;
        _targetGroup = targetGroup;

        Title = replaceModule.HasValue
            ? (UiText.Language == "es" ? $"Cambiar Pedal [{PresetDocument.Modules[replaceModule.Value]}] por otro" : $"Swap / Morph [{PresetDocument.Modules[replaceModule.Value]}]")
            : (UiText.Language == "es" ? "Añadir Pedal a la Cadena" : "Add Pedal to Chain");

        Width = 760;
        Height = 650;
        MinWidth = 660;
        MinHeight = 500;
        Background = DarkUiTheme.AuraBgDeepBrush;
        Foreground = DarkUiTheme.AuraTextMainBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        DarkUiTheme.ApplyGlobalAuraCodeTheme();
        PopulateCatalog();
        Content = BuildUi();

        // Focus search box on load
        Loaded += (_, _) => _searchBox.Focus();
    }

    void PopulateCatalog()
    {
        _allItems.Clear();

        for (int m = 0; m < 11; m++)
        {
            byte modByte = (byte)m;
            string catName = Categories[m + 1];
            var algos = _catalog.ForModule(m);

            foreach (var algo in algos)
            {
                if (algo.IsNone) continue;

                double dsp = m switch
                {
                    0 => 3.5,
                    1 => 2.5,
                    2 => algo.Name.Contains("Fuzz", StringComparison.OrdinalIgnoreCase) ? 4.8 : 4.0,
                    3 => algo.Name.Contains("NAM", StringComparison.OrdinalIgnoreCase) ? 22.5 : 11.8,
                    4 => 2.0,
                    5 => algo.Name.Contains("IR", StringComparison.OrdinalIgnoreCase) ? 6.8 : 3.5,
                    6 => 3.0,
                    7 => 4.5,
                    8 => algo.Name.Contains("Dual", StringComparison.OrdinalIgnoreCase) ? 7.2 : 4.8,
                    9 => algo.Name.Contains("Shimmer", StringComparison.OrdinalIgnoreCase) ? 11.0 : 6.5,
                    10 => 1.0,
                    _ => 3.0
                };

                string desc = GetEffectDescription(m, algo.Name);

                _allItems.Add(new PedalBrowserItem(modByte, algo, catName, desc, dsp));
            }
        }
    }

    static string GetEffectDescription(int module, string name) => module switch
    {
        0 => "Compresor y preamplificación dinámica de entrada",
        1 => "Filtro envolvente y wah interactivo de respuesta rápida",
        2 => "Saturación analógica, distorsión y sobremarcha de alta ganancia",
        3 => "Modelado de preamplificador y etapa de potencia a válvulas",
        4 => "Puerta supresora de ruido de umbral ultrarrápido",
        5 => "Respuesta al impulso (IR) y simulación acústica de cabina",
        6 => "Modelador tonal de frecuencias multibanda y precisión Q",
        7 => "Modulación estéreo: chorus cálido, flanger, phaser y vibrato",
        8 => "Retardo rítmico digital, analógico y eco de cinta vintage",
        9 => "Reverberación espacial y reflexiones envolventes de sala",
        10 => "Control de nivel y curva de atenuación maestra",
        _ => "Efecto de audio DSP"
    };

    FrameworkElement BuildUi()
    {
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Aura Window Header Bar
        var header = new AuraWindowHeaderBar(this, Title);
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Target placement badge
        var targetInfo = new Border
        {
            Background = DarkUiTheme.Brush("#0D1526"),
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 8, 16, 8)
        };
        var targetText = new TextBlock
        {
            FontSize = 12,
            Foreground = DarkUiTheme.AuraCyanPrimaryBrush
        };
        if (_replaceModule.HasValue)
        {
            targetText.Text = UiText.Language == "es"
                ? $"⇄ Sustituyendo bloque actual: [{PresetDocument.Modules[_replaceModule.Value]}]. El nuevo efecto mantendrá su posición en la cadena."
                : $"⇄ Replacing current block: [{PresetDocument.Modules[_replaceModule.Value]}]. New pedal preserves chain position.";
        }
        else if (_targetGroup.HasValue)
        {
            string grp = _targetGroup.Value switch { 0 => "IN (Entrada)", 1 => "RAMA A", 2 => "RAMA B", _ => "OUT (Salida)" };
            targetText.Text = UiText.Language == "es"
                ? $"➕ Insertando nuevo pedal en: {grp} de la cadena dual."
                : $"➕ Inserting new pedal into: {grp} of dual chain.";
        }
        else if (_targetPosition.HasValue)
        {
            targetText.Text = UiText.Language == "es"
                ? $"➕ Insertando nuevo pedal en la posición #{_targetPosition.Value + 1} de la cadena de señal."
                : $"➕ Inserting new pedal at position #{_targetPosition.Value + 1} of signal chain.";
        }
        else
        {
            targetText.Text = UiText.Language == "es"
                ? "➕ Selecciona cualquier pedal para activarlo o añadirlo a la cadena."
                : "➕ Select any pedal to enable or insert it into the chain.";
        }
        targetInfo.Child = targetText;
        Grid.SetRow(targetInfo, 1);
        mainGrid.Children.Add(targetInfo);

        // Search & Filter Bar
        var filterBar = new Border
        {
            Background = DarkUiTheme.AuraBgPanelBrush,
            Padding = new Thickness(16, 10, 16, 10),
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var filterStack = new StackPanel();

        // Search row
        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var searchLabel = new TextBlock
        {
            Text = UiText.Language == "es" ? "🔍 Buscar Pedal:" : "🔍 Search Pedal:",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchRow.Children.Add(searchLabel);

        _searchBox.Style = (Style)FindResource(typeof(TextBox));
        _searchBox.Padding = new Thickness(8, 6, 8, 6);
        _searchBox.TextChanged += (_, _) => FilterList();
        searchRow.Children.Add(_searchBox);
        filterStack.Children.Add(searchRow);

        // Category Chips Scroll
        var categoryScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var categoryChips = new StackPanel { Orientation = Orientation.Horizontal };

        for (int i = 0; i < Categories.Length; i++)
        {
            int catIdx = i;
            var chip = new Button
            {
                Content = Categories[i],
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(2, 0, 4, 0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Background = catIdx == 0 ? DarkUiTheme.Brush("#0077B6") : DarkUiTheme.Brush("#142238"),
                Foreground = catIdx == 0 ? Brushes.White : DarkUiTheme.AuraTextMutedBrush
            };

            chip.Click += (_, _) =>
            {
                _selectedCategoryIndex = catIdx;
                foreach (Button c in categoryChips.Children)
                {
                    c.Background = DarkUiTheme.Brush("#142238");
                    c.Foreground = DarkUiTheme.AuraTextMutedBrush;
                }
                chip.Background = DarkUiTheme.Brush("#0077B6");
                chip.Foreground = Brushes.White;
                FilterList();
            };
            categoryChips.Children.Add(chip);
        }
        categoryScroll.Content = categoryChips;
        filterStack.Children.Add(categoryScroll);

        filterBar.Child = filterStack;
        Grid.SetRow(filterBar, 2);
        mainGrid.Children.Add(filterBar);

        // Items List Scroll
        var itemsScroll = new ScrollViewer
        {
            Content = _itemsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(16, 8, 16, 8)
        };
        Grid.SetRow(itemsScroll, 3);
        mainGrid.Children.Add(itemsScroll);

        // Bottom Actions
        var bottomCard = new Border
        {
            Background = DarkUiTheme.AuraBgInputBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 12, 16, 12)
        };
        var bottomDock = new DockPanel();

        var cancelBtn = new Button
        {
            Content = UiText.Language == "es" ? "Cancelar" : "Cancel",
            Padding = new Thickness(16, 6, 16, 6),
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (_, _) => { Confirmed = false; Close(); };
        DockPanel.SetDock(cancelBtn, Dock.Right);
        bottomDock.Children.Add(cancelBtn);

        var hint = new TextBlock
        {
            Text = UiText.Language == "es"
                ? "Doble clic en un pedal o pulsa 'Seleccionar' para integrarlo inmediatamente a la cadena."
                : "Double-click a pedal or click 'Select' to insert it into the chain immediately.",
            FontSize = 11,
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        bottomDock.Children.Add(hint);

        bottomCard.Child = bottomDock;
        Grid.SetRow(bottomCard, 4);
        mainGrid.Children.Add(bottomCard);

        FilterList();
        return mainGrid;
    }

    void FilterList()
    {
        _itemsHost.Children.Clear();
        string query = _searchBox.Text.Trim();

        var filtered = _allItems.Where(item =>
        {
            if (_selectedCategoryIndex > 0 && item.Module != _selectedCategoryIndex - 1)
                return false;

            if (!string.IsNullOrEmpty(query))
            {
                bool matchesName = item.Algo.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesCat = item.CategoryName.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesDesc = item.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matchesName && !matchesCat && !matchesDesc) return false;
            }

            return true;
        }).ToList();

        if (filtered.Count == 0)
        {
            _itemsHost.Children.Add(new TextBlock
            {
                Text = UiText.Language == "es" ? "No se encontraron pedales coincidentes." : "No matching pedals found.",
                FontSize = 13,
                Foreground = DarkUiTheme.AuraTextMutedBrush,
                Margin = new Thickness(0, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var item in filtered)
        {
            var itemCard = new Border
            {
                Background = DarkUiTheme.AuraBgPanelBrush,
                BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };

            var itemDock = new DockPanel();

            // Right: DSP and Select button
            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(rightStack, Dock.Right);

            var dspBadge = new Border
            {
                Background = DarkUiTheme.Brush("#1A2B3C"),
                BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"~{item.EstimatedDsp:F1}% DSP",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = DarkUiTheme.AuraCyanPrimaryBrush
                }
            };
            rightStack.Children.Add(dspBadge);

            var selectBtn = new Button
            {
                Content = _replaceModule.HasValue
                    ? (UiText.Language == "es" ? "⇄ Sustituir" : "⇄ Replace")
                    : (UiText.Language == "es" ? "➕ Añadir" : "➕ Add"),
                Padding = new Thickness(12, 5, 12, 5),
                Cursor = Cursors.Hand
            };
            selectBtn.Click += (_, _) => ChooseItem(item);
            rightStack.Children.Add(selectBtn);
            itemDock.Children.Add(rightStack);

            // Left: Module category badge & Name
            var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };

            Brush catColor = item.Module switch
            {
                0 => DarkUiTheme.Brush("#38BDF8"),
                1 => DarkUiTheme.Brush("#F59E0B"),
                2 => DarkUiTheme.Brush("#EF4444"),
                3 => DarkUiTheme.Brush("#FBBF24"),
                4 => DarkUiTheme.Brush("#10B981"),
                5 => DarkUiTheme.Brush("#A855F7"),
                6 => DarkUiTheme.Brush("#06B6D4"),
                7 => DarkUiTheme.Brush("#EC4899"),
                8 => DarkUiTheme.Brush("#3B82F6"),
                9 => DarkUiTheme.Brush("#8B5CF6"),
                _ => DarkUiTheme.AuraCyanPrimaryBrush
            };

            var catBadge = new Border
            {
                Background = DarkUiTheme.Brush("#152238"),
                BorderBrush = catColor,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = PresetDocument.Modules[item.Module],
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = catColor
                }
            };
            titleRow.Children.Add(catBadge);

            var nameText = new TextBlock
            {
                Text = item.Algo.Name,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = DarkUiTheme.AuraTextMainBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(nameText);
            leftStack.Children.Add(titleRow);

            var descText = new TextBlock
            {
                Text = item.Description,
                FontSize = 11,
                Foreground = DarkUiTheme.AuraTextMutedBrush
            };
            leftStack.Children.Add(descText);

            itemDock.Children.Add(leftStack);
            itemCard.Child = itemDock;

            // Hover and Click events
            itemCard.MouseEnter += (_, _) =>
            {
                itemCard.BorderBrush = DarkUiTheme.AuraCyanPrimaryBrush;
                itemCard.Background = DarkUiTheme.Brush("#142238");
            };
            itemCard.MouseLeave += (_, _) =>
            {
                itemCard.BorderBrush = DarkUiTheme.AuraBorderSubtleBrush;
                itemCard.Background = DarkUiTheme.AuraBgPanelBrush;
            };
            itemCard.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    ChooseItem(item);
                }
            };

            _itemsHost.Children.Add(itemCard);
        }
    }

    void ChooseItem(PedalBrowserItem item)
    {
        SelectedAlgorithm = item.Algo;
        SelectedModule = item.Module;
        Confirmed = true;
        DialogResult = true;
        Close();
    }
}

