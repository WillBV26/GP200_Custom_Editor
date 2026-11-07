using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>
/// Reusable AuraCode Glassmorphism Window Header Bar with integrated Title, Status Badge,
/// and persistent UI Zoom controls [-] 100% [+] (0.75x to 1.50x scale).
/// </summary>
public sealed class AuraWindowHeaderBar : Border
{
    private readonly Window _targetWindow;
    private readonly UIElement? _scalableRoot;
    private readonly TextBlock _titleBlock = new()
    {
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = DarkUiTheme.AuraTextMainBrush,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _statusBadge = new()
    {
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0)
    };
    private readonly TextBlock _scaleDisplay = new()
    {
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        Width = 46
    };

    public string Title
    {
        get => _titleBlock.Text;
        set => _titleBlock.Text = value;
    }

    public string StatusText
    {
        get => _statusBadge.Text;
        set => _statusBadge.Text = value;
    }

    public AuraWindowHeaderBar(Window targetWindow, string title, UIElement? scalableRoot = null, string? subtitle = null)
    {
        _targetWindow = targetWindow;
        _scalableRoot = scalableRoot;
        Title = title;
        if (!string.IsNullOrEmpty(subtitle))
            StatusText = subtitle;

        Background = new SolidColorBrush(Color.FromArgb(240, 13, 21, 38));
        BorderBrush = DarkUiTheme.AuraBorderSubtleBrush;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(14, 8, 14, 8);
        SnapsToDevicePixels = true;

        var dock = new DockPanel();

        // Right side: Zoom scale controls [-] 100% [+]
        var zoomPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(zoomPanel, Dock.Right);

        // Live Sync status & quick re-sync
        var liveSyncBadge = new TextBlock
        {
            Text = LivePedalSyncCoordinator.Instance.IsLiveConnected
                ? (UiText.Language == "es" ? "⚡ LIVE SYNC: ACTIVO" : "⚡ LIVE SYNC: ON")
                : (UiText.Language == "es" ? "⚡ LIVE SYNC: PAUSA" : "⚡ LIVE SYNC: PAUSED"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = LivePedalSyncCoordinator.Instance.IsLiveConnected
                ? DarkUiTheme.AuraCyanPrimaryBrush
                : DarkUiTheme.AuraTextMutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var reSyncBtn = new Button
        {
            Content = UiText.Language == "es" ? "🔄 Re-Sincronizar" : "🔄 Sync Pedal",
            Height = 22,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 14, 0),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = UiText.Language == "es" ? "Sincroniza esta vista con el preset activo de la GP-200" : "Sync view with GP-200 active preset"
        };
        reSyncBtn.Click += async (_, _) => await LivePedalSyncCoordinator.Instance.ForceFullSyncWithPedalAsync();
        LivePedalSyncCoordinator.Instance.LiveSyncStatusChanged += connected =>
        {
            _targetWindow.Dispatcher.InvokeAsync(() =>
            {
                liveSyncBadge.Text = connected
                    ? (UiText.Language == "es" ? "⚡ LIVE SYNC: ACTIVO" : "⚡ LIVE SYNC: ON")
                    : (UiText.Language == "es" ? "⚡ LIVE SYNC: PAUSA" : "⚡ LIVE SYNC: PAUSED");
                liveSyncBadge.Foreground = connected
                    ? DarkUiTheme.AuraCyanPrimaryBrush
                    : DarkUiTheme.AuraTextMutedBrush;
            });
        };
        zoomPanel.Children.Add(liveSyncBadge);
        zoomPanel.Children.Add(reSyncBtn);

        var zoomLabel = new TextBlock
        {
            Text = "ZOOM",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        zoomPanel.Children.Add(zoomLabel);

        var btnMinus = new Button
        {
            Content = "-",
            Width = 26,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 2, 0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            ToolTip = UiText.Language == "es" ? "Reducir escala (Zoom -)" : "Zoom Out (-)"
        };
        btnMinus.Click += (_, _) => AdjustZoom(-5);
        zoomPanel.Children.Add(btnMinus);

        zoomPanel.Children.Add(_scaleDisplay);

        var btnPlus = new Button
        {
            Content = "+",
            Width = 26,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 2, 0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            ToolTip = UiText.Language == "es" ? "Aumentar escala (Zoom +)" : "Zoom In (+)"
        };
        btnPlus.Click += (_, _) => AdjustZoom(+5);
        zoomPanel.Children.Add(btnPlus);

        dock.Children.Add(zoomPanel);

        // Left side: Title and optional status badge
        var leftPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var iconDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = DarkUiTheme.AuraCyanPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        leftPanel.Children.Add(iconDot);
        leftPanel.Children.Add(_titleBlock);
        leftPanel.Children.Add(_statusBadge);

        dock.Children.Add(leftPanel);

        Child = dock;

        // Initialize scale from user preferences
        UpdateScaleDisplay();
        ApplyCurrentScale();
    }

    private void AdjustZoom(int delta)
    {
        var prefs = AppPreferences.Load();
        int newScale = Math.Clamp(prefs.ScalePercent + delta, 75, 150);
        if (newScale != prefs.ScalePercent)
        {
            (prefs with { ScalePercent = newScale }).Save();
            UpdateScaleDisplay();
            ApplyCurrentScale();
        }
    }

    private void UpdateScaleDisplay()
    {
        int scale = AppPreferences.Load().ScalePercent;
        _scaleDisplay.Text = $"{scale}%";
    }

    private void ApplyCurrentScale()
    {
        int scale = AppPreferences.Load().ScalePercent;
        double factor = scale / 100.0;
        var transform = new ScaleTransform(factor, factor);

        if (_scalableRoot is FrameworkElement fe)
        {
            fe.LayoutTransform = transform;
        }
        else if (_scalableRoot != null)
        {
            _scalableRoot.RenderTransform = transform;
        }
        else if (_targetWindow.Content is FrameworkElement rootElement)
        {
            // If header is part of target window content, transform the interior child if possible
            if (rootElement is DockPanel or Grid or StackPanel or Border)
            {
                // root element transform
                rootElement.LayoutTransform = transform;
            }
        }
    }
}
