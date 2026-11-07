using NAudio.Wave;

namespace Gp200.Replica;

internal sealed class AudioSampleReader
{
    static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");
    readonly bool floatingPoint;
    readonly bool pcm;
    readonly int bitsPerSample;

    internal int Channels { get; }
    internal int FrameSize { get; }
    internal int SampleSize => Math.Max(1, bitsPerSample / 8);

    internal AudioSampleReader(WaveFormat format)
    {
        Channels = Math.Max(1, format.Channels);
        bitsPerSample = format.BitsPerSample > 0 ? format.BitsPerSample : 16;

        floatingPoint = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubtype);
        pcm = format.Encoding == WaveFormatEncoding.Pcm ||
            (format is WaveFormatExtensible pcmExtensible && pcmExtensible.SubFormat == PcmSubtype);

        // Resilient fallback for any Windows WASAPI/WinMM driver format
        if (!floatingPoint && !pcm)
        {
            if (bitsPerSample == 32) floatingPoint = true;
            else pcm = true;
        }

        FrameSize = checked((bitsPerSample / 8) * Channels);
        if (FrameSize <= 0) FrameSize = 4;
    }

    internal bool TryReadSample(byte[] buffer, int offset, out float value)
    {
        if (offset < 0 || offset > buffer.Length - SampleSize)
        {
            value = 0f;
            return false;
        }

        if (floatingPoint)
        {
            value = BitConverter.ToSingle(buffer, offset);
            return float.IsFinite(value);
        }

        value = bitsPerSample switch
        {
            8 => (buffer[offset] - 128) / 128f,
            16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            24 => ReadInt24(buffer, offset) / 8388608f,
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0f
        };
        return true;
    }

    static int ReadInt24(byte[] buffer, int offset)
    {
        int value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }
}
