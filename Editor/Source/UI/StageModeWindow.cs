using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>
/// High-contrast Fullscreen Live Performance View ("Modo Escenario").
/// Displays huge preset name, tempo, DSP load, master volume, fast tuner,
/// 4 Scene triggers (A/B/C/D), and 8 giant interactive footswitches (FS1-FS8)
/// illuminated with genuine LED ring colors.
/// </summary>
public sealed class StageModeWindow : Window
{
    readonly EditorWindow _editor;
    readonly TextBlock _presetTitle = new() { FontSize = 44, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraTextMainBrush, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _bpmBadge = new() { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    readonly TextBlock _volBadge = new() { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.SuccessBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    readonly TextBlock _dspBadge = new() { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanSecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    readonly TextBlock _quickTunerNote = new() { Text = "TUNER: —", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center };

    readonly Button[] _sceneButtons = new Button[4];
    readonly Border[] _fsCards = new Border[8];
    readonly TextBlock[] _fsLabels = new TextBlock[8];
    readonly TextBlock[] _fsStates = new TextBlock[8];

    public StageModeWindow(EditorWindow editor)
    {
        _editor = editor;
        EditorWindow.StyleDialog(this, false);
        Title = UiText.Language == "es" ? "GP-200 · Modo Escenario (En Vivo)" : "GP-200 · Stage Mode (Live)";
        WindowState = WindowState.Maximized;
        WindowStyle = WindowStyle.None; // Immersive stage view
        Background = DarkUiTheme.AuraBgDeepBrush;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Top Bar
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Huge Preset Banner
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4 Scenes
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 8 Footswitches

        // 1. Top Bar
        var topBar = new Border
        {
            Background = DarkUiTheme.AuraBgPanelBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 10, 20, 10)
        };
        var topDock = new DockPanel();

        var exitBtn = new Button
        {
            Content = UiText.Language == "es" ? "✕ Salir del Modo Escenario" : "✕ Exit Stage View",
            Background = DarkUiTheme.AuraBgCardBrush,
            Foreground = DarkUiTheme.AuraTextMainBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            Padding = new Thickness(16, 8, 16, 8),
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        exitBtn.Click += (_, _) => Close();
        DockPanel.SetDock(exitBtn, Dock.Right);
        topDock.Children.Add(exitBtn);

        var topCenter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        topCenter.Children.Add(_quickTunerNote);
        topDock.Children.Add(topCenter);

        topBar.Child = topDock;
        Grid.SetRow(topBar, 0);
        rootGrid.Children.Add(topBar);

        // 2. Huge Preset Banner
        var bannerBorder = new Border
        {
            Background = DarkUiTheme.AuraBgPanelBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(30, 20, 30, 20),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var bannerStack = new StackPanel();
        var bannerRow = new StackPanel { Orientation = Orientation.Horizontal };
        bannerRow.Children.Add(_presetTitle);
        bannerRow.Children.Add(_bpmBadge);
        bannerRow.Children.Add(_volBadge);
        bannerRow.Children.Add(_dspBadge);
        bannerStack.Children.Add(bannerRow);
        bannerBorder.Child = bannerStack;
        Grid.SetRow(bannerBorder, 1);
        rootGrid.Children.Add(bannerBorder);

        // 3. 4 Scenes (A / B / C / D)
        var scenesCard = new Border
        {
            Background = DarkUiTheme.AuraBgPanelBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(24, 0, 24, 16)
        };
        var scenesGrid = new Grid();
        for (int i = 0; i < 4; i++)
            scenesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string[] sceneNames = ["SCENE A", "SCENE B", "SCENE C", "SCENE D"];
        for (int i = 0; i < 4; i++)
        {
            int sceneIdx = i;
            var btn = new Button
            {
                Content = sceneNames[i],
                Height = 65,
                Margin = new Thickness(8, 0, 8, 0),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Background = DarkUiTheme.AuraBgCardBrush,
                Foreground = DarkUiTheme.AuraTextMainBrush,
                BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Click += (_, _) => SelectScene(sceneIdx);
            _sceneButtons[i] = btn;
            Grid.SetColumn(btn, i);
            scenesGrid.Children.Add(btn);
        }
        scenesCard.Child = scenesGrid;
        Grid.SetRow(scenesCard, 2);
        rootGrid.Children.Add(scenesCard);

        // 4. 8 Giant Footswitch Cards (2 rows of 4: FS1-FS4 top, FS5-FS8 bottom)
        var fsGrid = new Grid { Margin = new Thickness(24, 0, 24, 24) };
        fsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        fsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int c = 0; c < 4; c++)
            fsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Color[] ringColors = [
            Color.FromRgb(0, 229, 255),  // FS1: Cyan (PRE/Boost)
            Color.FromRgb(245, 158, 11), // FS2: Amber (DST/Drive)
            Color.FromRgb(239, 68, 68),  // FS3: Red (AMP/Lead)
            Color.FromRgb(16, 185, 129), // FS4: Emerald (MOD)
            Color.FromRgb(56, 189, 248), // FS5: Sky Blue (DLY)
            Color.FromRgb(168, 85, 247), // FS6: Purple (RVB)
            Color.FromRgb(236, 72, 153), // FS7: Pink (CTRL)
            Color.FromRgb(0, 119, 182)   // FS8: Deep Blue (TAP/Bypass)
        ];

        for (int i = 0; i < 8; i++)
        {
            int fsIdx = i;
            var card = new Border
            {
                Background = DarkUiTheme.AuraBgCardBrush,
                BorderBrush = new SolidColorBrush(ringColors[i]),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8),
                Padding = new Thickness(16),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var ringDot = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(ringColors[i]),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            sp.Children.Add(ringDot);

            var fsNumber = new TextBlock
            {
                Text = $"FS {i + 1}",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sp.Children.Add(fsNumber);

            _fsLabels[i] = new TextBlock
            {
                Text = GetDefaultFsAssignment(i),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            sp.Children.Add(_fsLabels[i]);

            _fsStates[i] = new TextBlock
            {
                Text = "ON",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = DarkUiTheme.SuccessBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            sp.Children.Add(_fsStates[i]);

            card.Child = sp;
            card.PreviewMouseLeftButtonDown += (_, _) => ToggleFootswitch(fsIdx);

            _fsCards[i] = card;
            int row = i < 4 ? 0 : 1;
            int col = i % 4;
            Grid.SetRow(card, row);
            Grid.SetColumn(card, col);
            fsGrid.Children.Add(card);
        }

        Grid.SetRow(fsGrid, 3);
        rootGrid.Children.Add(fsGrid);

        Content = rootGrid;
        RefreshFromActivePreset();
    }

    public void RefreshFromActivePreset()
    {
        var doc = _editor.CurrentPresetDocument;
        if (doc == null)
        {
            _presetTitle.Text = "61-A MCK Fender SLP";
            _bpmBadge.Text = "BPM: 120";
            _volBadge.Text = "VOL: 85";
            _dspBadge.Text = "DSP LOAD: 54%";
            return;
        }

        string pos = BankService.Position(doc.Index);
        _presetTitle.Text = $"{pos}  {doc.Name.ToUpperInvariant()}";
        _bpmBadge.Text = $"BPM: {doc.Tempo}";
        _volBadge.Text = $"VOL: {doc.Volume}";

        var dsp = DSPHardwareOptimizer.Analyze(doc);
        _dspBadge.Text = $"DSP LOAD: {dsp.TotalPercent:F0}%";
        _dspBadge.Foreground = dsp.Admission == HardwareAdmissionStatus.Ok ? DarkUiTheme.SuccessBrush : DarkUiTheme.DangerBrush;

        // Highlight Active Scene (Default Scene A)
        HighlightScene(0);

        // Update block states in footswitches
        for (int m = 0; m < Math.Min(8, 11); m++)
        {
            var effect = doc.GetEffect(m);
            _fsLabels[m].Text = PresetDocument.Modules[m];
            _fsStates[m].Text = effect.Enabled ? "ON" : "OFF";
            _fsStates[m].Foreground = effect.Enabled ? DarkUiTheme.SuccessBrush : DarkUiTheme.AuraTextMutedBrush;
            _fsCards[m].Opacity = effect.Enabled ? 1.0 : 0.45;
        }
    }

    private void SelectScene(int sceneIndex)
    {
        HighlightScene(sceneIndex);
        // Switch scene in Editor
        _editor.ExecuteSceneByIndex(sceneIndex);
    }

    private void HighlightScene(int activeIdx)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i == activeIdx)
            {
                _sceneButtons[i].Background = DarkUiTheme.HighlightBrush;
                _sceneButtons[i].BorderBrush = DarkUiTheme.AuraCyanPrimaryBrush;
                _sceneButtons[i].Foreground = Brushes.White;
            }
            else
            {
                _sceneButtons[i].Background = DarkUiTheme.AuraBgCardBrush;
                _sceneButtons[i].BorderBrush = DarkUiTheme.AuraBorderSubtleBrush;
                _sceneButtons[i].Foreground = DarkUiTheme.AuraTextMutedBrush;
            }
        }
    }

    private void ToggleFootswitch(int index)
    {
        if (index is >= 0 and < 11)
        {
            _editor.ToggleModuleBypass(index);
            RefreshFromActivePreset();
        }
    }

    private static string GetDefaultFsAssignment(int index) => index switch
    {
        0 => "PRE (Boost)",
        1 => "DST (Drive)",
        2 => "AMP (Lead)",
        3 => "MOD (Chorus)",
        4 => "DLY (Echo)",
        5 => "RVB (Space)",
        6 => "CTRL (Solo)",
        7 => "TAP / MUTE",
        _ => "CTRL"
    };
}

