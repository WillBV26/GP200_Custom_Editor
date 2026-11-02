namespace Gp200.Replica;

internal readonly record struct LoudnessSnapshot(double? MomentaryLufs,double? IntegratedLufs,double? SamplePeakDbfs);

internal sealed class LoudnessMeter
{
    const double LoudnessOffset=-0.691;
    const double AbsoluteGateLufs=-70;
    readonly int channels;
    readonly int subBlockFrames;
    readonly double[] sumSquares;
    readonly Biquad[] highShelf;
    readonly Biquad[] highPass;
    readonly Queue<double[]> subBlocks=[];
    readonly List<double> gatedBlocks=[];
    int framesInSubBlock;
    double? momentaryLufs;
    double samplePeak;

    internal LoudnessMeter(int sampleRate,int channels)
    {
        if(sampleRate is <8000 or >192000)throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if(channels is <1 or >2)throw new ArgumentOutOfRangeException(nameof(channels));
        this.channels=channels;
        subBlockFrames=Math.Max(1,(int)Math.Round(sampleRate*0.1));
        sumSquares=new double[channels];
        highShelf=Enumerable.Range(0,channels).Select(_=>Biquad.HighShelf(sampleRate,1681.974450955533,3.99984385397,0.7071752369554196)).ToArray();
        highPass=Enumerable.Range(0,channels).Select(_=>Biquad.HighPass(sampleRate,38.13547087602444,0.5003270373238773)).ToArray();
    }

    internal void ProcessFrame(ReadOnlySpan<float> frame)
    {
        if(frame.Length!=channels)throw new ArgumentException("Frame channel count does not match the meter.",nameof(frame));
        for(int channel=0;channel<channels;channel++)
            if(!float.IsFinite(frame[channel]))return;

        for(int channel=0;channel<channels;channel++)
        {
            float input=frame[channel];
            samplePeak=Math.Max(samplePeak,Math.Abs(input));
            double filtered=highPass[channel].Process(highShelf[channel].Process(input));
            sumSquares[channel]+=filtered*filtered;
        }
        if(++framesInSubBlock<subBlockFrames)return;

        var energy=new double[channels];
        for(int channel=0;channel<channels;channel++)
        {
            energy[channel]=sumSquares[channel]/framesInSubBlock;
            sumSquares[channel]=0;
        }
        framesInSubBlock=0;
        subBlocks.Enqueue(energy);
        if(subBlocks.Count>4)subBlocks.Dequeue();
        if(subBlocks.Count<4)return;

        var windowEnergy=new double[channels];
        foreach(double[] block in subBlocks)
            for(int channel=0;channel<channels;channel++)windowEnergy[channel]+=block[channel]/4;
        double weightedEnergy=windowEnergy.Sum();
        momentaryLufs=ToLufs(weightedEnergy);
        if(momentaryLufs>=AbsoluteGateLufs)gatedBlocks.Add(weightedEnergy);
    }

    internal void ProcessFrame(float[] frame)=>ProcessFrame(frame.AsSpan());

    internal LoudnessSnapshot Snapshot()
    {
        double? integrated=null;
        if(gatedBlocks.Count>0)
        {
            double absoluteMean=gatedBlocks.Average();
            double relativeGate=absoluteMean*0.1;
            double acceptedTotal=0;
            int acceptedCount=0;
            foreach(double energy in gatedBlocks)
            {
                if(energy<relativeGate)continue;
                acceptedTotal+=energy;
                acceptedCount++;
            }
            if(acceptedCount>0)integrated=ToLufs(acceptedTotal/acceptedCount);
        }

        double? peak=samplePeak>0?20*Math.Log10(samplePeak):null;
        return new(momentaryLufs,integrated,peak);
    }

    static double? ToLufs(double energy)=>energy>0?-0.691+10*Math.Log10(energy):null;

    struct Biquad(double b0,double b1,double b2,double a1,double a2)
    {
        double x1,x2,y1,y2;

        internal double Process(double input)
        {
            double output=b0*input+b1*x1+b2*x2-a1*y1-a2*y2;
            x2=x1;x1=input;y2=y1;y1=output;
            return output;
        }

        internal static Biquad HighPass(int sampleRate,double frequency,double q)
        {
            double k=Math.Tan(Math.PI*frequency/sampleRate);
            double denominator=1+k/q+k*k;
            return new(1,-2,1,
                2*(k*k-1)/denominator,(1-k/q+k*k)/denominator);
        }

        internal static Biquad HighShelf(int sampleRate,double frequency,double gainDb,double q)
        {
            // DeMan's BS.1770 form reproduces the specified K-weighting response.
            double k=Math.Tan(Math.PI*frequency/sampleRate);
            double highGain=Math.Pow(10,gainDb/20);
            double bandGain=Math.Pow(highGain,0.499666774155);
            double denominator=1+k/q+k*k;
            double b0=(highGain+bandGain*k/q+k*k)/denominator;
            double b1=2*(k*k-highGain)/denominator;
            double b2=(highGain-bandGain*k/q+k*k)/denominator;
            double a1=2*(k*k-1)/denominator;
            double a2=(1-k/q+k*k)/denominator;
            return new(b0,b1,b2,a1,a2);
        }
    }
}
