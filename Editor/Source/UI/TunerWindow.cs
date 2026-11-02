using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gp200.Replica;

internal sealed class TunerWindow : Window
{
    const int WindowSize = 4096;
    readonly ComboBox input = new() { MinWidth = 280, Margin = new Thickness(0, 0, 0, 6) };
    readonly TextBlock note = new() { Text = "—", FontSize = 64, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushHex("#F8FAFC"), Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock frequency = new() { FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 2, 0, 4) };
    readonly TextBlock tuneBadge = new() { FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 8) };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = BrushHex("#94A3B8"), FontSize = 12 };
    readonly TextBlock diagBadge = new() { Text = "", FontSize = 11, Foreground = BrushHex("#38BDF8"), Margin = new Thickness(0, 2, 0, 6) };
    readonly Canvas meter = new() { Height = 80, Margin = new Thickness(12, 10, 12, 10) };
    readonly ProgressBar inputVuMeter = new() { Height = 8, Minimum = -60, Maximum = 0, Value = -60, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock inputVuLabel = new() { Text = UiText.Language == "es" ? "Nivel de entrada (Input VU): —" : "Input Level (VU): —", FontSize = 11, Foreground = BrushHex("#94A3B8") };
    readonly Button startStop = new() { MinWidth = 140, Cursor = System.Windows.Input.Cursors.Hand };
    readonly Button closeBtn = new() { Content = UiText.Language == "es" ? "Cerrar" : "Close", MinWidth = 90, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };
    
    readonly List<float> samples = new(WindowSize * 2);
    readonly AudioCaptureEngine engine = new();
    bool closing;

    internal TunerWindow()
    {
        DarkUiTheme.Apply(this);
        Title = UiText.Language == "es" ? "Afinador de Audio de Estudio · GP-200" : "Studio Audio Tuner · GP-200";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Width = 540;
        Height = 580;
        MinWidth = 460;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        startStop.Content = UiText.Language == "es" ? "Detener afinador" : "Stop Tuner";
        startStop.Background = BrushHex("#10B981");
        startStop.Foreground = Brushes.White;
        startStop.FontWeight = FontWeights.Bold;

        closeBtn.Background = BrushHex("#334155");
        closeBtn.Foreground = BrushHex("#F8FAFC");

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

        // Card 1: Input selector & VU meter & Diagnostics
        var inputCard = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var inStack = new StackPanel();
        inStack.Children.Add(new TextBlock
        {
            Text = UiText.Language == "es" ? "Entrada de Audio (Prioridad GP-200 y Loopback PC)" : "Audio Input (GP-200 Priority & PC Loopback)",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#38BDF8"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        inStack.Children.Add(input);
        inStack.Children.Add(diagBadge);
        inStack.Children.Add(inputVuLabel);
        inStack.Children.Add(inputVuMeter);
        inputCard.Child = inStack;
        mainStack.Children.Add(inputCard);

        // Card 2: Tuner Pedal Display
        var displayCard = new Border
        {
            Background = BrushHex("#18202C"),
            BorderBrush = BrushHex("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var dispStack = new StackPanel();
        dispStack.Children.Add(note);
        dispStack.Children.Add(frequency);
        dispStack.Children.Add(tuneBadge);
        meter.SizeChanged += (_, _) => DrawMeter(0);
        dispStack.Children.Add(meter);
        displayCard.Child = dispStack;
        mainStack.Children.Add(displayCard);

        root.Children.Add(mainStack);
        Content = root;

        // Wire events
        engine.StatusChanged += text => Dispatcher.BeginInvoke(() => { diagBadge.Text = text; });
        engine.SamplesAvailable += ProcessSamples;

        input.ItemsSource = engine.Devices.Select(d => d.Name).ToArray();
        if (engine.SelectedIndex >= 0 && engine.SelectedIndex < engine.Devices.Count)
        {
            input.SelectedIndex = engine.SelectedIndex;
        }

        input.SelectionChanged += (_, _) =>
        {
            if (input.SelectedIndex >= 0)
            {
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
                diagBadge.Text = UiText.Language == "es" ? "Afinador pausado." : "Tuner paused.";
                inputVuMeter.Value = -60;
                inputVuLabel.Text = UiText.Language == "es" ? "Nivel de entrada: —" : "Input Level: —";
                note.Text = "—";
                frequency.Text = "";
                tuneBadge.Text = "";
                DrawMeter(0);
            }
            else
            {
                if (input.SelectedIndex >= 0)
                {
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

        double left = correlations[bestLag - 1], center = correlations[bestLag], right = correlations[bestLag + 1];
        double denominator = left - 2 * center + right;
        double correction = Math.Abs(denominator) < 1e-12 ? 0 : 0.5 * (left - right) / denominator;
        double period = bestLag + Math.Clamp(correction, -0.5, 0.5);
        return (sampleRate / period, rms);
    }

    void UpdatePitch((double Frequency, double Rms)? detected)
    {
        if (!engine.IsCapturing) return;
        if (detected is not { } pitch)
        {
            tuneBadge.Text = "";
            return;
        }

        int midi = (int)Math.Round(69 + 12 * Math.Log2(pitch.Frequency / 440));
        double cents = 1200 * Math.Log2(pitch.Frequency / (440 * Math.Pow(2, (midi - 69) / 12.0)));
        string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
        note.Text = $"{names[(midi % 12 + 12) % 12]}{midi / 12 - 1}";
        frequency.Text = $"{pitch.Frequency:F1} Hz   ({cents:+0.0;-0.0;0} cents)";

        bool inTune = Math.Abs(cents) <= 3.0;
        if (inTune)
        {
            tuneBadge.Text = UiText.Language == "es" ? "✓ AFINADO" : "✓ IN TUNE";
            tuneBadge.Foreground = BrushHex("#10B981");
            note.Foreground = BrushHex("#10B981");
        }
        else if (cents < 0)
        {
            tuneBadge.Text = UiText.Language == "es" ? "♭ GRAVE / BEMOL (Sube la cuerda)" : "♭ FLAT (Tune Up)";
            tuneBadge.Foreground = BrushHex("#F59E0B");
            note.Foreground = BrushHex("#F59E0B");
        }
        else
        {
            tuneBadge.Text = UiText.Language == "es" ? "♯ AGUDO / SOSTENIDO (Baja la cuerda)" : "♯ SHARP (Tune Down)";
            tuneBadge.Foreground = BrushHex("#06B6D4");
            note.Foreground = BrushHex("#06B6D4");
        }

        DrawMeter(cents);
    }

    void DrawMeter(double cents)
    {
        meter.Children.Clear();
        double width = meter.ActualWidth;
        if (width <= 0) width = 440;
        double height = meter.ActualHeight;
        if (height <= 0) height = 80;
        double center = width / 2;

        meter.Children.Add(new Rectangle
        {
            Width = width - 20,
            Height = 12,
            RadiusX = 6,
            RadiusY = 6,
            Fill = BrushHex("#1E2736"),
            Margin = new Thickness(10, height / 2 - 6, 0, 0)
        });

        meter.Children.Add(new Rectangle
        {
            Width = width * 0.08,
            Height = 16,
            RadiusX = 4,
            RadiusY = 4,
            Fill = BrushHex("#064E3B"),
            Margin = new Thickness(center - width * 0.04, height / 2 - 8, 0, 0)
        });

        for (int c = -50; c <= 50; c += 10)
        {
            double x = center + (c / 50.0) * (center * 0.85);
            bool isMajor = c == 0 || c == -50 || c == 50;
            meter.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = height / 2 - (isMajor ? 18 : 10),
                Y2 = height / 2 + (isMajor ? 18 : 10),
                Stroke = isMajor ? BrushHex("#94A3B8") : BrushHex("#334155"),
                StrokeThickness = isMajor ? 2 : 1
            });
        }

        meter.Children.Add(new Line
        {
            X1 = center,
            X2 = center,
            Y1 = 8,
            Y2 = height - 8,
            Stroke = BrushHex("#F0F4F8"),
            StrokeThickness = 2
        });

        double needleX = center + (Math.Clamp(cents, -50, 50) / 50.0) * (center * 0.85);
        bool inTune = Math.Abs(cents) <= 3.0;
        Brush needleColor = inTune ? BrushHex("#10B981") : (cents < 0 ? BrushHex("#F59E0B") : BrushHex("#06B6D4"));

        meter.Children.Add(new Line
        {
            X1 = needleX,
            X2 = needleX,
            Y1 = 4,
            Y2 = height - 4,
            Stroke = needleColor,
            StrokeThickness = 4
        });
    }

    static Brush BrushHex(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}
