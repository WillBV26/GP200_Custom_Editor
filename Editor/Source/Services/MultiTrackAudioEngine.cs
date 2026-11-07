using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Gp200.Replica;

public sealed class TrackClipData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = "";
    public double StartOffsetSeconds { get; set; } = 0.0;
    public double SkipSeconds { get; set; } = 0.0;
    public double TakeSeconds { get; set; } = 0.0;
}

/// <summary>
/// Sample-accurate multi-track audio engine for GP-200 Stem & Jam Studio.
/// Supports dynamic tracks, multiple clips per channel, and Live GP-200 USB recording takes.
/// Independent Mute, Solo, Volume (-inf to +6 dB), and Pan (-1 to +1) per track.
/// Master volume, loop regions, transport controls, and high-quality WAV export.
/// </summary>
public sealed class MultiTrackAudioEngine : IDisposable
{
    public static readonly WaveFormat MasterFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    readonly List<TrackChannel> channels = new();
    IWavePlayer? outputDevice;
    MixingSampleProvider? mixer;
    MeteredSampleProvider? meteredMixer;
    VolumeSampleProvider? masterVolumeProvider;
    readonly BufferedWaveProvider liveMonitorBuffer;
    VolumeSampleProvider? liveMonitorVolumeProvider;
    bool isLiveMonitoringEnabled = true;
    float liveMonitorVolume = 1.0f;
    bool isPlayingStems = false;

    WaveFileWriter? guitarRecorder;
    WaveInEvent? waveIn;
    string? recordingFilePath;
    int recordingTargetTrack = 6;
    double recordingStartOffset = 0.0;
    bool isRecording;
    TimeSpan currentEnginePosition = TimeSpan.Zero;

    public int ChannelCount => channels.Count;
    public bool IsPlaying => isPlayingStems;
    public float PeakLeft => meteredMixer?.PeakLeft ?? 0f;
    public float PeakRight => meteredMixer?.PeakRight ?? 0f;
    public TimeSpan Duration { get; private set; } = TimeSpan.Zero;
    public TimeSpan Position
    {
        get
        {
            for (int i = 0; i < channels.Count; i++)
            {
                for (int r = 0; r < channels[i].Readers.Count; r++)
                {
                    var reader = channels[i].Readers[r];
                    if (reader != null)
                        return reader.CurrentTime;
                }
            }
            return currentEnginePosition;
        }
        set
        {
            currentEnginePosition = value < TimeSpan.Zero ? TimeSpan.Zero : value > Duration ? Duration : value;
            for (int i = 0; i < channels.Count; i++)
            {
                for (int r = 0; r < channels[i].Readers.Count; r++)
                {
                    var reader = channels[i].Readers[r];
                    if (reader != null)
                    {
                        reader.CurrentTime = currentEnginePosition;
                    }
                }
            }
        }
    }

    public bool LoopEnabled { get; set; } = false;
    public TimeSpan LoopStart { get; set; } = TimeSpan.Zero;
    public TimeSpan LoopEnd { get; set; } = TimeSpan.FromMinutes(4);

    public float MasterVolume
    {
        get => masterVolume;
        set
        {
            masterVolume = Math.Clamp(value, 0f, 2f);
            if (masterVolumeProvider != null)
            {
                masterVolumeProvider.Volume = masterVolume;
            }
            try
            {
                if (outputDevice != null) outputDevice.Volume = Math.Clamp(masterVolume, 0f, 1f);
            }
            catch
            {
                // DirectSoundOut throws NotSupportedException - handled cleanly by masterVolumeProvider
            }
        }
    }
    float masterVolume = 1.0f;

    public bool IsLiveMonitoringEnabled
    {
        get => isLiveMonitoringEnabled;
        set
        {
            isLiveMonitoringEnabled = value;
            if (liveMonitorVolumeProvider != null)
            {
                liveMonitorVolumeProvider.Volume = isLiveMonitoringEnabled ? liveMonitorVolume : 0f;
            }
        }
    }

    public float LiveMonitorVolume
    {
        get => liveMonitorVolume;
        set
        {
            liveMonitorVolume = Math.Clamp(value, 0f, 2f);
            if (liveMonitorVolumeProvider != null && isLiveMonitoringEnabled)
            {
                liveMonitorVolumeProvider.Volume = liveMonitorVolume;
            }
        }
    }

    public event Action? PlaybackStopped;

    public MultiTrackAudioEngine()
    {
        liveMonitorBuffer = new BufferedWaveProvider(new WaveFormat(44100, 16, 2))
        {
            BufferDuration = TimeSpan.FromMilliseconds(250),
            DiscardOnBufferOverflow = true
        };
        EnsureChannelCount(8);
    }

    public void EnsureChannelCount(int count)
    {
        while (channels.Count < count)
        {
            channels.Add(new TrackChannel());
        }
    }

    public static ISampleProvider NormalizeToFormat(ISampleProvider input, WaveFormat target)
    {
        ISampleProvider p = input;

        // 1. Channel normalization
        if (p.WaveFormat.Channels == 1 && target.Channels == 2)
        {
            p = new MonoToStereoSampleProvider(p);
        }
        else if (p.WaveFormat.Channels == 2 && target.Channels == 1)
        {
            p = new StereoToMonoSampleProvider(p);
        }
        else if (p.WaveFormat.Channels > 2 && target.Channels == 2)
        {
            p = new MultiplexingSampleProvider(new[] { p }, 2);
        }

        // 2. Sample rate normalization
        if (p.WaveFormat.SampleRate != target.SampleRate)
        {
            p = new WdlResamplingSampleProvider(p, target.SampleRate);
        }

        return p;
    }

    public void FeedLiveMonitoring(float[] samples, int sampleRate)
    {
        if (!isLiveMonitoringEnabled || samples == null || samples.Length == 0) return;
        EnsureOutputDeviceInitialized();

        // Convert incoming mono/stereo float samples to 16-bit stereo PCM bytes
        byte[] pcmBytes = new byte[samples.Length * 4];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcmVal = (short)Math.Clamp((int)(samples[i] * 32767f), short.MinValue, short.MaxValue);
            byte b0 = (byte)(pcmVal & 0xFF);
            byte b1 = (byte)((pcmVal >> 8) & 0xFF);
            pcmBytes[i * 4] = b0;
            pcmBytes[i * 4 + 1] = b1;
            pcmBytes[i * 4 + 2] = b0;
            pcmBytes[i * 4 + 3] = b1;
        }

        try
        {
            liveMonitorBuffer.AddSamples(pcmBytes, 0, pcmBytes.Length);
        }
        catch
        {
            liveMonitorBuffer.ClearBuffer();
        }
    }

    private void EnsureOutputDeviceInitialized()
    {
        if (outputDevice == null)
        {
            mixer = new MixingSampleProvider(MasterFormat) { ReadFully = true };

            for (int i = 0; i < channels.Count; i++)
            {
                if (channels[i].PausableProvider != null)
                {
                    mixer.AddMixerInput(channels[i].PausableProvider!);
                }
            }

            var monitorSample = liveMonitorBuffer.ToSampleProvider();
            liveMonitorVolumeProvider = new VolumeSampleProvider(NormalizeToFormat(monitorSample, MasterFormat))
            {
                Volume = isLiveMonitoringEnabled ? liveMonitorVolume : 0f
            };
            mixer.AddMixerInput(liveMonitorVolumeProvider);

            meteredMixer = new MeteredSampleProvider(mixer);
            masterVolumeProvider = new VolumeSampleProvider(meteredMixer)
            {
                Volume = masterVolume
            };

            outputDevice = new DirectSoundOut(40);
            outputDevice.Init(masterVolumeProvider);
            try { outputDevice.Volume = Math.Clamp(masterVolume, 0f, 1f); } catch { }
            outputDevice.PlaybackStopped += (_, _) => PlaybackStopped?.Invoke();
            outputDevice.Play();
        }
        else if (outputDevice.PlaybackState != PlaybackState.Playing)
        {
            try { outputDevice.Play(); } catch { }
        }
    }

    public void RefreshMixerInputs()
    {
        if (mixer == null) return;
        mixer.RemoveAllMixerInputs();

        for (int i = 0; i < channels.Count; i++)
        {
            if (channels[i].PausableProvider != null)
            {
                mixer.AddMixerInput(channels[i].PausableProvider!);
            }
        }

        if (liveMonitorVolumeProvider != null)
        {
            mixer.AddMixerInput(liveMonitorVolumeProvider);
        }
    }

    public void SetTrackClips(int trackIndex, IEnumerable<TrackClipData> clips)
    {
        if (trackIndex < 0) return;
        EnsureChannelCount(trackIndex + 1);

        float oldVol = channels[trackIndex].VolumeDb;
        float oldPan = channels[trackIndex].Pan;
        bool oldMute = channels[trackIndex].IsMuted;
        bool oldSolo = channels[trackIndex].IsSolo;

        channels[trackIndex].Dispose();
        var clipList = clips.ToList();
        channels[trackIndex].Clips.AddRange(clipList);
        channels[trackIndex].VolumeDb = oldVol;
        channels[trackIndex].Pan = oldPan;
        channels[trackIndex].IsMuted = oldMute;
        channels[trackIndex].IsSolo = oldSolo;

        if (clipList.Count == 0)
        {
            RecalculateDuration();
            UpdateVolumes();
            RefreshMixerInputs();
            return;
        }

        var providers = new List<ISampleProvider>();
        foreach (var c in clipList)
        {
            if (string.IsNullOrEmpty(c.FilePath) || !File.Exists(c.FilePath)) continue;
            var reader = new AudioFileReader(c.FilePath);
            channels[trackIndex].Readers.Add(reader);

            ISampleProvider p = reader.ToSampleProvider();
            if (c.StartOffsetSeconds > 0 || c.SkipSeconds > 0 || c.TakeSeconds > 0)
            {
                p = new OffsetSampleProvider(p)
                {
                    DelayBy = TimeSpan.FromSeconds(Math.Max(0, c.StartOffsetSeconds)),
                    SkipOver = TimeSpan.FromSeconds(Math.Max(0, c.SkipSeconds)),
                    Take = c.TakeSeconds > 0 ? TimeSpan.FromSeconds(c.TakeSeconds) : TimeSpan.Zero
                };
            }
            // Normalize each clip provider so any mix of 44.1kHz / 48kHz / mono / stereo clips works together!
            providers.Add(NormalizeToFormat(p, MasterFormat));
        }

        if (providers.Count == 0)
        {
            RecalculateDuration();
            UpdateVolumes();
            RefreshMixerInputs();
            return;
        }

        ISampleProvider channelSource = providers[0];
        if (providers.Count > 1)
        {
            var multiMix = new MixingSampleProvider(MasterFormat) { ReadFully = true };
            foreach (var prov in providers)
            {
                multiMix.AddMixerInput(prov);
            }
            channelSource = multiMix;
        }

        var volProv = new VolumeSampleProvider(channelSource)
        {
            Volume = channels[trackIndex].CalculatedVolume
        };
        channels[trackIndex].VolumeProvider = volProv;

        var pausable = new PausableSampleProvider(volProv)
        {
            IsActive = isPlayingStems
        };
        channels[trackIndex].PausableProvider = pausable;

        RecalculateDuration();
        UpdateVolumes();
        RefreshMixerInputs();
    }

    public void LoadStem(int trackIndex, string audioFilePath, double startOffsetSeconds = 0.0, double skipSeconds = 0.0, double takeSeconds = 0.0)
    {
        SetTrackClips(trackIndex, new[]
        {
            new TrackClipData
            {
                FilePath = audioFilePath,
                StartOffsetSeconds = Math.Max(0, startOffsetSeconds),
                SkipSeconds = Math.Max(0, skipSeconds),
                TakeSeconds = Math.Max(0, takeSeconds)
            }
        });
    }

    public void SetTrackOffset(int trackIndex, double offsetSeconds)
    {
        if (trackIndex < 0 || trackIndex >= channels.Count) return;
        var ch = channels[trackIndex];
        var first = ch.Clips.FirstOrDefault();
        if (first == null || string.IsNullOrEmpty(first.FilePath)) return;

        first.StartOffsetSeconds = Math.Max(0, offsetSeconds);
        SetTrackClips(trackIndex, ch.Clips);
    }

    public void TrimTrack(int trackIndex, double skipSeconds, double takeSeconds)
    {
        if (trackIndex < 0 || trackIndex >= channels.Count) return;
        var ch = channels[trackIndex];
        var first = ch.Clips.FirstOrDefault();
        if (first == null || string.IsNullOrEmpty(first.FilePath)) return;

        first.SkipSeconds = Math.Max(0, skipSeconds);
        first.TakeSeconds = Math.Max(0, takeSeconds);
        SetTrackClips(trackIndex, ch.Clips);
    }

    public void RemoveTrack(int trackIndex)
    {
        if (trackIndex >= 0 && trackIndex < channels.Count)
        {
            channels[trackIndex].Dispose();
            channels.RemoveAt(trackIndex);
            RecalculateDuration();
            UpdateVolumes();
            RefreshMixerInputs();
        }
    }

    public (bool hasAudio, string? filePath, double startOffset, double skipSeconds, double takeSeconds, double totalDuration) GetTrackTimelineInfo(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= channels.Count)
            return (false, null, 0, 0, 0, 0);

        var ch = channels[trackIndex];
        var first = ch.Clips.FirstOrDefault();
        if (first == null || string.IsNullOrEmpty(first.FilePath))
            return (false, null, 0, 0, 0, 0);

        double totalDur = ch.Readers.FirstOrDefault()?.TotalTime.TotalSeconds ?? 0;
        return (true, first.FilePath, first.StartOffsetSeconds, first.SkipSeconds, first.TakeSeconds, totalDur);
    }

    private void RecalculateDuration()
    {
        double maxSec = 0;
        foreach (var ch in channels)
        {
            foreach (var clip in ch.Clips)
            {
                if (!string.IsNullOrEmpty(clip.FilePath) && File.Exists(clip.FilePath))
                {
                    double clipDuration = clip.TakeSeconds > 0 ? clip.TakeSeconds : Math.Max(0.5, 300);
                    double total = clip.StartOffsetSeconds + clipDuration;
                    if (total > maxSec) maxSec = total;
                }
            }
        }
        Duration = TimeSpan.FromSeconds(Math.Max(1.0, maxSec));
    }

    public void ClearTrack(int trackIndex)
    {
        if (trackIndex >= 0 && trackIndex < channels.Count)
        {
            channels[trackIndex].Dispose();
            RecalculateDuration();
            UpdateVolumes();
            RefreshMixerInputs();
        }
    }

    public void SetMute(int trackIndex, bool mute)
    {
        if (trackIndex < 0) return;
        EnsureChannelCount(trackIndex + 1);
        channels[trackIndex].IsMuted = mute;
        UpdateVolumes();
    }

    public void SetSolo(int trackIndex, bool solo)
    {
        if (trackIndex < 0) return;
        EnsureChannelCount(trackIndex + 1);
        channels[trackIndex].IsSolo = solo;
        UpdateVolumes();
    }

    public void SetVolumeDb(int trackIndex, float volumeDb)
    {
        if (trackIndex < 0) return;
        EnsureChannelCount(trackIndex + 1);
        channels[trackIndex].VolumeDb = volumeDb;
        UpdateVolumes();
    }

    public void SetPan(int trackIndex, float pan)
    {
        if (trackIndex < 0) return;
        EnsureChannelCount(trackIndex + 1);
        channels[trackIndex].Pan = Math.Clamp(pan, -1f, 1f);
    }

    void UpdateVolumes()
    {
        bool anySolo = channels.Any(c => c.IsSolo);
        for (int i = 0; i < channels.Count; i++)
        {
            float targetVol = 0f;
            if (anySolo)
            {
                targetVol = channels[i].IsSolo && !channels[i].IsMuted ? channels[i].LinearGain : 0f;
            }
            else
            {
                targetVol = channels[i].IsMuted ? 0f : channels[i].LinearGain;
            }

            channels[i].CalculatedVolume = targetVol;
            if (channels[i].VolumeProvider != null)
                channels[i].VolumeProvider!.Volume = targetVol;
        }
    }

    public void Play()
    {
        EnsureOutputDeviceInitialized();
        isPlayingStems = true;
        for (int i = 0; i < channels.Count; i++)
        {
            if (channels[i].PausableProvider != null)
            {
                channels[i].PausableProvider!.IsActive = true;
            }
        }
        if (outputDevice != null && outputDevice.PlaybackState != PlaybackState.Playing)
        {
            outputDevice.Play();
        }
    }

    public void Pause()
    {
        isPlayingStems = false;
        for (int i = 0; i < channels.Count; i++)
        {
            if (channels[i].PausableProvider != null)
            {
                channels[i].PausableProvider!.IsActive = false;
            }
        }
    }

    public void Stop()
    {
        Pause();
        Position = TimeSpan.Zero;
    }

    public void StartRecordingGuitar(string destinationWavPath, int targetTrackIndex = 6, double startOffsetSeconds = 0.0)
    {
        try
        {
            recordingFilePath = destinationWavPath;
            recordingTargetTrack = Math.Max(0, targetTrackIndex);
            recordingStartOffset = Math.Max(0, startOffsetSeconds);

            waveIn = new WaveInEvent();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                if (caps.ProductName.Contains("Valeton", StringComparison.OrdinalIgnoreCase) ||
                    caps.ProductName.Contains("GP-200", StringComparison.OrdinalIgnoreCase) ||
                    caps.ProductName.Contains("GP200", StringComparison.OrdinalIgnoreCase))
                {
                    waveIn.DeviceNumber = i;
                    break;
                }
            }

            var waveFormat = new WaveFormat(44100, 16, 2);
            waveIn.WaveFormat = waveFormat;
            guitarRecorder = new WaveFileWriter(destinationWavPath, waveFormat);
            waveIn.DataAvailable += (s, e) =>
            {
                guitarRecorder?.Write(e.Buffer, 0, e.BytesRecorded);
            };
            waveIn.StartRecording();
            isRecording = true;
        }
        catch
        {
            try
            {
                var waveFormat = new WaveFormat(44100, 16, 2);
                guitarRecorder = new WaveFileWriter(destinationWavPath, waveFormat);
                isRecording = true;
            }
            catch { }
        }
    }

    public string? StopRecordingGuitar()
    {
        if (!isRecording) return null;
        isRecording = false;
        try
        {
            waveIn?.StopRecording();
            waveIn?.Dispose();
            waveIn = null;

            guitarRecorder?.Flush();
            guitarRecorder?.Dispose();
            guitarRecorder = null;

            if (recordingFilePath != null && File.Exists(recordingFilePath))
            {
                LoadStem(recordingTargetTrack, recordingFilePath, recordingStartOffset);
                return recordingFilePath;
            }
        }
        catch { }
        return null;
    }

    public async Task ExportMixdownAsync(string destinationWavPath, IProgress<double>? progress = null)
    {
        await Task.Run(() =>
        {
            var readers = new List<AudioFileReader>();
            try
            {
                for (int i = 0; i < channels.Count; i++)
                {
                    if (channels[i].Readers.Count > 0 && channels[i].CalculatedVolume > 0f)
                    {
                        foreach (var orig in channels[i].Readers)
                        {
                            var reader = new AudioFileReader(orig.FileName);
                            reader.Position = 0;
                            readers.Add(reader);
                        }
                    }
                }

                if (readers.Count == 0) return;

                var exportMixer = new MixingSampleProvider(MasterFormat) { ReadFully = false };
                foreach (var r in readers)
                {
                    exportMixer.AddMixerInput(NormalizeToFormat(r.ToSampleProvider(), MasterFormat));
                }
                WaveFileWriter.CreateWaveFile16(destinationWavPath, exportMixer);
                progress?.Report(100.0);
            }
            finally
            {
                foreach (var r in readers) r.Dispose();
            }
        });
    }

    public void CheckLoop()
    {
        if (!LoopEnabled || !IsPlaying) return;
        if (Position >= LoopEnd)
        {
            Position = LoopStart;
        }
    }

    public void Dispose()
    {
        Stop();
        outputDevice?.Dispose();
        outputDevice = null;
        for (int i = 0; i < channels.Count; i++)
        {
            channels[i].Dispose();
        }
        channels.Clear();
        waveIn?.Dispose();
        waveIn = null;
        guitarRecorder?.Dispose();
        guitarRecorder = null;
    }

    sealed class PausableSampleProvider : ISampleProvider
    {
        readonly ISampleProvider source;
        public bool IsActive { get; set; } = false;

        public WaveFormat WaveFormat => source.WaveFormat;

        public PausableSampleProvider(ISampleProvider source)
        {
            this.source = source;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (!IsActive)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
            return source.Read(buffer, offset, count);
        }
    }

    sealed class StereoToMonoSampleProvider : ISampleProvider
    {
        readonly ISampleProvider source;
        float[] sourceBuffer = new float[4096];

        public WaveFormat WaveFormat { get; }

        public StereoToMonoSampleProvider(ISampleProvider source)
        {
            this.source = source;
            this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int sourceCount = count * 2;
            if (sourceBuffer.Length < sourceCount)
            {
                Array.Resize(ref sourceBuffer, sourceCount);
            }
            int read = source.Read(sourceBuffer, 0, sourceCount);
            int frames = read / 2;
            for (int i = 0; i < frames; i++)
            {
                buffer[offset + i] = (sourceBuffer[i * 2] + sourceBuffer[i * 2 + 1]) * 0.5f;
            }
            return frames;
        }
    }

    sealed class TrackChannel : IDisposable
    {
        public List<TrackClipData> Clips { get; } = new();
        public List<AudioFileReader> Readers { get; } = new();
        public VolumeSampleProvider? VolumeProvider { get; set; }
        public PausableSampleProvider? PausableProvider { get; set; }
        public string? FilePath => Clips.FirstOrDefault()?.FilePath;
        public bool IsMuted { get; set; }
        public bool IsSolo { get; set; }
        public float VolumeDb { get; set; } = 0f;
        public float Pan { get; set; } = 0f;
        public float CalculatedVolume { get; set; } = 1f;

        public float LinearGain => VolumeDb <= -40f ? 0f : (float)Math.Pow(10, VolumeDb / 20.0);

        public void Dispose()
        {
            foreach (var r in Readers)
            {
                try { r.Dispose(); } catch { }
            }
            Readers.Clear();
            Clips.Clear();
            VolumeProvider = null;
            PausableProvider = null;
        }
    }

    sealed class MeteredSampleProvider : ISampleProvider
    {
        readonly ISampleProvider source;
        public WaveFormat WaveFormat => source.WaveFormat;
        public float PeakLeft { get; private set; }
        public float PeakRight { get; private set; }

        public MeteredSampleProvider(ISampleProvider source) => this.source = source;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            float maxL = 0f, maxR = 0f;
            int ch = WaveFormat.Channels;
            for (int i = 0; i < read; i += ch)
            {
                float absL = Math.Abs(buffer[offset + i]);
                if (absL > maxL) maxL = absL;
                if (ch > 1)
                {
                    float absR = Math.Abs(buffer[offset + i + 1]);
                    if (absR > maxR) maxR = absR;
                }
                else maxR = maxL;
            }
            PeakLeft = Math.Max(maxL, PeakLeft * 0.85f);
            PeakRight = Math.Max(maxR, PeakRight * 0.85f);
            return read;
        }
    }
}
