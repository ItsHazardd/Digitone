using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace Digitone
{
    internal static class WaveformDecoder
    {

        internal static double[] Decode(string path, int count, CancellationToken cancellation)
        {
            if (!LocalFiles.IsLocal(path) || !File.Exists(path)) return null;
            cancellation.ThrowIfCancellationRequested();
            double[] native = WaveReader.Read(path, count);
            if (native != null) return native;
            string executable = Path.Combine(AudioDownloader.FfmpegFolder, "ffmpeg.exe");
            if (!File.Exists(executable)) return null;
            var args = new[] { "-v", "error", "-nostdin", "-protocol_whitelist", "file,pipe", "-i", path, "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "22050", "-f", "s16le", "pipe:1" };
            var info = new ProcessStartInfo(executable, String.Join(" ", args.Select(AudioDownloader.Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var job = new ProcessJob())
            using (var process = new Process { StartInfo = info })
            {
                process.Start(); job.Attach(process);

                using (cancellation.Register(delegate { job.Dispose(); }))
                using (var timeout = new Timer(delegate { job.Dispose(); }, null, 120000, Timeout.Infinite))
                {
                    var errors = Task.Run(delegate { return process.StandardError.ReadToEnd(); });
                    var buckets = new List<double>();
                    using (var reader = new BinaryReader(process.StandardOutput.BaseStream, Encoding.UTF8, true))
                    {
                        double peak = 0; int samples = 0;
                        try
                        {
                            while (true)
                            {
                                short sample = reader.ReadInt16(); peak = Math.Max(peak, Math.Abs(sample / 32768.0));
                                if (++samples == 1024) { buckets.Add(peak); peak = 0; samples = 0; cancellation.ThrowIfCancellationRequested(); }
                            }
                        }
                        catch (EndOfStreamException) { if (samples > 0) buckets.Add(peak); }
                    }
                    process.WaitForExit(); string diagnostic = errors.Result; cancellation.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0 || buckets.Count == 0) return null;
                    var peaks = new double[count];
                    for (int i = 0; i < buckets.Count; i++) { int bin = Math.Min(count - 1, (int)((long)i * count / buckets.Count)); peaks[bin] = Math.Max(peaks[bin], buckets[i]); }
                    return peaks;
                }
            }
        }
    }
}

