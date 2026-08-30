using System;
using System.Linq;
using System.Windows;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Threading;

namespace Digitone
{
    internal sealed class AudioOutputChoice
    {
        internal string Id,Name;
        public override string ToString(){return Name;}
    }
    internal sealed class EqualizerSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly float[] frequencies = { 60, 230, 910, 3600, 14000 };
        private BiQuadFilter[,] filters;
        private float[] gains = new float[5];
        private float[] currentGains = new float[5];
        private int appliedVersion=-1, version;
        internal bool Enabled { get; set; }
        internal EqualizerSampleProvider(ISampleProvider source){this.source=source; Rebuild();}
        public WaveFormat WaveFormat { get { return source.WaveFormat; } }
        internal void SetGains(double[] values){ gains=Enumerable.Range(0,5).Select(i=>(float)Math.Max(-12,Math.Min(12,values!=null&&i<values.Length?values[i]:0))).ToArray(); System.Threading.Interlocked.Increment(ref version); }
        private void Rebuild(){ filters=new BiQuadFilter[WaveFormat.Channels,frequencies.Length]; for(int c=0;c<WaveFormat.Channels;c++)for(int b=0;b<frequencies.Length;b++)filters[c,b]=BiQuadFilter.PeakingEQ(WaveFormat.SampleRate,frequencies[b],.8f,currentGains[b]); appliedVersion=version; }
        private void SmoothCoefficients(){bool changed=false;for(int b=0;b<5;b++){float delta=gains[b]-currentGains[b];if(Math.Abs(delta)>.001f){currentGains[b]+=Math.Sign(delta)*Math.Min(Math.Abs(delta),.05f);changed=true;}}if(changed)for(int c=0;c<WaveFormat.Channels;c++)for(int b=0;b<5;b++)filters[c,b].SetPeakingEq(WaveFormat.SampleRate,frequencies[b],.8f,currentGains[b]);}
        public int Read(float[] buffer,int offset,int count)
        {
            int read=source.Read(buffer,offset,count); if(!Enabled)return read;if(appliedVersion!=version)appliedVersion=version;
            int channels=WaveFormat.Channels; for(int n=0;n<read;n++){int channel=n%channels;if(channel==0&&((n/channels)%32)==0)SmoothCoefficients();float sample=buffer[offset+n]; for(int b=0;b<frequencies.Length;b++)sample=filters[channel,b].Transform(sample); buffer[offset+n]=Math.Max(-1f,Math.Min(1f,sample));} return read;
        }
        internal static double TestBandResponse(double tone,int band,double gain){var filters=Enumerable.Range(0,5).Select(i=>BiQuadFilter.PeakingEQ(48000,new[]{60f,230f,910f,3600f,14000f}[i],.8f,i==band?(float)gain:0)).ToArray();double input=0,output=0;for(int n=0;n<48000;n++){float x=(float)(.1*Math.Sin(2*Math.PI*tone*n/48000));float y=x;foreach(var filter in filters)y=filter.Transform(y);if(n>4000){input+=x*x;output+=y*y;}}return 10*Math.Log10(output/input);}
    }

    internal sealed class AudioPlayback : IDisposable
    {
        private MediaFoundationReader reader;
        private WasapiOut output;
        private VolumeSampleProvider volume;
        private EqualizerSampleProvider equalizer;
        private MMDevice selectedDevice;
        private bool suppressStop, muted;
        private double desiredVolume=.7;
        internal event EventHandler MediaEnded;
        internal event Action<Exception> MediaFailed;
        internal TimeSpan Position { get { return reader==null?TimeSpan.Zero:reader.CurrentTime; } set { if(reader!=null)reader.CurrentTime=value<TimeSpan.Zero?TimeSpan.Zero:value>reader.TotalTime?reader.TotalTime:value; } }
        internal TimeSpan Duration { get { return reader==null?TimeSpan.Zero:reader.TotalTime; } }
        internal double Volume { get { return desiredVolume; } set { desiredVolume=Math.Max(0,Math.Min(1,value)); ApplyVolume(); } }
        internal bool IsMuted { get { return muted; } set { muted=value; ApplyVolume(); } }
        private void ApplyVolume(){if(volume!=null)volume.Volume=muted?0:(float)desiredVolume;}
        internal void ConfigureEqualizer(bool enabled,double[] gains){if(equalizer!=null){equalizer.SetGains(gains); equalizer.Enabled=enabled;}}
        internal static AudioOutputChoice[] Outputs()
        {
            try
            {
                using(var devices=new MMDeviceEnumerator())
                {
                    using(var preferred=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))
                    {
                        var choices=new System.Collections.Generic.List<AudioOutputChoice>{new AudioOutputChoice{Id="",Name="Windows default · "+preferred.FriendlyName}};
                        foreach(var device in devices.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))if(device.ID!=preferred.ID)choices.Add(new AudioOutputChoice{Id=device.ID,Name=device.FriendlyName});
                        return choices.ToArray();
                    }
                }
            }
            catch{return new[]{new AudioOutputChoice{Id="",Name="Windows default output"}};}
        }
        internal bool Open(Uri uri,bool eqEnabled,double[] gains,string outputDeviceId=null)
        {
            Close();
            try
            {
                reader=new MediaFoundationReader(uri.LocalPath); equalizer=new EqualizerSampleProvider(reader.ToSampleProvider()); equalizer.SetGains(gains); equalizer.Enabled=eqEnabled;
                volume=new VolumeSampleProvider(equalizer); ApplyVolume();
                if(!String.IsNullOrWhiteSpace(outputDeviceId)){try{using(var devices=new MMDeviceEnumerator())selectedDevice=devices.GetDevice(outputDeviceId);}catch{selectedDevice=null;}}
                output=selectedDevice==null?new WasapiOut(AudioClientShareMode.Shared,true,50):new WasapiOut(selectedDevice,AudioClientShareMode.Shared,true,50);output.Init(new SampleToWaveProvider(volume));output.PlaybackStopped+=Stopped;return true;
            }
            catch(Exception e){Close();var failed=MediaFailed;if(failed!=null)failed(e);return false;}
        }
        internal void Play(){if(output!=null)output.Play();}
        internal void Pause(){if(output!=null)output.Pause();}
        private void Stopped(object sender,StoppedEventArgs e){if(suppressStop)return;if(e.Exception!=null){var failed=MediaFailed;if(failed!=null)failed(e.Exception);return;}if(reader!=null&&reader.Position>=reader.Length){var ended=MediaEnded;if(ended!=null)ended(this,EventArgs.Empty);}}
        internal void Close(){suppressStop=true;try{if(output!=null){output.PlaybackStopped-=Stopped;output.Stop();output.Dispose();}}finally{output=null;if(reader!=null)reader.Dispose();if(selectedDevice!=null)selectedDevice.Dispose();selectedDevice=null;reader=null;volume=null;equalizer=null;suppressStop=false;}}
        public void Dispose(){Close();}
    }
}
