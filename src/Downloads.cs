using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace Digitone
{
    internal sealed class DownloadResult
    {
        internal bool Cancelled;
        internal int ExitCode;
        internal int Errors;
        internal readonly List<string> Files = new List<string>();
    }
    // Owns the entire subprocess tree, including conversion and JavaScript helpers.
    internal sealed class ProcessJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int type, ref ExtendedLimits info, uint length);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private IntPtr handle;
        public ProcessJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf(limits))) { Dispose(); throw new InvalidOperationException("Windows could not create a cancellable download process."); }
        }
        internal void Attach(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) { try { process.Kill(); } catch { } throw new InvalidOperationException("Windows could not isolate the download process. Download stopped."); } }
        public void Dispose() { IntPtr value = System.Threading.Interlocked.Exchange(ref handle, IntPtr.Zero); if (value != IntPtr.Zero) CloseHandle(value); }
    }
    internal sealed class AudioDownloader
    {
        private readonly object gate = new object();
        private ProcessJob job;
        private bool cancelled;
        internal static string ToolRoot { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools"); } }
        internal static string FfmpegFolder { get { return Path.Combine(ToolRoot, "ffmpeg", "bin"); } }
        internal static bool ToolsReady { get { return new[] { Path.Combine(ToolRoot, "yt-dlp.exe"), Path.Combine(ToolRoot, "deno.exe"), Path.Combine(FfmpegFolder, "ffmpeg.exe"), Path.Combine(FfmpegFolder, "ffprobe.exe") }.All(p => LocalFiles.IsLocal(p) && File.Exists(p)); } }
        internal static bool ValidUrl(string input)
        {
            Uri uri;
            return !String.IsNullOrWhiteSpace(input) && input.Length <= 4096 && !input.Any(Char.IsControl) && Uri.TryCreate(input, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && !String.IsNullOrEmpty(uri.Host) && String.IsNullOrEmpty(uri.UserInfo);
        }
        internal static string PlaylistFolder(string parent, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 64 || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.Any(Char.IsControl)) throw new ArgumentException("Enter a playlist name without slashes or Windows filename characters.");
            string stem = name.Split('.')[0];
            if (System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new ArgumentException("That name is reserved by Windows. Choose another playlist name.");
            if (!LocalFiles.IsLocal(parent) || !Directory.Exists(parent)) throw new ArgumentException("Choose an existing local parent folder.");
            string path = Path.GetFullPath(Path.Combine(parent, name));
            if (!LocalFiles.IsLocal(path)) throw new ArgumentException("The playlist folder cannot be a link or network location.");
            Directory.CreateDirectory(path); return path;
        }
        // Windows CRT quoting, not shell quoting. No shell or interpolated command is used.
        internal static string Quote(string input)
        {
            var output = new StringBuilder("\""); int slashes = 0;
            foreach (char ch in input) { if (ch == '\\') { slashes++; continue; } if (ch == '"') { output.Append('\\', slashes * 2 + 1); output.Append(ch); } else { output.Append('\\', slashes); output.Append(ch); } slashes = 0; }
            output.Append('\\', slashes * 2); output.Append('"'); return output.ToString();
        }
        internal static List<string> Arguments(string url, string folder, bool playlist = false)
        {
            var args = new List<string> {
                "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-cookies", "--no-cookies-from-browser",
                "--no-remote-components", "--no-js-runtimes", "--js-runtimes", "deno:" + Path.Combine(ToolRoot, "deno.exe"),
                "--proxy", "",
                "--socket-timeout", "20", "--retries", "2", "--fragment-retries", "2", "--no-overwrites",
                "--windows-filenames", "--newline", "--no-colors", "--encoding", "utf-8", "--progress", "--progress-delta", "0.25",
                "--progress-template", "download:STILL_PROGRESS:%(progress._percent_str)s",
                "--print", "after_move:STILL_FILE:%(filepath)j", "--no-simulate",
                "--extract-audio", "--audio-format", "mp3", "--audio-quality", "0", "--embed-metadata",
                "--ffmpeg-location", FfmpegFolder, "--paths", folder,
                "--output", playlist ? "%(playlist_index)03d - %(title).120B.%(ext)s" : "%(title).140B.%(ext)s"
            };
            if (playlist) args.AddRange(new[] { "--yes-playlist", "--ignore-errors" });
            else args.AddRange(new[] { "--no-playlist", "--playlist-items", "1", "--max-downloads", "1" });
            args.Add("--"); args.Add(url); return args;
        }
        internal static string CompletedFile(string line, string folder)
        {
            if (!line.StartsWith("STILL_FILE:", StringComparison.Ordinal)) return null;
            try
            {
                string path = new JavaScriptSerializer().Deserialize<string>(line.Substring(11));
                if (!LocalFiles.IsLocal(path)) return null;
                path = Path.GetFullPath(path);
                string root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && String.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
            }
            catch { return null; }
        }
        internal void Cancel() { lock (gate) { cancelled = true; if (job != null) { job.Dispose(); job = null; } } }
        internal Task<DownloadResult> Run(string url, string folder, Action<string> output, bool playlist = false)
        {
            if (!ValidUrl(url)) throw new ArgumentException("Paste a public HTTP or HTTPS link without a username or password.");
            if (!LocalFiles.IsLocal(folder) || !Directory.Exists(folder)) throw new ArgumentException("Choose an existing local download folder. Network and linked folders are not supported.");
            if (!ToolsReady) throw new InvalidOperationException("Download tools are missing. Run Install-DownloadTools.ps1 once from the DAP folder.");
            return Task.Run(delegate
            {
                var result = new DownloadResult();
                string staging = Path.Combine(folder, ".digitone-download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
                var info = new ProcessStartInfo(Path.Combine(ToolRoot, "yt-dlp.exe"), String.Join(" ", Arguments(url, staging, playlist).Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = ToolRoot };
                using (var process = new Process { StartInfo = info })
                {
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data == null) return; string file = CompletedFile(e.Data, staging); if (file != null) { lock (result.Files) if (!result.Files.Contains(file)) result.Files.Add(file); } output(e.Data); };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) { if (e.Data.StartsWith("ERROR:", StringComparison.Ordinal)) result.Errors++; output(e.Data); } };
                    try
                    {
                        lock (gate)
                        {
                            if (cancelled) { result.Cancelled = true; return result; }
                            job = new ProcessJob();
                            process.Start(); job.Attach(process); process.StandardInput.Close();
                        }
                        process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                        result.ExitCode = process.ExitCode;
                    }
                    finally { lock (gate) { result.Cancelled = cancelled; if (job != null) { job.Dispose(); job = null; } } }
                }
                for (int i = 0; i < result.Files.Count; i++)
                {
                    string source = result.Files[i], destination = Path.Combine(folder, Path.GetFileName(source)); int suffix = 2;
                    while (File.Exists(destination)) destination = Path.Combine(folder, Path.GetFileNameWithoutExtension(source) + " (" + suffix++ + ")" + Path.GetExtension(source));
                    File.Move(source, destination); result.Files[i] = destination;
                }
                if (!Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
                return result;
            });
        }
    }
    internal sealed class DownloadController
    {
        private readonly PlayerApp player;
        private readonly Action save;
        private AudioDownloader downloader;
        internal bool Busy { get; private set; }
        internal Task ActiveTask { get; private set; }
        internal DownloadController(PlayerApp player, Action save)
        {
            this.player = player; this.save = save;
            if (!String.IsNullOrEmpty(player.Data.DownloadFolder) && LocalFiles.IsLocal(player.Data.DownloadFolder)) player.Find<TextBlock>("DownloadFolder").Text = player.Data.DownloadFolder;
            player.Find<Button>("DownloadBrowse").Click += delegate
            {
                using (var dialog = new Forms.FolderBrowserDialog { Description = "Choose where Digitone should save downloaded MP3 files.", ShowNewFolderButton = true })
                    if (dialog.ShowDialog() == Forms.DialogResult.OK)
                    {
                        if (!LocalFiles.IsLocal(dialog.SelectedPath)) { Status("Choose a local folder, not a network, cloud placeholder, or linked folder."); return; }
                        player.Data.DownloadFolder = dialog.SelectedPath; player.Find<TextBlock>("DownloadFolder").Text = dialog.SelectedPath; save();
                    }
            };
            player.Find<Button>("DownloadStart").Click += delegate { ActiveTask = Start(); };
            player.Find<Button>("DownloadCancel").Click += delegate { Cancel(); };
            RefreshTools();
        }
        internal void RefreshTools() { player.Find<TextBlock>("DownloadToolsStatus").Text = AudioDownloader.ToolsReady ? "yt-dlp + FFmpeg ready" : "Download tools missing · run Install-DownloadTools.ps1."; }
        private void Status(string text) { player.Find<TextBlock>("DownloadStatus").Text = text; }
        internal void Cancel() { if (downloader != null && Busy) { Status("Cancelling download and conversion…"); downloader.Cancel(); player.Find<Button>("DownloadCancel").IsEnabled = false; } }
        private void SetBusy(bool busy)
        {
            Busy = busy;
            foreach (string name in new[] { "DownloadStart", "DownloadBrowse", "AddFolderButton", "AddFilesButton" }) player.Find<Button>(name).IsEnabled = !busy;
            player.Find<TextBox>("DownloadUrl").IsEnabled = !busy;
            player.Find<CheckBox>("DownloadPlaylist").IsEnabled = !busy;
            player.Find<TextBox>("DownloadPlaylistName").IsEnabled = !busy;
            player.Find<Button>("DownloadCancel").IsEnabled = busy;
        }
        private void Output(string line)
        {
            player.Window.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!Busy) return;
                if (line.StartsWith("STILL_PROGRESS:", StringComparison.Ordinal))
                {
                    double percent;
                    if (Double.TryParse(line.Substring(15).Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out percent)) { player.Find<ProgressBar>("DownloadProgress").IsIndeterminate = false; player.Find<ProgressBar>("DownloadProgress").Value = Math.Max(0, Math.Min(100, percent)); Status(percent >= 100 ? "Audio received. Finishing MP3 conversion…" : "Downloading audio · " + percent.ToString("0", CultureInfo.InvariantCulture) + "%"); }
                    return;
                }
                if (line.StartsWith("STILL_FILE:", StringComparison.Ordinal)) return;
                var log = player.Find<TextBox>("DownloadLog");
                string next = log.Text + (line.Length > 1500 ? line.Substring(0, 1500) : line) + Environment.NewLine;
                log.Text = next.Length > 10000 ? next.Substring(next.Length - 10000) : next; log.ScrollToEnd();
            }));
        }
        private async Task Start()
        {
            if (Busy) return;
            if (player.IsImporting) { Status("Wait for the current library import to finish first."); return; }
            string url = player.Find<TextBox>("DownloadUrl").Text.Trim(); string folder = player.Data.DownloadFolder;
            if (!AudioDownloader.ValidUrl(url)) { Status("Paste an HTTP or HTTPS link first."); return; }
            if (!LocalFiles.IsLocal(folder) || !Directory.Exists(folder)) { Status("Choose an existing local folder first."); return; }
            bool playlist = player.Find<CheckBox>("DownloadPlaylist").IsChecked == true;
            string playlistName = player.Find<TextBox>("DownloadPlaylistName").Text.Trim();
            if (playlist) { try { folder = AudioDownloader.PlaylistFolder(folder, playlistName); } catch (Exception e) { Status(e.Message); return; } }
            SetBusy(true); player.Find<TextBox>("DownloadLog").Clear(); player.Find<ProgressBar>("DownloadProgress").Value = 0; player.Find<ProgressBar>("DownloadProgress").IsIndeterminate = true; Status("Connecting to the source you chose…");
            downloader = new AudioDownloader();
            try
            {
                DownloadResult result = await downloader.Run(url, folder, Output, playlist);
                if (result.Files.Count > 0)
                {
                    await player.Import(result.Files.ToArray(), true);
                    if (playlist)
                    {
                        player.CreatePlaylist(playlistName, result.Files.Select(p => player.Data.Tracks.FirstOrDefault(t => String.Equals(t.Path, p, StringComparison.OrdinalIgnoreCase))).Where(t => t != null));
                        player.ShowDownloads(true);
                    }
                    player.Find<ProgressBar>("DownloadProgress").Value = 100;
                    Status(result.Files.Count + " tracks saved and added to All music." + (playlist ? " Folder: " + folder + "." : "") + (result.Cancelled ? " Cancelled; finished tracks were kept." : result.Errors > 0 || result.ExitCode != 0 && result.ExitCode != 101 ? " Some entries failed; see details." : ""));
                }
                else Status(result.Cancelled ? "Cancelled. Unfinished files remain in the download staging folder." : "Download could not finish. See details below.");
            }
            catch (Exception e) { Status("Download failed: " + e.Message); }
            finally { player.Find<ProgressBar>("DownloadProgress").IsIndeterminate = false; SetBusy(false); downloader = null; }
        }
    }
}
