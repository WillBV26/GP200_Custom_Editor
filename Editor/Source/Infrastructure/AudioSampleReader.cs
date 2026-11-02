using NAudio.Wave;

namespace Gp200.Replica;

internal sealed class AudioSampleReader
{
    static readonly Guid IeeeFloatSubtype=new("00000003-0000-0010-8000-00aa00389b71");
    static readonly Guid PcmSubtype=new("00000001-0000-0010-8000-00aa00389b71");
    readonly bool floatingPoint;
    readonly bool pcm;
    readonly int bitsPerSample;

    internal int Channels { get; }
    internal int FrameSize { get; }

    internal AudioSampleReader(WaveFormat format)
    {
        Channels=format.Channels;
        bitsPerSample=format.BitsPerSample;
        floatingPoint=format.Encoding==WaveFormatEncoding.IeeeFloat||
            format is WaveFormatExtensible extensible&&extensible.SubFormat==IeeeFloatSubtype;
        pcm=format.Encoding==WaveFormatEncoding.Pcm||
            format is WaveFormatExtensible pcmExtensible&&pcmExtensible.SubFormat==PcmSubtype;
        if(Channels<=0||!((floatingPoint&&bitsPerSample==32)||(pcm&&bitsPerSample is 8 or 16 or 24 or 32)))
            throw new NotSupportedException(UiText.T("Input format is not supported by this tuner."));
        FrameSize=checked(bitsPerSample/8*Channels);
    }

    internal bool TryReadSample(byte[] buffer,int offset,out float value)
    {
        if(floatingPoint)
        {
            value=BitConverter.ToSingle(buffer,offset);
            return float.IsFinite(value);
        }

        value=bitsPerSample switch
        {
            8=>(buffer[offset]-128)/128f,
            16=>BitConverter.ToInt16(buffer,offset)/32768f,
            24=>ReadInt24(buffer,offset)/8388608f,
            32=>BitConverter.ToInt32(buffer,offset)/2147483648f,
            _=>0
        };
        return pcm;
    }

    static int ReadInt24(byte[] buffer,int offset)
    {
        int value=buffer[offset]|buffer[offset+1]<<8|buffer[offset+2]<<16;
        return (value&0x800000)!=0?value|unchecked((int)0xFF000000):value;
    }
}
