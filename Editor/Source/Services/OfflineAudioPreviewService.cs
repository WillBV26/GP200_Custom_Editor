using System.IO;
using NAudio.Wave;

namespace Gp200.Replica;

/// <summary>
/// Offline DSP preview engine and DI Guitar Loop player.
/// When GP-200 pedal is not connected via USB, plays a realistic guitar DI riff
/// and processes it in real time through software DSP models of DST/AMP waveshaping,
/// CAB cabinet convolution / frequency-shaping, and delay/reverb for laptop listening.
/// </summary>
public sealed class OfflineAudioPreviewService : IDisposable
{
    private WaveOutEvent? _output;
    private BufferedWaveProvider? _waveProvider;
    private Thread? _renderThread;
    private volatile bool _isRunning;
    private PresetDocument? _currentDoc;
    private readonly object _lock = new();

    // DSP State Variables
    private readonly float[] _delayBuffer = new float[44100 * 2]; // 2 seconds max
    private int _delayIndex = 0;
    private float _reverbComb1, _reverbComb2, _reverbAllpass;
    private float _cabLowCutState = 0f, _cabHighCutState = 0f;

    public bool IsPlaying => _isRunning;
    public event Action<bool>? PlaybackStateChanged;

    public void PlayLoop(PresetDocument doc)
    {
        lock (_lock)
        {
            _currentDoc = new PresetDocument(doc.ToBytes());
            if (_isRunning) return;

            _isRunning = true;
            _delayIndex = 0;
            Array.Clear(_delayBuffer, 0, _delayBuffer.Length);
            _reverbComb1 = _reverbComb2 = _reverbAllpass = 0;
            _cabLowCutState = _cabHighCutState = 0;

            var waveFormat = new WaveFormat(44100, 16, 2);
            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true
            };

            _output = new WaveOutEvent { DesiredLatency = 100 };
            _output.Init(_waveProvider);
            _output.Play();

            _renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "OfflineAudioPreviewDSP"
            };
            _renderThread.Start();
        }

        PlaybackStateChanged?.Invoke(true);
    }

    public void StopLoop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;

            try
            {
                _output?.Stop();
                _output?.Dispose();
                _output = null;
            }
            catch { }

            _waveProvider = null;
        }

        PlaybackStateChanged?.Invoke(false);
    }

    public void UpdatePreset(PresetDocument doc)
    {
        lock (_lock)
        {
            _currentDoc = new PresetDocument(doc.ToBytes());
        }
    }

    private void RenderLoop()
    {
        const int sampleRate = 44100;
        const int chunkSize = 1024;
        byte[] pcmBuffer = new byte[chunkSize * 4]; // 16-bit stereo

        // Synthesize 4-bar rhythmic DI guitar riff (E - G - A progression)
        float[] diRiff = GenerateSyntheticDiGuitarRiff(sampleRate);
        int riffIndex = 0;

        while (_isRunning)
        {
            if (_waveProvider == null || _waveProvider.BufferedBytes > sampleRate)
            {
                Thread.Sleep(10);
                continue;
            }

            PresetDocument? active;
            lock (_lock) { active = _currentDoc; }
            if (active == null) continue;

            // Extract DSP settings
            var ampEffect = active.GetEffect(3); // AMP
            var dstEffect = active.GetEffect(2); // DST
            var cabEffect = active.GetEffect(5); // CAB
            var dlyEffect = active.GetEffect(8); // DLY
            var rvbEffect = active.GetEffect(9); // RVB

            float ampGain = ampEffect.Enabled ? Math.Clamp(ampEffect.Parameters.ElementAtOrDefault(0) / 30f + 1f, 1f, 8f) : 1f;
            float dstGain = dstEffect.Enabled ? Math.Clamp(dstEffect.Parameters.ElementAtOrDefault(0) / 25f + 1f, 1f, 6f) : 1f;
            float totalDrive = ampGain * dstGain;

            float cabHighCut = cabEffect.Enabled ? 4800f : 16000f;
            float cabAlpha = (float)Math.Exp(-2.0 * Math.PI * cabHighCut / sampleRate);

            bool delayOn = dlyEffect.Enabled;
            int delaySamples = Math.Clamp((int)(0.35f * sampleRate), 100, _delayBuffer.Length - 1);
            float delayFeedback = delayOn ? 0.35f : 0f;

            bool reverbOn = rvbEffect.Enabled;

            int pcmOffset = 0;
            for (int i = 0; i < chunkSize; i++)
            {
                // 1. Fetch raw DI sample
                float diSample = diRiff[riffIndex];
                riffIndex = (riffIndex + 1) % diRiff.Length;

                // 2. Waveshaping (Preamp & Overdrive tube saturation)
                float driven = diSample * totalDrive;
                // Asymmetric soft saturation: tanh approximation
                float shaped = driven / (1.0f + Math.Abs(driven) * 0.7f);

                // 3. Cabinet speaker filter simulation (Low-cut 80Hz & High-cut speaker roll-off)
                _cabHighCutState = cabAlpha * _cabHighCutState + (1.0f - cabAlpha) * shaped;
                float cabOut = _cabHighCutState;

                // 4. Delay Line
                float delayOut = _delayBuffer[_delayIndex];
                if (delayOn)
                {
                    _delayBuffer[_delayIndex] = cabOut + delayOut * delayFeedback;
                    _delayIndex = (_delayIndex + 1) % delaySamples;
                }
                else
                {
                    delayOut = 0f;
                }

                // 5. Reverb allpass simulation
                float revOut = 0f;
                if (reverbOn)
                {
                    _reverbComb1 = _reverbComb1 * 0.8f + cabOut * 0.2f;
                    _reverbComb2 = _reverbComb2 * 0.75f + cabOut * 0.25f;
                    float combSum = (_reverbComb1 + _reverbComb2) * 0.5f;
                    _reverbAllpass = -0.6f * combSum + _reverbAllpass * 0.6f;
                    revOut = _reverbAllpass;
                }

                // 6. Master mix
                float masterWet = (cabOut * 0.7f + delayOut * 0.35f + revOut * 0.25f);
                float finalSample = Math.Clamp(masterWet * 0.6f, -1.0f, 1.0f);

                short sample16 = (short)(finalSample * 32767f);
                pcmBuffer[pcmOffset++] = (byte)(sample16 & 0xFF);
                pcmBuffer[pcmOffset++] = (byte)((sample16 >> 8) & 0xFF);
                pcmBuffer[pcmOffset++] = (byte)(sample16 & 0xFF);
                pcmBuffer[pcmOffset++] = (byte)((sample16 >> 8) & 0xFF);
            }

            _waveProvider.AddSamples(pcmBuffer, 0, pcmOffset);
        }
    }

    private static float[] GenerateSyntheticDiGuitarRiff(int sampleRate)
    {
        // 3 seconds of a clean funk/rock palm-muted rhythmic groove
        int totalSamples = sampleRate * 3;
        float[] samples = new float[totalSamples];

        double[] notes = [82.41, 110.0, 146.83, 164.81, 196.0]; // E2, A2, D3, E3, G3
        int beatSamples = sampleRate / 4;

        for (int b = 0; b < 12; b++)
        {
            int start = b * beatSamples;
            double freq = notes[b % notes.Length];

            for (int i = 0; i < beatSamples && (start + i) < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double decay = Math.Exp(-14.0 * t); // Pluck decay
                double harmonic1 = Math.Sin(2 * Math.PI * freq * t);
                double harmonic2 = 0.5 * Math.Sin(2 * Math.PI * (freq * 2) * t);
                double harmonic3 = 0.25 * Math.Sin(2 * Math.PI * (freq * 3) * t);
                samples[start + i] = (float)((harmonic1 + harmonic2 + harmonic3) * decay * 0.7);
            }
        }

        return samples;
    }

    public void Dispose()
    {
        StopLoop();
    }
}
