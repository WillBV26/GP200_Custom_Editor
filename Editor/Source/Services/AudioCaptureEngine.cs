using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Gp200.Replica;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsLoopback, MMDevice? Device);

/// <summary>
/// Universal resilient audio capture engine for Windows 11.
/// Supports Valeton GP-200 USB audio, multi-channel DI routing, PC system loopback,
/// and automatic WASAPI / WinMM fallback to prevent any stuck -96 dBFS audio.
/// </summary>
public sealed class AudioCaptureEngine : IDisposable
{
    private readonly List<AudioDeviceInfo> _devices = [];
    private MMDeviceEnumerator? _enumerator;
    private IWaveIn? _capture;
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

        // 1. Loopback PC Audio option
        _devices.Add(new AudioDeviceInfo("LOOPBACK", UiText.Language == "es" ? "🔊 [Loopback] Audio del Sistema (PC)" : "🔊 [Loopback] PC System Audio", true, null));

        // 2. Physical active endpoints
        try
        {
            var endPoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var ep in endPoints)
            {
                _devices.Add(new AudioDeviceInfo(ep.ID, ep.FriendlyName, false, ep));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error enumerating audio devices: {ex.Message}");
        }

        // Auto-select priority: GP-200 > Valeton > USB Audio > first physical > loopback
        int bestIndex = _devices.FindIndex(d =>
            !d.IsLoopback && (
                d.Name.IndexOf("GP-200", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.Name.IndexOf("Valeton", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.Name.IndexOf("USB Audio", StringComparison.OrdinalIgnoreCase) >= 0));

        if (bestIndex < 0 && _devices.Count > 1)
        {
            bestIndex = 1; // First physical mic / line in
        }
        else if (bestIndex < 0)
        {
            bestIndex = 0; // Loopback
        }

        SelectedIndex = bestIndex;
    }

    public bool Start(int deviceIndex)
    {
        Stop();
        if (deviceIndex < 0 || deviceIndex >= _devices.Count) return false;

        SelectedIndex = deviceIndex;
        var info = _devices[deviceIndex];
        PacketsReceived = 0;
        SamplesReceived = 0;
        PeakDbfs = -96.0;
        RmsDbfs = -96.0;

        try
        {
            if (info.IsLoopback)
            {
                // System loopback capture
                var loopback = new WasapiLoopbackCapture();
                _capture = loopback;
                InitCapture(loopback.WaveFormat);
            }
            else if (info.Device != null)
            {
                // Try WASAPI Shared mode first
                try
                {
                    var wasapi = new WasapiCapture(info.Device, false, 50);
                    _capture = wasapi;
                    InitCapture(wasapi.WaveFormat);
                }
                catch
                {
                    // Fallback to WaveInEvent (WinMM)
                    var waveIn = new WaveInEvent
                    {
                        WaveFormat = new WaveFormat(48000, 16, 2),
                        BufferMilliseconds = 50
                    };
                    _capture = waveIn;
                    InitCapture(waveIn.WaveFormat);
                }
            }
            else
            {
                return false;
            }

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _capturing = true;

            NotifyStatus(UiText.Language == "es"
                ? $"Iniciado: {SampleRate} Hz ({Channels} ch) · Esperando señal..."
                : $"Started: {SampleRate} Hz ({Channels} ch) · Waiting for signal...");

            return true;
        }
        catch (Exception ex)
        {
            Stop();
            NotifyStatus((UiText.Language == "es" ? "Error al iniciar captura: " : "Capture error: ") + ex.Message);
            return false;
        }
    }

    private void InitCapture(WaveFormat format)
    {
        SampleRate = format.SampleRate;
        Channels = format.Channels;
        _sampleReader = new AudioSampleReader(format);
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
            // Scan across all channels in frame (taking the channel with highest amplitude, crucial for mono guitar on stereo DI)
            for (int ch = 0; ch < reader.Channels; ch++)
            {
                int offset = i * frameSize + ch * (frameSize / reader.Channels);
                if (offset + 2 <= e.BytesRecorded && reader.TryReadSample(e.Buffer, offset, out float val))
                {
                    float abs = Math.Abs(val);
                    if (abs > peakInFrame)
                    {
                        peakInFrame = abs;
                        monoBlock[i] = val; // Take strongest channel value
                    }
                }
            }

            sumSquares += monoBlock[i] * monoBlock[i];
            if (peakInFrame > peakAmp) peakAmp = peakInFrame;
        }

        SamplesReceived += frames;

        // Compute RMS and Peak dBFS
        double rms = Math.Sqrt(sumSquares / frames);
        RmsDbfs = rms > 1e-6 ? 20 * Math.Log10(rms) : -96.0;
        PeakDbfs = peakAmp > 1e-6 ? 20 * Math.Log10(peakAmp) : -96.0;

        if (PacketsReceived % 5 == 0)
        {
            string diag = UiText.Language == "es"
                ? $"Capturando ({SampleRate} Hz {Channels}ch · Pkts: {PacketsReceived} · Pico: {PeakDbfs:+0.0;-0.0;0} dBFS)"
                : $"Capturing ({SampleRate} Hz {Channels}ch · Pkts: {PacketsReceived} · Peak: {PeakDbfs:+0.0;-0.0;0} dBFS)";
            NotifyStatus(diag);
        }

        SamplesAvailable?.Invoke(monoBlock, SampleRate, PeakDbfs, RmsDbfs);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            NotifyStatus((UiText.Language == "es" ? "Captura detenida por error: " : "Capture stopped with error: ") + e.Exception.Message);
        }
    }

    private void NotifyStatus(string text)
    {
        StatusChanged?.Invoke(text);
    }

    public void Stop()
    {
        _capturing = false;
        var cap = _capture;
        _capture = null;
        _sampleReader = null;

        if (cap != null)
        {
            try
            {
                cap.DataAvailable -= OnDataAvailable;
                cap.RecordingStopped -= OnRecordingStopped;
                cap.StopRecording();
            }
            catch {}
            finally
            {
                cap.Dispose();
            }
        }

        PeakDbfs = -96.0;
        RmsDbfs = -96.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _enumerator?.Dispose();
        _enumerator = null;
    }
}

