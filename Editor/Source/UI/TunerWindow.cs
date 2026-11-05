using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gp200.Replica;

internal sealed class TunerWindow : Window
{
    const int WindowSize = 4096;
    readonly ComboBox input = new() { MinWidth = 280, Margin = new Thickness(0, 0, 0, 6) };
    readonly Border signalLed = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBlock note = new() { Text = "—", FontSize = 72, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock frequency = new() { FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Foreground = DarkUiTheme.AuraCyanSecondaryBrush, Margin = new Thickness(0, 2, 0, 4) };
    readonly TextBlock tuneBadge = new() { FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 8) };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = DarkUiTheme.AuraTextMutedBrush, FontSize = 12 };
    readonly Canvas meter = new() { Height = 90, Margin = new Thickness(12, 8, 12, 8) };
    readonly ProgressBar inputVuMeter = new() { Height = 8, Minimum = -60, Maximum = 0, Value = -60, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock inputVuLabel = new() { Text = UiText.Language == "es" ? "Nivel de entrada: —" : "Input Level: —", FontSize = 11, Foreground = DarkUiTheme.AuraTextMutedBrush };
    readonly Button startStop = new() { MinWidth = 140, Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button closeBtn = new() { Content = UiText.Language == "es" ? "Cerrar" : "Close", MinWidth = 90, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };

    // Hardware Tuner Controls
    int referencePitchHz = 440;
    bool isMutedMode = true;
    readonly TextBlock pitchDisplay = new() { Text = "440 Hz", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    readonly Button muteModeBtn = new() { Content = UiText.Language == "es" ? "Modo: MUTE (Silencio)" : "Mode: MUTE (Silent)", MinWidth = 140, Cursor = System.Windows.Input.Cursors.Hand };

    readonly List<float> samples = new(WindowSize * 2);
    readonly AudioCaptureEngine engine = new();
    bool closing;
    double currentCents;
    bool noSignalDisplayed;

    internal TunerWindow()
    {
        EditorWindow.StyleDialog(this, false);
        Title = UiText.Language == "es" ? "Afinador Cromático de Estudio · GP-200" : "Studio Chromatic Tuner · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 590;
        Height = 660;
        MinWidth = 500;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        startStop.Content = UiText.Language == "es" ? "Iniciar afinador" : "Start Tuner";
        startStop.Background = DarkUiTheme.HighlightBrush;
        startStop.Foreground = Brushes.White;
        startStop.FontWeight = FontWeights.Bold;

        closeBtn.Background = DarkUiTheme.AuraBgCardBrush;
        closeBtn.Foreground = DarkUiTheme.AuraTextMainBrush;
        closeBtn.BorderBrush = DarkUiTheme.AuraBorderSubtleBrush;

        muteModeBtn.Background = DarkUiTheme.HighlightBrush;
        muteModeBtn.Foreground = Brushes.White;
        muteModeBtn.Click += async (_, _) =>
        {
            isMutedMode = !isMutedMode;
            muteModeBtn.Content = isMutedMode
                ? (UiText.Language == "es" ? "Modo: MUTE (Silencio)" : "Mode: MUTE (Silent)")
                : (UiText.Language == "es" ? "Modo: BYPASS (Sonido)" : "Mode: BYPASS (Audible)");
            muteModeBtn.Background = isMutedMode ? DarkUiTheme.HighlightBrush : DarkUiTheme.AuraBorderSubtleBrush;

            // Sync with physical pedal via SysEx if connected
            if (ReadOnlyMidi.IsDeviceAvailable())
            {
                try
                {
                    // Global setting for tuner mute/bypass
                    var change = DeviceSettings.SetGlobal(35, isMutedMode ? 0 : 1);
                    await DeviceSettings.ApplyAsync(change);
                }
                catch { }
            }
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // AuraCode Reusable Window Header Bar with Title and Zoom [-] 100% [+]
        var headerBar = new AuraWindowHeaderBar(this, Title, rootGrid, UiText.Language == "es" ? "BAJA LATENCIA" : "LOW LATENCY");
        Grid.SetRow(headerBar, 0);
        rootGrid.Children.Add(headerBar);

        var innerDock = new DockPanel { Margin = new Thickness(16) };
        Grid.SetRow(innerDock, 1);
        rootGrid.Children.Add(innerDock);

        // Bottom status & buttons
        var bottomBar = new StackPanel();
        DockPanel.SetDock(bottomBar, Dock.Bottom);
        bottomBar.Children.Add(status);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        btnRow.Children.Add(startStop);
        btnRow.Children.Add(closeBtn);
        bottomBar.Children.Add(btnRow);
        innerDock.Children.Add(bottomBar);

        // Main stack
        var mainStack = new StackPanel();

        // Card 1: Input selector with Signal LED & VU meter (Clean UI without debug strings)
        var inputCard = new Border
        {
            Background = DarkUiTheme.AuraBgPanelBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var inStack = new StackPanel();

        var inputHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        inputHeaderRow.Children.Add(signalLed);
        inputHeaderRow.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Entrada de Audio (ASIO / WASAPI < 5ms / USB GP-200)" : "Audio Input (ASIO / WASAPI < 5ms / GP-200 USB)",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = DarkUiTheme.AuraCyanPrimaryBrush
        });
        inStack.Children.Add(inputHeaderRow);
        inStack.Children.Add(input);
        inStack.Children.Add(inputVuLabel);
        inStack.Children.Add(inputVuMeter);
        inputCard.Child = inStack;
        mainStack.Children.Add(inputCard);

        // Card 2: Hardware Controls (Mute/Bypass + 435-445 Hz Pitch Calibration)
        var hwCard = new Border
        {
            Background = DarkUiTheme.AuraBgCardBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var hwGrid = new Grid();
        hwGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hwGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hwGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        hwGrid.Children.Add(muteModeBtn);

        var pitchStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        pitchStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Calibración Pitch:" : "Pitch Calibration:",
            Foreground = DarkUiTheme.AuraTextMutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0)
        });

        var pitchDownBtn = new Button { Content = "−", Width = 28, Height = 26, Cursor = System.Windows.Input.Cursors.Hand };
        pitchDownBtn.Click += (_, _) =>
        {
            if (referencePitchHz > 435)
            {
                referencePitchHz--;
                pitchDisplay.Text = $"{referencePitchHz} Hz";
            }
        };

        var pitchUpBtn = new Button { Content = "+", Width = 28, Height = 26, Cursor = System.Windows.Input.Cursors.Hand };
        pitchUpBtn.Click += (_, _) =>
        {
            if (referencePitchHz < 445)
            {
                referencePitchHz++;
                pitchDisplay.Text = $"{referencePitchHz} Hz";
            }
        };

        pitchStack.Children.Add(pitchDownBtn);
        pitchStack.Children.Add(pitchDisplay);
        pitchStack.Children.Add(pitchUpBtn);

        Grid.SetColumn(pitchStack, 2);
        hwGrid.Children.Add(pitchStack);
        hwCard.Child = hwGrid;
        mainStack.Children.Add(hwCard);

        // Card 3: Tuner Display (GP-200R Strobe / Vertical Bar Style)
        var displayCard = new Border
        {
            Background = DarkUiTheme.AuraBgDeepBrush,
            BorderBrush = DarkUiTheme.AuraCyanPrimaryBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var dispStack = new StackPanel();
        dispStack.Children.Add(note);
        dispStack.Children.Add(frequency);
        dispStack.Children.Add(tuneBadge);
        dispStack.Children.Add(meter);
        displayCard.Child = dispStack;
        mainStack.Children.Add(displayCard);

        innerDock.Children.Add(new ScrollViewer
        {
            Content = mainStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        });

        Content = rootGrid;

        engine.SamplesAvailable += ProcessSamples;
        engine.StatusChanged += msg => Dispatcher.BeginInvoke(() =>
        {
            status.Text = msg;
            status.Foreground = msg.Contains("Error", StringComparison.OrdinalIgnoreCase) ? DarkUiTheme.DangerBrush :
                msg.Contains("fallback", StringComparison.OrdinalIgnoreCase) ? DarkUiTheme.WarningBrush : DarkUiTheme.AuraTextMutedBrush;
        });

        input.ItemsSource = engine.Devices.Select(d => d.Name).ToArray();
        if (engine.Devices.Count > 0)
        {
            input.SelectedIndex = engine.SelectedIndex >= 0 ? engine.SelectedIndex : 0;
            tuneBadge.Text = UiText.Language == "es" ? "Toca una cuerda · esperando señal" : "Play a string · waiting for signal";
        }
        else
        {
            tuneBadge.Text = UiText.Language == "es" ? "No hay entradas de audio disponibles" : "No audio inputs available";
            status.Text = tuneBadge.Text;
        }
        meter.SizeChanged += (_, _) => DrawMeter(currentCents);

        input.SelectionChanged += (_, _) =>
        {
            if (input.SelectedIndex >= 0)
            {
                lock (samples) samples.Clear();
                note.Text = "—";
                frequency.Text = "";
                currentCents = 0;
                noSignalDisplayed = false;
                tuneBadge.Text = UiText.Language == "es" ? "Toca una cuerda · esperando señal" : "Play a string · waiting for signal";
                DrawMeter(0);
                SetCaptureState(engine.Start(input.SelectedIndex));
            }
        };

        startStop.Click += (_, _) =>
        {
            if (engine.IsCapturing)
            {
                engine.Stop();
                startStop.Content = UiText.Language == "es" ? "Iniciar afinador" : "Start Tuner";
                startStop.Background = DarkUiTheme.HighlightBrush;
                inputVuMeter.Value = -60;
                inputVuLabel.Text = UiText.Language == "es" ? "Nivel de entrada: —" : "Input Level: —";
                signalLed.Background = Brushes.DimGray;
                note.Text = "—";
                frequency.Text = "";
                tuneBadge.Text = "";
                DrawMeter(0);
            }
            else
            {
                if (input.SelectedIndex >= 0)
                {
                    lock (samples) samples.Clear();
                    SetCaptureState(engine.Start(input.SelectedIndex));
                }
            }
        };

        closeBtn.Click += (_, _) => Close();
        Loaded += (_, _) =>
        {
            if (input.SelectedIndex >= 0) SetCaptureState(engine.Start(input.SelectedIndex));
        };
        Closed += (_, _) =>
        {
            closing = true;
            engine.Dispose();
        };

        DrawMeter(0);
    }

    void SetCaptureState(bool started)
    {
        startStop.Content = started
            ? (UiText.Language == "es" ? "Detener afinador" : "Stop Tuner")
            : (UiText.Language == "es" ? "Iniciar afinador" : "Start Tuner");
        startStop.Background = started ? DarkUiTheme.SuccessBrush : DarkUiTheme.HighlightBrush;
    }

    void ProcessSamples(float[] block, int sampleRate, double peakDbfs, double rmsDbfs)
    {
        if (closing || !engine.IsCapturing) return;

        Dispatcher.BeginInvoke(() =>
        {
            inputVuMeter.Value = Math.Clamp(peakDbfs, -60, 0);
            inputVuLabel.Text = $"{peakDbfs:+0.0;-0.0;0} dBFS (RMS: {rmsDbfs:+0.0;-0.0;0} dBFS)";
            signalLed.Background = peakDbfs > -45.0 ? DarkUiTheme.SuccessBrush : Brushes.DimGray;
        });

        lock (samples)
        {
            samples.AddRange(block);
            if (samples.Count > WindowSize * 4)
            {
                samples.RemoveRange(0, samples.Count - WindowSize * 2);
            }
        }

        while (true)
        {
            float[] window;
            lock (samples)
            {
                if (samples.Count < WindowSize) break;
                window = samples.Take(WindowSize).ToArray();
                samples.RemoveRange(0, WindowSize / 2);
            }

            var pitch = DetectPitch(window, sampleRate);
            Dispatcher.BeginInvoke(() => UpdatePitch(pitch));
        }
    }

    static (double Frequency, double Rms)? DetectPitch(float[] input, int sampleRate)
    {
        if (sampleRate <= 0) return null;
        double mean = input.Average();
        var signal = new double[input.Length];
        double squareSum = 0;
        for (int i = 0; i < input.Length; i++)
        {
            signal[i] = input[i] - mean;
            squareSum += signal[i] * signal[i];
        }
        double rms = Math.Sqrt(squareSum / input.Length);
        if (rms < 0.003) return null;

        int minLag = Math.Max(2, sampleRate / 1200);
        int maxLag = Math.Min(input.Length / 2, sampleRate / 40);
        var correlations = new double[maxLag + 1];
        double maximum = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double cross = 0, first = 0, second = 0;
            for (int i = 0; i < signal.Length - lag; i++)
            {
                double a = signal[i], b = signal[i + lag];
                cross += a * b;
                first += a * a;
                second += b * b;
            }
            double correlation = first > 0 && second > 0 ? cross / Math.Sqrt(first * second) : 0;
            correlations[lag] = correlation;
            maximum = Math.Max(maximum, correlation);
        }
        if (maximum < 0.50) return null;

        int bestLag = 0;
        for (int lag = minLag + 1; lag < maxLag; lag++)
        {
            if (correlations[lag] >= maximum * 0.88 &&
                correlations[lag] >= correlations[lag - 1] &&
                correlations[lag] >= correlations[lag + 1])
            {
                bestLag = lag;
                break;
            }
        }
        if (bestLag == 0) return null;

        double exactLag = bestLag;
        if (bestLag > 1 && bestLag < correlations.Length - 1)
        {
            double alpha = correlations[bestLag - 1];
            double beta = correlations[bestLag];
            double gamma = correlations[bestLag + 1];
            double denom = 2 * (2 * beta - alpha - gamma);
            if (Math.Abs(denom) > 1e-6)
            {
                exactLag += (alpha - gamma) / denom;
            }
        }

        double fundamental = sampleRate / exactLag;
        if (fundamental is < 20 or > 1200) return null;
        return (fundamental, rms);
    }

    void UpdatePitch((double Frequency, double Rms)? pitch)
    {
        if (pitch == null)
        {
            if (!noSignalDisplayed)
            {
                note.Text = "—";
                frequency.Text = "";
                tuneBadge.Text = UiText.Language == "es" ? "Sin señal · toca una cuerda" : "No signal · play a string";
                tuneBadge.Foreground = DarkUiTheme.AuraTextMutedBrush;
                currentCents = 0;
                DrawMeter(0);
                noSignalDisplayed = true;
            }
            return;
        }

        noSignalDisplayed = false;
        var (freq, _) = pitch.Value;
        double a4 = referencePitchHz;
        double midiNote = 69 + 12 * Math.Log2(freq / a4);
        int nearest = (int)Math.Round(midiNote);
        double cents = (midiNote - nearest) * 100.0;
        currentCents = cents;

        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        int noteIndex = (nearest % 12 + 12) % 12;
        int octave = nearest / 12 - 1;

        note.Text = $"{names[noteIndex]}{octave}";
        frequency.Text = $"{freq:F1} Hz";

        if (Math.Abs(cents) <= 2.0)
        {
            tuneBadge.Text = UiText.Language == "es" ? "✓ PERFECTAMENTE AFINADO (±0 cent)" : "✓ IN TUNE (±0 cent)";
            tuneBadge.Foreground = DarkUiTheme.SuccessBrush;
            note.Foreground = DarkUiTheme.SuccessBrush;
        }
        else if (cents < 0)
        {
            tuneBadge.Text = $"{cents:F0} cents · " + (UiText.Language == "es" ? "BEMOL (♭ Bajo)" : "FLAT (♭ Low)");
            tuneBadge.Foreground = cents < -15 ? DarkUiTheme.DangerBrush : DarkUiTheme.WarningBrush;
            note.Foreground = DarkUiTheme.AuraCyanPrimaryBrush;
        }
        else
        {
            tuneBadge.Text = $"+{cents:F0} cents · " + (UiText.Language == "es" ? "SOSTENIDO (♯ Alto)" : "SHARP (♯ High)");
            tuneBadge.Foreground = cents > 15 ? DarkUiTheme.DangerBrush : DarkUiTheme.WarningBrush;
            note.Foreground = DarkUiTheme.AuraCyanPrimaryBrush;
        }

        DrawMeter(cents);
    }

    /// <summary>
    /// Renders the GP-200R style vertical illuminated strobe / segmented bar meter.
    /// Red/amber at extremes, vibrant emerald-green / cyan in the exact center ±1 cent.
    /// </summary>
    void DrawMeter(double cents)
    {
        meter.Children.Clear();
        double w = meter.ActualWidth > 0 ? meter.ActualWidth : 440;
        double h = meter.ActualHeight > 0 ? meter.ActualHeight : 80;
        double cx = w / 2;

        int barsPerSide = 24;
        double barWidth = Math.Max(3, (w - 60) / (barsPerSide * 2 + 1));
        double spacing = barWidth + 2;

        int activeBar = (int)Math.Clamp(Math.Round(cents / 2.0), -barsPerSide, barsPerSide);

        for (int i = -barsPerSide; i <= barsPerSide; i++)
        {
            double x = cx + i * spacing - barWidth / 2;
            double barHeight = Math.Abs(i) switch
            {
                0 => h - 16,
                <= 3 => h - 22,
                <= 8 => h - 30,
                <= 16 => h - 38,
                _ => h - 46
            };
            double y = (h - barHeight) / 2;

            bool isLit = false;
            if (activeBar == 0)
            {
                isLit = (i is >= -1 and <= 1);
            }
            else if (activeBar > 0)
            {
                isLit = (i >= 0 && i <= activeBar);
            }
            else
            {
                isLit = (i <= 0 && i >= activeBar);
            }

            Brush barBrush;
            if (isLit)
            {
                if (Math.Abs(i) <= 1)
                    barBrush = DarkUiTheme.SuccessBrush; // Center In-Tune: Emerald Green
                else if (Math.Abs(i) <= 8)
                    barBrush = DarkUiTheme.AuraCyanPrimaryBrush; // Near Center: Vibrant Cyan
                else if (Math.Abs(i) <= 16)
                    barBrush = DarkUiTheme.WarningBrush; // Moderate: Amber
                else
                    barBrush = DarkUiTheme.DangerBrush; // Out of tune: Red
            }
            else
            {
                barBrush = new SolidColorBrush(Color.FromArgb(40, 30, 58, 95)); // Dim inactive bar
            }

            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 2,
                RadiusY = 2,
                Fill = barBrush
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            meter.Children.Add(rect);
        }

        // Center cursor triangle
        var centerMarker = new Polygon
        {
            Points = new PointCollection { new Point(cx - 5, h - 6), new Point(cx + 5, h - 6), new Point(cx, h - 14) },
            Fill = DarkUiTheme.SuccessBrush
        };
        meter.Children.Add(centerMarker);
    }
}
