using System;

namespace Digitone
{
    internal static class FrequencyBands
    {
        internal const int SampleRate = 48000, WindowSize = 4096;
        private static readonly double[] Window = new double[WindowSize];
        private static readonly double WindowPower;
        static FrequencyBands()
        {
            for(int i=0;i<WindowSize;i++) { Window[i]=.5-.5*Math.Cos(2*Math.PI*i/(WindowSize-1)); WindowPower+=Window[i]*Window[i]; }
        }
        internal static double[] Analyze(double[] samples)
        {
            if(samples == null || samples.Length != WindowSize) throw new ArgumentException("A complete analysis window is required.");
            int n=WindowSize; var real=new double[n]; var imaginary=new double[n];
            for(int i=0;i<n;i++) real[i]=samples[i]*Window[i];
            for(int i=1,j=0;i<n;i++) { int bit=n>>1; for(; (j&bit)!=0; bit>>=1) j^=bit; j^=bit; if(i<j) { double temp=real[i]; real[i]=real[j]; real[j]=temp; } }
            for(int length=2;length<=n;length<<=1)
            {
                double stepReal=Math.Cos(-2*Math.PI/length), stepImaginary=Math.Sin(-2*Math.PI/length);
                for(int offset=0;offset<n;offset+=length)
                {
                    double wr=1,wi=0;
                    for(int j=0;j<length/2;j++)
                    {
                        int a=offset+j,b=a+length/2;
                        double tr=real[b]*wr-imaginary[b]*wi,ti=real[b]*wi+imaginary[b]*wr;
                        real[b]=real[a]-tr; imaginary[b]=imaginary[a]-ti; real[a]+=tr; imaginary[a]+=ti;
                        double next=wr*stepReal-wi*stepImaginary; wi=wr*stepImaginary+wi*stepReal; wr=next;
                    }
                }
            }
            var bands=new double[3];
            for(int bin=1;bin<n/2;bin++)
            {
                double hz=(double)bin*SampleRate/n; if(hz<20 || hz>20000) continue;
                int band=hz<250?0:hz<4000?1:2;
                bands[band]+=2*(real[bin]*real[bin]+imaginary[bin]*imaginary[bin])/(n*WindowPower);
            }
            for(int i=0;i<3;i++) bands[i]=Math.Sqrt(bands[i]);
            return bands;
        }
    }
}
