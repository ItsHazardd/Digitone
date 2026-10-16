using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Digitone
{
    internal sealed class LiveWaveSource : IDisposable
    {
        private sealed class Block { internal double Start; internal short[] Samples; }
        private readonly string path;
        private readonly object gate = new object();
        private readonly Dictionary<int, Block> cache = new Dictionary<int, Block>();
        private readonly HashSet<int> pending = new HashSet<int>(), failed = new HashSet<int>();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool disposed;
        private int wanted;
        private int lastBandFrame = -1;
        private double[] lastBands;
        internal bool Failed { get { lock (gate) return failed.Contains(wanted); } }
        internal LiveWaveSource(string path) { this.path = path; }
        internal double[] Sample(double position)
        {
            if (Double.IsNaN(position) || Double.IsInfinity(position) || position < 0) return null;
            int index = (int)(position / 4);
            lock (gate) { if (disposed) return null; wanted = index; }
            Ensure(index); if (position - index * 4 > 2.5) Ensure(index + 1);
            lock (gate)
            {
                Block block; if (!cache.TryGetValue(index, out block)) return null;
                var wave = new double[384]; int first = (int)((position - block.Start) * FrequencyBands.SampleRate) - wave.Length*6;
                double filtered = 0;
                for (int i = 0; i < wave.Length; i++) { double sample=0; for(int j=0;j<6;j++) { int at=first+i*6+j; if(at>=0 && at<block.Samples.Length) sample+=block.Samples[at]/(32768.0*6); } filtered += .55 * (sample - filtered); wave[i] = filtered; }
                return wave;
            }
        }
        internal double[] SampleBands(double position)
        {
            if(Double.IsNaN(position) || Double.IsInfinity(position) || position<0) return null;
            int index=(int)(position/4), frame=(int)(position*30);
            double[] samples;
            lock(gate)
            {
                if(disposed) return null;
                if(frame==lastBandFrame && lastBands!=null) return lastBands;
                Block block; if(!cache.TryGetValue(index,out block)) return null;
                samples=new double[FrequencyBands.WindowSize];
                int first=(int)((position-block.Start)*FrequencyBands.SampleRate)-samples.Length;
                for(int i=0;i<samples.Length;i++) { int at=first+i; if(at>=0 && at<block.Samples.Length) samples[i]=block.Samples[at]/32768.0; }
            }
            lastBands=FrequencyBands.Analyze(samples); lastBandFrame=frame; return lastBands;
        }
        // The low end only (a 120 Hz low-pass over the real 48 kHz audio): loudness over the last ~21 ms and ~85 ms. Drives kicks and sub-bass.
        internal double[] SampleLow(double position)
        {
            if (Double.IsNaN(position) || Double.IsInfinity(position) || position < 0) return null;
            int index = (int)(position / 4); short[] data; int end;
            lock (gate) { if (disposed) return null; wanted = index; }
            Ensure(index); if (position - index * 4 > 2.5) Ensure(index + 1); // decode the audio around here if it is not ready yet
            lock (gate) { if (disposed) return null; Block block; if (!cache.TryGetValue(index, out block)) return null; data = block.Samples; end = (int)((position - block.Start) * FrequencyBands.SampleRate); }
            const int window = 4096, recent = 1024, settle = 1024; int start = end - window - settle;
            double w0 = 2 * Math.PI * 120 / FrequencyBands.SampleRate, cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * .7071), a0 = 1 + alpha;
            double b0 = (1 - cos) / 2 / a0, b1 = (1 - cos) / a0, b2 = b0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0, longSum = 0, shortSum = 0;
            for (int i = start; i < end; i++)
            {
                double x = i >= 0 && i < data.Length ? data[i] / 32768.0 : 0, y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y;
                if (i >= end - window) { longSum += y * y; if (i >= end - recent) shortSum += y * y; }
            }
            return new[] { Math.Sqrt(shortSum / recent), Math.Sqrt(longSum / window) };
        }
        private void Ensure(int index)
        {
            lock (gate) { if (disposed || cache.ContainsKey(index) || pending.Contains(index) || failed.Contains(index) || pending.Count >= 2) return; pending.Add(index); }
            Task.Run(delegate
            {
                try
                {
                    Block block = Decode(index, cancellation.Token);
                    lock (gate) { if (!disposed && Math.Abs(index - wanted) <= 1) { cache[index] = block; foreach (int old in cache.Keys.Where(k => Math.Abs(k - wanted) > 1).ToArray()) cache.Remove(old); } }
                }
                catch (OperationCanceledException) { }
                catch { lock (gate) if (!disposed) failed.Add(index); }
                finally { lock (gate) { pending.Remove(index); if (disposed && pending.Count == 0) cancellation.Dispose(); } }
            });
        }
        private Block Decode(int index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!LocalFiles.IsLocal(path) || !File.Exists(path)) throw new IOException("Audio file unavailable.");
            double start = Math.Max(0, index * 4 - .1);
            var args = new[] { "-v", "error", "-nostdin", "-ss", start.ToString(System.Globalization.CultureInfo.InvariantCulture), "-protocol_whitelist", "file,pipe", "-i", path, "-t", "4.2", "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "48000", "-f", "s16le", "pipe:1" };
            var info = new ProcessStartInfo(Path.Combine(AudioDownloader.FfmpegFolder, "ffmpeg.exe"), String.Join(" ", args.Select(AudioDownloader.Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var job = new ProcessJob())
            using (var process = Process.Start(info))
            {
                job.Attach(process);
                using (token.Register(job.Dispose))
                using (var timeout = new Timer(delegate { job.Dispose(); }, null, 20000, Timeout.Infinite))
                {
                    var error = Task.Run(delegate { return process.StandardError.ReadToEnd(); });
                    using (var memory = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192]; int read;
                        while ((read = process.StandardOutput.BaseStream.Read(buffer, 0, buffer.Length)) > 0) { if (memory.Length + read > 500000) throw new IOException("Unexpected PCM output size."); memory.Write(buffer, 0, read); }
                        process.WaitForExit(); string details = error.Result; token.ThrowIfCancellationRequested();
                        if (process.ExitCode != 0) throw new IOException(details);
                        byte[] bytes = memory.ToArray(); var samples = new short[bytes.Length / 2]; Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2); return new Block { Start = start, Samples = samples };
                    }
                }
            }
        }
        public void Dispose() { lock (gate) { if (disposed) return; disposed = true; cancellation.Cancel(); cache.Clear(); if (pending.Count == 0) cancellation.Dispose(); } }
    }
}
