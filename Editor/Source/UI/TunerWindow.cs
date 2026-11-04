using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gp200.Replica;

internal sealed class TunerWindow : Window
{
    const int WindowSize = 4096;
    readonly ComboBox input = new() { MinWidth = 280, Margin = new Thickness(0, 0, 0, 6) };
    readonly TextBlock note = new() { Text = "—", FontSize = 72, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushHex("#00E5FF"), Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock frequency = new() { FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 2, 0, 4) };
    readonly TextBlock tuneBadge = new() { FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 8) };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = BrushHex("#94A3B8"), FontSize = 12 };
    readonly Canvas meter = new() { Height = 90, Margin = new Thickness(12, 8, 12, 8) };
    readonly ProgressBar inputVuMeter = new() { Height = 8, Minimum = -60, Maximum = 0, Value = -60, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock inputVuLabel = new() { Text = UiText.Language == "es" ? "Nivel de entrada (Input VU): —" : "Input Level (VU): —", FontSize = 11, Foreground = BrushHex("#94A3B8") };
    readonly Button startStop = new() { MinWidth = 140, Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button closeBtn = new() { Content = UiText.Language == "es" ? "Cerrar" : "Close", MinWidth = 90, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };
    
    // Hardware Tuner Controls
    int referencePitchHz = 440;
    bool isMutedMode = true;
    readonly TextBlock pitchDisplay = new() { Text = "440 Hz", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = BrushHex("#00E5FF"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    readonly Button muteModeBtn = new() { Content = UiText.Language == "es" ? "Modo: MUTE (Silencio)" : "Mode: MUTE (Silent)", MinWidth = 140, Cursor = System.Windows.Input.Cursors.Hand };

    readonly List<float> samples = new(WindowSize * 2);
    readonly AudioCaptureEngine engine = new();
    bool closing;

    internal TunerWindow()
    {
        DarkUiTheme.Apply(this);
        Title = UiText.Language == "es" ? "Afinador Cromático de Estudio · GP-200" : "Studio Chromatic Tuner · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 580;
        Height = 640;
        MinWidth = 480;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        startStop.Content = UiText.Language == "es" ? "Detener afinador" : "Stop Tuner";
        startStop.Background = BrushHex("#10B981");
        startStop.Foreground = Brushes.White;
        startStop.FontWeight = FontWeights.Bold;

        closeBtn.Background = BrushHex("#142036");
        closeBtn.Foreground = BrushHex("#F8FAFC");
        closeBtn.BorderBrush = BrushHex("#3300E5FF");

        muteModeBtn.Background = BrushHex("#0077B6");
        muteModeBtn.Foreground = Brushes.White;
        muteModeBtn.Click += (_, _) =>
        {
            isMutedMode = !isMutedMode;
            muteModeBtn.Content = isMutedMode
                ? (UiText.Language == "es" ? "Modo: MUTE (Silencio)" : "Mode: MUTE (Silent)")
                : (UiText.Language == "es" ? "Modo: BYPASS (Sonido)" : "Mode: BYPASS (Audible)");
            muteModeBtn.Background = isMutedMode ? BrushHex("#0077B6") : BrushHex("#334155");
        };

        var root = new DockPanel { Margin = new Thickness(16) };

        // Bottom status & buttons
        var bottomBar = new StackPanel();
        DockPanel.SetDock(bottomBar, Dock.Bottom);
        bottomBar.Children.Add(status);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        btnRow.Children.Add(startStop);
        btnRow.Children.Add(closeBtn);
        bottomBar.Children.Add(btnRow);
        root.Children.Add(bottomBar);

        // Main stack
        var mainStack = new StackPanel();

        // Card 1: Input selector & VU meter
        var inputCard = new Border
        {
            Background = BrushHex("#0D1524"),
            BorderBrush = BrushHex("#3300E5FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var inStack = new StackPanel();
        inStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Entrada de Audio (Prioridad GP-200 y Loopback PC)" : "Audio Input (GP-200 Priority & PC Loopback)",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#00E5FF"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        inStack.Children.Add(input);
        inStack.Children.Add(inputVuLabel);
        inStack.Children.Add(inputVuMeter);
        inputCard.Child = inStack;
        mainStack.Children.Add(inputCard);

        // Card 2: Hardware Controls (Mute/Bypass + 435-445 Hz Reference)
        var hwCard = new Border
        {
            Background = BrushHex("#142036"),
            BorderBrush = BrushHex("#3300E5FF"),
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
            Text = UiText.Language == "es" ? "Calibración A4:" : "A4 Reference:",
            Foreground = BrushHex("#94A3B8"),
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

        // Card 3: Tuner Pedal Display
        var displayCard = new Border
        {
            Background = BrushHex("#070B12"),
            BorderBrush = BrushHex("#00E5FF"),
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

        root.Children.Add(mainStack);

        engine.SamplesAvailable += ProcessSamples;
        engine.StatusChanged += msg => Dispatcher.BeginInvoke(() => { status.Text = msg; status.Foreground = BrushHex("#EF4444"); });

        input.ItemsSource = engine.Devices.Select(d => d.Name).ToArray();
        if (engine.Devices.Count > 0)
        {
            input.SelectedIndex = engine.SelectedIndex >= 0 ? engine.SelectedIndex : 0;
        }

        input.SelectionChanged += (_, _) =>
        {
            if (input.SelectedIndex >= 0)
            {
                lock (samples) samples.Clear();
                engine.Start(input.SelectedIndex);
                startStop.Content = UiText.Language == "es" ? "Detener afinador" : "Stop Tuner";
                startStop.Background = BrushHex("#10B981");
            }
        };

        startStop.Click += (_, _) =>
        {
            if (engine.IsCapturing)
            {
                engine.Stop();
                startStop.Content = UiText.Language == "es" ? "Iniciar afinador" : "Start Tuner";
                startStop.Background = BrushHex("#2563EB");
                inputVuMeter.Value = -60;
                inputVuLabel.Text = UiText.Language == "es" ? "Nivel de entrada (Input VU): —" : "Input Level (VU): —";
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
                    engine.Start(input.SelectedIndex);
                    startStop.Content = UiText.Language == "es" ? "Detener afinador" : "Stop Tuner";
                    startStop.Background = BrushHex("#10B981");
                }
            }
        };

        closeBtn.Click += (_, _) => Close();
        Loaded += (_, _) =>
        {
            if (input.SelectedIndex >= 0) engine.Start(input.SelectedIndex);
        };
        Closed += (_, _) =>
        {
            closing = true;
            engine.Dispose();
        };

        DrawMeter(0);
    }

    void ProcessSamples(float[] block, int sampleRate, double peakDbfs, double rmsDbfs)
    {
        if (closing || !engine.IsCapturing) return;

        Dispatcher.BeginInvoke(() =>
        {
            inputVuMeter.Value = Math.Clamp(peakDbfs, -60, 0);
            inputVuLabel.Text = $"{UiText.Language switch { "es" => "Nivel de entrada: ", _ => "Input Level: " }}{peakDbfs:+0.0;-0.0;0} dBFS (RMS: {rmsDbfs:+0.0;-0.0;0} dBFS)";
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
            tuneBadge.Text = "";
            return;
        }

        var (f, _) = pitch.Value;
        // Calcular número de semitonos con respecto a A4 calibrado (referencePitchHz)
        double semitonesFromA4 = 12 * Math.Log2(f / referencePitchHz);
        int midiNote = (int)Math.Round(semitonesFromA4) + 69;
        double cents = (semitonesFromA4 - Math.Round(semitonesFromA4)) * 100;

        string[] noteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        string noteName = noteNames[(midiNote % 12 + 12) % 12];
        int octave = (midiNote / 12) - 1;

        note.Text = $"{noteName}{octave}";
        frequency.Text = $"{f:F1} Hz";

        if (Math.Abs(cents) < 4.0)
        {
            tuneBadge.Text = UiText.Language == "es" ? "● PERFECTO (AFINADO)" : "● PERFECT (IN TUNE)";
            tuneBadge.Foreground = BrushHex("#10B981");
            note.Foreground = BrushHex("#10B981");
        }
        else if (cents < 0)
        {
            tuneBadge.Text = $"{cents:F0} cents (GRAVE / FLAT)";
            tuneBadge.Foreground = BrushHex("#F59E0B");
            note.Foreground = BrushHex("#00E5FF");
        }
        else
        {
            tuneBadge.Text = $"+{cents:F0} cents (AGUDO / SHARP)";
            tuneBadge.Foreground = BrushHex("#EF4444");
            note.Foreground = BrushHex("#00E5FF");
        }

        DrawMeter(cents);
    }

    void DrawMeter(double cents)
    {
        meter.Children.Clear();
        double w = meter.ActualWidth > 0 ? meter.ActualWidth : 500;
        double h = meter.Height;
        double cx = w / 2;

        // Barra de escala central
        var track = new Line
        {
            X1 = 20,
            X2 = w - 20,
            Y1 = h / 2,
            Y2 = h / 2,
            Stroke = BrushHex("#1E293B"),
            StrokeThickness = 4
        };
        meter.Children.Add(track);

        // Marcas de división
        for (int c = -50; c <= 50; c += 10)
        {
            double x = cx + (c / 50.0) * (cx - 30);
            var mark = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = (c == 0) ? h / 2 - 18 : h / 2 - 8,
                Y2 = (c == 0) ? h / 2 + 18 : h / 2 + 8,
                Stroke = (c == 0) ? BrushHex("#10B981") : BrushHex("#334155"),
                StrokeThickness = (c == 0) ? 3 : 1.5
            };
            meter.Children.Add(mark);
        }

        // Aguja indicadora fluida con brillo AuraCode
        double clampedCents = Math.Clamp(cents, -50, 50);
        double needleX = cx + (clampedCents / 50.0) * (cx - 30);

        Color needleColor = Math.Abs(clampedCents) < 4.0
            ? Color.FromRgb(16, 185, 129)
            : (clampedCents < 0 ? Color.FromRgb(0, 229, 255) : Color.FromRgb(239, 68, 68));

        var needle = new Line
        {
            X1 = needleX,
            X2 = needleX,
            Y1 = 8,
            Y2 = h - 8,
            Stroke = new SolidColorBrush(needleColor),
            StrokeThickness = 4
        };
        meter.Children.Add(needle);

        var glow = new Line
        {
            X1 = needleX,
            X2 = needleX,
            Y1 = 4,
            Y2 = h - 4,
            Stroke = new SolidColorBrush(Color.FromArgb(90, needleColor.R, needleColor.G, needleColor.B)),
            StrokeThickness = 10
        };
        meter.Children.Add(glow);
    }

    static Brush BrushHex(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}
