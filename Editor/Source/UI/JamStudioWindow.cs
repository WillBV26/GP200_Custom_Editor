using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using IOPath = System.IO.Path;

namespace Gp200.Replica;

/// <summary>
/// Automation marker to trigger a preset or scene change on the Valeton GP-200 at a specific timeline timestamp.
/// </summary>
public sealed class TimelinePresetTrigger
{
    public double TimeSeconds { get; set; }
    public string Label { get; set; } = "Change Scene";
    public int TargetScene { get; set; } = -1; // 0=A, 1=B, 2=C, 3=D, -1=Preset Slot
    public int TargetSlot { get; set; } = -1;  // 0..255
    public bool Triggered { get; set; }

    public string DisplayTime => TimeSpan.FromSeconds(TimeSeconds).ToString(@"mm\:ss\.ff");
    public string DisplayAction => TargetScene >= 0
        ? $"Scene {(char)('A' + TargetScene)}"
        : (TargetSlot >= 0 ? $"Slot {BankService.Position(TargetSlot)}" : "No action");
}

/// <summary>
/// Audio clip placed inside a DAW track timeline.
/// </summary>
public sealed class JamClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public double StartOffsetSeconds { get; set; } = 0.0;
    public double SkipSeconds { get; set; } = 0.0;
    public double TakeSeconds { get; set; } = 0.0;
    public double TotalDurationSeconds { get; set; } = 0.0;
    public float[]? WaveformPeaks { get; set; }
    public bool IsMuted { get; set; } = false;
    public Color Color { get; set; } = Color.FromRgb(0, 229, 255);

    public double EndSeconds => StartOffsetSeconds + (TakeSeconds > 0 ? TakeSeconds : Math.Max(0, TotalDurationSeconds - SkipSeconds));
}

/// <summary>
/// Audio track representing a musical stem or recording channel in the DAW.
/// Supports multiple clips on the same timeline lane (takes, sliced audio, loops).
/// </summary>
public sealed class JamTrack
{
    public int Index { get; set; }
    public string RoleKey { get; set; } = "other"; // guitar, vocals, piano, bass, drums, other, gp200
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "🎵";
    public Color ThemeColor { get; set; } = Color.FromRgb(0, 229, 255);
    public List<JamClip> Clips { get; } = new();

    private string? _filePath;
    public string? FilePath
    {
        get => Clips.Count > 0 ? Clips[0].FilePath : _filePath;
        set
        {
            _filePath = value;
            if (string.IsNullOrEmpty(value))
            {
                Clips.Clear();
            }
            else
            {
                if (Clips.Count == 0)
                {
                    Clips.Add(new JamClip { FilePath = value, Name = Name, Color = ThemeColor });
                }
                else
                {
                    Clips[0].FilePath = value;
                }
            }
        }
    }

    public AudioFileReader? Reader { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    public bool IsArmed { get; set; } // Armed for recording
    public bool IsMonitoring { get; set; } // Live input listening
    public float VolumeDb { get; set; } = 0.0f; // -30dB to +6dB
    public float Pan { get; set; } = 0.0f;      // -1.0 (L) to +1.0 (R)

    private float[]? _waveformPeaks;
    public float[]? WaveformPeaks
    {
        get => Clips.Count > 0 ? Clips[0].WaveformPeaks : _waveformPeaks;
        set
        {
            _waveformPeaks = value;
            if (Clips.Count > 0) Clips[0].WaveformPeaks = value;
        }
    }

    private double _startOffsetSeconds = 0.0;
    public double StartOffsetSeconds
    {
        get => Clips.Count > 0 ? Clips[0].StartOffsetSeconds : _startOffsetSeconds;
        set
        {
            _startOffsetSeconds = value;
            if (Clips.Count > 0) Clips[0].StartOffsetSeconds = value;
        }
    }

    private double _skipSeconds = 0.0;
    public double SkipSeconds
    {
        get => Clips.Count > 0 ? Clips[0].SkipSeconds : _skipSeconds;
        set
        {
            _skipSeconds = value;
            if (Clips.Count > 0) Clips[0].SkipSeconds = value;
        }
    }

    private double _takeSeconds = 0.0;
    public double TakeSeconds
    {
        get => Clips.Count > 0 ? Clips[0].TakeSeconds : _takeSeconds;
        set
        {
            _takeSeconds = value;
            if (Clips.Count > 0) Clips[0].TakeSeconds = value;
        }
    }

    private double _totalDurationSeconds = 0.0;
    public double TotalDurationSeconds
    {
        get => Clips.Count > 0 ? Clips.Max(c => c.EndSeconds) : _totalDurationSeconds;
        set
        {
            _totalDurationSeconds = value;
            if (Clips.Count > 0) Clips[0].TotalDurationSeconds = value;
        }
    }

    public bool HasAudio => Clips.Any(c => !string.IsNullOrEmpty(c.FilePath) && File.Exists(c.FilePath)) || (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath));
    public bool IsGuitarStem { get; set; } = false;
}

/// <summary>
/// Project state snapshot for Undo and Redo operations in Jam Studio.
/// </summary>
public sealed class JamStudioSnapshot
{
    public sealed class ClipSnapshot
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string FilePath { get; set; } = "";
        public double StartOffsetSeconds { get; set; }
        public double SkipSeconds { get; set; }
        public double TakeSeconds { get; set; }
        public double TotalDurationSeconds { get; set; }
        public bool IsMuted { get; set; }
        public Color Color { get; set; }
        public float[]? WaveformPeaks { get; set; }
    }

    public sealed class TrackSnapshot
    {
        public int Index { get; set; }
        public string RoleKey { get; set; } = "other";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "🎵";
        public Color ThemeColor { get; set; }
        public bool IsMuted { get; set; }
        public bool IsSolo { get; set; }
        public bool IsArmed { get; set; }
        public bool IsMonitoring { get; set; }
        public float VolumeDb { get; set; }
        public float Pan { get; set; }
        public List<ClipSnapshot> Clips { get; set; } = new();
    }

    public sealed class TriggerSnapshot
    {
        public double TimeSeconds { get; set; }
        public string Label { get; set; } = "";
        public int TargetScene { get; set; } = -1;
        public int TargetSlot { get; set; } = -1;
    }

    public List<TrackSnapshot> Tracks { get; set; } = new();
    public List<TriggerSnapshot> Triggers { get; set; } = new();
    public double LoopStartSeconds { get; set; }
    public double LoopEndSeconds { get; set; }
    public bool IsLooping { get; set; }
    public int ActiveTrackIndex { get; set; }
    public string Description { get; set; } = "";
}

public enum PlaylistTool
{
    Draw,   // Lápiz: Seleccionar clips y ubicar cabezal
    Paint,  // Pincel: Repetir / editar clips
    Slice,  // Cuchilla: Cortar clip interactivo
    Delete, // Borrador: Eliminar audio del clip
    Mute,   // Silenciar: Silenciar / activar pista
    Slip,   // Deslizar: Mover contenido de audio dentro del clip
    Zoom,   // Zoom: Acercar / alejar ondas
    Select  // Selección: Seleccionar región
}

public enum GridSnapMode
{
    Line,
    Cell,
    Bar,
    HalfBeat,
    QuarterBeat,
    Off
}

/// <summary>
/// GP-200 Stem & Jam Studio (FL Studio & BandLab Inspired DAW-Lite) for Valeton GP-200.
/// Full guitar-oriented frontend with isolated StemKit GitHub backend:
/// 1. Auto-downloads and manages danielravina/stemkit releases behind the scenes.
/// 2. Auto-discovers StemKit on PC and auto-ingests separated stems into DAW tracks.
/// 3. FL Studio Playlist Toolbar with interactive Razor / Slice (✂️), Draw, Paint, Mute, Delete, Slip, Zoom.
/// 4. Dynamic unconstrained tracks (Track 1, Track 2...) with full freedom to add, rename, re-color, or map stems.
/// 5. Top FL Studio LCD center bar: SONG/PAT mode, digital BPM steppers, Bar:Beat:Step timecode, stereo Peak VU meters, telemetry.
/// 6. Guitarist super-tools: 1-click Mute Original Guitar for jam practice, GP-200 Live Track with scene buttons,
///    pitch shift semitones (-6 to +6 st), speed slow-down (50%-100%), loop A-B, tap tempo, and master WAV export.
/// </summary>
public sealed class JamStudioWindow : Window
{
    private readonly EditorWindow editor;
    private readonly Catalog catalog;
    private readonly Func<PresetDocument?> getDocument;
    private readonly Action<Action<PresetDocument>> edit;

    // Multi-track audio engine & backend bridge
    private readonly MultiTrackAudioEngine audioEngine = new();
    private readonly StemKitBridgeService stemKitBridge = new();
    private readonly List<JamTrack> tracks = [];
    private readonly List<TimelinePresetTrigger> automationTriggers = [];

    // State
    private double currentPositionSeconds = 0;
    private double totalDurationSeconds = 0;
    private bool isRecording = false;
    private bool isLooping = false;
    private double loopStartSeconds = 0;
    private double loopEndSeconds = 0;
    private int currentBpm = 120;
    private string currentKey = "C min";
    private int pitchShiftSemitones = 0;
    private double playbackSpeedRate = 1.0;
    private string? loadedSongPath;
    private CancellationTokenSource? operationCts;

    // Timeline Zoom & Layout Zoom
    private double pixelsPerSecond = 50.0; // 20 to 200 px/s
    private double currentWindowZoom = 1.0;
    private const double TrackHeight = 84.0;
    private const double RulerHeight = 28.0;
    private const double AutoTrackHeight = 32.0;
    private const double EditingToolbarHeight = 36.0;
    private int selectedTrackIndex = 0;
    private readonly HashSet<int> selectedTrackIndices = new() { 0 };
    private bool isDraggingTrackSelection = false;
    private double recordingStartOffsetSeconds = 0.0;
    private JamClip? selectedClip = null;
    private StemKitBridgeService.StemKitWatcher? stemKitWatcher;

    // Real-Time GP-200 Input Monitoring & Bit-Perfect Recording
    private readonly AudioCaptureEngine inputCaptureEngine = new();
    private readonly Border inputMonitorBorder = new();
    private readonly Ellipse inputSignalLed = new() { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(74, 85, 104)), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Indicador de señal de guitarra GP-200 en tiempo real" };
    private readonly TextBlock inputSignalText = new() { Text = "Silencio", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DarkUiTheme.AuraTextMutedBrush, VerticalAlignment = VerticalAlignment.Center, MinWidth = 46 };
    private readonly Button inputDeviceBtn = new() { Content = "🎸 GP-200 IN ▾", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, Cursor = Cursors.Hand, ToolTip = "Entrada de audio activa para grabar la guitarra. Haz clic para cambiar de dispositivo." };
    private readonly Button autoDetectInputBtn = new() { Content = "🔍 Auto", FontSize = 10, FontWeight = FontWeights.Bold, Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(3, 0, 3, 0), Background = DarkUiTheme.AuraBgCardBrush, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, Cursor = Cursors.Hand, ToolTip = "Escanear y auto-conectar a la entrada que tenga sonido o guitarra activa en la laptop." };
    private readonly Button liveMonitorBtn = new() { Content = "🎧 Mon: ON", FontSize = 10, FontWeight = FontWeights.Bold, Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(3, 0, 5, 0), Background = DarkUiTheme.SuccessBrush, Foreground = Brushes.White, Cursor = Cursors.Hand, ToolTip = "Monitoreo en Vivo (ON): Escucha el pedal y tu guitarra por los altavoces o auriculares en tiempo real." };
    private WaveFileWriter? guitarWaveWriter;
    private string? recordingFilePath;
    private bool isRecordingGuitarActive = false;

    // Scalable Root Grid for real 100% zoom
    private readonly Grid scalableRootGrid = new();
    private readonly TextBlock zoomDisplay = new() { Text = "100%", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock activeTrackPill = new() { Text = "Pista Activa: Track 1", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };

    // FL Studio Tools, Undo/Redo and Grid Snap
    private readonly Stack<JamStudioSnapshot> undoStack = new();
    private readonly Stack<JamStudioSnapshot> redoStack = new();
    private readonly Button undoBtn = new()
    {
        Content = "↶",
        FontSize = 13,
        FontWeight = FontWeights.Bold,
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(2, 0, 2, 0),
        Cursor = Cursors.Hand,
        ToolTip = "Deshacer (Ctrl+Z)",
        IsEnabled = false,
        Opacity = 0.45
    };
    private readonly Button redoBtn = new()
    {
        Content = "↷",
        FontSize = 13,
        FontWeight = FontWeights.Bold,
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(2, 0, 4, 0),
        Cursor = Cursors.Hand,
        ToolTip = "Rehacer (Ctrl+Y / Ctrl+Shift+Z)",
        IsEnabled = false,
        Opacity = 0.45
    };
    private PlaylistTool currentTool = PlaylistTool.Slice;
    private GridSnapMode currentSnap = GridSnapMode.Line;
    private readonly Dictionary<PlaylistTool, Button> toolButtons = new();
    private readonly ComboBox snapCombo = new() { Width = 115, Margin = new Thickness(4, 0, 4, 0), ToolTip = "Ajuste de Rejilla (Snap a compás/tiempo)", Cursor = Cursors.Hand };
    private readonly Button stemKitQuickImportBtn = new() { Content = "📥 Importar de StemKit ▾", FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = "Ver e importar canciones y stems ya separados en StemKit" };

    // FL Studio Top Center Bar Elements
    private bool isPatternMode = false;
    private readonly ToggleButton flModeToggleBtn = new()
    {
        Content = "● SONG",
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        MinWidth = 62,
        Height = 24,
        Cursor = Cursors.Hand,
        Background = DarkUiTheme.AuraBgCardBrush,
        Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
        ToolTip = "Modo: SONG (todas las pistas) / PAT (solo la pista activa seleccionada)"
    };
    private readonly TextBlock flBpmReadout = new()
    {
        Text = "120.000",
        FontFamily = new FontFamily("Consolas, Courier New"),
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        Width = 58
    };
    private readonly Button bpmDownBtn = new() { Content = "▼", Width = 20, Height = 22, Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "Reducir BPM (-1)" };
    private readonly Button bpmUpBtn = new() { Content = "▲", Width = 20, Height = 22, Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "Aumentar BPM (+1)" };

    private readonly TextBlock flTimecodeBarBeat = new()
    {
        Text = "01:01:00",
        FontFamily = new FontFamily("Consolas, Courier New"),
        FontSize = 13,
        FontWeight = FontWeights.Bold,
        Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 4, 0),
        ToolTip = "Compás : Tiempo : Subdivisión (Bar : Beat : Step)"
    };
    private readonly TextBlock flTimecodeMinSec = new()
    {
        Text = "00:00.0",
        FontFamily = new FontFamily("Consolas, Courier New"),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = DarkUiTheme.AuraTextMutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0)
    };

    // Stereo Peak VU Meters
    private readonly ProgressBar vuMeterLeft = new()
    {
        Orientation = Orientation.Vertical,
        Width = 4,
        Height = 24,
        Minimum = 0,
        Maximum = 100,
        Value = 0,
        Margin = new Thickness(1, 0, 1, 0),
        Foreground = DarkUiTheme.SuccessBrush,
        Background = DarkUiTheme.AuraBgInputBrush,
        BorderThickness = new Thickness(0)
    };
    private readonly ProgressBar vuMeterRight = new()
    {
        Orientation = Orientation.Vertical,
        Width = 4,
        Height = 24,
        Minimum = 0,
        Maximum = 100,
        Value = 0,
        Margin = new Thickness(1, 0, 6, 0),
        Foreground = DarkUiTheme.SuccessBrush,
        Background = DarkUiTheme.AuraBgInputBrush,
        BorderThickness = new Thickness(0)
    };

    // Telemetry readout (CPU / RAM)
    private readonly TextBlock flTelemetryText = new()
    {
        Text = "CPU 2% | 185 MB",
        FontFamily = new FontFamily("Consolas, Courier New"),
        FontSize = 10,
        Foreground = DarkUiTheme.AuraTextMutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 6, 0)
    };

    // Periodic timers
    private readonly DispatcherTimer stemKitPollTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private readonly DispatcherTimer telemetryTimer = new() { Interval = TimeSpan.FromSeconds(2.0) };

    // UI Elements - Top Transport Bar (Row 0)
    private readonly Button loadAudioBtn = new() { Content = "+ Cargar Audio / YouTube ▾", FontWeight = FontWeights.SemiBold, MinWidth = 145, Cursor = Cursors.Hand };
    private readonly Button stemKitBackendBtn = new() { Content = "⚙️ StemKit ▾", MinWidth = 110, Cursor = Cursors.Hand, ToolTip = "Opciones y estado del backend de StemKit" };
    private readonly Button separateStemsBtn = new() { Content = "✨ Separar Stems", FontWeight = FontWeights.Bold, MinWidth = 135, Cursor = Cursors.Hand, Background = DarkUiTheme.AuraCyanPrimaryBrush, Foreground = DarkUiTheme.AuraBgDeepBrush };

    // Consolidated Top Sub-Menus to prevent any overcrowding
    private readonly Button practiceModesMenuBtn = new()
    {
        Content = "🎸 Modos Práctica ▾",
        ToolTip = "Modos de Práctica: Práctica Guitarra, Karaoke, Ritmo (Batería+Bajo), Restablecer",
        FontWeight = FontWeights.Bold,
        Cursor = Cursors.Hand,
        Background = DarkUiTheme.SuccessBrush,
        Foreground = Brushes.White,
        Margin = new Thickness(6, 0, 0, 0)
    };
    private readonly Button exportMenuBtn = new()
    {
        Content = "💾 Exportar ▾",
        ToolTip = "Exportar mezcla máster (.WAV) o stems individuales",
        FontWeight = FontWeights.SemiBold,
        Cursor = Cursors.Hand,
        Margin = new Thickness(0, 0, 4, 0)
    };
    private readonly Button viewMenuBtn = new()
    {
        Content = "🔍 Vista ▾",
        ToolTip = "Zoom global de interfaz y zoom horizontal de ondas",
        Cursor = Cursors.Hand,
        Margin = new Thickness(0, 0, 6, 0)
    };

    // Quick Practice Presets & Export Actions
    private readonly Button practiceModeBtn = new() { Content = "🎸 Modo Práctica Guitarra", ToolTip = "Silencia la guitarra original y activa el acompañamiento para que toques encima", FontWeight = FontWeights.Bold, Cursor = Cursors.Hand, Background = DarkUiTheme.SuccessBrush, Foreground = Brushes.White, Margin = new Thickness(6, 0, 0, 0) };
    private readonly Button karaokeModeBtn = new() { Content = "🎤 Karaoke", ToolTip = "Silencia las voces para cantar encima", Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0) };
    private readonly Button drumsBassModeBtn = new() { Content = "🥁 Batería + Bajo", ToolTip = "Modo Ritmo: Solo reproduce batería y bajo para practicar groove y tiempo con tu guitarra", Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0) };
    private readonly Button resetMixBtn = new() { Content = "🔄 Reset", ToolTip = "Restablece todos los silenciados y volúmenes", Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 8, 0) };
    private readonly Button exportMixBtn = new() { Content = "💾 Exportar Mezcla", FontWeight = FontWeights.SemiBold, ToolTip = "Exporta la mezcla master actual como un archivo WAV", Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
    private readonly Button exportStemsBtn = new() { Content = "📁 Exportar Stems", FontWeight = FontWeights.SemiBold, ToolTip = "Exporta todos los stems separados como archivos .WAV individuales a una carpeta", Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };

    // Transport Controls
    private readonly TextBox bpmBox = new() { Text = "120", MinWidth = 45, Padding = new Thickness(4, 2, 4, 2), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeights.Bold };
    private readonly Button tapTempoBtn = new() { Content = "⚡ Tap GP-200", ToolTip = "Enviar BPM al pedal Valeton GP-200", Cursor = Cursors.Hand };
    private readonly TextBlock timeSignaturePill = new() { Text = "4/4", FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock keyPill = new() { Text = "C min", FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    
    // Pitch Shift & Speed Selector
    private readonly ComboBox pitchShiftCombo = new() { Width = 72, ToolTip = "Pitch Shift (Cambia la afinación de la canción sin tocar las clavijas de tu guitarra)", Cursor = Cursors.Hand };
    private readonly ComboBox speedCombo = new() { Width = 68, ToolTip = "Velocidad de práctica (Ralentiza solos rápidos para aprenderlos fácil)", Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 8, 0) };

    private readonly Button rewindBtn = new() { Content = "⏮", MinWidth = 40, Cursor = Cursors.Hand, ToolTip = "Ir al inicio" };
    private readonly Button playPauseBtn = new() { Content = "▶ Play", FontWeight = FontWeights.Bold, MinWidth = 80, Cursor = Cursors.Hand };
    private readonly Button recGuitarBtn = new() { Content = "🔴 REC Guitarra", FontWeight = FontWeights.Bold, MinWidth = 125, Cursor = Cursors.Hand };
    private readonly ToggleButton loopToggle = new() { Content = "🔁 Loop: OFF", Cursor = Cursors.Hand, MinWidth = 85 };
    private readonly TextBlock digitalClock = new() { Text = "00:00.0 / 00:00.0", FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };

    // Right Controls
    private readonly Slider masterVolSlider = new() { Minimum = 0, Maximum = 100, Value = 85, Width = 90, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock masterVolText = new() { Text = "-1.5 dB", FontSize = 10, Foreground = DarkUiTheme.AuraTextMutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };

    // Middle Canvas & Headers (Row 1)
    private readonly StackPanel trackHeadersStack = new();
    private readonly ScrollViewer trackHeadersScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Canvas timelineCanvas = new() { Background = DarkUiTheme.AuraBgSurfaceBrush, ClipToBounds = true };
    private readonly ScrollViewer timelineScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Line playheadLine = new() { Stroke = Brushes.White, StrokeThickness = 2, Y1 = 0, Y2 = 1200 };
    private readonly Polygon playheadTop = new() { Fill = Brushes.White, Points = new PointCollection { new Point(-6, 0), new Point(6, 0), new Point(0, 10) } };

    // Automation Lane & Flags
    private readonly Canvas automationLaneCanvas = new() { Height = AutoTrackHeight, Background = new SolidColorBrush(Color.FromArgb(50, 0, 229, 255)) };

    // Bottom Bar (Row 2)
    private readonly Border bottomDropBar = new()
    {
        Height = 52,
        Background = DarkUiTheme.AuraBgCardBrush,
        BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
        BorderThickness = new Thickness(0, 1, 0, 0),
        AllowDrop = true
    };
    private readonly TextBlock dropPromptText = new()
    {
        Text = "📥 Arrastra aquí cualquier archivo .mp3, .wav o .flac desde tu equipo para comenzar la práctica",
        FontSize = 12,
        Foreground = DarkUiTheme.AuraTextMutedBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ProgressBar stemProgressBar = new() { Width = 220, Height = 14, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock stemProgressText = new() { FontSize = 11, Foreground = DarkUiTheme.AuraCyanPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Button cancelOperationBtn = new() { Content = "Cancelar", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, Cursor = Cursors.Hand };

    // Dispatcher Timers
    private readonly DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };

    // Backend Options
    private StemKitModel chosenModel = StemKitModel.Htdemucs6s;
    private StemKitDevice chosenDevice = StemKitDevice.AutoGpu;
    private StemKitOutputFormat chosenFormat = StemKitOutputFormat.Wav24;

    public JamStudioWindow(EditorWindow editor, Catalog catalog, Func<PresetDocument?> getDocument, Action<Action<PresetDocument>> edit)
    {
        this.editor = editor;
        this.catalog = catalog;
        this.getDocument = getDocument;
        this.edit = edit;

        Title = "GP-200 Stem & Jam Studio";
        Width = 1320;
        Height = 860;
        MinWidth = 1000;
        MinHeight = 640;
        Background = DarkUiTheme.AuraBgDeepBrush;
        Foreground = DarkUiTheme.AuraTextMainBrush;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        InitializeDefaultTracks();
        BuildBandLabInterface();
        WireEvents();
        InitializeInputMonitoring();

        playbackTimer.Tick += PlaybackTimer_Tick;

        // Auto-start StemKit Watcher in background so external StemKit runs are caught
        try
        {
            stemKitWatcher = new StemKitBridgeService.StemKitWatcher(folder =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    IngestStemsFromFolder(folder, isAutoSync: true);
                });
            });
        }
        catch { }

        // Start periodic polling timer to detect songs separated in StemKit
        stemKitPollTimer.Tick += (_, _) => CheckAndAutoIngestNewStems();
        stemKitPollTimer.Start();

        // Start system telemetry timer
        telemetryTimer.Tick += (_, _) => UpdateSystemTelemetry();
        telemetryTimer.Start();

        // Check immediately on startup for existing separated songs (e.g. Bethel Music "Entrego Todo")
        Dispatcher.InvokeAsync(CheckAndAutoIngestNewStems, DispatcherPriority.Background);

        Closed += (_, _) =>
        {
            playbackTimer.Stop();
            stemKitPollTimer.Stop();
            telemetryTimer.Stop();
            audioEngine.Dispose();
            inputCaptureEngine.Dispose();
            guitarWaveWriter?.Dispose();
            operationCts?.Cancel();
            stemKitWatcher?.Dispose();
        };

        // Keyboard Shortcuts (Space = Play/Pause, Ctrl+Z/Y = Undo/Redo, 1..8 = Mute tracks, R = Arm Record, S = Split, Del = Clear Clip, L = Loop)
        KeyDown += (s, e) =>
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (bpmBox.IsFocused) return;

            if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    Redo();
                else
                    Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Redo();
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                TogglePlayPause();
                e.Handled = true;
            }
            else if (e.Key == Key.S)
            {
                SplitActiveTrackAtPlayhead();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                ClearActiveTrack();
                e.Handled = true;
            }
            else if (e.Key == Key.L)
            {
                loopToggle.IsChecked = !(loopToggle.IsChecked == true);
                isLooping = loopToggle.IsChecked == true;
                loopToggle.Content = isLooping ? "🔁 Loop: ON" : "🔁 Loop: OFF";
                audioEngine.LoopEnabled = isLooping;
                audioEngine.LoopStart = TimeSpan.FromSeconds(loopStartSeconds);
                audioEngine.LoopEnd = TimeSpan.FromSeconds(loopEndSeconds > loopStartSeconds ? loopEndSeconds : Math.Max(totalDurationSeconds, 1.0));
                e.Handled = true;
            }
            else if (e.Key is >= Key.D1 and <= Key.D8)
            {
                int trackIdx = e.Key - Key.D1;
                if (trackIdx < tracks.Count)
                {
                    tracks[trackIdx].IsMuted = !tracks[trackIdx].IsMuted;
                    audioEngine.SetMute(trackIdx, tracks[trackIdx].IsMuted);
                    RenderTrackHeaders();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.R)
            {
                ToggleGuitarRecording();
                e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                selectedTrackIndices.Clear();
                for (int t = 0; t < tracks.Count; t++) selectedTrackIndices.Add(t);
                UpdateTrackHeaderSelectionVisuals();
                RefreshCanvas();
                UpdateSelectedTrackUI();
                e.Handled = true;
            }
        };

        PreviewMouseLeftButtonUp += (s, e) =>
        {
            isDraggingTrackSelection = false;
        };

        PreviewMouseWheel += (s, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                AdjustWindowZoom(e.Delta > 0 ? 0.05 : -0.05);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                timelineScroll.ScrollToHorizontalOffset(Math.Clamp(timelineScroll.HorizontalOffset - (e.Delta / 120.0 * 80), 0, timelineScroll.ScrollableWidth));
                e.Handled = true;
            }
        };

        // Auto-check StemKit on PC quietly in background
        Task.Run(() =>
        {
            string? found = StemKitBridgeService.AutoLocateStemKitOnComputer();
            Dispatcher.InvokeAsync(() => UpdateBackendStatusBanner(found));
        });
    }

    private void InitializeDefaultTracks()
    {
        tracks.Clear();
        Color[] trackPalette = [
            Color.FromRgb(59, 130, 246),  // Blue
            Color.FromRgb(16, 185, 129),  // Emerald
            Color.FromRgb(245, 158, 11),  // Amber
            Color.FromRgb(249, 115, 22),  // Orange
            Color.FromRgb(168, 85, 247),  // Purple
            Color.FromRgb(239, 68, 68),   // Red
            Color.FromRgb(234, 179, 8),   // Yellow
            Color.FromRgb(20, 184, 166)   // Teal
        ];

        for (int i = 0; i < 7; i++)
        {
            tracks.Add(new JamTrack
            {
                Index = i,
                RoleKey = "unassigned",
                Name = $"Track {i + 1}",
                Icon = "🎵",
                ThemeColor = trackPalette[i % trackPalette.Length]
            });
        }

        // Track 8: GP-200 Live Track
        tracks.Add(new JamTrack
        {
            Index = 7,
            RoleKey = "gp200",
            Name = "Track 8 · ⚡ MI GUITARRA GP-200",
            Icon = "⚡",
            ThemeColor = Color.FromRgb(0, 229, 255),
            IsArmed = true,
            IsMonitoring = true
        });
    }

    private void BuildBandLabInterface()
    {
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });             // Row 0: Transport
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 1: BandLab 2-Col
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });             // Row 2: Drop & StemKit

        // Row 0: Top Transport Bar
        var topBar = BuildTopTransportBar();
        Grid.SetRow(topBar, 0);
        mainGrid.Children.Add(topBar);

        // Row 1: BandLab 2-Column Body
        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) }); // Col 0: Track Headers
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 1: Timeline & Waves

        // Col 0: Left Track Headers
        var leftHeadersPanel = BuildLeftHeadersPanel();
        Grid.SetColumn(leftHeadersPanel, 0);
        bodyGrid.Children.Add(leftHeadersPanel);

        // Col 1: Right Waveform & Automation Canvas
        var rightCanvasPanel = BuildRightTimelinePanel();
        Grid.SetColumn(rightCanvasPanel, 1);
        bodyGrid.Children.Add(rightCanvasPanel);

        // Synchronize Vertical Scrolling between Headers and Canvas
        timelineScroll.ScrollChanged += (s, e) =>
        {
            if (e.VerticalChange != 0)
            {
                trackHeadersScroll.ScrollToVerticalOffset(timelineScroll.VerticalOffset);
            }
        };

        Grid.SetRow(bodyGrid, 1);
        mainGrid.Children.Add(bodyGrid);

        // Row 2: Bottom Drag & Drop and StemKit Status
        var bottomBar = BuildBottomDropBar();
        Grid.SetRow(bottomBar, 2);
        mainGrid.Children.Add(bottomBar);

        // Wrap into Scalable Root Grid for 100% Zoom
        scalableRootGrid.Children.Clear();
        scalableRootGrid.Children.Add(mainGrid);
        Content = scalableRootGrid;

        // Apply saved scale
        var prefs = AppPreferences.Load();
        AdjustWindowZoom((prefs.ScalePercent / 100.0) - 1.0);
    }

    private Border BuildTopTransportBar()
    {
        var border = new Border
        {
            Background = DarkUiTheme.AuraBgDeepBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };

        var dock = new DockPanel();

        // Left: Load & StemKit actions + Consolidated Practice Modes Menu
        var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        loadAudioBtn.Padding = new Thickness(8, 3, 8, 3);
        leftPanel.Children.Add(loadAudioBtn);
        stemKitBackendBtn.Padding = new Thickness(8, 3, 8, 3);
        leftPanel.Children.Add(stemKitBackendBtn);
        separateStemsBtn.Padding = new Thickness(8, 3, 8, 3);
        leftPanel.Children.Add(separateStemsBtn);
        practiceModesMenuBtn.Padding = new Thickness(8, 3, 8, 3);
        leftPanel.Children.Add(practiceModesMenuBtn);
        DockPanel.SetDock(leftPanel, Dock.Left);
        dock.Children.Add(leftPanel);

        // Right: Consolidated Export Menu, View/Zoom Menu, Master Volume
        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        exportMenuBtn.Padding = new Thickness(8, 3, 8, 3);
        rightPanel.Children.Add(exportMenuBtn);
        viewMenuBtn.Padding = new Thickness(8, 3, 8, 3);
        rightPanel.Children.Add(viewMenuBtn);

        var masterLabel = new TextBlock { Text = "MASTER", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = DarkUiTheme.AuraTextMutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
        rightPanel.Children.Add(masterLabel);
        masterVolSlider.Width = 70;
        rightPanel.Children.Add(masterVolSlider);
        rightPanel.Children.Add(masterVolText);

        DockPanel.SetDock(rightPanel, Dock.Right);
        dock.Children.Add(rightPanel);

        // Center: Transport, GP-200 Live Input Monitor & FL Studio LCD Bar
        var centerPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        
        // 1. FL Studio Mode Toggle [● SONG / PAT]
        centerPanel.Children.Add(flModeToggleBtn);

        // 2. FL Studio LCD BPM Stepper [ ▼ ] [ 120.000 ] [ ▲ ] + [⚡ Tap GP-200]
        var bpmBorder = new Border
        {
            Background = DarkUiTheme.AuraBgInputBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Margin = new Thickness(4, 0, 4, 0)
        };
        var bpmStack = new StackPanel { Orientation = Orientation.Horizontal };
        bpmStack.Children.Add(bpmDownBtn);
        bpmStack.Children.Add(flBpmReadout);
        bpmStack.Children.Add(bpmUpBtn);
        bpmBorder.Child = bpmStack;
        centerPanel.Children.Add(bpmBorder);
        centerPanel.Children.Add(tapTempoBtn);

        // 3. FL Studio Digital Timecode LCD (Bar:Beat:Step and Min:Sec)
        var timecodeBorder = new Border
        {
            Background = DarkUiTheme.AuraBgInputBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(4, 0, 4, 0)
        };
        var timecodeStack = new StackPanel { Orientation = Orientation.Horizontal };
        timecodeStack.Children.Add(flTimecodeBarBeat);
        timecodeStack.Children.Add(new TextBlock { Text = " · ", Foreground = DarkUiTheme.AuraTextMutedBrush, VerticalAlignment = VerticalAlignment.Center });
        timecodeStack.Children.Add(flTimecodeMinSec);
        timecodeBorder.Child = timecodeStack;
        centerPanel.Children.Add(timecodeBorder);

        // 4. Dancing Stereo Peak VU Meters (Left / Right)
        var vuMeterStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
        vuMeterStack.Children.Add(vuMeterLeft);
        vuMeterStack.Children.Add(vuMeterRight);
        centerPanel.Children.Add(vuMeterStack);

        // 5. System Telemetry (CPU & RAM)
        centerPanel.Children.Add(flTelemetryText);

        // 6. Transport Controls (Rewind, Play/Pause, Rec, GP-200 Live Input Monitor, Loop)
        centerPanel.Children.Add(rewindBtn);
        centerPanel.Children.Add(playPauseBtn);
        centerPanel.Children.Add(recGuitarBtn);

        // GP-200 Real-Time Input Signal Monitor Badge
        inputMonitorBorder.Background = DarkUiTheme.AuraBgInputBrush;
        inputMonitorBorder.BorderBrush = DarkUiTheme.AuraBorderSubtleBrush;
        inputMonitorBorder.BorderThickness = new Thickness(1);
        inputMonitorBorder.CornerRadius = new CornerRadius(4);
        inputMonitorBorder.Padding = new Thickness(6, 2, 6, 2);
        inputMonitorBorder.Margin = new Thickness(4, 0, 4, 0);
        inputMonitorBorder.VerticalAlignment = VerticalAlignment.Center;
        var inputStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        inputStack.Children.Add(inputDeviceBtn);
        inputStack.Children.Add(autoDetectInputBtn);
        inputStack.Children.Add(liveMonitorBtn);
        inputStack.Children.Add(inputSignalLed);
        inputStack.Children.Add(inputSignalText);
        inputMonitorBorder.Child = inputStack;
        centerPanel.Children.Add(inputMonitorBorder);

        centerPanel.Children.Add(loopToggle);

        // 7. Signature & Key
        var sigBorder = new Border
        {
            Background = DarkUiTheme.AuraBgCardBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(4, 0, 4, 0)
        };
        var sigStack = new StackPanel { Orientation = Orientation.Horizontal };
        sigStack.Children.Add(timeSignaturePill);
        sigStack.Children.Add(new TextBlock { Text = " · ", Foreground = DarkUiTheme.AuraTextMutedBrush, VerticalAlignment = VerticalAlignment.Center });
        sigStack.Children.Add(keyPill);
        sigBorder.Child = sigStack;
        centerPanel.Children.Add(sigBorder);

        // 8. Pitch Shift & Speed
        pitchShiftCombo.Items.Clear();
        for (int p = -6; p <= 6; p++)
            pitchShiftCombo.Items.Add(p == 0 ? "Pitch 0" : (p > 0 ? $"+{p} st" : $"{p} st"));
        pitchShiftCombo.SelectedIndex = 6;
        pitchShiftCombo.SelectionChanged += (_, _) =>
        {
            pitchShiftSemitones = pitchShiftCombo.SelectedIndex - 6;
        };
        centerPanel.Children.Add(pitchShiftCombo);

        speedCombo.Items.Clear();
        speedCombo.Items.Add("100%");
        speedCombo.Items.Add("90%");
        speedCombo.Items.Add("80%");
        speedCombo.Items.Add("75%");
        speedCombo.Items.Add("50%");
        speedCombo.SelectedIndex = 0;
        speedCombo.SelectionChanged += (_, _) =>
        {
            playbackSpeedRate = speedCombo.SelectedIndex switch
            {
                1 => 0.9,
                2 => 0.8,
                3 => 0.75,
                4 => 0.5,
                _ => 1.0
            };
        };
        centerPanel.Children.Add(speedCombo);

        dock.Children.Add(centerPanel);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = dock,
            Focusable = false
        };
        border.Child = scroll;
        return border;
    }

    private Border BuildLeftHeadersPanel()
    {
        var border = new Border
        {
            Background = DarkUiTheme.AuraBgDeepBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 1, 0)
        };

        var mainStack = new StackPanel();

        // Top actions (matches Ruler + Automation + Editing Toolbar height)
        var topActionsGrid = new Grid { Height = RulerHeight + AutoTrackHeight + EditingToolbarHeight, Background = DarkUiTheme.AuraBgSurfaceBrush };
        var topActionsRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        var addTrackBtn = new Button { Content = "+ Pista", Padding = new Thickness(8, 3, 8, 3), FontSize = 11, Cursor = Cursors.Hand, ToolTip = "Añadir una nueva pista de audio vacía" };
        addTrackBtn.Click += (_, _) => AddNewEmptyTrack();
        var importFolderBtn = new Button { Content = "📂 Stems", Padding = new Thickness(8, 3, 8, 3), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Importar carpeta con stems de audio" };
        importFolderBtn.Click += (_, _) => ImportStemsFromFolderDialog();
        var addTriggerBtn = new Button { Content = "⚡ + Disparador", Padding = new Thickness(8, 3, 8, 3), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Añadir cambio de Escena o Preset GP-200" };
        addTriggerBtn.Click += (_, _) => OpenAddTriggerDialog(currentPositionSeconds);
        topActionsRow.Children.Add(addTrackBtn);
        topActionsRow.Children.Add(importFolderBtn);
        topActionsRow.Children.Add(addTriggerBtn);
        topActionsGrid.Children.Add(topActionsRow);
        mainStack.Children.Add(topActionsGrid);

        // 7 Track Headers
        RenderTrackHeaders();
        trackHeadersScroll.Content = trackHeadersStack;
        mainStack.Children.Add(trackHeadersScroll);

        border.Child = mainStack;
        return border;
    }

    private void RenderTrackHeaders()
    {
        trackHeadersStack.Children.Clear();
        for (int i = 0; i < tracks.Count; i++)
        {
            int capturedIndex = i;
            var track = tracks[i];
            bool isSelected = selectedTrackIndices.Contains(i);

            var headerCard = new Border
            {
                Height = TrackHeight,
                Background = isSelected ? DarkUiTheme.AuraBgCardBrush : DarkUiTheme.AuraBgSurfaceBrush,
                BorderBrush = isSelected ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraBorderSubtleBrush,
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Padding = new Thickness(6, 4, 6, 4),
                Cursor = Cursors.Hand,
                AllowDrop = true
            };

            // Drag-selection across track headers (sweeping over tracks with left button down)
            headerCard.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is Button || e.OriginalSource is Slider || e.OriginalSource is TextBox || e.OriginalSource is CheckBox) return;

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    if (selectedTrackIndices.Contains(capturedIndex))
                        selectedTrackIndices.Remove(capturedIndex);
                    else
                        selectedTrackIndices.Add(capturedIndex);
                }
                else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    selectedTrackIndices.Clear();
                    selectedTrackIndices.Add(capturedIndex);
                }
                selectedTrackIndex = capturedIndex;
                isDraggingTrackSelection = true;
                UpdateTrackHeaderSelectionVisuals();
                RefreshCanvas();
                UpdateSelectedTrackUI();
            };

            headerCard.MouseEnter += (s, e) =>
            {
                if (isDraggingTrackSelection || e.LeftButton == MouseButtonState.Pressed)
                {
                    selectedTrackIndices.Add(capturedIndex);
                    selectedTrackIndex = capturedIndex;
                    UpdateTrackHeaderSelectionVisuals();
                    RefreshCanvas();
                    UpdateSelectedTrackUI();
                }
            };

            headerCard.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effects = DragDropEffects.Copy;
            };

            headerCard.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0) LoadAudioIntoTrack(capturedIndex, files[0]);
                }
            };

            // Rich Context Menu for every track
            var ctx = new ContextMenu();
            var miLoad = new MenuItem { Header = "📁 Cargar archivo de audio a esta pista..." };
            miLoad.Click += (_, _) => OpenFileForTrack(capturedIndex);
            ctx.Items.Add(miLoad);

            var miRename = new MenuItem { Header = "✏️ Renombrar pista..." };
            miRename.Click += (_, _) => OpenRenameTrackDialog(track);
            ctx.Items.Add(miRename);

            var miColor = new MenuItem { Header = "🎨 Cambiar color de pista..." };
            miColor.Click += (_, _) => OpenColorPicker(track);
            ctx.Items.Add(miColor);

            ctx.Items.Add(new Separator());

            var miSplit = new MenuItem { Header = "✂️ Dividir audio en el cabezal (S)" };
            miSplit.Click += (_, _) => { selectedTrackIndex = capturedIndex; SplitActiveTrackAtPlayhead(); };
            ctx.Items.Add(miSplit);

            var miTrim = new MenuItem { Header = "⏱️ Recortar audio a la zona de Loop" };
            miTrim.Click += (_, _) => { selectedTrackIndex = capturedIndex; TrimActiveTrackToLoop(); };
            ctx.Items.Add(miTrim);

            var miNudgeL = new MenuItem { Header = "⏪ Adelantar audio (-0.5s)" };
            miNudgeL.Click += (_, _) => { selectedTrackIndex = capturedIndex; NudgeActiveTrack(-0.5); };
            ctx.Items.Add(miNudgeL);

            var miNudgeR = new MenuItem { Header = "⏩ Retrasar audio (+0.5s)" };
            miNudgeR.Click += (_, _) => { selectedTrackIndex = capturedIndex; NudgeActiveTrack(+0.5); };
            ctx.Items.Add(miNudgeR);

            ctx.Items.Add(new Separator());

            var miDup = new MenuItem { Header = "📋 Duplicar pista" };
            miDup.Click += (_, _) => DuplicateTrack(capturedIndex);
            ctx.Items.Add(miDup);

            var miClear = new MenuItem { Header = "🗑️ Vaciar audio de esta pista" };
            miClear.Click += (_, _) => { selectedTrackIndex = capturedIndex; ClearActiveTrack(); };
            ctx.Items.Add(miClear);

            if (tracks.Count > 1)
            {
                var miDelete = new MenuItem { Header = "❌ Eliminar esta pista", Foreground = DarkUiTheme.DangerBrush };
                miDelete.Click += (_, _) => { selectedTrackIndex = capturedIndex; DeleteActiveTrack(); };
                ctx.Items.Add(miDelete);
            }
            headerCard.ContextMenu = ctx;

            var headerGrid = new Grid();
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Row 0: Mute Dot (FL Style) + Color stripe + Icon + Name + [✏️] + [📁] + [M] + [S] + [R]
            var row0 = new DockPanel();
            var buttonStack = new StackPanel { Orientation = Orientation.Horizontal };

            // Record Arm button (Universal for ANY track to record multiple GP-200 guitar takes)
            var rBtn = new Button
            {
                Content = "R",
                Width = 24,
                Height = 22,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = track.IsArmed ? DarkUiTheme.DangerBrush : DarkUiTheme.AuraBgCardBrush,
                Foreground = track.IsArmed ? Brushes.White : DarkUiTheme.AuraTextMutedBrush,
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                ToolTip = track.IsArmed ? "Pista Armada para Grabación (Haz clic para desarmar)" : "Armar esta pista para grabar tu guitarra GP-200"
            };
            rBtn.Click += (_, e) =>
            {
                e.Handled = true;
                track.IsArmed = !track.IsArmed;
                rBtn.Background = track.IsArmed ? DarkUiTheme.DangerBrush : DarkUiTheme.AuraBgCardBrush;
                rBtn.Foreground = track.IsArmed ? Brushes.White : DarkUiTheme.AuraTextMutedBrush;
                dropPromptText.Text = track.IsArmed ? $"● Pista '{track.Name}' armada para grabar con tu GP-200." : $"Pista '{track.Name}' desarmada.";
                dropPromptText.Foreground = track.IsArmed ? DarkUiTheme.DangerBrush : DarkUiTheme.AuraTextMutedBrush;
            };
            buttonStack.Children.Add(rBtn);

            // Live Input Monitor button for GP-200 hardware track
            if (track.RoleKey == "gp200")
            {
                var monBtn = new Button
                {
                    Content = "🎧",
                    Width = 24,
                    Height = 22,
                    FontSize = 10,
                    Background = track.IsMonitoring ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraBgCardBrush,
                    Foreground = track.IsMonitoring ? DarkUiTheme.AuraBgDeepBrush : DarkUiTheme.AuraTextMutedBrush,
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = "Monitor de Entrada USB GP-200"
                };
                monBtn.Click += (_, e) =>
                {
                    e.Handled = true;
                    track.IsMonitoring = !track.IsMonitoring;
                    monBtn.Background = track.IsMonitoring ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraBgCardBrush;
                    monBtn.Foreground = track.IsMonitoring ? DarkUiTheme.AuraBgDeepBrush : DarkUiTheme.AuraTextMutedBrush;
                };
                buttonStack.Children.Add(monBtn);
            }

            var mBtn = new Button
            {
                Content = "M",
                Width = 24,
                Height = 22,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Background = track.IsMuted ? DarkUiTheme.DangerBrush : DarkUiTheme.AuraBgCardBrush,
                Foreground = track.IsMuted ? Brushes.White : DarkUiTheme.AuraTextMainBrush,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Mute (Silenciar pista)"
            };
            var sBtn = new Button
            {
                Content = "S",
                Width = 24,
                Height = 22,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Background = track.IsSolo ? DarkUiTheme.WarningBrush : DarkUiTheme.AuraBgCardBrush,
                Foreground = track.IsSolo ? Brushes.Black : DarkUiTheme.AuraTextMainBrush,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Solo"
            };

            mBtn.Click += (_, e) =>
            {
                e.Handled = true;
                track.IsMuted = !track.IsMuted;
                mBtn.Background = track.IsMuted ? DarkUiTheme.DangerBrush : DarkUiTheme.AuraBgCardBrush;
                mBtn.Foreground = track.IsMuted ? Brushes.White : DarkUiTheme.AuraTextMainBrush;
                audioEngine.SetMute(track.Index, track.IsMuted);
                RenderTrackHeaders();
            };

            sBtn.Click += (_, e) =>
            {
                e.Handled = true;
                track.IsSolo = !track.IsSolo;
                sBtn.Background = track.IsSolo ? DarkUiTheme.WarningBrush : DarkUiTheme.AuraBgCardBrush;
                sBtn.Foreground = track.IsSolo ? Brushes.Black : DarkUiTheme.AuraTextMainBrush;
                audioEngine.SetSolo(track.Index, track.IsSolo);
            };

            buttonStack.Children.Add(mBtn);
            buttonStack.Children.Add(sBtn);
            DockPanel.SetDock(buttonStack, Dock.Right);
            row0.Children.Add(buttonStack);

            var nameStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            
            // FL Studio Style Mute LED Indicator Dot
            var muteDot = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = track.IsMuted ? DarkUiTheme.DangerBrush : DarkUiTheme.SuccessBrush,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = track.IsMuted ? "Pista Silenciada (Clic para activar)" : "Pista Activa (Clic para silenciar)"
            };
            muteDot.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                track.IsMuted = !track.IsMuted;
                audioEngine.SetMute(track.Index, track.IsMuted);
                RenderTrackHeaders();
            };
            nameStack.Children.Add(muteDot);

            var colorPill = new Border
            {
                Width = 4,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(track.ThemeColor),
                Margin = new Thickness(0, 0, 4, 0)
            };
            var nameTxt = new TextBlock
            {
                Text = $"{track.Icon} {track.Name}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = DarkUiTheme.AuraTextMainBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 100,
                ToolTip = "Doble clic para renombrar pista"
            };
            nameTxt.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    OpenRenameTrackDialog(track);
                }
            };

            var renameBtn = new Button
            {
                Content = "✏️",
                Width = 18,
                Height = 18,
                FontSize = 8,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Renombrar pista"
            };
            renameBtn.Click += (s, e) =>
            {
                e.Handled = true;
                OpenRenameTrackDialog(track);
            };

            var loadTrackBtn = new Button
            {
                Content = "📁",
                Width = 18,
                Height = 18,
                FontSize = 8,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Cargar audio en esta pista (o arrastra y suelta aquí)"
            };
            loadTrackBtn.Click += (s, e) =>
            {
                e.Handled = true;
                OpenFileForTrack(capturedIndex);
            };

            nameStack.Children.Add(colorPill);
            nameStack.Children.Add(nameTxt);
            nameStack.Children.Add(renameBtn);
            nameStack.Children.Add(loadTrackBtn);

            // Quick practice mute button for guitar track
            if (track.IsGuitarStem)
            {
                var mutePracticeBtn = new Button
                {
                    Content = track.IsMuted ? "✓ Silenciada" : "🔇 Tocar",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(3, 1, 3, 1),
                    Margin = new Thickness(3, 0, 0, 0),
                    Background = track.IsMuted ? DarkUiTheme.SuccessBrush : DarkUiTheme.AuraBgDeepBrush,
                    Foreground = Brushes.White,
                    Cursor = Cursors.Hand,
                    ToolTip = "Silencia la guitarra original para tocar encima con tu GP-200"
                };
                mutePracticeBtn.Click += (_, e) =>
                {
                    e.Handled = true;
                    track.IsMuted = !track.IsMuted;
                    mutePracticeBtn.Content = track.IsMuted ? "✓ Silenciada" : "🔇 Tocar";
                    mutePracticeBtn.Background = track.IsMuted ? DarkUiTheme.SuccessBrush : DarkUiTheme.AuraBgDeepBrush;
                    mBtn.Background = track.IsMuted ? DarkUiTheme.DangerBrush : DarkUiTheme.AuraBgCardBrush;
                    audioEngine.SetMute(track.Index, track.IsMuted);
                };
                nameStack.Children.Add(mutePracticeBtn);
            }

            row0.Children.Add(nameStack);
            Grid.SetRow(row0, 0);
            headerGrid.Children.Add(row0);

            // Row 1: Volume Slider + Pan Knob (or Scene Selector on GP-200 live track)
            var row1 = new Grid();
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

            if (track.RoleKey == "gp200")
            {
                // Quick GP-200 Scene switch buttons: [ A ] [ B ] [ C ] [ D ]
                var scenesStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                string[] sNames = ["A", "B", "C", "D"];
                for (int s = 0; s < 4; s++)
                {
                    int capturedScene = s;
                    var sBtnQuick = new Button
                    {
                        Content = sNames[s],
                        Width = 24,
                        Height = 22,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 3, 0),
                        Cursor = Cursors.Hand,
                        ToolTip = $"Cambiar GP-200 a Escena {sNames[s]}"
                    };
                    sBtnQuick.Click += async (_, e) =>
                    {
                        e.Handled = true;
                        await editor.SendSceneChangeSysExAsync(capturedScene);
                    };
                    scenesStack.Children.Add(sBtnQuick);
                }
                Grid.SetColumn(scenesStack, 0);
                row1.Children.Add(scenesStack);
            }
            else
            {
                var volSlider = new Slider
                {
                    Minimum = -30,
                    Maximum = 6,
                    Value = track.VolumeDb,
                    Margin = new Thickness(0, 2, 6, 2),
                    ToolTip = "Volumen (-30dB a +6dB)"
                };
                volSlider.ValueChanged += (_, e) =>
                {
                    track.VolumeDb = (float)e.NewValue;
                    audioEngine.SetVolumeDb(track.Index, track.VolumeDb);
                };
                Grid.SetColumn(volSlider, 0);
                row1.Children.Add(volSlider);
            }

            var panStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var panLabel = new TextBlock { Text = "PAN", FontSize = 9, Foreground = DarkUiTheme.AuraTextMutedBrush, Margin = new Thickness(0, 0, 4, 0) };
            var panSlider = new Slider { Minimum = -1, Maximum = 1, Value = track.Pan, Width = 36, ToolTip = "Paneo Estéreo (L / R)" };
            panSlider.ValueChanged += (_, e) =>
            {
                track.Pan = (float)e.NewValue;
                audioEngine.SetPan(track.Index, track.Pan);
            };
            panStack.Children.Add(panLabel);
            panStack.Children.Add(panSlider);
            Grid.SetColumn(panStack, 1);
            row1.Children.Add(panStack);

            Grid.SetRow(row1, 1);
            headerGrid.Children.Add(row1);

            headerCard.Child = headerGrid;
            trackHeadersStack.Children.Add(headerCard);
        }
    }

    private Border BuildRightTimelinePanel()
    {
        var border = new Border
        {
            Background = DarkUiTheme.AuraBgSurfaceBrush
        };

        var mainStack = new StackPanel();

        // 1. Measure & Beat Ruler (Height = 28)
        var rulerCanvas = new Canvas { Height = RulerHeight, Background = DarkUiTheme.AuraBgInputBrush };
        DrawMeasureRuler(rulerCanvas);
        rulerCanvas.MouseLeftButtonDown += (s, e) =>
        {
            var p = e.GetPosition(rulerCanvas);
            SeekToSeconds(p.X / pixelsPerSecond);
        };
        mainStack.Children.Add(rulerCanvas);

        // 2. GP-200 Automation Lane (Height = 32)
        automationLaneCanvas.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                var p = e.GetPosition(automationLaneCanvas);
                OpenAddTriggerDialog(p.X / pixelsPerSecond);
            }
        };
        DrawAutomationMarkers();
        mainStack.Children.Add(automationLaneCanvas);

        // 3. FL Studio Playlist Editing Toolbar (Height = 36)
        var editingToolbar = BuildEditingToolbar();
        mainStack.Children.Add(editingToolbar);

        // 4. Multi-track Waveform Canvas
        timelineCanvas.Height = Math.Max(TrackHeight * 8, tracks.Count * TrackHeight);
        timelineCanvas.Width = 300 * pixelsPerSecond;
        DrawTrackWaveforms();

        // Add Playhead
        timelineCanvas.Children.Add(playheadLine);
        timelineCanvas.Children.Add(playheadTop);
        Canvas.SetLeft(playheadLine, 0);
        Canvas.SetLeft(playheadTop, 0);

        timelineCanvas.MouseLeftButtonDown += (s, e) =>
        {
            var p = e.GetPosition(timelineCanvas);
            SeekToSeconds(p.X / pixelsPerSecond);
        };

        timelineScroll.Content = timelineCanvas;
        mainStack.Children.Add(timelineScroll);

        border.Child = mainStack;
        return border;
    }

    private Border BuildEditingToolbar()
    {
        var bar = new Border
        {
            Height = EditingToolbarHeight,
            Background = DarkUiTheme.AuraBgDeepBrush,
            BorderBrush = DarkUiTheme.AuraBorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 2, 6, 2)
        };

        var dock = new DockPanel();

        // Right side: Track & Stem actions
        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var addTrackBtn = new Button
        {
            Content = "+ Pista",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = "Añadir una nueva pista vacía"
        };
        addTrackBtn.Click += (_, _) => AddNewEmptyTrack();
        rightStack.Children.Add(addTrackBtn);

        var splitBtn = new Button
        {
            Content = "✂️ Cortar (S)",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = "Dividir el clip de audio en la posición del cabezal (Atajo: tecla S)"
        };
        splitBtn.Click += (_, _) => SplitActiveTrackAtPlayhead();
        rightStack.Children.Add(splitBtn);

        var trimBtn = new Button
        {
            Content = "⏱️ Loop",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = "Recortar el audio de la pista al rango de Loop"
        };
        trimBtn.Click += (_, _) => TrimActiveTrackToLoop();
        rightStack.Children.Add(trimBtn);

        var clearBtn = new Button
        {
            Content = "🗑️ Vaciar",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = "Vacía el archivo de audio de la pista actual (Atajo: tecla Delete)"
        };
        clearBtn.Click += (_, _) => ClearActiveTrack();
        rightStack.Children.Add(clearBtn);

        stemKitQuickImportBtn.Content = "📥 Stems ▾";
        stemKitQuickImportBtn.Padding = new Thickness(6, 2, 6, 2);
        stemKitQuickImportBtn.Margin = new Thickness(2, 0, 0, 0);
        stemKitQuickImportBtn.Click -= StemKitQuickImportBtn_Click;
        stemKitQuickImportBtn.Click += StemKitQuickImportBtn_Click;
        rightStack.Children.Add(stemKitQuickImportBtn);

        var stemKitGuiBtn = new Button
        {
            Content = "🚀 StemKit",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = "Abrir la aplicación StemKit oficial con sincronización automática"
        };
        stemKitGuiBtn.Click += (_, _) => OpenStemKitGuiWithAutoSync();
        rightStack.Children.Add(stemKitGuiBtn);

        DockPanel.SetDock(rightStack, Dock.Right);
        dock.Children.Add(rightStack);

        // Left side: FL Studio Tool Palette + Grid Snap + Active Track Pill
        var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // Undo & Redo Symbol Buttons (↶ and ↷)
        undoBtn.Click -= UndoBtn_Click;
        undoBtn.Click += UndoBtn_Click;
        ToolTipService.SetShowOnDisabled(undoBtn, true);
        leftStack.Children.Add(undoBtn);

        redoBtn.Click -= RedoBtn_Click;
        redoBtn.Click += RedoBtn_Click;
        ToolTipService.SetShowOnDisabled(redoBtn, true);
        leftStack.Children.Add(redoBtn);

        var undoRedoSeparator = new Border
        {
            Width = 1,
            Height = 18,
            Background = DarkUiTheme.AuraBorderSubtleBrush,
            Margin = new Thickness(3, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        leftStack.Children.Add(undoRedoSeparator);

        toolButtons.Clear();
        (PlaylistTool tool, string label, string tip)[] tools = [
            (PlaylistTool.Draw, "✏️ Lápiz", "Lápiz: Seleccionar clips y ubicar cabezal"),
            (PlaylistTool.Paint, "🖌️ Pincel", "Pincel: Edición ágil de clips"),
            (PlaylistTool.Slice, "✂️ Corte", "Cuchilla: Haz clic sobre cualquier onda para cortarla"),
            (PlaylistTool.Delete, "❌ Borrar", "Borrador: Haz clic sobre un clip para eliminar su audio"),
            (PlaylistTool.Mute, "🔇 Mute", "Silenciador: Haz clic en una pista para silenciar / activar"),
            (PlaylistTool.Slip, "⬌ Deslizar", "Deslizar: Desplaza el inicio del audio dentro del clip"),
            (PlaylistTool.Zoom, "🔍 Zoom", "Zoom: Clic izquierdo amplía ondas, clic derecho reduce"),
            (PlaylistTool.Select, "⬚ Select", "Selección de región")
        ];

        foreach (var (t, label, tip) in tools)
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(1, 0, 1, 0),
                Cursor = Cursors.Hand,
                ToolTip = tip
            };
            var capturedTool = t;
            btn.Click += (_, _) => SetActiveTool(capturedTool);
            toolButtons[t] = btn;
            leftStack.Children.Add(btn);
        }
        SetActiveTool(PlaylistTool.Slice);

        // Snap to Grid ComboBox
        snapCombo.Width = 85;
        snapCombo.Items.Clear();
        snapCombo.Items.Add("🧲 Línea");
        snapCombo.Items.Add("🧲 Celda");
        snapCombo.Items.Add("🧲 1 Compás");
        snapCombo.Items.Add("🧲 1/2 Beat");
        snapCombo.Items.Add("🧲 1/4 Beat");
        snapCombo.Items.Add("🧲 Ninguno");
        snapCombo.SelectedIndex = 0;
        snapCombo.SelectionChanged += (_, _) =>
        {
            currentSnap = snapCombo.SelectedIndex switch
            {
                0 => GridSnapMode.Line,
                1 => GridSnapMode.Cell,
                2 => GridSnapMode.Bar,
                3 => GridSnapMode.HalfBeat,
                4 => GridSnapMode.QuarterBeat,
                _ => GridSnapMode.Off
            };
        };
        leftStack.Children.Add(snapCombo);

        leftStack.Children.Add(activeTrackPill);

        dock.Children.Add(leftStack);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = dock,
            Focusable = false
        };
        bar.Child = scroll;
        return bar;
    }

    private void SetActiveTool(PlaylistTool tool)
    {
        currentTool = tool;
        foreach (var (t, btn) in toolButtons)
        {
            bool isActive = t == tool;
            btn.Background = isActive ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraBgCardBrush;
            btn.Foreground = isActive ? DarkUiTheme.AuraBgDeepBrush : DarkUiTheme.AuraTextMainBrush;
            btn.BorderBrush = isActive ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraBorderSubtleBrush;
        }

        timelineCanvas.Cursor = tool switch
        {
            PlaylistTool.Slice => Cursors.Cross,
            PlaylistTool.Zoom => Cursors.SizeAll,
            PlaylistTool.Delete => Cursors.No,
            PlaylistTool.Mute => Cursors.Hand,
            _ => Cursors.Arrow
        };

        dropPromptText.Text = tool switch
        {
            PlaylistTool.Slice => "✂️ Cuchilla (Slice): Haz clic en cualquier parte de una onda para cortar el audio en dos trozos independientes.",
            PlaylistTool.Delete => "❌ Borrador: Haz clic en un clip para eliminar su audio.",
            PlaylistTool.Mute => "🔇 Silenciador: Haz clic en una pista para activar o silenciar.",
            PlaylistTool.Zoom => "🔍 Zoom: Clic izquierdo amplía ondas, clic derecho reduce.",
            PlaylistTool.Draw => "✏️ Lápiz: Seleccionar pistas y saltar a puntos de la línea de tiempo.",
            PlaylistTool.Paint => "🖌️ Pincel: Edición ágil de clips.",
            PlaylistTool.Slip => "⬌ Deslizar: Ajusta la posición de inicio del audio dentro del clip.",
            PlaylistTool.Select => "⬚ Selección: Marca regiones en la línea de tiempo.",
            _ => ""
        };
        dropPromptText.Foreground = DarkUiTheme.AuraCyanPrimaryBrush;
    }

    private void UpdateTrackHeaderSelectionVisuals()
    {
        for (int i = 0; i < trackHeadersStack.Children.Count; i++)
        {
            if (trackHeadersStack.Children[i] is Border card)
            {
                bool isSel = selectedTrackIndices.Contains(i);
                card.Background = isSel ? DarkUiTheme.AuraBgCardBrush : DarkUiTheme.AuraBgSurfaceBrush;
                card.BorderBrush = isSel ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraBorderSubtleBrush;
                card.BorderThickness = new Thickness(isSel ? 2 : 1);
            }
        }
    }

    private void SyncTrackClipsToEngine(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= tracks.Count) return;
        var track = tracks[trackIndex];
        audioEngine.SetTrackClips(trackIndex, track.Clips.Select(c => new TrackClipData
        {
            Id = c.Id,
            FilePath = c.FilePath,
            StartOffsetSeconds = c.StartOffsetSeconds,
            SkipSeconds = c.SkipSeconds,
            TakeSeconds = c.TakeSeconds
        }));
        totalDurationSeconds = Math.Max(totalDurationSeconds, audioEngine.Duration.TotalSeconds);
    }

    private void CalculateClipPeaks(JamClip clip, string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            clip.TotalDurationSeconds = reader.TotalTime.TotalSeconds;
            int samplesToRead = 600;
            var peaks = new List<float>(samplesToRead);
            float[] buffer = new float[reader.WaveFormat.SampleRate / 2];

            while (peaks.Count < samplesToRead && reader.Read(buffer, 0, buffer.Length) > 0)
            {
                float max = buffer.Max(Math.Abs);
                peaks.Add(max);
            }
            clip.WaveformPeaks = peaks.ToArray();
        }
        catch { }
    }

    private void SliceTrackAtTime(int trackIndex, double splitTime)
    {
        if (trackIndex < 0 || trackIndex >= tracks.Count) return;
        var track = tracks[trackIndex];
        if (!track.HasAudio)
        {
            dropPromptText.Text = "⚠️ Esta pista no contiene audio para cortar.";
            dropPromptText.Foreground = DarkUiTheme.WarningBrush;
            return;
        }

        splitTime = SnapSeconds(splitTime);

        // Find the specific clip under splitTime
        var targetClip = track.Clips.FirstOrDefault(c => splitTime > c.StartOffsetSeconds && splitTime < c.EndSeconds);
        if (targetClip == null)
        {
            dropPromptText.Text = "⚠️ Haz clic dentro de una onda de audio para cortarla con la cuchilla en esa posición.";
            dropPromptText.Foreground = DarkUiTheme.WarningBrush;
            return;
        }

        double clipStart = targetClip.StartOffsetSeconds;
        double clipOriginalDuration = targetClip.TakeSeconds > 0 ? targetClip.TakeSeconds : (targetClip.TotalDurationSeconds - targetClip.SkipSeconds);
        double firstPartTake = splitTime - clipStart;
        double secondPartTake = clipOriginalDuration - firstPartTake;

        if (firstPartTake <= 0.05 || secondPartTake <= 0.05)
        {
            return;
        }

        PushUndo("Cortar clip con cuchilla");

        // Clip 1: First part remains on targetClip in place
        targetClip.TakeSeconds = firstPartTake;

        // Clip 2: Second part inserted immediately after targetClip in the EXACT SAME TRACK
        var secondClip = new JamClip
        {
            Name = $"{targetClip.Name} (Corte)",
            FilePath = targetClip.FilePath,
            Color = targetClip.Color,
            StartOffsetSeconds = splitTime,
            SkipSeconds = targetClip.SkipSeconds + firstPartTake,
            TakeSeconds = secondPartTake,
            TotalDurationSeconds = targetClip.TotalDurationSeconds
        };
        CalculateClipPeaks(secondClip, targetClip.FilePath);

        int insertIndex = track.Clips.IndexOf(targetClip) + 1;
        track.Clips.Insert(insertIndex, secondClip);

        SyncTrackClipsToEngine(trackIndex);

        dropPromptText.Text = $"✂️ Cuchilla: Clip cortado en dos partes en {TimeSpan.FromSeconds(splitTime):mm\\:ss\\.ff}. Ambas partes permanecen en la misma pista ({track.Name}).";
        dropPromptText.Foreground = DarkUiTheme.AuraCyanPrimaryBrush;

        selectedTrackIndex = trackIndex;
        RefreshCanvas();
        UpdateSelectedTrackUI();
    }

    private double SnapSeconds(double seconds)
    {
        double secondsPerBeat = 60.0 / Math.Max(20, currentBpm);
        double secondsPerBar = secondsPerBeat * 4.0;

        return currentSnap switch
        {
            GridSnapMode.Bar => Math.Round(seconds / secondsPerBar) * secondsPerBar,
            GridSnapMode.HalfBeat => Math.Round(seconds / (secondsPerBeat * 0.5)) * (secondsPerBeat * 0.5),
            GridSnapMode.QuarterBeat => Math.Round(seconds / (secondsPerBeat * 0.25)) * (secondsPerBeat * 0.25),
            GridSnapMode.Line => Math.Round(seconds / secondsPerBeat) * secondsPerBeat,
            GridSnapMode.Cell => Math.Round(seconds / (secondsPerBeat * 0.25)) * (secondsPerBeat * 0.25),
            _ => seconds
        };
    }

    private void SelectTrack(int index)
    {
        if (index < 0 || index >= tracks.Count) return;
        selectedTrackIndex = index;
        selectedTrackIndices.Clear();
        selectedTrackIndices.Add(index);
        UpdateSelectedTrackUI();
        UpdateTrackHeaderSelectionVisuals();
        RefreshCanvas();
    }

    private void UpdateSelectedTrackUI()
    {
        if (selectedTrackIndex >= 0 && selectedTrackIndex < tracks.Count)
        {
            var t = tracks[selectedTrackIndex];
            string selInfo = selectedTrackIndices.Count > 1 ? $" (+{selectedTrackIndices.Count - 1} pistas seleccionadas)" : "";
            activeTrackPill.Text = $"Pista Activa: {t.Name}{selInfo}";
            activeTrackPill.Foreground = new SolidColorBrush(t.ThemeColor);
        }
    }

    private void DrawTrackWaveforms()
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            int capturedIndex = i;
            var track = tracks[i];
            double top = i * TrackHeight;
            bool isSelected = selectedTrackIndices.Contains(i);

            var laneBorder = new Rectangle
            {
                Width = timelineCanvas.Width,
                Height = TrackHeight,
                Fill = isSelected
                    ? new SolidColorBrush(Color.FromArgb(35, 0, 229, 255))
                    : (i % 2 == 0 ? DarkUiTheme.AuraBgSurfaceBrush : DarkUiTheme.AuraBgCardBrush),
                Stroke = isSelected ? DarkUiTheme.AuraCyanPrimaryBrush : DarkUiTheme.AuraBorderSubtleBrush,
                StrokeThickness = isSelected ? 1.5 : 0.5,
                AllowDrop = true
            };

            laneBorder.MouseLeftButtonDown += (s, e) =>
            {
                var p = e.GetPosition(timelineCanvas);
                double clickSec = p.X / pixelsPerSecond;

                switch (currentTool)
                {
                    case PlaylistTool.Slice:
                        SliceTrackAtTime(capturedIndex, clickSec);
                        break;
                    case PlaylistTool.Delete:
                        var clipToDelete = track.Clips.FirstOrDefault(c => clickSec >= c.StartOffsetSeconds && clickSec <= c.EndSeconds);
                        if (clipToDelete != null)
                        {
                            track.Clips.Remove(clipToDelete);
                            SyncTrackClipsToEngine(capturedIndex);
                            RefreshCanvas();
                            dropPromptText.Text = $"❌ Clip '{clipToDelete.Name}' eliminado de {track.Name}.";
                            dropPromptText.Foreground = DarkUiTheme.WarningBrush;
                        }
                        else
                        {
                            selectedTrackIndex = capturedIndex;
                            ClearActiveTrack();
                        }
                        break;
                    case PlaylistTool.Mute:
                        PushUndo(tracks[capturedIndex].IsMuted ? "Activar pista" : "Silenciar pista");
                        tracks[capturedIndex].IsMuted = !tracks[capturedIndex].IsMuted;
                        audioEngine.SetMute(capturedIndex, tracks[capturedIndex].IsMuted);
                        RenderTrackHeaders();
                        RefreshCanvas();
                        break;
                    case PlaylistTool.Zoom:
                        AdjustTimelineZoom(+20);
                        break;
                    case PlaylistTool.Slip:
                        var slipClip = track.Clips.FirstOrDefault(c => clickSec >= c.StartOffsetSeconds && clickSec <= c.EndSeconds);
                        if (slipClip != null)
                        {
                            PushUndo("Deslizar clip");
                            slipClip.StartOffsetSeconds = Math.Max(0, SnapSeconds(clickSec));
                            SyncTrackClipsToEngine(capturedIndex);
                            RefreshCanvas();
                        }
                        break;
                    case PlaylistTool.Select:
                    case PlaylistTool.Draw:
                    case PlaylistTool.Paint:
                    default:
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                        {
                            if (selectedTrackIndices.Contains(capturedIndex))
                                selectedTrackIndices.Remove(capturedIndex);
                            else
                                selectedTrackIndices.Add(capturedIndex);
                        }
                        else
                        {
                            selectedTrackIndices.Clear();
                            selectedTrackIndices.Add(capturedIndex);
                        }
                        selectedTrackIndex = capturedIndex;
                        UpdateTrackHeaderSelectionVisuals();
                        RefreshCanvas();
                        UpdateSelectedTrackUI();
                        SeekToSeconds(clickSec);
                        break;
                }
            };

            laneBorder.MouseRightButtonDown += (s, e) =>
            {
                if (currentTool == PlaylistTool.Zoom)
                {
                    AdjustTimelineZoom(-20);
                    e.Handled = true;
                }
            };

            laneBorder.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effects = DragDropEffects.Copy;
            };

            laneBorder.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0) LoadAudioIntoTrack(capturedIndex, files[0]);
                }
            };

            Canvas.SetLeft(laneBorder, 0);
            Canvas.SetTop(laneBorder, top);
            timelineCanvas.Children.Add(laneBorder);

            // Render all clips on this track lane (including sliced parts and multiple takes)
            foreach (var clip in track.Clips)
            {
                if (string.IsNullOrEmpty(clip.FilePath) || !File.Exists(clip.FilePath)) continue;

                double clipX = clip.StartOffsetSeconds * pixelsPerSecond;
                double effectiveSec = clip.TakeSeconds > 0 ? clip.TakeSeconds : Math.Max(0.5, clip.TotalDurationSeconds - clip.SkipSeconds);
                double clipW = effectiveSec * pixelsPerSecond;

                // Visual clip card container
                var clipBox = new Border
                {
                    Width = Math.Max(4, clipW),
                    Height = TrackHeight - 8,
                    Background = new SolidColorBrush(Color.FromArgb(clip.IsMuted ? (byte)20 : (byte)45, clip.Color.R, clip.Color.G, clip.Color.B)),
                    BorderBrush = new SolidColorBrush(clip.Color),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Cursor = currentTool switch
                    {
                        PlaylistTool.Slice => Cursors.Cross,
                        PlaylistTool.Delete => Cursors.No,
                        PlaylistTool.Mute => Cursors.Hand,
                        PlaylistTool.Slip => Cursors.SizeWE,
                        _ => Cursors.Hand
                    }
                };

                var capturedClip = clip;
                clipBox.MouseLeftButtonDown += (s, e) =>
                {
                    var p = e.GetPosition(timelineCanvas);
                    double clickSec = p.X / pixelsPerSecond;

                    if (currentTool == PlaylistTool.Slice)
                    {
                        SliceTrackAtTime(capturedIndex, clickSec);
                        e.Handled = true;
                    }
                    else if (currentTool == PlaylistTool.Delete)
                    {
                        track.Clips.Remove(capturedClip);
                        SyncTrackClipsToEngine(capturedIndex);
                        RefreshCanvas();
                        dropPromptText.Text = $"❌ Clip '{capturedClip.Name}' eliminado de {track.Name}.";
                        dropPromptText.Foreground = DarkUiTheme.WarningBrush;
                        e.Handled = true;
                    }
                    else if (currentTool == PlaylistTool.Mute)
                    {
                        capturedClip.IsMuted = !capturedClip.IsMuted;
                        RefreshCanvas();
                        e.Handled = true;
                    }
                    else if (currentTool == PlaylistTool.Slip)
                    {
                        selectedClip = capturedClip;
                    }
                };

                Canvas.SetLeft(clipBox, clipX);
                Canvas.SetTop(clipBox, top + 4);
                timelineCanvas.Children.Add(clipBox);

                // Waveform path
                if (clip.WaveformPeaks != null && clip.WaveformPeaks.Length > 0)
                {
                    var waveGeometry = new StreamGeometry();
                    using (var gc = waveGeometry.Open())
                    {
                        double midY = top + (TrackHeight / 2.0);
                        double xStep = clipW / clip.WaveformPeaks.Length;

                        gc.BeginFigure(new Point(clipX, midY), true, true);
                        for (int p = 0; p < clip.WaveformPeaks.Length; p++)
                        {
                            double x = clipX + (p * xStep);
                            double amp = clip.WaveformPeaks[p] * (TrackHeight / 2.3);
                            gc.LineTo(new Point(x, midY - amp), false, false);
                        }
                        for (int p = clip.WaveformPeaks.Length - 1; p >= 0; p--)
                        {
                            double x = clipX + (p * xStep);
                            double amp = clip.WaveformPeaks[p] * (TrackHeight / 2.3);
                            gc.LineTo(new Point(x, midY + amp), false, false);
                        }
                    }

                    var wavePath = new System.Windows.Shapes.Path
                    {
                        Data = waveGeometry,
                        Fill = new SolidColorBrush(Color.FromArgb(clip.IsMuted ? (byte)50 : (byte)180, clip.Color.R, clip.Color.G, clip.Color.B)),
                        Stroke = new SolidColorBrush(clip.Color),
                        StrokeThickness = 1,
                        IsHitTestVisible = false
                    };
                    timelineCanvas.Children.Add(wavePath);
                }

                // Clip title tag
                var clipLabel = new TextBlock
                {
                    Text = !string.IsNullOrEmpty(clip.Name) ? clip.Name : track.Name,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(clipX + 6, top + 6, 0, 0),
                    IsHitTestVisible = false
                };
                timelineCanvas.Children.Add(clipLabel);

                // Razor Cut Divider Line (distinct visual separation between adjacent clips)
                if (clip.StartOffsetSeconds > 0)
                {
                    var cutLine = new Line
                    {
                        X1 = clipX,
                        Y1 = top + 4,
                        X2 = clipX,
                        Y2 = top + TrackHeight - 4,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        StrokeDashArray = new DoubleCollection { 2, 2 },
                        IsHitTestVisible = false
                    };
                    timelineCanvas.Children.Add(cutLine);
                }
            }
        }
    }

    private Border BuildBottomDropBar()
    {
        bottomDropBar.Drop += (s, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    LoadSongFile(files[0]);
                }
            }
        };
        bottomDropBar.DragEnter += (s, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
        };

        var dock = new DockPanel { Margin = new Thickness(16, 0, 16, 0) };

        var progressStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        progressStack.Children.Add(stemProgressBar);
        progressStack.Children.Add(stemProgressText);
        progressStack.Children.Add(cancelOperationBtn);
        cancelOperationBtn.Click += (_, _) =>
        {
            operationCts?.Cancel();
            SetOperationStatus(false);
        };

        DockPanel.SetDock(progressStack, Dock.Right);
        dock.Children.Add(progressStack);
        dock.Children.Add(dropPromptText);

        bottomDropBar.Child = dock;
        return bottomDropBar;
    }

    private void DrawMeasureRuler(Canvas canvas)
    {
        canvas.Children.Clear();
        double totalSeconds = Math.Max(300, totalDurationSeconds);
        double totalWidth = totalSeconds * pixelsPerSecond;
        canvas.Width = totalWidth;

        double secondsPerBeat = 60.0 / currentBpm;
        double secondsPerBar = secondsPerBeat * 4.0;
        int bar = 1;

        for (double t = 0; t < totalSeconds; t += secondsPerBar)
        {
            double x = t * pixelsPerSecond;
            var barLine = new Line
            {
                X1 = x, X2 = x,
                Y1 = 0, Y2 = RulerHeight,
                Stroke = DarkUiTheme.AuraBorderSubtleBrush,
                StrokeThickness = 1
            };
            canvas.Children.Add(barLine);

            var barText = new TextBlock
            {
                Text = bar.ToString(),
                FontSize = 10,
                Foreground = DarkUiTheme.AuraTextMutedBrush,
                Margin = new Thickness(x + 4, 4, 0, 0)
            };
            canvas.Children.Add(barText);
            bar++;
        }
    }

    private void DrawAutomationMarkers()
    {
        automationLaneCanvas.Children.Clear();
        automationLaneCanvas.Width = Math.Max(300, totalDurationSeconds) * pixelsPerSecond;

        var laneTag = new TextBlock
        {
            Text = "⚡ Pista de Automatización GP-200 (Doble clic para añadir cambio de Escena/Preset)",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = DarkUiTheme.AuraCyanPrimaryBrush,
            Margin = new Thickness(8, 8, 0, 0)
        };
        automationLaneCanvas.Children.Add(laneTag);

        foreach (var trigger in automationTriggers)
        {
            double x = trigger.TimeSeconds * pixelsPerSecond;

            var flag = new Border
            {
                Background = DarkUiTheme.AuraCyanPrimaryBrush,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Cursor = Cursors.Hand,
                ToolTip = $"{trigger.DisplayTime} - {trigger.DisplayAction}"
            };
            var flagText = new TextBlock
            {
                Text = trigger.DisplayAction,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = DarkUiTheme.AuraBgDeepBrush
            };
            flag.Child = flagText;

            Canvas.SetLeft(flag, x);
            Canvas.SetTop(flag, 4);
            automationLaneCanvas.Children.Add(flag);
        }
    }



    private void WireEvents()
    {
        // 1. Load Audio / YouTube
        loadAudioBtn.Click += (_, _) => ShowLoadAudioMenu();

        // 2. StemKit Backend Hub
        stemKitBackendBtn.Click += (_, _) => ShowStemKitBackendMenu();

        // 3. Separate Stems
        separateStemsBtn.Click += async (_, _) => await StartStemSeparationAsync();

        // 4. Consolidated Sub-Menus
        practiceModesMenuBtn.Click += (_, _) => ShowPracticeModesMenu(practiceModesMenuBtn);
        exportMenuBtn.Click += (_, _) => ShowExportMenu(exportMenuBtn);
        viewMenuBtn.Click += (_, _) => ShowViewZoomMenu(viewMenuBtn);

        // Quick Practice Presets & Export Actions
        practiceModeBtn.Click += (_, _) => ApplyGuitarPracticeMode();
        karaokeModeBtn.Click += (_, _) => ApplyKaraokeMode();
        drumsBassModeBtn.Click += (_, _) => ApplyDrumsAndBassMode();
        resetMixBtn.Click += (_, _) => ResetMix();
        exportMixBtn.Click += async (_, _) => await ExportMixdownDialogAsync();
        exportStemsBtn.Click += (_, _) => ExportIndividualStems();

        // 5. Transport Controls
        playPauseBtn.Click += (_, _) => TogglePlayPause();
        rewindBtn.Click += (_, _) => SeekToSeconds(0);
        recGuitarBtn.Click += (_, _) => ToggleGuitarRecording();
        loopToggle.Click += (_, _) =>
        {
            isLooping = loopToggle.IsChecked == true;
            loopToggle.Content = isLooping ? "🔁 Loop: ON" : "🔁 Loop: OFF";
            audioEngine.LoopEnabled = isLooping;
            audioEngine.LoopStart = TimeSpan.FromSeconds(loopStartSeconds);
            audioEngine.LoopEnd = TimeSpan.FromSeconds(loopEndSeconds > loopStartSeconds ? loopEndSeconds : Math.Max(totalDurationSeconds, 1.0));
        };

        // 6. BPM & Tap Tempo
        tapTempoBtn.Click += async (_, _) =>
        {
            if (int.TryParse(bpmBox.Text, out int bpm))
            {
                currentBpm = Math.Clamp(bpm, 40, 250);
                flBpmReadout.Text = $"{currentBpm:0.000}";
                await editor.SendTapTempoToPedalAsync(currentBpm);
            }
        };

        bpmUpBtn.Click += async (_, _) =>
        {
            currentBpm = Math.Clamp(currentBpm + 1, 40, 250);
            bpmBox.Text = currentBpm.ToString();
            flBpmReadout.Text = $"{currentBpm:0.000}";
            await editor.SendTapTempoToPedalAsync(currentBpm);
        };

        bpmDownBtn.Click += async (_, _) =>
        {
            currentBpm = Math.Clamp(currentBpm - 1, 40, 250);
            bpmBox.Text = currentBpm.ToString();
            flBpmReadout.Text = $"{currentBpm:0.000}";
            await editor.SendTapTempoToPedalAsync(currentBpm);
        };

        bpmBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(bpmBox.Text, out int b))
            {
                currentBpm = Math.Clamp(b, 40, 250);
                flBpmReadout.Text = $"{currentBpm:0.000}";
            }
        };

        // FL Studio Playback Mode Toggle [● SONG / PAT]
        flModeToggleBtn.Click += (_, _) =>
        {
            isPatternMode = flModeToggleBtn.IsChecked == true;
            flModeToggleBtn.Content = isPatternMode ? "● PAT" : "● SONG";
            flModeToggleBtn.Foreground = isPatternMode ? DarkUiTheme.WarningBrush : DarkUiTheme.AuraCyanPrimaryBrush;
            if (isPatternMode)
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    bool solo = (i == selectedTrackIndex);
                    audioEngine.SetSolo(i, solo);
                }
                dropPromptText.Text = $"● Modo PAT (Pattern): Reproduciendo únicamente {tracks[selectedTrackIndex].Name}";
                dropPromptText.Foreground = DarkUiTheme.WarningBrush;
            }
            else
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    audioEngine.SetSolo(i, tracks[i].IsSolo);
                }
                dropPromptText.Text = "● Modo SONG: Reproduciendo todas las pistas de la canción";
                dropPromptText.Foreground = DarkUiTheme.AuraCyanPrimaryBrush;
            }
        };

        // 7. Master Volume
        masterVolSlider.ValueChanged += (_, e) =>
        {
            float db = (float)((e.NewValue - 100) * 0.3);
            masterVolText.Text = $"{db:F1} dB";
            audioEngine.MasterVolume = (float)(e.NewValue / 100.0);
        };

        // 9. Drop zone prompt click
        dropPromptText.Cursor = Cursors.Hand;
        dropPromptText.MouseDown += (_, _) => ShowLoadAudioMenu();
    }

    private async Task ExportMixdownDialogAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Archivo de Onda WAV (*.wav)|*.wav",
            FileName = $"{IOPath.GetFileNameWithoutExtension(loadedSongPath ?? "Mix")}_GP200_Jam.wav"
        };
        if (dialog.ShowDialog() == true)
        {
            await audioEngine.ExportMixdownAsync(dialog.FileName);
            UiDialogs.Show(this, "✓ Mezcla multipista exportada con éxito en formato WAV de estudio.", "Exportar Mezcla");
        }
    }

    private void ShowLoadAudioMenu()
    {
        var menu = new ContextMenu();

        var localFileItem = new MenuItem { Header = "📁 Abrir archivo local (.mp3, .wav, .flac)...", FontWeight = FontWeights.SemiBold };
        localFileItem.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Archivos de Audio|*.mp3;*.wav;*.flac;*.m4a;*.ogg|Todos los archivos|*.*",
                Title = "Cargar Audio para GP-200 Jam Studio"
            };
            if (dialog.ShowDialog() == true)
            {
                LoadSongFile(dialog.FileName);
            }
        };
        menu.Items.Add(localFileItem);

        var urlItem = new MenuItem { Header = "🌐 Pegar enlace de YouTube o URL de audio...", ToolTip = "Descarga y procesa automáticamente canciones desde YouTube" };
        urlItem.Click += (_, _) => OpenYouTubeUrlDialog();
        menu.Items.Add(urlItem);

        menu.Items.Add(new Separator());

        var stemKitGuiItem = new MenuItem
        {
            Header = "🚀 Abrir StemKit GUI (Auto-Sync en tiempo real)...",
            ToolTip = "Abre StemKit.exe y sincroniza automáticamente los stems con este DAW al terminar",
            FontWeight = FontWeights.SemiBold
        };
        stemKitGuiItem.Click += (_, _) => OpenStemKitGuiWithAutoSync();
        menu.Items.Add(stemKitGuiItem);

        var folderItem = new MenuItem
        {
            Header = "📂 Importar carpeta con stems (.wav, .flac, .mp3)...",
            ToolTip = "Carga todos los archivos de audio de una carpeta en pistas independientes"
        };
        folderItem.Click += (_, _) => ImportStemsFromFolderDialog();
        menu.Items.Add(folderItem);

        menu.PlacementTarget = loadAudioBtn;
        menu.IsOpen = true;
    }

    private void OpenYouTubeUrlDialog()
    {
        var dialog = new Window
        {
            Title = "Importar Canción desde YouTube",
            Width = 440,
            Height = 180,
            Background = DarkUiTheme.AuraBgDeepBrush,
            Foreground = DarkUiTheme.AuraTextMainBrush,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Pega aquí el enlace del video de YouTube:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

        var urlBox = new TextBox { Text = "https://www.youtube.com/watch?v=", Margin = new Thickness(0, 0, 0, 14) };
        stack.Children.Add(urlBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "Descargar y Separar", Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand };
        okBtn.Click += async (_, _) =>
        {
            string url = urlBox.Text.Trim();
            dialog.Close();
            if (!string.IsNullOrEmpty(url))
            {
                await ProcessYouTubeUrlAsync(url);
            }
        };
        btnRow.Children.Add(okBtn);
        stack.Children.Add(btnRow);

        dialog.Content = stack;
        dialog.ShowDialog();
    }

    private async Task ProcessYouTubeUrlAsync(string url)
    {
        operationCts = new CancellationTokenSource();
        SetOperationStatus(true, "Descargando audio de alta fidelidad desde YouTube...");

        var progress = new Progress<StemKitProgressStatus>(status =>
        {
            stemProgressBar.Value = status.Percentage;
            stemProgressText.Text = status.Message;
        });

        try
        {
            string? wavPath = await StemKitBridgeService.DownloadYouTubeAudioAsync(url, progress, operationCts.Token);
            if (wavPath != null && File.Exists(wavPath))
            {
                LoadSongFile(wavPath);
                dropPromptText.Text = $"🌐 Canción descargada desde YouTube: {IOPath.GetFileNameWithoutExtension(wavPath)}";
                SetOperationStatus(false);
                await StartStemSeparationAsync();
            }
            else
            {
                SetOperationStatus(false);
                UiDialogs.Show(this, "No se pudo descargar el audio del video de YouTube. Verifica el enlace o carga un archivo local (.mp3/.wav).", "YouTube");
            }
        }
        catch (OperationCanceledException)
        {
            SetOperationStatus(false);
        }
        catch (Exception ex)
        {
            SetOperationStatus(false);
            UiDialogs.Show(this, $"Error al descargar desde YouTube: {ex.Message}", "YouTube");
        }
    }

    private void ShowStemKitBackendMenu()
    {
        var menu = new ContextMenu();

        string? installedPath = StemKitBridgeService.GetInstalledStemKitPath();
        bool isInstalled = installedPath != null;

        var statusHeader = new MenuItem
        {
            Header = isInstalled ? $"✓ StemKit Backend: Activo ({IOPath.GetFileName(installedPath)})" : "○ StemKit Backend: No instalado en el equipo",
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            Foreground = isInstalled ? DarkUiTheme.SuccessBrush : DarkUiTheme.WarningBrush
        };
        menu.Items.Add(statusHeader);

        menu.Items.Add(new Separator());

        // Option A: Open StemKit GUI with Auto-Sync
        var guiItem = new MenuItem
        {
            Header = "🚀 Abrir StemKit GUI con Auto-Sync (Sincronización en tiempo real)",
            ToolTip = "Abre StemKit.exe y detecta e importa automáticamente los stems cuando termines",
            FontWeight = FontWeights.Bold,
            Foreground = DarkUiTheme.AuraCyanPrimaryBrush
        };
        guiItem.Click += (_, _) => OpenStemKitGuiWithAutoSync();
        menu.Items.Add(guiItem);

        // Option B: Import Stems Folder
        var importFolderItem = new MenuItem
        {
            Header = "📂 Importar carpeta con stems de proyecto (.wav, .flac, .mp3)...",
            ToolTip = "Selecciona una carpeta donde tengas stems para cargarlos de inmediato"
        };
        importFolderItem.Click += (_, _) => ImportStemsFromFolderDialog();
        menu.Items.Add(importFolderItem);

        menu.Items.Add(new Separator());

        // Option 1: Automatic Download from GitHub
        var downloadItem = new MenuItem
        {
            Header = "⬇️ Descargar Última Versión desde GitHub (danielravina/stemkit v0.2.0)",
            ToolTip = "Descarga e instala de forma aislada sin terminal ni configuraciones complejas"
        };
        downloadItem.Click += async (_, _) => await TriggerDownloadStemKitAsync();
        menu.Items.Add(downloadItem);

        // Option 2: Auto-detect on PC
        var autoDetectItem = new MenuItem
        {
            Header = "🔍 Buscar StemKit en este equipo automáticamente",
            ToolTip = "Escanea LocalAppData, Programs, Downloads y el sistema"
        };
        autoDetectItem.Click += (_, _) =>
        {
            string? found = StemKitBridgeService.AutoLocateStemKitOnComputer();
            if (found != null)
            {
                UiDialogs.Show(this, $"✓ StemKit detectado con éxito en:\n{found}", "StemKit Backend");
                UpdateBackendStatusBanner(found);
            }
            else
            {
                UiDialogs.Show(this, "No se encontró una instalación existente de StemKit en las rutas estándar. Puedes descargarlo en 1 clic.", "StemKit");
            }
        };
        menu.Items.Add(autoDetectItem);

        // Option 3: Manual browse
        var browseItem = new MenuItem
        {
            Header = "📁 Seleccionar ubicación de StemKit manualmente...",
            ToolTip = "Elige la carpeta o ejecutable de StemKit en tu disco duro"
        };
        browseItem.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Ejecutable StemKit (*.exe)|*.exe|Todos los archivos|*.*",
                Title = "Seleccionar ejecutable de StemKit o Demucs"
            };
            if (dialog.ShowDialog() == true)
            {
                StemKitBridgeService.SaveCustomStemKitPath(dialog.FileName);
                UiDialogs.Show(this, "✓ Ruta de StemKit guardada correctamente.", "StemKit Backend");
                UpdateBackendStatusBanner(dialog.FileName);
            }
        };
        menu.Items.Add(browseItem);

        menu.Items.Add(new Separator());

        // Model selection
        var modelHeader = new MenuItem { Header = "MODELO DE SEPARACIÓN (DEMUCS / MDX):", IsEnabled = false, FontWeight = FontWeights.Bold };
        menu.Items.Add(modelHeader);

        var m6s = new MenuItem { Header = "htdemucs_6s (6 Stems: Voz, Batería, Bajo, Guitarra, Piano, Otros)", IsChecked = chosenModel == StemKitModel.Htdemucs6s };
        m6s.Click += (_, _) => chosenModel = StemKitModel.Htdemucs6s;
        menu.Items.Add(m6s);

        var m4ft = new MenuItem { Header = "htdemucs_ft (4 Stems Alta Fidelidad)", IsChecked = chosenModel == StemKitModel.Htdemucs4sFinetuned };
        m4ft.Click += (_, _) => chosenModel = StemKitModel.Htdemucs4sFinetuned;
        menu.Items.Add(m4ft);

        var m4fast = new MenuItem { Header = "htdemucs (Rápido)", IsChecked = chosenModel == StemKitModel.Htdemucs4sFast };
        m4fast.Click += (_, _) => chosenModel = StemKitModel.Htdemucs4sFast;
        menu.Items.Add(m4fast);

        menu.PlacementTarget = stemKitBackendBtn;
        menu.IsOpen = true;
    }

    private async Task TriggerDownloadStemKitAsync()
    {
        operationCts = new CancellationTokenSource();
        SetOperationStatus(true, "Iniciando descarga de StemKit v0.2.0 desde GitHub...");

        var progress = new Progress<StemKitDownloadProgress>(p =>
        {
            stemProgressBar.Value = p.Percentage;
            stemProgressText.Text = p.StatusText;
        });

        string? result = await StemKitBridgeService.DownloadLatestStemKitFromGitHubAsync(progress, operationCts.Token);
        SetOperationStatus(false);

        if (result != null)
        {
            UiDialogs.Show(this, "✓ StemKit descargado e instalado con éxito en segundo plano. Listo para separar stems.", "StemKit Backend");
            UpdateBackendStatusBanner(result);
        }
        else
        {
            UiDialogs.Show(this, "No se pudo completar la descarga. Verifica tu conexión a internet o utiliza el motor DSP offline.", "Descarga");
        }
    }

    private void UpdateBackendStatusBanner(string? path)
    {
        if (path != null)
        {
            stemKitBackendBtn.Content = "✓ StemKit Backend ▾";
            stemKitBackendBtn.Foreground = DarkUiTheme.SuccessBrush;
        }
        else
        {
            stemKitBackendBtn.Content = "⚙️ StemKit Backend ▾";
            stemKitBackendBtn.Foreground = DarkUiTheme.AuraTextMainBrush;
        }
    }

    private async Task StartStemSeparationAsync()
    {
        if (string.IsNullOrEmpty(loadedSongPath) || !File.Exists(loadedSongPath))
        {
            UiDialogs.Show(this, "Por favor carga un archivo de audio primero usando [+ Cargar Audio / YouTube].", "StemKit");
            return;
        }

        // Check if backend is available or prompt download
        if (!StemKitBridgeService.IsStemKitInstalled())
        {
            var res = MessageBox.Show(
                this,
                "El backend de StemKit no está configurado aún en tu equipo.\n\n" +
                "¿Deseas descargarlo automáticamente desde GitHub (danielravina/stemkit v0.2.0) sin tocar terminales ni dependencias complejas?\n\n" +
                "Pulsa 'Sí' para descarga automática, o 'No' para usar la separación DSP interna inmediata.",
                "Instalación de StemKit",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
            {
                await TriggerDownloadStemKitAsync();
            }
            else if (res == MessageBoxResult.Cancel)
            {
                return;
            }
        }

        operationCts = new CancellationTokenSource();
        SetOperationStatus(true, "Separando stems con StemKit en segundo plano...");

        try
        {
            var opts = new StemKitSeparationOptions
            {
                Model = chosenModel,
                Device = chosenDevice,
                OutputFormat = chosenFormat
            };

            var prog = new Progress<StemKitProgressStatus>(status =>
            {
                stemProgressBar.Value = status.Percentage;
                stemProgressText.Text = $"⚙️ StemKit ({chosenModel}) {status.Percentage}% — {status.Message}";
                currentBpm = status.Bpm;
                currentKey = status.Key;
                bpmBox.Text = currentBpm.ToString();
                keyPill.Text = currentKey;
            });

            var result = await stemKitBridge.SeparateSongAsync(loadedSongPath, opts, prog, operationCts.Token);

            // Dynamically ingest and map detected stems
            var detected = StemKitBridgeService.MapStemsFromDirectory(result.OutputDirectory);
            ApplyDynamicStemMapping(detected);

            totalDurationSeconds = audioEngine.Duration.TotalSeconds;
            RefreshCanvas();
            SetOperationStatus(false);
            UiDialogs.Show(this, "✓ Separación completada. Pistas cargadas y mapeadas para práctica de guitarra.", "StemKit");
        }
        catch (OperationCanceledException)
        {
            SetOperationStatus(false);
        }
        catch (Exception ex)
        {
            SetOperationStatus(false);
            UiDialogs.Show(this, $"Error durante la separación: {ex.Message}", "Error StemKit");
        }
    }

    /// <summary>
    /// Dynamically assigns the detected stems into the DAW channels.
    /// Never forces rigid hardcoded names; properly flags guitar stem for practice.
    /// </summary>
    private void ApplyDynamicStemMapping(List<DetectedStemInfo> detectedStems)
    {
        audioEngine.Stop();

        for (int i = 0; i < detectedStems.Count; i++)
        {
            while (tracks.Count <= i)
            {
                AddNewEmptyTrack();
            }

            var d = detectedStems[i];
            tracks[i].Name = $"Track {i + 1} · {d.DisplayName}";
            tracks[i].Icon = d.Icon;
            tracks[i].ThemeColor = d.ThemeColor;
            tracks[i].FilePath = d.FilePath;
            tracks[i].IsGuitarStem = d.IsGuitarStem;
            tracks[i].RoleKey = d.RoleKey;
            tracks[i].StartOffsetSeconds = 0;
            tracks[i].SkipSeconds = 0;
            tracks[i].TakeSeconds = 0;

            audioEngine.LoadStem(i, d.FilePath);
            CalculateTrackPeaks(tracks[i], d.FilePath);
        }

        // Ensure GP-200 live guitar track exists at the end
        if (!tracks.Any(t => t.RoleKey == "gp200"))
        {
            int liveIdx = tracks.Count;
            tracks.Add(new JamTrack
            {
                Index = liveIdx,
                RoleKey = "gp200",
                Name = $"Track {liveIdx + 1} · ⚡ MI GUITARRA GP-200",
                Icon = "⚡",
                ThemeColor = Color.FromRgb(0, 229, 255),
                IsArmed = true,
                IsMonitoring = true
            });
            audioEngine.EnsureChannelCount(tracks.Count);
        }

        totalDurationSeconds = audioEngine.Duration.TotalSeconds;
        selectedTrackIndex = 0;
        RenderTrackHeaders();
        RefreshCanvas();
        UpdateSelectedTrackUI();
    }

    private void ShowPracticeModesMenu(Button anchor)
    {
        var menu = new ContextMenu();

        var miPractice = new MenuItem
        {
            Header = "🎸 Modo Práctica Guitarra (Silenciar guitarra original)",
            FontWeight = FontWeights.Bold,
            ToolTip = "Silencia la pista de guitarra original de la canción para que toques la tuya encima con tu GP-200"
        };
        miPractice.Click += (_, _) => ApplyGuitarPracticeMode();
        menu.Items.Add(miPractice);

        var miKaraoke = new MenuItem
        {
            Header = "🎤 Modo Karaoke (Silenciar voces)",
            ToolTip = "Silencia la pista de voces de la canción"
        };
        miKaraoke.Click += (_, _) => ApplyKaraokeMode();
        menu.Items.Add(miKaraoke);

        var miRhythm = new MenuItem
        {
            Header = "🥁 Modo Ritmo (Solo Batería + Bajo)",
            ToolTip = "Solo reproduce batería y bajo para practicar tempo, compás y groove"
        };
        miRhythm.Click += (_, _) => ApplyDrumsAndBassMode();
        menu.Items.Add(miRhythm);

        menu.Items.Add(new Separator());

        var miReset = new MenuItem
        {
            Header = "🔄 Restablecer Pistas (Desmutear todas)",
            ToolTip = "Desactiva todos los silenciados de las pistas"
        };
        miReset.Click += (_, _) => ResetMix();
        menu.Items.Add(miReset);

        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void ShowExportMenu(Button anchor)
    {
        var menu = new ContextMenu();

        var miExportMix = new MenuItem
        {
            Header = "💾 Exportar Mezcla Master (.WAV)",
            FontWeight = FontWeights.Bold,
            ToolTip = "Exporta la mezcla master actual con todas las pistas activas como archivo .WAV"
        };
        miExportMix.Click += async (_, _) => await ExportMixdownDialogAsync();
        menu.Items.Add(miExportMix);

        var miExportStems = new MenuItem
        {
            Header = "📁 Exportar Todos los Stems Individuales (.WAV)",
            ToolTip = "Exporta cada stem separado como archivo de audio independiente a una carpeta"
        };
        miExportStems.Click += (_, _) => ExportIndividualStems();
        menu.Items.Add(miExportStems);

        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void ShowViewZoomMenu(Button anchor)
    {
        var menu = new ContextMenu();

        var miZoom100 = new MenuItem { Header = "🔍 Zoom 100% (Restablecer)" };
        miZoom100.Click += (_, _) => AdjustWindowZoom(1.0 - currentWindowZoom);
        menu.Items.Add(miZoom100);

        var miZoomIn = new MenuItem { Header = "➕ Ampliar Ventana (+5%)" };
        miZoomIn.Click += (_, _) => AdjustWindowZoom(+0.05);
        menu.Items.Add(miZoomIn);

        var miZoomOut = new MenuItem { Header = "➖ Reducir Ventana (-5%)" };
        miZoomOut.Click += (_, _) => AdjustWindowZoom(-0.05);
        menu.Items.Add(miZoomOut);

        menu.Items.Add(new Separator());

        var miWaveIn = new MenuItem { Header = "🌊 Ampliar Ondas Horizontales (+15 px/s)" };
        miWaveIn.Click += (_, _) => AdjustTimelineZoom(+15);
        menu.Items.Add(miWaveIn);

        var miWaveOut = new MenuItem { Header = "📉 Reducir Ondas Horizontales (-15 px/s)" };
        miWaveOut.Click += (_, _) => AdjustTimelineZoom(-15);
        menu.Items.Add(miWaveOut);

        var miWaveReset = new MenuItem { Header = "↕️ Restaurar Ondas a 50 px/s" };
        miWaveReset.Click += (_, _) => { pixelsPerSecond = 50.0; RefreshCanvas(); };
        menu.Items.Add(miWaveReset);

        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void ApplyGuitarPracticeMode()
    {
        // Find guitar stem and mute it, unmute all backing stems, un-mute GP-200 live guitar
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].IsGuitarStem || tracks[i].RoleKey == "guitar")
            {
                tracks[i].IsMuted = true;
                audioEngine.SetMute(i, true);
            }
            else
            {
                tracks[i].IsMuted = false;
                audioEngine.SetMute(i, false);
            }
            tracks[i].IsSolo = false;
            audioEngine.SetSolo(i, false);
        }
        RenderTrackHeaders();
        UiDialogs.Show(this, "✓ Modo Práctica de Guitarra activado:\n\n• Guitarra original silenciada.\n• Acompañamiento activo.\n• Entrada de tu GP-200 armada para tocar encima.", "Práctica de Guitarra");
    }

    private void ApplyKaraokeMode()
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].RoleKey == "vocals")
            {
                tracks[i].IsMuted = true;
                audioEngine.SetMute(i, true);
            }
            else
            {
                tracks[i].IsMuted = false;
                audioEngine.SetMute(i, false);
            }
            tracks[i].IsSolo = false;
            audioEngine.SetSolo(i, false);
        }
        RenderTrackHeaders();
    }

    private void ApplyDrumsAndBassMode()
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            bool isRhythm = tracks[i].RoleKey is "drums" or "bass";
            tracks[i].IsMuted = !isRhythm;
            audioEngine.SetMute(i, !isRhythm);
            tracks[i].IsSolo = isRhythm;
            audioEngine.SetSolo(i, isRhythm);
        }
        RenderTrackHeaders();
        UiDialogs.Show(this, "✓ Modo Ritmo (Batería + Bajo) activado:\n\n• Batería y bajo aislados.\n• Guitarras, voces y teclados silenciados.\n• Listo para entrenar tiempo, groove y compás con tu GP-200.", "Ritmo Batería + Bajo");
    }

    private void ResetMix()
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            tracks[i].IsMuted = false;
            tracks[i].IsSolo = false;
            tracks[i].VolumeDb = 0f;
            tracks[i].Pan = 0f;
            audioEngine.SetMute(i, false);
            audioEngine.SetSolo(i, false);
            audioEngine.SetVolumeDb(i, 0f);
            audioEngine.SetPan(i, 0f);
        }
        RenderTrackHeaders();
    }

    private void ExportIndividualStems()
    {
        var stemsWithFiles = tracks.Where(t => !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath)).ToList();
        if (stemsWithFiles.Count == 0)
        {
            UiDialogs.Show(this, "No hay pistas de stems separadas cargadas para exportar. Separa una canción primero.", "Exportar Stems");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleccionar carpeta de destino para los Stems (.wav)"
        };
        if (dialog.ShowDialog(this) == true)
        {
            string destDir = dialog.FolderName;
            int count = 0;
            foreach (var t in stemsWithFiles)
            {
                if (string.IsNullOrEmpty(t.FilePath)) continue;
                string safeName = StemKitBridgeService.SanitizeFileName(t.Name);
                string destFile = IOPath.Combine(destDir, $"{safeName}.wav");
                File.Copy(t.FilePath, destFile, overwrite: true);
                count++;
            }
            UiDialogs.Show(this, $"✓ {count} stems exportados con éxito en formato WAV a:\n{destDir}", "Exportar Stems");
        }
    }

    private void SetOperationStatus(bool active, string? message = null)
    {
        stemProgressBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        stemProgressText.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        cancelOperationBtn.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        dropPromptText.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        if (message != null) stemProgressText.Text = message;
    }

    private void LoadSongFile(string filePath)
    {
        try
        {
            loadedSongPath = filePath;
            dropPromptText.Text = $"🎵 Canción cargada: {IOPath.GetFileName(filePath)} — Pulsa [✨ Separar Stems con StemKit] o [🚀 StemKit GUI]";
            
            // Load as preview into Track 0
            audioEngine.LoadStem(0, filePath);
            tracks[0].FilePath = filePath;
            tracks[0].Name = IOPath.GetFileNameWithoutExtension(filePath);
            tracks[0].StartOffsetSeconds = 0;
            tracks[0].SkipSeconds = 0;
            tracks[0].TakeSeconds = 0;
            CalculateTrackPeaks(tracks[0], filePath);

            totalDurationSeconds = audioEngine.Duration.TotalSeconds;
            selectedTrackIndex = 0;
            UpdateSelectedTrackUI();
            RefreshCanvas();
            RenderTrackHeaders();
        }
        catch (Exception ex)
        {
            UiDialogs.Show(this, $"No se pudo abrir el archivo de audio: {ex.Message}", "Error");
        }
    }

    private void CalculateTrackPeaks(JamTrack track, string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            track.TotalDurationSeconds = reader.TotalTime.TotalSeconds;
            int samplesToRead = 600;
            var peaks = new List<float>(samplesToRead);
            float[] buffer = new float[reader.WaveFormat.SampleRate / 2];

            while (peaks.Count < samplesToRead && reader.Read(buffer, 0, buffer.Length) > 0)
            {
                float max = buffer.Max(Math.Abs);
                peaks.Add(max);
            }
            track.WaveformPeaks = peaks.ToArray();
        }
        catch { }
    }

    private void OpenStemKitGuiWithAutoSync()
    {
        string? exe = StemKitBridgeService.GetInstalledStemKitPath() ?? StemKitBridgeService.AutoLocateStemKitOnComputer();
        if (exe == null || !File.Exists(exe))
        {
            var res = MessageBox.Show(
                this,
                "StemKit GUI no está instalado aún en tu equipo.\n\n" +
                "¿Deseas descargarlo automáticamente desde GitHub (danielravina/stemkit v0.2.0) en 1 clic?",
                "StemKit GUI Auto-Sync",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (res == MessageBoxResult.Yes)
            {
                _ = TriggerDownloadStemKitAsync();
            }
            return;
        }

        // Start watcher if not running
        stemKitWatcher ??= new StemKitBridgeService.StemKitWatcher(folder =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                IngestStemsFromFolder(folder, isAutoSync: true);
            });
        });

        // Launch StemKit GUI with environment variable pointing to our sync directory
        bool ok = StemKitBridgeService.LaunchStemKitGui(loadedSongPath);
        if (ok)
        {
            dropPromptText.Text = "👀 StemKit GUI en ejecución. Separa tu canción allí; al terminar, los stems se cargarán aquí automáticamente.";
            dropPromptText.Foreground = DarkUiTheme.SuccessBrush;
            UiDialogs.Show(
                this,
                "✓ StemKit GUI se ha abierto con sincronización automática activa.\n\n" +
                "• Separa cualquier canción en la ventana de StemKit.\n" +
                "• Cuando StemKit termine de generar los stems, este DAW los detectará y cargará automáticamente en tus pistas sin que tengas que copiar archivos.",
                "StemKit GUI Auto-Sync");
        }
    }

    private void IngestStemsFromFolder(string folder, bool isAutoSync = false)
    {
        var detected = StemKitBridgeService.MapStemsFromDirectory(folder);
        if (detected.Count == 0) return;

        stemKitWatcher?.MarkProcessed(folder);
        PushUndo("Importar stems");
        ApplyDynamicStemMapping(detected);
        totalDurationSeconds = audioEngine.Duration.TotalSeconds;
        RefreshCanvas();

        dropPromptText.Text = $"✓ {detected.Count} stems importados con éxito desde StemKit ({IOPath.GetFileName(folder)})";
        dropPromptText.Foreground = DarkUiTheme.SuccessBrush;
        Title = $"GP-200 Stem & Jam Studio — {detected.Count} Stems ({IOPath.GetFileName(folder)})";
    }

    private void ImportStemsFromFolderDialog()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleccionar carpeta que contiene stems de audio (.wav, .flac, .mp3)"
        };
        if (dialog.ShowDialog(this) == true)
        {
            IngestStemsFromFolder(dialog.FolderName, isAutoSync: false);
            UiDialogs.Show(this, $"✓ Stems importados desde:\n{dialog.FolderName}", "Importar Stems");
        }
    }

    private void OpenFileForTrack(int index)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Archivos de Audio (*.wav;*.flac;*.mp3;*.m4a;*.ogg)|*.wav;*.flac;*.mp3;*.m4a;*.ogg|Todos los archivos|*.*",
            Title = $"Cargar audio para Pista {index + 1}"
        };
        if (dialog.ShowDialog(this) == true)
        {
            LoadAudioIntoTrack(index, dialog.FileName);
        }
    }

    private void LoadAudioIntoTrack(int trackIndex, string filePath)
    {
        try
        {
            if (trackIndex < 0) return;
            PushUndo("Cargar archivo de audio");
            while (tracks.Count <= trackIndex)
            {
                AddNewEmptyTrack();
            }

            var track = tracks[trackIndex];
            track.FilePath = filePath;
            track.StartOffsetSeconds = 0;
            track.SkipSeconds = 0;
            track.TakeSeconds = 0;

            string fileName = IOPath.GetFileNameWithoutExtension(filePath);
            track.Name = fileName;

            string lower = fileName.ToLowerInvariant();
            if (lower.Contains("guitar") || lower.Contains("gtr"))
            {
                track.RoleKey = "guitar";
                track.Icon = "🎸";
                track.ThemeColor = Color.FromRgb(16, 185, 129);
                track.IsGuitarStem = true;
            }
            else if (lower.Contains("vocal") || lower.Contains("vox") || lower.Contains("voice"))
            {
                track.RoleKey = "vocals";
                track.Icon = "🎤";
                track.ThemeColor = Color.FromRgb(249, 115, 22);
            }
            else if (lower.Contains("bass") || lower.Contains("bajo"))
            {
                track.RoleKey = "bass";
                track.Icon = "🎸";
                track.ThemeColor = Color.FromRgb(234, 179, 8);
            }
            else if (lower.Contains("drum") || lower.Contains("percussion") || lower.Contains("beat"))
            {
                track.RoleKey = "drums";
                track.Icon = "🥁";
                track.ThemeColor = Color.FromRgb(168, 85, 247);
            }
            else if (lower.Contains("piano") || lower.Contains("keys"))
            {
                track.RoleKey = "piano";
                track.Icon = "🎹";
                track.ThemeColor = Color.FromRgb(245, 158, 11);
            }

            audioEngine.LoadStem(trackIndex, filePath);
            CalculateTrackPeaks(track, filePath);
            totalDurationSeconds = Math.Max(totalDurationSeconds, audioEngine.Duration.TotalSeconds);

            selectedTrackIndex = trackIndex;
            RenderTrackHeaders();
            RefreshCanvas();
            UpdateSelectedTrackUI();
        }
        catch (Exception ex)
        {
            UiDialogs.Show(this, $"Error al cargar audio en la pista: {ex.Message}", "Error de Carga");
        }
    }

    private void OpenRenameTrackDialog(JamTrack track)
    {
        var dialog = new Window
        {
            Title = $"Renombrar Pista — {track.Name}",
            Width = 360,
            Height = 220,
            Background = DarkUiTheme.AuraBgDeepBrush,
            Foreground = DarkUiTheme.AuraTextMainBrush,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Nombre de la pista:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        var nameBox = new TextBox { Text = track.Name, Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(nameBox);

        stack.Children.Add(new TextBlock { Text = "Icono / Rol:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        var iconCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 16) };
        iconCombo.Items.Add("🎸 Guitarra");
        iconCombo.Items.Add("🎤 Voces");
        iconCombo.Items.Add("🎹 Teclado / Piano");
        iconCombo.Items.Add("🎸 Bajo");
        iconCombo.Items.Add("🥁 Batería");
        iconCombo.Items.Add("🎼 Sintetizador / Otros");
        iconCombo.Items.Add("⚡ GP-200 En Vivo");
        iconCombo.SelectedIndex = track.RoleKey switch
        {
            "guitar" => 0,
            "vocals" => 1,
            "piano" => 2,
            "bass" => 3,
            "drums" => 4,
            "gp200" => 6,
            _ => 5
        };
        stack.Children.Add(iconCombo);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "Guardar", Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand };
        okBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(nameBox.Text))
            {
                track.Name = nameBox.Text.Trim();
                track.Icon = iconCombo.SelectedIndex switch
                {
                    0 => "🎸", 1 => "🎤", 2 => "🎹", 3 => "🎸", 4 => "🥁", 6 => "⚡", _ => "🎼"
                };
                track.RoleKey = iconCombo.SelectedIndex switch
                {
                    0 => "guitar", 1 => "vocals", 2 => "piano", 3 => "bass", 4 => "drums", 6 => "gp200", _ => "other"
                };
                track.IsGuitarStem = track.RoleKey == "guitar";
                RenderTrackHeaders();
                UpdateSelectedTrackUI();
            }
            dialog.Close();
        };
        btnRow.Children.Add(okBtn);
        stack.Children.Add(btnRow);

        dialog.Content = stack;
        dialog.ShowDialog();
    }

    private void OpenColorPicker(JamTrack track)
    {
        var dialog = new Window
        {
            Title = $"Color de Pista — {track.Name}",
            Width = 320,
            Height = 160,
            Background = DarkUiTheme.AuraBgDeepBrush,
            Foreground = DarkUiTheme.AuraTextMainBrush,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };
        var wrap = new WrapPanel { Margin = new Thickness(16) };
        Color[] palette = [
            Color.FromRgb(16, 185, 129),  // Emerald
            Color.FromRgb(0, 229, 255),   // Cyan
            Color.FromRgb(59, 130, 246),  // Blue
            Color.FromRgb(168, 85, 247),  // Purple
            Color.FromRgb(249, 115, 22),  // Orange
            Color.FromRgb(245, 158, 11),  // Amber
            Color.FromRgb(239, 68, 68),   // Red
            Color.FromRgb(234, 179, 8)    // Yellow
        ];
        foreach (var col in palette)
        {
            var btn = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(4),
                Background = new SolidColorBrush(col),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btn.Click += (_, _) =>
            {
                track.ThemeColor = col;
                RenderTrackHeaders();
                RefreshCanvas();
                dialog.Close();
            };
            wrap.Children.Add(btn);
        }
        dialog.Content = wrap;
        dialog.ShowDialog();
    }

    private void UndoBtn_Click(object? sender, RoutedEventArgs e) => Undo();
    private void RedoBtn_Click(object? sender, RoutedEventArgs e) => Redo();

    private void PushUndo(string description)
    {
        undoStack.Push(CaptureSnapshot(description));
        redoStack.Clear();
        UpdateUndoRedoVisuals();
    }

    private void Undo()
    {
        if (undoStack.Count == 0) return;
        var snap = undoStack.Pop();
        redoStack.Push(CaptureSnapshot(snap.Description));
        ApplySnapshot(snap);
        UpdateUndoRedoVisuals();
        dropPromptText.Text = $"↶ Deshecho: {snap.Description}";
        dropPromptText.Foreground = DarkUiTheme.AuraCyanPrimaryBrush;
    }

    private void Redo()
    {
        if (redoStack.Count == 0) return;
        var snap = redoStack.Pop();
        undoStack.Push(CaptureSnapshot(snap.Description));
        ApplySnapshot(snap);
        UpdateUndoRedoVisuals();
        dropPromptText.Text = $"↷ Rehecho: {snap.Description}";
        dropPromptText.Foreground = DarkUiTheme.AuraCyanPrimaryBrush;
    }

    private void UpdateUndoRedoVisuals()
    {
        undoBtn.IsEnabled = undoStack.Count > 0;
        undoBtn.Opacity = undoStack.Count > 0 ? 1.0 : 0.45;
        undoBtn.ToolTip = undoStack.Count > 0
            ? $"Deshacer: {undoStack.Peek().Description} (Ctrl+Z)"
            : "Deshacer (Ctrl+Z)";

        redoBtn.IsEnabled = redoStack.Count > 0;
        redoBtn.Opacity = redoStack.Count > 0 ? 1.0 : 0.45;
        redoBtn.ToolTip = redoStack.Count > 0
            ? $"Rehacer: {redoStack.Peek().Description} (Ctrl+Y / Ctrl+Shift+Z)"
            : "Rehacer (Ctrl+Y / Ctrl+Shift+Z)";
    }

    private JamStudioSnapshot CaptureSnapshot(string description)
    {
        var snap = new JamStudioSnapshot
        {
            Description = description,
            LoopStartSeconds = loopStartSeconds,
            LoopEndSeconds = loopEndSeconds,
            IsLooping = isLooping,
            ActiveTrackIndex = selectedTrackIndex
        };

        foreach (var t in tracks)
        {
            var ts = new JamStudioSnapshot.TrackSnapshot
            {
                Index = t.Index,
                RoleKey = t.RoleKey,
                Name = t.Name,
                Icon = t.Icon,
                ThemeColor = t.ThemeColor,
                IsMuted = t.IsMuted,
                IsSolo = t.IsSolo,
                IsArmed = t.IsArmed,
                IsMonitoring = t.IsMonitoring,
                VolumeDb = t.VolumeDb,
                Pan = t.Pan
            };
            foreach (var c in t.Clips)
            {
                ts.Clips.Add(new JamStudioSnapshot.ClipSnapshot
                {
                    Id = c.Id,
                    Name = c.Name,
                    FilePath = c.FilePath,
                    StartOffsetSeconds = c.StartOffsetSeconds,
                    SkipSeconds = c.SkipSeconds,
                    TakeSeconds = c.TakeSeconds,
                    TotalDurationSeconds = c.TotalDurationSeconds,
                    IsMuted = c.IsMuted,
                    Color = c.Color,
                    WaveformPeaks = c.WaveformPeaks != null ? (float[])c.WaveformPeaks.Clone() : null
                });
            }
            snap.Tracks.Add(ts);
        }

        foreach (var tr in automationTriggers)
        {
            snap.Triggers.Add(new JamStudioSnapshot.TriggerSnapshot
            {
                TimeSeconds = tr.TimeSeconds,
                Label = tr.Label,
                TargetScene = tr.TargetScene,
                TargetSlot = tr.TargetSlot
            });
        }

        return snap;
    }

    private void ApplySnapshot(JamStudioSnapshot snap)
    {
        loopStartSeconds = snap.LoopStartSeconds;
        loopEndSeconds = snap.LoopEndSeconds;
        isLooping = snap.IsLooping;
        loopToggle.IsChecked = isLooping;
        loopToggle.Content = isLooping ? "🔁 Loop: ON" : "🔁 Loop: OFF";
        selectedTrackIndex = Math.Clamp(snap.ActiveTrackIndex, 0, Math.Max(0, snap.Tracks.Count - 1));

        tracks.Clear();
        for (int i = 0; i < snap.Tracks.Count; i++)
        {
            var ts = snap.Tracks[i];
            var t = new JamTrack
            {
                Index = ts.Index,
                RoleKey = ts.RoleKey,
                Name = ts.Name,
                Icon = ts.Icon,
                ThemeColor = ts.ThemeColor,
                IsMuted = ts.IsMuted,
                IsSolo = ts.IsSolo,
                IsArmed = ts.IsArmed,
                IsMonitoring = ts.IsMonitoring,
                VolumeDb = ts.VolumeDb,
                Pan = ts.Pan,
                IsGuitarStem = ts.RoleKey is "guitar" or "gp200"
            };
            foreach (var cs in ts.Clips)
            {
                t.Clips.Add(new JamClip
                {
                    Id = cs.Id,
                    Name = cs.Name,
                    FilePath = cs.FilePath,
                    StartOffsetSeconds = cs.StartOffsetSeconds,
                    SkipSeconds = cs.SkipSeconds,
                    TakeSeconds = cs.TakeSeconds,
                    TotalDurationSeconds = cs.TotalDurationSeconds,
                    IsMuted = cs.IsMuted,
                    Color = cs.Color,
                    WaveformPeaks = cs.WaveformPeaks
                });
            }
            tracks.Add(t);
        }

        automationTriggers.Clear();
        foreach (var tr in snap.Triggers)
        {
            automationTriggers.Add(new TimelinePresetTrigger
            {
                TimeSeconds = tr.TimeSeconds,
                Label = tr.Label,
                TargetScene = tr.TargetScene,
                TargetSlot = tr.TargetSlot
            });
        }

        // Resync multi-track audio engine
        while (audioEngine.ChannelCount > tracks.Count)
        {
            audioEngine.RemoveTrack(audioEngine.ChannelCount - 1);
        }
        audioEngine.EnsureChannelCount(tracks.Count);
        for (int i = 0; i < tracks.Count; i++)
        {
            SyncTrackClipsToEngine(i);
            audioEngine.SetMute(i, tracks[i].IsMuted);
            audioEngine.SetSolo(i, tracks[i].IsSolo);
            audioEngine.SetVolumeDb(i, tracks[i].VolumeDb);
            audioEngine.SetPan(i, tracks[i].Pan);
        }

        double maxDur = 0;
        foreach (var tr in tracks)
        {
            foreach (var cl in tr.Clips)
            {
                if (cl.EndSeconds > maxDur) maxDur = cl.EndSeconds;
            }
        }
        totalDurationSeconds = Math.Max(maxDur, audioEngine.Duration.TotalSeconds);

        audioEngine.LoopEnabled = isLooping;
        audioEngine.LoopStart = TimeSpan.FromSeconds(loopStartSeconds);
        audioEngine.LoopEnd = TimeSpan.FromSeconds(loopEndSeconds > loopStartSeconds ? loopEndSeconds : Math.Max(totalDurationSeconds, 1.0));

        // Refresh UI
        RenderTrackHeaders();
        RefreshCanvas();
        DrawAutomationMarkers();
        UpdateSelectedTrackUI();
    }

    private void SplitActiveTrackAtPlayhead()
    {
        if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;
        SliceTrackAtTime(selectedTrackIndex, currentPositionSeconds);
    }

    private void TrimActiveTrackToLoop()
    {
        if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;
        var activeTrack = tracks[selectedTrackIndex];
        if (!activeTrack.HasAudio || string.IsNullOrEmpty(activeTrack.FilePath)) return;

        double lStart = loopStartSeconds;
        double lEnd = loopEndSeconds > loopStartSeconds ? loopEndSeconds : totalDurationSeconds;
        if (lEnd <= lStart + 0.5)
        {
            UiDialogs.Show(this, "Activa o define primero un rango de Loop válido para recortar el clip.", "Recortar Audio");
            return;
        }

        PushUndo("Recortar a Loop");
        double skip = Math.Max(0, lStart - activeTrack.StartOffsetSeconds);
        double take = lEnd - lStart;
        activeTrack.StartOffsetSeconds = lStart;
        activeTrack.SkipSeconds = skip;
        activeTrack.TakeSeconds = take;

        audioEngine.LoadStem(activeTrack.Index, activeTrack.FilePath, lStart, skip, take);
        RefreshCanvas();
    }

    private void NudgeActiveTrack(double deltaSeconds)
    {
        if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;
        var activeTrack = tracks[selectedTrackIndex];
        if (!activeTrack.HasAudio) return;

        PushUndo("Mover pista");
        activeTrack.StartOffsetSeconds = Math.Max(0, activeTrack.StartOffsetSeconds + deltaSeconds);
        audioEngine.SetTrackOffset(activeTrack.Index, activeTrack.StartOffsetSeconds);
        totalDurationSeconds = Math.Max(totalDurationSeconds, audioEngine.Duration.TotalSeconds);
        RefreshCanvas();
    }

    private void ClearActiveTrack()
    {
        if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;
        var activeTrack = tracks[selectedTrackIndex];
        if (!activeTrack.HasAudio) return;

        PushUndo("Vaciar clip");
        activeTrack.FilePath = null;
        activeTrack.WaveformPeaks = null;
        activeTrack.StartOffsetSeconds = 0;
        activeTrack.SkipSeconds = 0;
        activeTrack.TakeSeconds = 0;
        audioEngine.ClearTrack(activeTrack.Index);
        RenderTrackHeaders();
        RefreshCanvas();
    }

    private void AddNewEmptyTrack(string? name = null)
    {
        PushUndo("Añadir nueva pista");
        int newIdx = tracks.Count;
        Color[] trackPalette = [
            Color.FromRgb(59, 130, 246),  // Blue
            Color.FromRgb(16, 185, 129),  // Emerald
            Color.FromRgb(245, 158, 11),  // Amber
            Color.FromRgb(249, 115, 22),  // Orange
            Color.FromRgb(168, 85, 247),  // Purple
            Color.FromRgb(239, 68, 68),   // Red
            Color.FromRgb(234, 179, 8),   // Yellow
            Color.FromRgb(20, 184, 166)   // Teal
        ];

        var track = new JamTrack
        {
            Index = newIdx,
            Name = name ?? $"Track {newIdx + 1}",
            Icon = "🎵",
            ThemeColor = trackPalette[newIdx % trackPalette.Length]
        };
        tracks.Add(track);
        audioEngine.EnsureChannelCount(tracks.Count);
        selectedTrackIndex = newIdx;
        RenderTrackHeaders();
        RefreshCanvas();
        UpdateSelectedTrackUI();
    }

    private void DeleteActiveTrack()
    {
        if (tracks.Count <= 1)
        {
            UiDialogs.Show(this, "Debe quedar al menos una pista en el proyecto.", "Eliminar Pista");
            return;
        }
        if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;

        PushUndo("Eliminar pista");
        int idxToRemove = selectedTrackIndex;
        audioEngine.RemoveTrack(idxToRemove);
        tracks.RemoveAt(idxToRemove);

        // Reindex remaining tracks
        for (int i = 0; i < tracks.Count; i++)
        {
            tracks[i].Index = i;
        }

        selectedTrackIndex = Math.Clamp(idxToRemove, 0, tracks.Count - 1);
        RenderTrackHeaders();
        RefreshCanvas();
        UpdateSelectedTrackUI();
    }

    private void DuplicateTrack(int index)
    {
        if (index < 0 || index >= tracks.Count) return;
        PushUndo("Duplicar pista");
        var src = tracks[index];
        int newIdx = tracks.Count;

        var dup = new JamTrack
        {
            Index = newIdx,
            Name = $"{src.Name} (Copia)",
            Icon = src.Icon,
            RoleKey = src.RoleKey,
            ThemeColor = src.ThemeColor,
            VolumeDb = src.VolumeDb,
            Pan = src.Pan
        };
        foreach (var c in src.Clips)
        {
            var dupClip = new JamClip
            {
                Name = $"{c.Name} (Copia)",
                FilePath = c.FilePath,
                Color = dup.ThemeColor,
                StartOffsetSeconds = c.StartOffsetSeconds,
                SkipSeconds = c.SkipSeconds,
                TakeSeconds = c.TakeSeconds,
                TotalDurationSeconds = c.TotalDurationSeconds,
                WaveformPeaks = c.WaveformPeaks
            };
            dup.Clips.Add(dupClip);
        }
        tracks.Add(dup);

        audioEngine.EnsureChannelCount(tracks.Count);
        SyncTrackClipsToEngine(newIdx);

        selectedTrackIndex = newIdx;
        selectedTrackIndices.Clear();
        selectedTrackIndices.Add(newIdx);
        RenderTrackHeaders();
        RefreshCanvas();
        UpdateSelectedTrackUI();
    }

    private void TogglePlayPause()
    {
        if (audioEngine.IsPlaying)
        {
            audioEngine.Pause();
            playbackTimer.Stop();
            playPauseBtn.Content = "▶ Play";
        }
        else
        {
            audioEngine.Play();
            playbackTimer.Start();
            playPauseBtn.Content = "⏸ Pausa";
        }
    }

    private void SeekToSeconds(double seconds)
    {
        currentPositionSeconds = Math.Clamp(seconds, 0, totalDurationSeconds);
        audioEngine.Position = TimeSpan.FromSeconds(currentPositionSeconds);
        UpdatePlayheadPosition();
    }

    private void StemKitQuickImportBtn_Click(object? s, RoutedEventArgs e) => ShowStemKitImportMenu();

    private void InitializeInputMonitoring()
    {
        inputCaptureEngine.RefreshDevices();
        int bestIdx = inputCaptureEngine.SelectedIndex;
        if (bestIdx < 0 && inputCaptureEngine.Devices.Count > 0) bestIdx = 0;
        if (bestIdx >= 0)
        {
            SwitchInputDevice(bestIdx, silent: true);
        }

        inputDeviceBtn.Click -= InputDeviceBtn_Click;
        inputDeviceBtn.Click += InputDeviceBtn_Click;

        autoDetectInputBtn.Click -= AutoDetectInputBtn_Click;
        autoDetectInputBtn.Click += AutoDetectInputBtn_Click;

        liveMonitorBtn.Click -= LiveMonitorBtn_Click;
        liveMonitorBtn.Click += LiveMonitorBtn_Click;

        inputCaptureEngine.SamplesAvailable += (samples, sampleRate, peakDbfs, rmsDbfs) =>
        {
            // 1. Live guitar audio monitoring: routes guitar directly to laptop speakers/headphones in real time!
            audioEngine.FeedLiveMonitoring(samples, sampleRate);

            // 2. Real-Time Meter Update on UI Thread
            Dispatcher.InvokeAsync(() =>
            {
                bool hasSignal = peakDbfs > -55.0;
                if (peakDbfs > -0.5)
                {
                    inputSignalLed.Fill = Brushes.Red;
                    inputSignalText.Foreground = Brushes.Red;
                    inputSignalText.Text = $"{peakDbfs:F1} dB";
                }
                else if (peakDbfs > -6.0)
                {
                    inputSignalLed.Fill = Brushes.Gold;
                    inputSignalText.Foreground = Brushes.Gold;
                    inputSignalText.Text = $"{peakDbfs:F1} dB";
                }
                else if (hasSignal)
                {
                    inputSignalLed.Fill = DarkUiTheme.SuccessBrush; // Bright green indicates real active guitar/audio signal!
                    inputSignalText.Foreground = DarkUiTheme.SuccessBrush;
                    inputSignalText.Text = $"{peakDbfs:F1} dB";
                }
                else
                {
                    inputSignalLed.Fill = new SolidColorBrush(Color.FromRgb(74, 85, 104));
                    inputSignalText.Foreground = DarkUiTheme.AuraTextMutedBrush;
                    inputSignalText.Text = "Silencio";
                }
            }, DispatcherPriority.Background);

            // 3. Stream PCM to WAV during active guitar recording
            if (isRecordingGuitarActive && guitarWaveWriter != null)
            {
                byte[] pcmBytes = new byte[samples.Length * 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    short pcm = (short)Math.Clamp((int)(samples[i] * 32767f), short.MinValue, short.MaxValue);
                    pcmBytes[i * 2] = (byte)(pcm & 0xFF);
                    pcmBytes[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
                }
                try
                {
                    guitarWaveWriter.Write(pcmBytes, 0, pcmBytes.Length);
                }
                catch { }
            }
        };
    }

    private void LiveMonitorBtn_Click(object? sender, RoutedEventArgs e)
    {
        audioEngine.IsLiveMonitoringEnabled = !audioEngine.IsLiveMonitoringEnabled;
        bool isMon = audioEngine.IsLiveMonitoringEnabled;
        liveMonitorBtn.Content = isMon ? "🎧 Mon: ON" : "🎧 Mon: OFF";
        liveMonitorBtn.Background = isMon ? DarkUiTheme.SuccessBrush : DarkUiTheme.AuraBgCardBrush;
        liveMonitorBtn.Foreground = isMon ? Brushes.White : DarkUiTheme.AuraTextMutedBrush;
        dropPromptText.Text = isMon
            ? "🎧 Monitoreo activo: Escuchando guitarra y pedal por los altavoces de la laptop/auriculares."
            : "🔇 Monitoreo silenciado: La guitarra solo sonará al reproducir grabaciones.";
        dropPromptText.Foreground = isMon ? DarkUiTheme.SuccessBrush : DarkUiTheme.AuraTextMutedBrush;
    }

    private async void AutoDetectInputBtn_Click(object? sender, RoutedEventArgs e)
    {
        autoDetectInputBtn.IsEnabled = false;
        autoDetectInputBtn.Content = "🔍 ...";
        dropPromptText.Text = "🔍 Escaneando todas las entradas de audio de la laptop... Toca tu guitarra.";
        dropPromptText.Foreground = DarkUiTheme.AuraCyanPrimaryBrush;

        var progress = new Progress<string>(msg =>
        {
            dropPromptText.Text = msg;
        });

        int activeIdx = await inputCaptureEngine.AutoDetectActiveDeviceAsync(progress);
        autoDetectInputBtn.IsEnabled = true;
        autoDetectInputBtn.Content = "🔍 Auto";

        if (activeIdx >= 0 && activeIdx < inputCaptureEngine.Devices.Count)
        {
            SwitchInputDevice(activeIdx, silent: false);
            dropPromptText.Text = $"✓ ¡Señal detectada! Conectado a: {inputCaptureEngine.Devices[activeIdx].Name}";
            dropPromptText.Foreground = DarkUiTheme.SuccessBrush;
        }
        else
        {
            dropPromptText.Text = "⚠️ No se detectó sonido activo en ninguna entrada. Conecta el pedal o toca la guitarra.";
            dropPromptText.Foreground = DarkUiTheme.WarningBrush;
        }
    }

    private void InputDeviceBtn_Click(object? sender, RoutedEventArgs e) => ShowInputDeviceMenu();

    private void ShowInputDeviceMenu()
    {
        var menu = new ContextMenu();
        inputCaptureEngine.RefreshDevices();

        var header = new MenuItem { Header = "ENTRADAS DE AUDIO DETECTADAS EN TU LAPTOP:", IsEnabled = false, FontWeight = FontWeights.Bold };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        for (int i = 0; i < inputCaptureEngine.Devices.Count; i++)
        {
            int idx = i;
            var dev = inputCaptureEngine.Devices[i];
            bool isSelected = idx == inputCaptureEngine.SelectedIndex;
            var item = new MenuItem
            {
                Header = (isSelected ? "✓  " : "    ") + dev.Name,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (_, _) => SwitchInputDevice(idx, silent: false);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var autoItem = new MenuItem
        {
            Header = "🔍 Auto-Detectar Señal Activa (Escanear qué entrada tiene sonido)",
            FontWeight = FontWeights.Bold
        };
        autoItem.Click += AutoDetectInputBtn_Click;
        menu.Items.Add(autoItem);

        var monItem = new MenuItem
        {
            Header = audioEngine.IsLiveMonitoringEnabled ? "✓ 🎧 Monitoreo en Vivo (Activo)" : "    🎧 Monitoreo en Vivo (Silenciado)",
            FontWeight = FontWeights.SemiBold
        };
        monItem.Click += LiveMonitorBtn_Click;
        menu.Items.Add(monItem);

        menu.PlacementTarget = inputDeviceBtn;
        menu.IsOpen = true;
    }

    private void SwitchInputDevice(int deviceIndex, bool silent = false)
    {
        bool started = inputCaptureEngine.Start(deviceIndex);
        if (deviceIndex >= 0 && deviceIndex < inputCaptureEngine.Devices.Count)
        {
            var dev = inputCaptureEngine.Devices[deviceIndex];
            string shortLabel;
            if (dev.Name.Contains("GP-200", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("Valeton", StringComparison.OrdinalIgnoreCase))
            {
                shortLabel = "🎸 GP-200 (USB)";
            }
            else if (dev.IsLoopback || dev.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
            {
                shortLabel = "🔊 Audio PC";
            }
            else if (dev.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("Mic", StringComparison.OrdinalIgnoreCase))
            {
                shortLabel = "💻 Mic Laptop";
            }
            else if (dev.DriverType == "ASIO")
            {
                shortLabel = "⚡ ASIO";
            }
            else
            {
                shortLabel = "🎤 Audio IN";
            }

            inputDeviceBtn.Content = $"{shortLabel} ▾";
            inputDeviceBtn.ToolTip = $"Entrada activa: {dev.Name}\nHaz clic para cambiar de dispositivo.";

            if (!silent)
            {
                dropPromptText.Text = started 
                    ? $"✓ Entrada conectada: {dev.Name}" 
                    : $"⚠️ No se pudo conectar a la entrada: {dev.Name}";
                dropPromptText.Foreground = started ? DarkUiTheme.SuccessBrush : DarkUiTheme.WarningBrush;
            }
        }
    }

    private void ToggleGuitarRecording()
    {
        isRecording = !isRecording;
        if (isRecording)
        {
            // Find armed track or active track
            var targetTrack = tracks.FirstOrDefault(t => t.IsArmed)
                              ?? (selectedTrackIndex >= 0 && selectedTrackIndex < tracks.Count ? tracks[selectedTrackIndex] : tracks.LastOrDefault());

            int targetTrackIdx = targetTrack != null ? targetTrack.Index : (tracks.Count - 1);
            recordingStartOffsetSeconds = currentPositionSeconds;

            string dir = StemKitBridgeService.JamProjectsDir;
            try { Directory.CreateDirectory(dir); } catch { }
            string recPath = IOPath.Combine(dir, $"GP200_Take_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            recordingFilePath = recPath;

            // Ensure AudioCaptureEngine is capturing from GP-200
            if (!inputCaptureEngine.IsCapturing)
            {
                int bestIdx = inputCaptureEngine.SelectedIndex >= 0 ? inputCaptureEngine.SelectedIndex : 0;
                inputCaptureEngine.Start(bestIdx);
            }

            try
            {
                int sRate = inputCaptureEngine.SampleRate > 0 ? inputCaptureEngine.SampleRate : 48000;
                guitarWaveWriter = new WaveFileWriter(recPath, new WaveFormat(sRate, 16, 1));
                isRecordingGuitarActive = true;
            }
            catch (Exception ex)
            {
                UiDialogs.Show(this, "Error al iniciar grabación de audio: " + ex.Message, "Error Grabación");
                isRecording = false;
                return;
            }

            recGuitarBtn.Content = "⏹ Parar REC";
            recGuitarBtn.Background = DarkUiTheme.DangerBrush;
            dropPromptText.Text = $"🔴 Grabando guitarra GP-200 en pista '{targetTrack?.Name}' desde {TimeSpan.FromSeconds(recordingStartOffsetSeconds):mm\\:ss\\.ff}... Toca tu guitarra.";
            dropPromptText.Foreground = DarkUiTheme.DangerBrush;

            if (!audioEngine.IsPlaying) TogglePlayPause();
        }
        else
        {
            isRecordingGuitarActive = false;
            try
            {
                guitarWaveWriter?.Flush();
                guitarWaveWriter?.Dispose();
                guitarWaveWriter = null;
            }
            catch { }

            recGuitarBtn.Content = "🔴 REC Guitarra";
            recGuitarBtn.Background = DarkUiTheme.AuraBgCardBrush;

            if (!string.IsNullOrEmpty(recordingFilePath) && File.Exists(recordingFilePath) && new FileInfo(recordingFilePath).Length > 44)
            {
                var targetTrack = tracks.FirstOrDefault(t => t.IsArmed)
                                  ?? (selectedTrackIndex >= 0 && selectedTrackIndex < tracks.Count ? tracks[selectedTrackIndex] : tracks.LastOrDefault());

                if (targetTrack != null)
                {
                    double recDuration = Math.Max(0.5, currentPositionSeconds - recordingStartOffsetSeconds);
                    var newClip = new JamClip
                    {
                        Name = $"Take GP-200 ({DateTime.Now:HH:mm:ss})",
                        FilePath = recordingFilePath,
                        Color = targetTrack.ThemeColor,
                        StartOffsetSeconds = recordingStartOffsetSeconds,
                        TakeSeconds = recDuration,
                        TotalDurationSeconds = recDuration
                    };
                    CalculateClipPeaks(newClip, recordingFilePath);
                    PushUndo("Grabar toma GP-200");
                    targetTrack.Clips.Add(newClip);
                    SyncTrackClipsToEngine(targetTrack.Index);

                    dropPromptText.Text = $"✓ Grabación guardada desde GP-200: {newClip.Name} ({recDuration:F1}s) en {targetTrack.Name}.";
                    dropPromptText.Foreground = DarkUiTheme.SuccessBrush;
                }
            }

            RenderTrackHeaders();
            RefreshCanvas();
            UpdateSelectedTrackUI();
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        currentPositionSeconds = audioEngine.Position.TotalSeconds;
        UpdatePlayheadPosition();

        // FL Studio Timecode (Bar : Beat : Step and Min : Sec . Tenths)
        double secondsPerBeat = 60.0 / Math.Max(20, currentBpm);
        double beats = currentPositionSeconds / secondsPerBeat;
        int bar = (int)(beats / 4) + 1;
        int beat = (int)(beats % 4) + 1;
        int step = (int)((beats % 1) * 4) + 1;
        flTimecodeBarBeat.Text = $"{bar:D2}:{beat:D2}:{step:D2}";

        var cur = TimeSpan.FromSeconds(currentPositionSeconds);
        var tot = TimeSpan.FromSeconds(totalDurationSeconds);
        flTimecodeMinSec.Text = $"{cur:mm\\:ss\\.f}";
        digitalClock.Text = $"{cur:mm\\:ss\\.f} / {tot:mm\\:ss\\.f}";

        // Real-time Dancing Stereo Peak VU Meters
        float leftPeak = audioEngine.PeakLeft;
        float rightPeak = audioEngine.PeakRight;
        vuMeterLeft.Value = Math.Clamp(leftPeak * 100f, 0, 100);
        vuMeterRight.Value = Math.Clamp(rightPeak * 100f, 0, 100);

        vuMeterLeft.Foreground = leftPeak > 0.85f ? DarkUiTheme.DangerBrush : (leftPeak > 0.60f ? DarkUiTheme.WarningBrush : DarkUiTheme.SuccessBrush);
        vuMeterRight.Foreground = rightPeak > 0.85f ? DarkUiTheme.DangerBrush : (rightPeak > 0.60f ? DarkUiTheme.WarningBrush : DarkUiTheme.SuccessBrush);

        CheckAutomationTriggers(currentPositionSeconds);
        audioEngine.CheckLoop();
    }

    private void CheckAndAutoIngestNewStems()
    {
        try
        {
            var songs = StemKitBridgeService.GetAvailableStemKitSongs();
            if (songs.Count > 0)
            {
                stemKitQuickImportBtn.Content = $"📥 Importar de StemKit ({songs.Count}) ▾";
                // Do NOT auto-load music on startup without user consent!
                // The tracks remain clean and waiting for user input.
            }
            else
            {
                stemKitQuickImportBtn.Content = "📥 Importar de StemKit ▾";
            }
        }
        catch { }
    }

    private void ShowStemKitImportMenu()
    {
        var menu = new ContextMenu();
        var songs = StemKitBridgeService.GetAvailableStemKitSongs();

        if (songs.Count == 0)
        {
            var noSong = new MenuItem { Header = "○ No hay canciones separadas detectadas aún en StemKit", IsEnabled = false };
            menu.Items.Add(noSong);

            var openGui = new MenuItem { Header = "🚀 Abrir StemKit GUI para separar una canción...", FontWeight = FontWeights.Bold };
            openGui.Click += (_, _) => OpenStemKitGuiWithAutoSync();
            menu.Items.Add(openGui);
        }
        else
        {
            var header = new MenuItem { Header = "CANCIONES DETECTADAS EN STEMKIT (Haz clic para cargar en pistas):", IsEnabled = false, FontWeight = FontWeights.Bold };
            menu.Items.Add(header);
            menu.Items.Add(new Separator());

            for (int i = 0; i < songs.Count; i++)
            {
                var s = songs[i];
                string badge = i == 0 ? "⭐ [MÁS RECIENTE] " : "";
                var songItem = new MenuItem
                {
                    Header = $"{badge}{s.Title} ({s.StemCount} stems)",
                    FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal
                };
                songItem.Click += (_, _) =>
                {
                    IngestStemsFromFolder(s.DirectoryPath, isAutoSync: false);
                    UiDialogs.Show(this, $"✓ Stems de '{s.Title}' cargados con éxito en las pistas.", "StemKit");
                };
                menu.Items.Add(songItem);
            }

            menu.Items.Add(new Separator());
            var openGui = new MenuItem { Header = "🚀 Abrir StemKit GUI para separar otra canción...", FontWeight = FontWeights.SemiBold };
            openGui.Click += (_, _) => OpenStemKitGuiWithAutoSync();
            menu.Items.Add(openGui);
        }

        menu.PlacementTarget = stemKitQuickImportBtn;
        menu.IsOpen = true;
    }

    private void UpdateSystemTelemetry()
    {
        try
        {
            long memMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            int cpuEst = Math.Min(25, Math.Max(2, (int)(audioEngine.PeakLeft * 20)));
            flTelemetryText.Text = $"CPU {cpuEst}% | {memMb} MB";
        }
        catch { }
    }

    private void UpdatePlayheadPosition()
    {
        double x = currentPositionSeconds * pixelsPerSecond;
        Canvas.SetLeft(playheadLine, x);
        Canvas.SetLeft(playheadTop, x);

        if (x > timelineScroll.HorizontalOffset + timelineScroll.ViewportWidth - 50)
        {
            timelineScroll.ScrollToHorizontalOffset(x - 50);
        }
    }

    private async void CheckAutomationTriggers(double currentTime)
    {
        foreach (var trigger in automationTriggers)
        {
            if (!trigger.Triggered && Math.Abs(currentTime - trigger.TimeSeconds) <= 0.15)
            {
                trigger.Triggered = true;
                if (trigger.TargetScene >= 0)
                {
                    await editor.SendSceneChangeSysExAsync(trigger.TargetScene);
                }
                else if (trigger.TargetSlot >= 0)
                {
                    await editor.SendSlotChangeSysExAsync(trigger.TargetSlot);
                }
            }
            else if (currentTime < trigger.TimeSeconds - 0.5)
            {
                trigger.Triggered = false;
            }
        }
    }

    private void OpenAddTriggerDialog(double timestampSeconds)
    {
        var dialog = new Window
        {
            Title = "Añadir Disparador GP-200",
            Width = 360,
            Height = 240,
            Background = DarkUiTheme.AuraBgDeepBrush,
            Foreground = DarkUiTheme.AuraTextMainBrush,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };
        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(new TextBlock { Text = $"Tiempo: {TimeSpan.FromSeconds(timestampSeconds):mm\\:ss\\.ff}", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 16) };
        combo.Items.Add("Cambiar a Scene A (Clean)");
        combo.Items.Add("Cambiar a Scene B (Crunch)");
        combo.Items.Add("Cambiar a Scene C (Lead)");
        combo.Items.Add("Cambiar a Scene D (Ambient)");
        combo.SelectedIndex = 1;
        stack.Children.Add(combo);

        var addBtn = new Button { Content = "Guardar Disparador", Padding = new Thickness(10, 6, 10, 6), Cursor = Cursors.Hand };
        addBtn.Click += (_, _) =>
        {
            PushUndo("Añadir disparador de escena");
            automationTriggers.Add(new TimelinePresetTrigger
            {
                TimeSeconds = timestampSeconds,
                TargetScene = combo.SelectedIndex,
                Label = combo.SelectedItem.ToString()!
            });
            DrawAutomationMarkers();
            dialog.Close();
        };
        stack.Children.Add(addBtn);

        dialog.Content = stack;
        dialog.ShowDialog();
    }

    private void OpenAddTrackDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Archivos de Audio (*.wav;*.flac;*.mp3;*.m4a;*.ogg)|*.wav;*.flac;*.mp3;*.m4a;*.ogg|Todos los archivos|*.*",
            Title = "Añadir Pista o Stem Individual"
        };
        if (dialog.ShowDialog(this) == true)
        {
            int freeIdx = tracks.FindIndex(t => !t.HasAudio && t.RoleKey != "gp200");
            if (freeIdx >= 0)
            {
                LoadAudioIntoTrack(freeIdx, dialog.FileName);
            }
            else
            {
                int newIdx = tracks.Count;
                AddNewEmptyTrack(IOPath.GetFileNameWithoutExtension(dialog.FileName));
                LoadAudioIntoTrack(newIdx, dialog.FileName);
            }
        }
    }

    private void AdjustWindowZoom(double delta)
    {
        currentWindowZoom = Math.Clamp(currentWindowZoom + delta, 0.70, 1.50);
        scalableRootGrid.LayoutTransform = new ScaleTransform(currentWindowZoom, currentWindowZoom);
        zoomDisplay.Text = $"{Math.Round(currentWindowZoom * 100)}%";
    }

    private void AdjustTimelineZoom(double delta)
    {
        pixelsPerSecond = Math.Clamp(pixelsPerSecond + delta, 20.0, 200.0);
        RefreshCanvas();
    }

    private void RefreshCanvas()
    {
        timelineCanvas.Children.Clear();
        timelineCanvas.Width = Math.Max(300, totalDurationSeconds) * pixelsPerSecond;
        DrawTrackWaveforms();
        timelineCanvas.Children.Add(playheadLine);
        timelineCanvas.Children.Add(playheadTop);
        DrawAutomationMarkers();
        UpdatePlayheadPosition();
    }
}
