using NAudio.Wave;

namespace Gp200.Replica;

public sealed record AudioToneReport(
    double DurationSeconds,
    double PeakDbfs,
    double RmsDbfs,
    double CrestFactorDb,
    string EstimatedGainCharacter,
    double SubBassPct,
    double BassPct,
    double LowMidsPct,
    double HighMidsPct,
    double PresencePct,
    double AirPct,
    double SpectralCentroidHz,
    int? EstimatedBpm,
    string Summary
);

public static class AudioToneAnalyzer
{
    public static WaveStream OpenAudioStream(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".wav")
        {
            try { return new WaveFileReader(filePath); } catch { }
        }
        else if (ext == ".mp3")
        {
            try { return new MediaFoundationReader(filePath); } catch { }
            try { return new Mp3FileReader(filePath); } catch { }
        }
        
        try { return new AudioFileReader(filePath); } catch { }
        try { return new MediaFoundationReader(filePath); } catch { }
        try { return new Mp3FileReader(filePath); } catch { }
        return new WaveFileReader(filePath);
    }

    public static AudioToneReport AnalyzeFile(string filePath, int maxDurationSeconds = 30)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        using var stream = OpenAudioStream(filePath);
        var sampleProvider = stream.ToSampleProvider();
        int sampleRate = sampleProvider.WaveFormat.SampleRate;
        int channels = sampleProvider.WaveFormat.Channels;

        int maxSamples = sampleRate * channels * maxDurationSeconds;
        var samples = new List<float>(Math.Min(maxSamples, 48000 * 2 * 30));
        float[] buffer = new float[4096];
        int read;

        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0 && samples.Count < maxSamples)
        {
            for (int i = 0; i < read && samples.Count < maxSamples; i++)
                samples.Add(buffer[i]);
        }

        return AnalyzeSamples(samples.ToArray(), sampleRate, channels);
    }

    public static AudioToneReport AnalyzeSamples(ReadOnlySpan<float> interleavedSamples, int sampleRate, int channels)
    {
        if (interleavedSamples.IsEmpty || sampleRate <= 0 || channels <= 0)
            return new(0, -96, -96, 0, "Unknown", 0, 0, 0, 0, 0, 0, 1000, null, "No audio data.");

        // Convert to mono
        int frameCount = interleavedSamples.Length / channels;
        float[] mono = new float[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += interleavedSamples[i * channels + c];
            mono[i] = sum / channels;
        }

        // Peak and RMS
        double peak = 0;
        double sumSquares = 0;
        foreach (float s in mono)
        {
            double abs = Math.Abs(s);
            if (abs > peak) peak = abs;
            sumSquares += s * s;
        }

        double rms = Math.Sqrt(sumSquares / Math.Max(1, mono.Length));
        double peakDbfs = peak > 0 ? 20 * Math.Log10(peak) : -96.0;
        double rmsDbfs = rms > 0 ? 20 * Math.Log10(rms) : -96.0;
        double crestFactor = peakDbfs - rmsDbfs;

        string gainCharacter = crestFactor switch
        {
            > 14.0 => "Clean (High dynamic headroom)",
            >= 9.0 => "Crunch / Overdrive (Moderate compression)",
            _ => "High-Gain / Distortion (Heavy compression / sustained clipping)"
        };

        // Band filters via simple biquads
        var subBassFilter = BiquadFilter.LowPass(sampleRate, 60, 0.707);
        var bassFilter = BiquadFilter.BandPass(sampleRate, 150, 1.0);
        var lowMidsFilter = BiquadFilter.BandPass(sampleRate, 375, 1.0);
        var highMidsFilter = BiquadFilter.BandPass(sampleRate, 1200, 1.0);
        var presenceFilter = BiquadFilter.BandPass(sampleRate, 3500, 1.0);
        var airFilter = BiquadFilter.HighPass(sampleRate, 6000, 0.707);

        double subBassEnergy = 0, bassEnergy = 0, lowMidsEnergy = 0, highMidsEnergy = 0, presenceEnergy = 0, airEnergy = 0;

        foreach (float s in mono)
        {
            double sb = subBassFilter.Process(s); subBassEnergy += sb * sb;
            double b = bassFilter.Process(s); bassEnergy += b * b;
            double lm = lowMidsFilter.Process(s); lowMidsEnergy += lm * lm;
            double hm = highMidsFilter.Process(s); highMidsEnergy += hm * hm;
            double p = presenceFilter.Process(s); presenceEnergy += p * p;
            double a = airFilter.Process(s); airEnergy += a * a;
        }

        double totalEnergy = subBassEnergy + bassEnergy + lowMidsEnergy + highMidsEnergy + presenceEnergy + airEnergy;
        if (totalEnergy <= 0) totalEnergy = 1.0;

        double subBassPct = Math.Round(subBassEnergy / totalEnergy * 100, 1);
        double bassPct = Math.Round(bassEnergy / totalEnergy * 100, 1);
        double lowMidsPct = Math.Round(lowMidsEnergy / totalEnergy * 100, 1);
        double highMidsPct = Math.Round(highMidsEnergy / totalEnergy * 100, 1);
        double presencePct = Math.Round(presenceEnergy / totalEnergy * 100, 1);
        double airPct = Math.Round(airEnergy / totalEnergy * 100, 1);

        double spectralCentroid = (subBassPct * 40 + bassPct * 150 + lowMidsPct * 375 + highMidsPct * 1200 + presencePct * 3500 + airPct * 8000) / 100.0;

        // Simple BPM estimation via envelope peak intervals
        int? estimatedBpm = EstimateBpm(mono, sampleRate);

        double duration = (double)frameCount / sampleRate;

        string summary =
            $"Audio Analysis ({duration:F1}s): Peak {peakDbfs:F1} dBFS, RMS {rmsDbfs:F1} dBFS, Crest {crestFactor:F1} dB -> {gainCharacter}. " +
            $"EQ Distribution: Bass/Sub {bassPct + subBassPct:F0}%, Mids {lowMidsPct + highMidsPct:F0}%, Treble/Air {presencePct + airPct:F0}%. " +
            $"Centroid: {spectralCentroid:F0} Hz" + (estimatedBpm.HasValue ? $", Est BPM: {estimatedBpm.Value}" : "");

        return new(
            duration,
            Math.Round(peakDbfs, 1),
            Math.Round(rmsDbfs, 1),
            Math.Round(crestFactor, 1),
            gainCharacter,
            subBassPct,
            bassPct,
            lowMidsPct,
            highMidsPct,
            presencePct,
            airPct,
            Math.Round(spectralCentroid, 0),
            estimatedBpm,
            summary
        );
    }

    static int? EstimateBpm(float[] mono, int sampleRate)
    {
        int decimation = Math.Max(1, sampleRate / 100);
        int envLen = mono.Length / decimation;
        if (envLen < 200) return null;

        double[] env = new double[envLen];
        for (int i = 0; i < envLen; i++)
        {
            double sum = 0;
            int offset = i * decimation;
            for (int j = 0; j < decimation && offset + j < mono.Length; j++)
                sum += Math.Abs(mono[offset + j]);
            env[i] = sum / decimation;
        }

        var peakIndices = new List<int>();
        double threshold = env.Average() * 1.5;
        for (int i = 1; i < envLen - 1; i++)
        {
            if (env[i] > threshold && env[i] > env[i - 1] && env[i] > env[i + 1])
            {
                if (peakIndices.Count == 0 || (i - peakIndices[^1]) > 25)
                    peakIndices.Add(i);
            }
        }

        if (peakIndices.Count < 4) return null;
        var diffs = new List<double>();
        for (int i = 1; i < peakIndices.Count; i++)
            diffs.Add(peakIndices[i] - peakIndices[i - 1]);

        double avgDiff = diffs.Average();
        if (avgDiff <= 0) return null;
        double secondsPerBeat = avgDiff / 100.0;
        int bpm = (int)Math.Round(60.0 / secondsPerBeat);

        while (bpm < 60) bpm *= 2;
        while (bpm > 190) bpm /= 2;

        return bpm >= 40 && bpm <= 250 ? bpm : null;
    }

    struct BiquadFilter
    {
        double b0, b1, b2, a1, a2;
        double x1, x2, y1, y2;

        internal static BiquadFilter LowPass(double fs, double f0, double q)
        {
            double w0 = 2 * Math.PI * f0 / fs;
            double alpha = Math.Sin(w0) / (2 * q);
            double cos = Math.Cos(w0);
            double a0 = 1 + alpha;
            return new()
            {
                b0 = ((1 - cos) / 2) / a0,
                b1 = (1 - cos) / a0,
                b2 = ((1 - cos) / 2) / a0,
                a1 = (-2 * cos) / a0,
                a2 = (1 - alpha) / a0
            };
        }

        internal static BiquadFilter HighPass(double fs, double f0, double q)
        {
            double w0 = 2 * Math.PI * f0 / fs;
            double alpha = Math.Sin(w0) / (2 * q);
            double cos = Math.Cos(w0);
            double a0 = 1 + alpha;
            return new()
            {
                b0 = ((1 + cos) / 2) / a0,
                b1 = (-(1 + cos)) / a0,
                b2 = ((1 + cos) / 2) / a0,
                a1 = (-2 * cos) / a0,
                a2 = (1 - alpha) / a0
            };
        }

        internal static BiquadFilter BandPass(double fs, double f0, double q)
        {
            double w0 = 2 * Math.PI * f0 / fs;
            double alpha = Math.Sin(w0) / (2 * q);
            double a0 = 1 + alpha;
            return new()
            {
                b0 = (alpha) / a0,
                b1 = 0,
                b2 = (-alpha) / a0,
                a1 = (-2 * Math.Cos(w0)) / a0,
                a2 = (1 - alpha) / a0
            };
        }

        internal double Process(double inVal)
        {
            double outVal = b0 * inVal + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = inVal;
            y2 = y1; y1 = outVal;
            return outVal;
        }
    }
}
