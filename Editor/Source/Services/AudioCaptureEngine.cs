using System.Diagnostics;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Gp200.Replica;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsLoopback, MMDevice? Device, string DriverType = "WASAPI");

/// <summary>
/// Universal low-latency audio capture engine for Windows 10/11.
/// Dynamically enumerates all sound sources on the laptop:
/// Valeton GP-200 USB, Laptop Microphones (Intel/Realtek), System Audio Loopback, and ASIO.
/// Auto-unmutes muted Windows endpoints and supports live signal detection.
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

        // 1. Physical WASAPI endpoints (Laptop Mics, Valeton GP-200 USB, Webcams)
        try
        {
            var endPoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var ep in endPoints)
            {
                string friendly = ep.FriendlyName;
                bool isValeton = friendly.Contains("GP-200", StringComparison.OrdinalIgnoreCase) ||
                                 friendly.Contains("Valeton", StringComparison.OrdinalIgnoreCase);
                bool isLaptopMic = friendly.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                                   friendly.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                                   friendly.Contains("micrófono", StringComparison.OrdinalIgnoreCase) ||
                                   friendly.Contains("microphone", StringComparison.OrdinalIgnoreCase);

                string tag = isValeton ? "🎸 [GP-200 USB] " : (isLaptopMic ? "💻 [Mic Laptop] " : "🎤 [Entrada] ");
                _devices.Add(new AudioDeviceInfo(ep.ID, tag + friendly, false, ep, "WASAPI"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error enumerating WASAPI audio endpoints: {ex.Message}");
        }

        // 2. Loopback PC Audio option (Captures everything playing on Windows: YouTube, Spotify, etc.)
        _devices.Add(new AudioDeviceInfo("LOOPBACK", UiText.Language == "es" ? "🔊 [Loopback] Audio del Sistema (PC / Stems / YouTube)" : "🔊 [Loopback] PC System Audio (YouTube / Stems)", true, null, "LOOPBACK"));

        // 3. Detect ASIO drivers from Windows Registry (Priority for Valeton GP-200 ASIO)
        try
        {
            var asioNames = GetRegisteredAsioDrivers();
            foreach (var asioName in asioNames)
            {
                bool isValeton = asioName.Contains("Valeton", StringComparison.OrdinalIgnoreCase) ||
                                 asioName.Contains("GP-200", StringComparison.OrdinalIgnoreCase);
                string label = isValeton ? $"⚡ [ASIO GP-200] {asioName}" : $"⚡ [ASIO] {asioName}";
                _devices.Add(new AudioDeviceInfo($"ASIO_{asioName}", label, false, null, "ASIO"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ASIO registry check: {ex.Message}");
        }

        // Auto-select priority: Valeton WASAPI / ASIO > first physical microphone > Loopback
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

    public static int FindWaveInDeviceIndex(string friendlyName)
    {
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            if (friendlyName.Contains(caps.ProductName, StringComparison.OrdinalIgnoreCase) ||
                caps.ProductName.Contains(friendlyName, StringComparison.OrdinalIgnoreCase) ||
                (friendlyName.Contains("Valeton", StringComparison.OrdinalIgnoreCase) && caps.ProductName.Contains("Valeton", StringComparison.OrdinalIgnoreCase)) ||
                (friendlyName.Contains("GP-200", StringComparison.OrdinalIgnoreCase) && caps.ProductName.Contains("GP", StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }
        return -1;
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
                    // Provide a valid dummy silence provider so AsioOut never throws NullReference on Play
                    var silence = new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
                    _asioCapture.InitRecordAndPlayback(silence, 2, 48000);
                    SampleRate = 48000;
                    Channels = 2;
                    _sampleReader = new AudioSampleReader(new WaveFormat(48000, 32, 2));
                    _capturing = true;
                    _asioCapture.Play();
                    captureBackend = $"ASIO ({driverName}) < 3ms";
                }
                catch
                {
                    // Fallback to WASAPI
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
                captureBackend = "WASAPI Loopback (PC System Audio)";
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capturing = true;
                _capture.StartRecording();
            }
            else if (info.Device != null)
            {
                // Auto-unmute Windows Audio Endpoint if muted or volume is at 0
                try
                {
                    if (info.Device.AudioEndpointVolume != null)
                    {
                        if (info.Device.AudioEndpointVolume.Mute)
                        {
                            info.Device.AudioEndpointVolume.Mute = false;
                        }
                        if (info.Device.AudioEndpointVolume.MasterVolumeLevelScalar < 0.10f)
                        {
                            info.Device.AudioEndpointVolume.MasterVolumeLevelScalar = 0.90f;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AudioEndpointVolume adjustment: {ex.Message}");
                }

                // Tier 1: Event-driven WASAPI (20ms quantum)
                bool started = false;
                try
                {
                    var wasapi = new WasapiCapture(info.Device, true, 20);
                    _capture = wasapi;
                    InitCapture(wasapi.WaveFormat);
                    captureBackend = "WASAPI Event-Driven (20ms)";
                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;
                    _capturing = true;
                    _capture.StartRecording();
                    started = true;
                }
                catch (Exception ex1)
                {
                    Debug.WriteLine($"WASAPI Event-driven failed: {ex1.Message}, trying standard WASAPI shared...");
                }

                if (!started)
                {
                    // Tier 2: Standard WASAPI shared mode (Windows default buffer negotiation)
                    try
                    {
                        var wasapi = new WasapiCapture(info.Device);
                        _capture = wasapi;
                        InitCapture(wasapi.WaveFormat);
                        captureBackend = "WASAPI Shared";
                        _capture.DataAvailable += OnDataAvailable;
                        _capture.RecordingStopped += OnRecordingStopped;
                        _capturing = true;
                        _capture.StartRecording();
                        started = true;
                    }
                    catch (Exception ex2)
                    {
                        Debug.WriteLine($"WASAPI standard failed: {ex2.Message}, trying WinMM for matching device...");
                    }
                }

                if (!started)
                {
                    // Tier 3: WinMM targeted to the EXACT matching device index
                    int targetDev = FindWaveInDeviceIndex(info.Device.FriendlyName);
                    var waveIn = new WaveInEvent
                    {
                        DeviceNumber = targetDev >= 0 ? targetDev : 0,
                        WaveFormat = new WaveFormat(44100, 16, 2),
                        BufferMilliseconds = 40
                    };
                    _capture = waveIn;
                    InitCapture(waveIn.WaveFormat);
                    captureBackend = $"WinMM (Device {waveIn.DeviceNumber})";
                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;
                    _capturing = true;
                    _capture.StartRecording();
                    started = true;
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

    public async Task<int> AutoDetectActiveDeviceAsync(IProgress<string>? progress = null)
    {
        int initialIndex = SelectedIndex;
        int bestDeviceIndex = -1;
        double highestPeak = -96.0;

        for (int i = 0; i < _devices.Count; i++)
        {
            var dev = _devices[i];
            progress?.Report(UiText.Language == "es" ? $"Probando: {dev.Name}..." : $"Testing: {dev.Name}...");

            bool started = Start(i);
            if (started)
            {
                await Task.Delay(250);
                if (PeakDbfs > -55.0 && PeakDbfs > highestPeak)
                {
                    highestPeak = PeakDbfs;
                    bestDeviceIndex = i;
                }
            }
        }

        if (bestDeviceIndex >= 0)
        {
            Start(bestDeviceIndex);
            return bestDeviceIndex;
        }

        // If no active signal found, restore initial
        if (initialIndex >= 0 && initialIndex < _devices.Count)
        {
            Start(initialIndex);
        }
        return -1;
    }

    private bool FallbackToWasapi(AudioDeviceInfo info)
    {
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

        int channelCount = Math.Max(1, e.InputBuffers.Length);
        int totalSamples = e.SamplesPerBuffer * channelCount;
        float[] interleaved = new float[totalSamples];
        int read = e.GetAsInterleavedSamples(interleaved);

        float[] monoBlock = new float[e.SamplesPerBuffer];
        double sumSquares = 0;
        float peakAmp = 0;

        for (int i = 0; i < e.SamplesPerBuffer; i++)
        {
            float maxSampleInFrame = 0f;
            for (int ch = 0; ch < channelCount; ch++)
            {
                int idx = i * channelCount + ch;
                if (idx < read)
                {
                    float s = interleaved[idx];
                    if (Math.Abs(s) > Math.Abs(maxSampleInFrame)) maxSampleInFrame = s;
                }
            }
            monoBlock[i] = maxSampleInFrame;
            float abs = Math.Abs(maxSampleInFrame);
            if (abs > peakAmp) peakAmp = abs;
            sumSquares += maxSampleInFrame * maxSampleInFrame;
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
