using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Gp200.Replica;

/// <summary>
/// Fully interactive AuraCode Parametric & Graphic EQ curve analyzer.
/// Features real-time draggable glowing nodes, mouse wheel Q-factor adjustment,
/// 30 ms debounced live SysEx / model updates, and 60 FPS biquad curve rendering.
/// </summary>
public sealed class EqGraph : FrameworkElement
{
    readonly Dictionary<int, float> values;
    readonly Dictionary<int, float> confirmed;
    readonly bool enabled;
    int dragging = -1;
    Point origin;
    Point currentMousePos;
    int hoveredNode = -1;

    // 30 ms Debounce timer for live hardware streaming
    readonly DispatcherTimer debounceTimer;
    bool pendingCommit = false;

    public event Action<IReadOnlyList<DeviceSettingChange>>? Committed;
    public event Action<IReadOnlyList<DeviceSettingChange>>? LiveDebouncedChange;

    public static readonly int[] Frequencies = [1, 8, 12, 16, 20, 28];
    public static readonly string[] BandLabels = ["LC", "BASS", "L-MID", "MID", "H-MID", "HC"];

    public EqGraph(DeviceSettings state)
    {
        values = DeviceSettings.Equalizer.ToDictionary(d => d.Selector, d => state.Eq(d.Selector));
        enabled = state.Global(30) != 0;
        confirmed = new(values);
        Height = 280;
        MinWidth = 460;
        Margin = new Thickness(0, 8, 0, 12);
        Cursor = Cursors.Cross;
        ToolTip = UiText.Language == "es"
            ? "Arrastra los nodos para ajustar ganancia y frecuencia en tiempo real. Usa la rueda del ratón para ajustar Q (ancho de banda)."
            : "Drag nodes to adjust gain and frequency in real time. Use mouse wheel over nodes to adjust Q factor.";

        debounceTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        debounceTimer.Tick += OnDebounceTick;
    }

    Rect Plot => new(48, 16, Math.Max(1, ActualWidth - 68), Math.Max(1, ActualHeight - 48));

    public static double Frequency(double fraction) => 20 * Math.Pow(1000, Math.Clamp(fraction, 0, 1));
    public static double Fraction(double hz) => Math.Log10(Math.Clamp(hz, 20, 20000) / 20) / 3.0;

    double X(double hz) => Plot.Left + Fraction(hz) * Plot.Width;
    double Y(double gain) => Plot.Top + (20 - Math.Clamp(gain, -20, 20)) / 40.0 * Plot.Height;

    public Point Handle(int i)
    {
        int freqSelector = Frequencies[i];
        float hz = values.TryGetValue(freqSelector, out float h) ? h : 1000f;
        float gain = 0f;
        if (i is > 0 and < 5 && values.TryGetValue(freqSelector + 2, out float g))
        {
            gain = g;
        }
        return new Point(X(hz), Y(gain));
    }

    void Text(DrawingContext dc, string text, Point at, Brush? color = null, double size = 11, bool bold = false)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size,
            color ?? DarkUiTheme.AuraTextMutedBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, at);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // 1. Panel Background
        var bgRect = new Rect(RenderSize);
        dc.DrawRoundedRectangle(DarkUiTheme.AuraBgInputBrush, new Pen(DarkUiTheme.AuraBorderSubtleBrush, 1), bgRect, 8, 8);

        // 2. Frequency Grid Lines & Labels
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 229, 255)), 0.5);
        var zeroDbPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 229, 255)), 1);

        foreach (int hz in new[] { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            double x = X(hz);
            dc.DrawLine(gridPen, new Point(x, Plot.Top), new Point(x, Plot.Bottom));
            if (hz is 20 or 100 or 1000 or 10000 or 20000)
            {
                string label = hz >= 1000 ? $"{hz / 1000}k" : hz.ToString();
                Text(dc, label, new Point(x - 9, Plot.Bottom + 5), DarkUiTheme.AuraTextMutedBrush, 10);
            }
        }

        // 3. dB Gain Lines & Labels
        foreach (int db in new[] { -15, -10, -5, 0, 5, 10, 15 })
        {
            double y = Y(db);
            dc.DrawLine(db == 0 ? zeroDbPen : gridPen, new Point(Plot.Left, y), new Point(Plot.Right, y));
            Text(dc, (db > 0 ? $"+{db}" : db.ToString()), new Point(14, y - 7), db == 0 ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraTextMutedBrush, 10);
        }

        // 4. Fill under Curve with Gradient Glassmorphism
        var fillGeometry = new StreamGeometry();
        using (var g = fillGeometry.Open())
        {
            g.BeginFigure(new Point(Plot.Left, Y(0)), true, true);
            for (int i = 0; i <= 360; i++)
            {
                double f = Frequency(i / 360.0);
                g.LineTo(new Point(X(f), Y(ResponseDb(values, f))), true, false);
            }
            g.LineTo(new Point(Plot.Right, Y(0)), true, false);
        }
        var gradientFill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(60, 0, 229, 255), 0.0),
                new GradientStop(Color.FromArgb(10, 0, 119, 182), 1.0)
            }
        };
        dc.PushClip(new RectangleGeometry(Plot));
        dc.DrawGeometry(gradientFill, null, fillGeometry);

        // 5. Curve Outline
        var curveGeometry = new StreamGeometry();
        using (var g = curveGeometry.Open())
        {
            for (int i = 0; i <= 360; i++)
            {
                double f = Frequency(i / 360.0);
                var pt = new Point(X(f), Y(ResponseDb(values, f)));
                if (i == 0) g.BeginFigure(pt, false, false);
                else g.LineTo(pt, true, false);
            }
        }
        dc.DrawGeometry(null, new Pen(enabled ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraTextMutedBrush, 2.2), curveGeometry);
        dc.Pop();

        // 6. Draw Glowing Circular Thumb Nodes
        for (int i = 0; i < 6; i++)
        {
            var p = Handle(i);
            bool isActive = (i == dragging || i == hoveredNode);
            var nodeBrush = isActive ? DarkUiTheme.AuraCyanPrimaryBrush : new SolidColorBrush(Color.FromRgb(0, 180, 216));
            var borderPen = new Pen(isActive ? Brushes.White : DarkUiTheme.AuraBgDeepBrush, isActive ? 2 : 1.5);

            // Glow ring on active node
            if (isActive)
            {
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 229, 255)), 4), p, 11, 11);
            }

            dc.DrawEllipse(nodeBrush, borderPen, p, 7, 7);
            Text(dc, BandLabels[i], new Point(p.X - 10, p.Y - 24), isActive ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraTextMainBrush, 10, bold: true);
        }

        // 7. Floating Tooltip Badge with exact Hz and dB
        int activeNodeIndex = dragging >= 0 ? dragging : hoveredNode;
        if (activeNodeIndex >= 0)
        {
            int selector = Frequencies[activeNodeIndex];
            float hzVal = values.TryGetValue(selector, out float hv) ? hv : 0;
            string hzText = hzVal >= 1000 ? $"{hzVal / 1000.0:F2} kHz" : $"{hzVal:F0} Hz";
            string tooltipText = BandLabels[activeNodeIndex] + ": " + hzText;
            if (activeNodeIndex is > 0 and < 5)
            {
                float dbVal = values.TryGetValue(selector + 2, out float dv) ? dv : 0;
                float qVal = values.TryGetValue(selector + 1, out float qv) ? qv : 1.0f;
                tooltipText += $" · {(dbVal >= 0 ? "+" : "")}{dbVal:F1} dB · Q:{qVal:F2}";
            }

            // Draw floating HUD tag
            var hudPos = new Point(Plot.Left + 12, Plot.Top + 10);
            var hudBg = new Rect(hudPos.X - 4, hudPos.Y - 3, 210, 22);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 13, 21, 38)), new Pen(DarkUiTheme.AuraCyanPrimaryBrush, 1), hudBg, 4, 4);
            Text(dc, tooltipText, hudPos, DarkUiTheme.AuraCyanPrimaryBrush, 11, bold: true);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(this);

        // Hit-test with 12px radius as specified
        dragging = -1;
        for (int i = 0; i < 6; i++)
        {
            if (Point.Subtract(point, Handle(i)).Length <= 12)
            {
                dragging = i;
                break;
            }
        }

        if (dragging < 0)
        {
            // Fallback: Closest within 20px
            var closest = Enumerable.Range(0, 6).OrderBy(i => (Handle(i) - point).LengthSquared).First();
            if ((Handle(closest) - point).Length <= 20)
                dragging = closest;
        }

        if (dragging >= 0)
        {
            origin = point;
            currentMousePos = point;
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        currentMousePos = point;

        if (dragging >= 0 && IsMouseCaptured)
        {
            // Convert X to logarithmic Frequency (20 Hz - 20 kHz)
            double xFraction = (point.X - Plot.Left) / Math.Max(1, Plot.Width);
            float newHz = (float)Math.Round(Frequency(xFraction));

            int selector = Frequencies[dragging];
            if (dragging == 0) // Low Cut
                values[selector] = Math.Clamp(newHz, 20f, 600f);
            else if (dragging == 5) // High Cut
                values[selector] = Math.Clamp(newHz, 1000f, 20000f);
            else // Parametric bands
            {
                values[selector] = Math.Clamp(newHz, 40f, 18000f);
                // Convert Y to Gain (-15 dB to +15 dB or -20 to +20)
                float newGain = (float)Math.Clamp(20 - 40 * (point.Y - Plot.Top) / Plot.Height, -15, 15);
                values[selector + 2] = (float)Math.Round(newGain, 1);
            }

            pendingCommit = true;
            if (!debounceTimer.IsEnabled)
                debounceTimer.Start();

            InvalidateVisual();
            e.Handled = true;
        }
        else
        {
            // Hover test
            int prevHovered = hoveredNode;
            hoveredNode = -1;
            for (int i = 0; i < 6; i++)
            {
                if (Point.Subtract(point, Handle(i)).Length <= 14)
                {
                    hoveredNode = i;
                    break;
                }
            }
            if (hoveredNode != prevHovered)
                InvalidateVisual();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        int targetNode = dragging >= 0 ? dragging : hoveredNode;
        if (targetNode is > 0 and < 5)
        {
            int selector = Frequencies[targetNode];
            int qSelector = selector + 1; // Q factor selector
            float currentQ = values.TryGetValue(qSelector, out float q) ? q : 1.4f;

            // Delta adjustment
            float deltaQ = e.Delta > 0 ? 0.15f : -0.15f;
            values[qSelector] = Math.Clamp(currentQ + deltaQ, 0.4f, 8.0f);

            pendingCommit = true;
            if (!debounceTimer.IsEnabled)
                debounceTimer.Start();

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragging < 0) return;

        int handle = dragging;
        dragging = -1;
        ReleaseMouseCapture();
        InvalidateVisual();

        // Final commit
        DispatchCommit(handle);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (dragging >= 0)
        {
            foreach (var pair in confirmed)
                values[pair.Key] = pair.Value;
        }
        dragging = -1;
        debounceTimer.Stop();
        InvalidateVisual();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        if (pendingCommit && dragging >= 0)
        {
            pendingCommit = false;
            var changes = BuildChanges(dragging);
            LiveDebouncedChange?.Invoke(changes);
        }
        else
        {
            debounceTimer.Stop();
        }
    }

    private List<DeviceSettingChange> BuildChanges(int handle)
    {
        int selector = Frequencies[handle];
        var changes = new List<DeviceSettingChange>
        {
            DeviceSettings.SetEq(selector, values[selector])
        };
        if (handle is > 0 and < 5)
        {
            if (values.TryGetValue(selector + 1, out float q))
                changes.Add(DeviceSettings.SetEq(selector + 1, q));
            if (values.TryGetValue(selector + 2, out float gain))
                changes.Add(DeviceSettings.SetEq(selector + 2, gain));
        }
        return changes;
    }

    private void DispatchCommit(int handle)
    {
        var changes = BuildChanges(handle);
        foreach (var kvp in values)
            confirmed[kvp.Key] = kvp.Value;
        Committed?.Invoke(changes);
    }

    public static double ResponseDb(IReadOnlyDictionary<int, float> v, double frequency)
    {
        double omega = 2 * Math.PI * frequency / 44100;
        Complex z = Complex.FromPolarCoordinates(1, -omega);
        Complex z2 = z * z;
        double db = 0;

        foreach (int s in new[] { 8, 12, 16, 20 })
        {
            float gain = v.TryGetValue(s + 2, out float g) ? g : 0f;
            float q = v.TryGetValue(s + 1, out float qVal) ? Math.Max(0.1f, qVal) : 1.4f;
            float freq = v.TryGetValue(s, out float f) ? f : 1000f;

            double a = Math.Pow(10, gain / 40.0);
            double w = 2 * Math.PI * freq / 44100;
            double alpha = Math.Sin(w) / (2 * q);
            double c = -2 * Math.Cos(w);

            var numerator = (1 + alpha * a) + c * z + (1 - alpha * a) * z2;
            var denominator = (1 + alpha / a) + c * z + (1 - alpha / a) * z2;
            db += 20 * Math.Log10(Math.Max(1e-12, (numerator / denominator).Magnitude));
        }

        if (v.TryGetValue(1, out float lc) && lc > 19)
            db -= 10 * Math.Log10(1 + Math.Pow(lc / frequency, 4));

        if (v.TryGetValue(28, out float hc) && hc < 20001)
            db -= 10 * Math.Log10(1 + Math.Pow(frequency / hc, 4));

        return db;
    }
}
