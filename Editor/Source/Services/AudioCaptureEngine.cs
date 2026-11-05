using System.Diagnostics;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Gp200.Replica;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsLoopback, MMDevice? Device, string DriverType = "WASAPI");

/// <summary>
/// Universal low-latency audio capture engine for Windows 10/11.
/// Supports Valeton GP-200 ASIO Driver, low-latency WASAPI (5-10 ms),
/// system loopback, and automatic WinMM fallback.
/// </summary>
public sealed class AudioCaptureEngine : IDisposable
{
    private readonly List<AudioDeviceInfo> _devices = [];
    private MMDeviceEnumerator? _enumerator;
    private IWaveIn? _capture;
    private AsioOut? _asioCapture;
    private AudioSampleReader? _sampleReader;
    private bool _capturing;
    private bool _disposed;

    public IReadOnlyList<AudioDeviceInfo> Devices => _devices;
    public bool IsCapturing => _capturing;
    public int SelectedIndex { get; private set; } = -1;
    public long PacketsReceived { get; private set; }
    public long SamplesReceived { get; private set; }
    public double PeakDbfs { get; private set; } = -96.0;
    public double RmsDbfs { get; private set; } = -96.0;
    public int SampleRate { get; private set; } = 48000;
    public int Channels { get; private set; } = 2;

    public event Action<float[], int, double, double>? SamplesAvailable;
    public event Action<string>? StatusChanged;

    public AudioCaptureEngine()
    {
        RefreshDevices();
    }

    public void RefreshDevices()
    {
        _devices.Clear();
        _enumerator?.Dispose();
        _enumerator = new MMDeviceEnumerator();

        // 1. Detect ASIO drivers from Windows Registry (Priority for Valeton GP-200 ASIO)
        try
        {
            var asioNames = GetRegisteredAsioDrivers();
            foreach (var asioName in asioNames)
            {
                bool isValeton = asioName.Contains("Valeton", StringComparison.OrdinalIgnoreCase) ||
                                 asioName.Contains("GP-200", StringComparison.OrdinalIgnoreCase);
                string label = isValeton ? $"⚡ [ASIO < 3ms] {asioName}" : $"[ASIO] {asioName}";
                _devices.Add(new AudioDeviceInfo($"ASIO_{asioName}", label, false, null, "ASIO"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ASIO registry check: {ex.Message}");
        }

        // 2. Loopback PC Audio option
        _devices.Add(new AudioDeviceInfo("LOOPBACK", UiText.Language == "es" ? "🔊 [Loopback] Audio del Sistema (PC)" : "🔊 [Loopback] PC System Audio", true, null, "LOOPBACK"));

        // 3. Physical WASAPI endpoints
        try
        {
            var endPoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var ep in endPoints)
            {
                string friendly = ep.FriendlyName;
                bool isValeton = friendly.Contains("GP-200", StringComparison.OrdinalIgnoreCase) ||
                                 friendly.Contains("Valeton", StringComparison.OrdinalIgnoreCase);
                string tag = isValeton ? "⚡ [WASAPI 5ms] " : "[WASAPI] ";
                _devices.Add(new AudioDeviceInfo(ep.ID, tag + friendly, false, ep, "WASAPI"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error enumerating audio devices: {ex.Message}");
        }

        // Auto-select priority: Valeton ASIO > Valeton WASAPI > first physical > loopback
        int bestIndex = _devices.FindIndex(d =>
            d.Name.Contains("Valeton", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("GP-200", StringComparison.OrdinalIgnoreCase));

        if (bestIndex < 0 && _devices.Count > 1)
        {
            bestIndex = _devices.FindIndex(d => !d.IsLoopback);
            if (bestIndex < 0) bestIndex = 0;
        }
        else if (bestIndex < 0)
        {
            bestIndex = 0; // Loopback
        }

        SelectedIndex = Math.Clamp(bestIndex, 0, Math.Max(0, _devices.Count - 1));
    }

    public static List<string> GetRegisteredAsioDrivers()
    {
        var list = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASIO");
            if (key != null)
            {
                foreach (var sub in key.GetSubKeyNames())
                {
                    if (!list.Contains(sub)) list.Add(sub);
                }
            }
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\ASIO");
            if (key != null)
            {
                foreach (var sub in key.GetSubKeyNames())
                {
                    if (!list.Contains(sub)) list.Add(sub);
                }
            }
        }
        catch { }

        return list;
    }

    public bool Start(int deviceIndex)
    {
        Stop();
        if (deviceIndex < 0 || deviceIndex >= _devices.Count)
        {
            NotifyStatus(UiText.Language == "es" ? "Error: selecciona una entrada de audio válida." : "Error: select a valid audio input.");
            return false;
        }

        SelectedIndex = deviceIndex;
        var info = _devices[deviceIndex];
        string captureBackend = info.DriverType;
        PacketsReceived = 0;
        SamplesReceived = 0;
        PeakDbfs = -96.0;
        RmsDbfs = -96.0;

        try
        {
            if (info.DriverType == "ASIO")
            {
                // ASIO Low-Latency path
                string driverName = info.Id.Replace("ASIO_", "");
                try
                {
                    _asioCapture = new AsioOut(driverName);
                    _asioCapture.AudioAvailable += OnAsioAudioAvailable;
                    // Attempt to initialize input recording
                    _asioCapture.InitRecordAndPlayback(null, 2, 48000);
                    SampleRate = 48000;
                    Channels = 2;
                    _sampleReader = new AudioSampleReader(new WaveFormat(48000, 32, 2));
                    _capturing = true;
                    _asioCapture.Play();
                    captureBackend = $"ASIO ({driverName}) < 3ms";
                }
                catch
                {
                    // If ASIO initialization fails (e.g. device disconnected or in use), fallback to WASAPI
                    _asioCapture?.Dispose();
                    _asioCapture = null;
                    return FallbackToWasapi(info);
                }
            }
            else if (info.IsLoopback)
            {
                var loopback = new WasapiLoopbackCapture();
                _capture = loopback;
                InitCapture(loopback.WaveFormat);
                captureBackend = "WASAPI Loopback";
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capturing = true;
                _capture.StartRecording();
            }
            else if (info.Device != null)
            {
                // Try WASAPI Low-Latency Shared (10ms)
                try
                {
                    var wasapi = new WasapiCapture(info.Device, false, 10);
                    _capture = wasapi;
                    InitCapture(wasapi.WaveFormat);
                    captureBackend = "WASAPI Low-Latency (10ms)";
                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;
                    _capturing = true;
                    _capture.StartRecording();
                }
                catch
                {
                    // Fallback to WaveInEvent (WinMM)
                    var waveIn = new WaveInEvent
                    {
                        WaveFormat = new WaveFormat(48000, 16, 2),
                        BufferMilliseconds = 25
                    };
                    _capture = waveIn;
                    InitCapture(waveIn.WaveFormat);
                    captureBackend = "WinMM Shared Audio";
                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;
                    _capturing = true;
                    _capture.StartRecording();
                }
            }
            else
            {
                NotifyStatus(UiText.Language == "es" ? "Error: entrada no disponible." : "Error: audio input unavailable.");
                return false;
            }

            NotifyStatus(UiText.Language == "es"
                ? $"Captura iniciada · {info.Name} · {captureBackend}"
                : $"Capture active · {info.Name} · {captureBackend}");

            return true;
        }
        catch (Exception ex)
        {
            Stop();
            NotifyStatus((UiText.Language == "es" ? "Error al iniciar captura: " : "Capture error: ") + ex.Message);
            return false;
        }
    }

    private bool FallbackToWasapi(AudioDeviceInfo info)
    {
        // Find matching WASAPI endpoint
        var wasapiDevice = _devices.FirstOrDefault(d => d.Device != null &&
            (d.Name.Contains("GP-200", StringComparison.OrdinalIgnoreCase) ||
             d.Name.Contains("Valeton", StringComparison.OrdinalIgnoreCase)));

        if (wasapiDevice != null)
        {
            int idx = _devices.IndexOf(wasapiDevice);
            return Start(idx);
        }
        return false;
    }

    private void InitCapture(WaveFormat format)
    {
        SampleRate = format.SampleRate;
        Channels = format.Channels;
        _sampleReader = new AudioSampleReader(format);
    }

    private void OnAsioAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        if (e.SamplesPerBuffer <= 0) return;
        PacketsReceived++;

        float[] monoBlock = new float[e.SamplesPerBuffer];
        double sumSquares = 0;
        float peakAmp = 0;

        float[] left = new float[e.SamplesPerBuffer];
        e.GetAsInterleavedSamples(left);

        for (int i = 0; i < e.SamplesPerBuffer; i++)
        {
            float val = i < left.Length ? left[i] : 0f;
            monoBlock[i] = val;
            float abs = Math.Abs(val);
            if (abs > peakAmp) peakAmp = abs;
            sumSquares += val * val;
        }

        UpdateLevels(monoBlock, sumSquares, peakAmp);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var reader = _sampleReader;
        if (reader == null || e.BytesRecorded <= 0) return;

        PacketsReceived++;
        int frameSize = reader.FrameSize;
        if (frameSize <= 0) return;

        int frames = e.BytesRecorded / frameSize;
        if (frames <= 0) return;

        float[] monoBlock = new float[frames];
        double sumSquares = 0;
        float peakAmp = 0;

        for (int i = 0; i < frames; i++)
        {
            float peakInFrame = 0;
            for (int ch = 0; ch < reader.Channels; ch++)
            {
                int offset = i * frameSize + ch * (frameSize / reader.Channels);
                if (offset + reader.SampleSize <= e.BytesRecorded && reader.TryReadSample(e.Buffer, offset, out float val))
                {
                    float abs = Math.Abs(val);
                    if (abs > peakInFrame)
                    {
                        peakInFrame = abs;
                        monoBlock[i] = val;
                    }
                }
            }

            sumSquares += monoBlock[i] * monoBlock[i];
            if (peakInFrame > peakAmp) peakAmp = peakInFrame;
        }

        UpdateLevels(monoBlock, sumSquares, peakAmp);
    }

    private void UpdateLevels(float[] monoBlock, double sumSquares, float peakAmp)
    {
        SamplesReceived += monoBlock.Length;
        double rms = Math.Sqrt(sumSquares / Math.Max(1, monoBlock.Length));
        PeakDbfs = peakAmp > 1e-5 ? 20.0 * Math.Log10(peakAmp) : -96.0;
        RmsDbfs = rms > 1e-5 ? 20.0 * Math.Log10(rms) : -96.0;

        SamplesAvailable?.Invoke(monoBlock, SampleRate, PeakDbfs, RmsDbfs);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _capturing = false;
        if (e.Exception != null)
        {
            NotifyStatus((UiText.Language == "es" ? "Error en captura: " : "Capture stopped: ") + e.Exception.Message);
        }
    }

    public void Stop()
    {
        _capturing = false;
        if (_capture != null)
        {
            try
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
            }
            catch { }
            _capture = null;
        }

        if (_asioCapture != null)
        {
            try
            {
                _asioCapture.AudioAvailable -= OnAsioAudioAvailable;
                _asioCapture.Stop();
                _asioCapture.Dispose();
            }
            catch { }
            _asioCapture = null;
        }

        PeakDbfs = -96.0;
        RmsDbfs = -96.0;
    }

    private void NotifyStatus(string message)
    {
        StatusChanged?.Invoke(message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _enumerator?.Dispose();
    }
}
