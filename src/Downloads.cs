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
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

namespace Digitone
{
    internal sealed class DownloadResult
    {
        internal bool Cancelled;
        internal int ExitCode;
        internal int Errors;
        internal readonly List<string> Files = new List<string>();
        internal readonly Dictionary<string, SongHints> Hints = new Dictionary<string, SongHints>(StringComparer.OrdinalIgnoreCase);
        internal bool NeedsSignIn; // YouTube asked for a signed-in account (age-restricted, or "confirm you're not a bot")
        internal readonly List<string> SignInIds = new List<string>();
    }

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
            var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000;
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
        // Deno and other tools otherwise cache in the Windows profile; keep it inside userdata so nothing is written outside Digitone's folder.
        internal static ProcessStartInfo KeepCachesPortable(ProcessStartInfo info)
        {
            string cache = PortableFolders.Current.Cache, temp = Path.Combine(cache, "temp");
            Directory.CreateDirectory(temp);
            info.EnvironmentVariables["DENO_DIR"] = Path.Combine(cache, "deno");
            info.EnvironmentVariables["TEMP"] = temp; info.EnvironmentVariables["TMP"] = temp;
            info.EnvironmentVariables["XDG_CACHE_HOME"] = cache;
            return info;
        }
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

        internal static string Quote(string input)
        {
            var output = new StringBuilder("\""); int slashes = 0;
            foreach (char ch in input) { if (ch == '\\') { slashes++; continue; } if (ch == '"') { output.Append('\\', slashes * 2 + 1); output.Append(ch); } else { output.Append('\\', slashes); output.Append(ch); } slashes = 0; }
            output.Append('\\', slashes * 2); output.Append('"'); return output.ToString();
        }
        internal static List<string> Arguments(string url, string folder, bool playlist = false, string signIn = null)
        {
            var args = new List<string> {
                "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-cookies", "--no-cookies-from-browser",
                "--no-remote-components", "--no-js-runtimes", "--js-runtimes", "deno:" + Path.Combine(ToolRoot, "deno.exe"),
                "--proxy", "",
                "--socket-timeout", "20", "--retries", "2", "--fragment-retries", "2", "--no-overwrites",
                "--windows-filenames", "--newline", "--no-colors", "--encoding", "utf-8", "--progress", "--progress-delta", "0.25",
                "--progress-template", "download:STILL_PROGRESS:%(progress._percent_str)s",
                "--print", "after_move:STILL_FILE:%(filepath)j",
                "--print", "after_move:STILL_META:%(.{filepath,title,uploader,channel,artist,track,album,release_year,description,duration,id})j", "--no-simulate",
                "--extract-audio", "--audio-format", "mp3", "--audio-quality", "0", "--embed-metadata",
                "--ffmpeg-location", FfmpegFolder, "--paths", folder,
                "--output", playlist ? "%(playlist_index)03d - %(title).120B.%(ext)s" : "%(title).140B.%(ext)s"
            };
            if (playlist) args.AddRange(new[] { "--yes-playlist", "--ignore-errors" });
            else args.AddRange(new[] { "--no-playlist", "--playlist-items", "1", "--max-downloads", "1" });
            // Age-restricted songs: use the YouTube sign-in from Digitone's own viewer, written to a temporary file for this one download.
            if (signIn != null) { int at = args.IndexOf("--no-cookies"); args[at] = "--cookies"; args.Insert(at + 1, signIn); }
            args.Add("--"); args.Add(url); return args;
        }
        // YouTube refuses some videos without a signed-in account.
        internal static bool NeedsSignIn(string line)
        {
            if (line == null || !line.StartsWith("ERROR:", StringComparison.Ordinal)) return false;
            return line.IndexOf("Sign in to confirm", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("age-restricted", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("inappropriate for some users", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        internal static string SignInVideo(string line)
        {
            var match = System.Text.RegularExpressions.Regex.Match(line ?? "", @"^ERROR: \[youtube[^\]]*\] ([A-Za-z0-9_-]{11}):");
            return match.Success ? match.Groups[1].Value : null;
        }
        // Album and playlist links (not a song inside one): music.youtube.com/browse/MPREb_…, …/playlist?list=…
        internal static bool IsCollectionLink(string url)
        {
            Uri uri; if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            string host = uri.DnsSafeHost.TrimEnd('.').ToLowerInvariant();
            if (host != "youtube.com" && !host.EndsWith(".youtube.com")) return false;
            string path = uri.AbsolutePath.TrimEnd('/'); string query = uri.Query;
            if (path.StartsWith("/browse/MPREb_", StringComparison.Ordinal) || path.StartsWith("/browse/VL", StringComparison.Ordinal)) return true;
            return path.Equals("/playlist", StringComparison.OrdinalIgnoreCase) && query.IndexOf("list=", StringComparison.Ordinal) >= 0 && query.IndexOf("v=", StringComparison.Ordinal) < 0;
        }
        // Title and song count of an album or playlist link, read in about two seconds without downloading anything.
        internal static Task<KeyValuePair<string, int>> PeekCollection(string url)
        {
            return Task.Run(delegate
            {
                var info = new ProcessStartInfo(Path.Combine(ToolRoot, "yt-dlp.exe"), String.Join(" ", new[] { "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-cookies", "--no-warnings", "--flat-playlist", "--yes-playlist", "--playlist-items", "1", "--print", "playlist:STILL_LIST:%(title)s\t%(playlist_count)s", "--", url }.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = ToolRoot };
                KeepCachesPortable(info);
                try
                {
                    using (var process = Process.Start(info))
                    {
                        var error = process.StandardError.ReadToEndAsync(); string output = process.StandardOutput.ReadToEnd();
                        if (!process.WaitForExit(20000)) { try { process.Kill(); } catch { } }
                        foreach (string line in output.Split('\n'))
                        {
                            if (!line.StartsWith("STILL_LIST:", StringComparison.Ordinal)) continue;
                            var parts = line.Substring(11).TrimEnd('\r').Split('\t'); int count; Int32.TryParse(parts.Length > 1 ? parts[1] : "", out count);
                            string title = parts[0].Trim(); if (title.StartsWith("Album - ", StringComparison.Ordinal)) title = title.Substring(8);
                            return new KeyValuePair<string, int>(title, count);
                        }
                    }
                }
                catch { }
                return new KeyValuePair<string, int>(null, 0);
            });
        }
        // A playlist/folder name made from an album title that Windows accepts.
        internal static string FolderName(string title)
        {
            var clean = new StringBuilder(); foreach (char ch in title ?? "") clean.Append(Path.GetInvalidFileNameChars().Contains(ch) || Char.IsControl(ch) ? ' ' : ch);
            string name = System.Text.RegularExpressions.Regex.Replace(clean.ToString(), @"\s+", " ").Trim().TrimEnd('.', ' ');
            if (name.Length > 60) name = name.Substring(0, 60).TrimEnd('.', ' ');
            return name.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(name.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "YouTube album" : name;
        }
        // What YouTube said about a finished file, used to recognize the song afterwards.
        internal static string CompletedHints(string line, string folder, out SongHints hints)
        {
            hints = null;
            if (!line.StartsWith("STILL_META:", StringComparison.Ordinal)) return null;
            try
            {
                var values = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<Dictionary<string, object>>(line.Substring(11));
                Func<string, string> text = delegate(string key) { object value; return values.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null; };
                string path = ValidFile(text("filepath"), folder); if (path == null) return null;
                double duration; Double.TryParse(text("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                string description = text("description"); if (description != null && description.Length > 5000) description = description.Substring(0, 5000);
                hints = new SongHints { Title = text("title"), Uploader = text("uploader"), Channel = text("channel"), Artist = text("artist"), Track = text("track"), Album = text("album"), ReleaseYear = text("release_year"), Description = description, Duration = duration, VideoId = text("id") };
                return path;
            }
            catch { return null; }
        }
        internal static string CompletedFile(string line, string folder)
        {
            if (!line.StartsWith("STILL_FILE:", StringComparison.Ordinal)) return null;
            try { return ValidFile(new JavaScriptSerializer().Deserialize<string>(line.Substring(11)), folder); }
            catch { return null; }
        }
        private static string ValidFile(string path, string folder)
        {
            try
            {
                if (!LocalFiles.IsLocal(path)) return null;
                path = Path.GetFullPath(path);
                string root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && String.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
            }
            catch { return null; }
        }
        internal void Cancel() { lock (gate) { cancelled = true; if (job != null) { job.Dispose(); job = null; } } }
        internal Task<DownloadResult> Run(string url, string folder, Action<string> output, bool playlist = false, string signIn = null)
        {
            if (!ValidUrl(url)) throw new ArgumentException("Paste a public HTTP or HTTPS link without a username or password.");
            if (!LocalFiles.IsLocal(folder) || !Directory.Exists(folder)) throw new ArgumentException("Choose an existing local download folder. Network and linked folders are not supported.");
            if (!ToolsReady) throw new InvalidOperationException("Download tools are missing from Digitone's app\\Tools folder. Reinstall Digitone's program files to restore them.");
            return Task.Run(delegate
            {
                var result = new DownloadResult();
                string staging = Path.Combine(folder, ".digitone-download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
                var info = new ProcessStartInfo(Path.Combine(ToolRoot, "yt-dlp.exe"), String.Join(" ", Arguments(url, staging, playlist, signIn).Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = ToolRoot }; KeepCachesPortable(info);
                using (var process = new Process { StartInfo = info })
                {
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data == null) return; string file = CompletedFile(e.Data, staging); if (file != null) { lock (result.Files) if (!result.Files.Contains(file)) result.Files.Add(file); } SongHints hints; string described = CompletedHints(e.Data, staging, out hints); if (described != null) lock (result.Files) result.Hints[described] = hints; output(e.Data); };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) { if (e.Data.StartsWith("ERROR:", StringComparison.Ordinal)) result.Errors++; if (NeedsSignIn(e.Data)) { string video = SignInVideo(e.Data); lock (result.Files) { result.NeedsSignIn = true; if (video != null && !result.SignInIds.Contains(video)) result.SignInIds.Add(video); } } output(e.Data); } };
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
                    SongHints hints; if (result.Hints.TryGetValue(source, out hints)) { result.Hints.Remove(source); result.Hints[destination] = hints; }
                }
                if (!Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
                return result;
            });
        }
    }
    internal sealed class DownloadTarget
    {
        internal Playlist Playlist;
        public string Label { get; set; }
    }
    internal sealed class YouTubeBrowser : IDisposable
    {
        private readonly PlayerApp player;private readonly Action<bool> useCurrent;private WebView2 browser;private Window window;private TextBlock status;private Button download,downloadPlaylist;private ComboBox playlistChoice;private bool disposed;
        internal YouTubeBrowser(PlayerApp player,Action<bool> useCurrent){this.player=player;this.useCurrent=useCurrent;player.Find<Button>("YouTubeToggle").Click+=async delegate{await Show();};player.Window.Closed+=delegate{Dispose();};}
        internal static bool Trusted(string address){Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||uri.Scheme!="https")return false;string host=uri.DnsSafeHost.TrimEnd('.');return host.Equals("youtube.com",StringComparison.OrdinalIgnoreCase)||host.EndsWith(".youtube.com",StringComparison.OrdinalIgnoreCase)||host.Equals("youtu.be",StringComparison.OrdinalIgnoreCase);}
        internal static bool AllowedNavigation(string address){if(Trusted(address))return true;Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||uri.Scheme!="https")return false;string host=uri.DnsSafeHost.TrimEnd('.');return host.Equals("accounts.google.com",StringComparison.OrdinalIgnoreCase)||host.Equals("myaccount.google.com",StringComparison.OrdinalIgnoreCase)||host.Equals("consent.google.com",StringComparison.OrdinalIgnoreCase);}
        internal string CurrentUrl{get{return browser!=null&&browser.Source!=null&&Trusted(browser.Source.AbsoluteUri)?browser.Source.AbsoluteUri:null;}}
        internal Playlist SelectedPlaylist{get{var target=playlistChoice==null?null:playlistChoice.SelectedItem as DownloadTarget;return target==null?null:target.Playlist;}}
        internal void RefreshPlaylists(IEnumerable<Playlist> playlists){if(playlistChoice==null)return;var selected=SelectedPlaylist;var targets=new List<DownloadTarget>{new DownloadTarget{Label="All music only"}};targets.AddRange(playlists.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).Select(p=>new DownloadTarget{Label=p.Name,Playlist=p}));playlistChoice.ItemsSource=targets;playlistChoice.SelectedItem=targets.FirstOrDefault(t=>t.Playlist==selected)??targets[0];}
        internal void SetBusy(bool busy){}
        internal const string SignInPage="https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";
        internal async Task OpenSignIn(){await Show(SignInPage);}
        private static readonly string[] SignInCookies={"LOGIN_INFO","SAPISID","__Secure-3PAPISID"};
        // Writes the viewer's YouTube sign-in to a Netscape cookie file for yt-dlp. False when nobody is signed in to the viewer.
        internal async Task<bool> WriteSignIn(string path)
        {
            List<CoreWebView2Cookie> cookies;
            if(browser!=null&&browser.CoreWebView2!=null)cookies=await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.youtube.com");
            else
            {
                string bundled=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Tools","WebView2"),data=PortableFolders.Current.YouTubeBrowser;
                if(!Directory.Exists(data))return false;
                var environment=await CoreWebView2Environment.CreateAsync(Directory.Exists(bundled)?bundled:null,data);
                var controller=await environment.CreateCoreWebView2ControllerAsync(new System.Windows.Interop.WindowInteropHelper(player.Window).Handle);
                try{controller.IsVisible=false;cookies=await controller.CoreWebView2.CookieManager.GetCookiesAsync("https://www.youtube.com");}
                finally{controller.Close();}
            }
            if(cookies==null||!cookies.Any(c=>SignInCookies.Contains(c.Name)))return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,CookieFile(cookies.Select(c=>new[]{c.Domain,c.Path,c.IsSecure?"TRUE":"FALSE",c.IsSession?"0":((long)Math.Max(0,(c.Expires.ToUniversalTime()-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds)).ToString(CultureInfo.InvariantCulture),c.Name,c.Value})),new UTF8Encoding(false));
            return true;
        }
        // Netscape cookie file: domain, include-subdomains, path, secure, expiry, name, value (one cookie per line).
        internal static string CookieFile(IEnumerable<string[]> cookies)
        {
            var text=new StringBuilder("# Netscape HTTP Cookie File\n");
            foreach(var c in cookies){if(c[4].IndexOfAny(new[]{'\t','\n','\r'})>=0||c[5].IndexOfAny(new[]{'\t','\n','\r'})>=0)continue;text.Append(c[0]).Append('\t').Append(c[0].StartsWith(".")?"TRUE":"FALSE").Append('\t').Append(String.IsNullOrEmpty(c[1])?"/":c[1]).Append('\t').Append(c[2]).Append('\t').Append(c[3]).Append('\t').Append(c[4]).Append('\t').Append(c[5]).Append('\n');}
            return text.ToString();
        }
        private async Task Show(string start=null)
        {
            if(disposed)return;if(window!=null){window.Show();window.Activate();if(start!=null&&browser!=null&&browser.CoreWebView2!=null)browser.CoreWebView2.Navigate(start);return;}BuildWindow();window.Show();status.Text="Opening YouTube…";
            try
            {
                string bundled=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Tools","WebView2"),data=PortableFolders.Current.YouTubeBrowser;Directory.CreateDirectory(data);var environment=await CoreWebView2Environment.CreateAsync(Directory.Exists(bundled)?bundled:null,data);if(disposed||browser==null)return;await browser.EnsureCoreWebView2Async(environment);if(disposed||browser==null)return;
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=true;browser.CoreWebView2.Settings.AreDevToolsEnabled=false;browser.CoreWebView2.Settings.AreHostObjectsAllowed=false;browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled=true;browser.CoreWebView2.Settings.IsGeneralAutofillEnabled=true;
                browser.CoreWebView2.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};browser.CoreWebView2.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;if(status!=null)status.Text="Use Digitone's Download button instead of the browser download.";};
                browser.CoreWebView2.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){if(!AllowedNavigation(e.Uri)){e.Cancel=true;if(status!=null)status.Text="The viewer blocked a page outside YouTube sign-in.";}};
                browser.CoreWebView2.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;if(AllowedNavigation(e.Uri))browser.CoreWebView2.Navigate(e.Uri);else if(status!=null)status.Text="The viewer blocked a pop-up outside YouTube sign-in.";};
                browser.CoreWebView2.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){if(status!=null)status.Text=e.IsSuccess?"":"YouTube couldn't load. Check your connection.";};browser.CoreWebView2.Navigate(start??"https://www.youtube.com/");
            }
            catch(Exception error){if(status!=null)status.Text="The YouTube viewer could not start: "+error.Message;}
        }
        private void BuildWindow()
        {
            browser=new WebView2();window=new Window{Owner=player.Window,Title="YouTube · Digitone",Width=1120,Height=760,MinWidth=760,MinHeight=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,Resources=player.Window.Resources,FontFamily=player.Window.FontFamily,Icon=player.Window.Icon,UseLayoutRounding=true};window.SetResourceReference(Control.BackgroundProperty,"B161917");var root=new Grid{Margin=new Thickness(14)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});window.Content=root;var bar=new WrapPanel{Margin=new Thickness(0,0,0,10)};root.Children.Add(bar);Button back=new Button{Content="←",ToolTip="Back",Padding=new Thickness(12,7,12,7)},forward=new Button{Content="→",ToolTip="Forward",Padding=new Thickness(12,7,12,7)},home=new Button{Content="YouTube home",Padding=new Thickness(12,7,12,7)};download=new Button{Content="↓ This song",ToolTip="Download the audio of the video you're watching",Padding=new Thickness(12,7,12,7)};downloadPlaylist=new Button{Content="↓ Whole playlist",ToolTip="Download every song in the playlist you're viewing",Padding=new Thickness(12,7,12,7)};playlistChoice=new ComboBox{MinWidth=220,Margin=new Thickness(8,0,0,0)};download.SetResourceReference(Control.BackgroundProperty,"BC1D3A8");download.SetResourceReference(Control.ForegroundProperty,"B20291E");bar.Children.Add(back);bar.Children.Add(forward);bar.Children.Add(home);bar.Children.Add(download);bar.Children.Add(downloadPlaylist);bar.Children.Add(playlistChoice);back.Click+=delegate{if(browser.CanGoBack)browser.GoBack();};forward.Click+=delegate{if(browser.CanGoForward)browser.GoForward();};home.Click+=delegate{if(browser.CoreWebView2!=null)browser.CoreWebView2.Navigate("https://www.youtube.com/");};download.Click+=delegate{useCurrent(false);};downloadPlaylist.Click+=delegate{useCurrent(true);};RefreshPlaylists(player.Data.Playlists);Grid.SetRow(browser,1);root.Children.Add(browser);status=new TextBlock{Text="",FontSize=11,Margin=new Thickness(0,9,0,0),TextWrapping=TextWrapping.Wrap};status.SetResourceReference(TextBlock.ForegroundProperty,"B919A92");Grid.SetRow(status,2);root.Children.Add(status);window.Closed+=delegate{if(browser!=null)browser.Dispose();browser=null;window=null;status=null;download=null;downloadPlaylist=null;playlistChoice=null;};NativeChrome.Apply(window);
        }
        public void Dispose(){if(disposed)return;disposed=true;if(window!=null)window.Close();else if(browser!=null)browser.Dispose();}
    }
    internal sealed class DownloadJob
    {
        internal string Url,Folder,Name,State="Pending",Error,MetadataTitle,MetadataArtist;internal bool EntirePlaylist,CreatePlaylist,NeedsSignIn;internal Playlist Destination;internal DownloadResult Result;internal readonly TaskCompletionSource<bool> Completion=new TaskCompletionSource<bool>();
        public string Display{get{return State+" · "+(EntirePlaylist?"Playlist · ":"")+(Destination!=null?Destination.Name:CreatePlaylist?Name:"All music")+" · "+Url;}}
        internal DownloadJob Copy(){return new DownloadJob{Url=Url,Folder=Folder,Name=Name,EntirePlaylist=EntirePlaylist,CreatePlaylist=CreatePlaylist,Destination=Destination,MetadataTitle=MetadataTitle,MetadataArtist=MetadataArtist};}
    }
    internal sealed class DownloadController
    {
        private readonly PlayerApp player;private readonly Action save;private readonly List<DownloadJob> jobs=new List<DownloadJob>();private readonly Queue<DownloadJob> pending=new Queue<DownloadJob>();private AudioDownloader downloader;private DownloadJob current;private readonly YouTubeBrowser youtube;private readonly SpotifyImportWindow spotify;private Playlist selectedDestination;private bool createDestination,refreshingDestinations,pumping;
        internal bool Busy{get{return current!=null;}}internal Task ActiveTask{get;private set;}
        internal DownloadController(PlayerApp player,Action save)
        {
            this.player=player;this.save=save;if(!String.IsNullOrEmpty(player.Data.DownloadFolder)&&LocalFiles.IsLocal(player.Data.DownloadFolder))player.Find<TextBlock>("DownloadFolder").Text=player.Data.DownloadFolder;
            player.Find<Button>("DownloadBrowse").Click+=delegate{using(var dialog=new Forms.FolderBrowserDialog{Description="Choose where Digitone should save downloaded MP3 files.",ShowNewFolderButton=true})if(dialog.ShowDialog()==Forms.DialogResult.OK){if(!LocalFiles.IsLocal(dialog.SelectedPath)){Status("Choose a local folder, not a network, cloud placeholder, or linked folder.");return;}player.Data.DownloadFolder=dialog.SelectedPath;player.Find<TextBlock>("DownloadFolder").Text=dialog.SelectedPath;save();}};
            player.Find<Button>("DownloadStart").Click+=delegate{ActiveTask=EnqueueFromForm();};player.Find<Button>("DownloadCancel").Click+=delegate{Cancel();};player.Find<Button>("DownloadExistingMode").Click+=delegate{SetMode(false);};player.Find<Button>("DownloadNewMode").Click+=delegate{SetMode(true);};
            player.Find<ComboBox>("DownloadPlaylistTarget").SelectionChanged+=delegate{if(!refreshingDestinations){var target=player.Find<ComboBox>("DownloadPlaylistTarget").SelectedItem as DownloadTarget;selectedDestination=target==null?null:target.Playlist;}UpdateTargetControls();};player.Find<CheckBox>("DownloadPlaylist").Click+=delegate{UpdateTargetControls();};
            player.Find<Button>("DownloadRetry").Click+=delegate{RetrySelected();};player.Find<Button>("DownloadJobCancel").Click+=delegate{CancelSelected();};player.Find<Button>("DownloadOpenFolder").Click+=delegate{OpenSelectedFolder();};player.Find<Button>("DownloadDetailsToggle").Click+=delegate{var log=player.Find<TextBox>("DownloadLog");bool show=log.Visibility!=Visibility.Visible;log.Visibility=show?Visibility.Visible:Visibility.Collapsed;player.Find<Button>("DownloadDetailsToggle").Content=show?"Hide details":"Show details";};
            player.Find<Button>("DownloadSignIn").Click+=async delegate{await youtube.OpenSignIn();};
            // Sign-in files are deleted right after each download; this removes any left behind if Digitone was closed mid-download.
            try{string temp=Path.Combine(PortableFolders.Current.Cache,"temp");if(Directory.Exists(temp))foreach(string old in Directory.GetFiles(temp,"yt-signin-*.txt"))File.Delete(old);}catch(Exception){}
            youtube=new YouTubeBrowser(player,UseYouTubePage);spotify=new SpotifyImportWindow(player,this,save);player.Find<Button>("SpotifyImportButton").Click+=delegate{spotify.Show();};RefreshTools();RefreshPlaylists();SetMode(false);RefreshJobs();
        }
        internal void RefreshPlaylists(){var box=player.Find<ComboBox>("DownloadPlaylistTarget");var targets=new List<DownloadTarget>{new DownloadTarget{Label="All music only"}};targets.AddRange(player.Data.Playlists.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).Select(p=>new DownloadTarget{Label=p.Name,Playlist=p}));refreshingDestinations=true;box.ItemsSource=targets;box.SelectedItem=selectedDestination==null?targets[0]:targets.FirstOrDefault(t=>t.Playlist==selectedDestination)??targets[0];refreshingDestinations=false;UpdateTargetControls();youtube.RefreshPlaylists(player.Data.Playlists);}
        internal void RefreshTools(){player.Find<TextBlock>("DownloadToolsStatus").Text=AudioDownloader.ToolsReady?"":"Download tools are missing. Run Repair Digitone to restore them.";}
        private void SetMode(bool create){createDestination=create;player.Find<StackPanel>("DownloadExistingControls").Visibility=create?Visibility.Collapsed:Visibility.Visible;player.Find<WrapPanel>("DownloadNewControls").Visibility=create?Visibility.Visible:Visibility.Collapsed;player.Find<Button>("DownloadExistingMode").SetResourceReference(Control.BackgroundProperty,create?"B1C211D":"B354233");player.Find<Button>("DownloadNewMode").SetResourceReference(Control.BackgroundProperty,create?"B354233":"B1C211D");UpdateTargetControls();}
        private void UpdateTargetControls(){player.Find<TextBox>("DownloadPlaylistName").IsEnabled=createDestination;player.Find<ComboBox>("DownloadPlaylistTarget").IsEnabled=!createDestination;}
        private void Status(string text){player.Find<TextBlock>("DownloadStatus").Text=text;}
        internal void Cancel(){if(downloader!=null&&current!=null){current.State="Cancelling";Status("Cancelling active download and conversion…");downloader.Cancel();RefreshJobs();}}
        // Asked when an album or playlist link is added as a single song: "all", "one", or null to cancel. Replaceable by tests.
        internal Func<string,int,string> AskWholeCollection;
        private string AskWholeCollectionDefault(string title,int count)
        {
            string what=String.IsNullOrWhiteSpace(title)?"This link is an album or playlist":"This link is an album or playlist: “"+title+"”"+(count>0?" ("+count+" songs)":"");
            var answer=MessageBox.Show(player.Window,what+".\n\nDownload all of it?\n\nYes = every song · No = just the first song","Digitone",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
            return answer==MessageBoxResult.Yes?"all":answer==MessageBoxResult.No?"one":null;
        }
        private async Task EnqueueFromForm()
        {
            string url=player.Find<TextBox>("DownloadUrl").Text.Trim();bool whole=createDestination&&player.Find<CheckBox>("DownloadPlaylist").IsChecked==true;
            string name=createDestination?player.Find<TextBox>("DownloadPlaylistName").Text.Trim():null;Playlist destination=createDestination?null:selectedDestination;
            if(!whole&&AudioDownloader.ValidUrl(url)&&AudioDownloader.IsCollectionLink(url)&&AudioDownloader.ToolsReady)
            {
                Status("Checking link…");var peek=await AudioDownloader.PeekCollection(url);
                string choice=(AskWholeCollection??AskWholeCollectionDefault)(peek.Key,peek.Value);
                if(choice==null){Status("Not added.");return;}
                if(choice=="all"){whole=true;if(destination==null&&(String.IsNullOrWhiteSpace(name)||name=="Downloaded playlist"))name=AudioDownloader.FolderName(peek.Key);}
            }
            await Enqueue(url,whole,name,destination);
        }
        internal Task Enqueue(string url,bool entirePlaylist,string name,Playlist destination,string metadataTitle=null,string metadataArtist=null)
        {
            string root=player.Data.DownloadFolder;if(!AudioDownloader.ValidUrl(url)){Status("Paste an HTTP or HTTPS link first.");return Task.FromResult(false);}if(!LocalFiles.IsLocal(root)||!Directory.Exists(root)){Status("Choose an existing local folder first.");return Task.FromResult(false);}var job=new DownloadJob{Url=url,EntirePlaylist=entirePlaylist,Name=name,Destination=destination,MetadataTitle=metadataTitle,MetadataArtist=metadataArtist,CreatePlaylist=destination==null&&(!String.IsNullOrWhiteSpace(name)||entirePlaylist)};try{job.Folder=job.CreatePlaylist||destination!=null?AudioDownloader.PlaylistFolder(root,destination!=null?destination.Name:name):root;}catch(Exception e){Status(e.Message);return Task.FromResult(false);}jobs.Add(job);pending.Enqueue(job);RefreshJobs();Status(pending.Count>1||current!=null?"Queued.":"Starting…");if(!pumping){pumping=true;var ignored=Pump();}return job.Completion.Task;
        }
        private async Task Pump()
        {
            var batch=new List<DownloadJob>();
            while(pending.Count>0){current=pending.Dequeue();batch.Add(current);current.State="Downloading";RefreshJobs();player.Find<Button>("DownloadCancel").IsEnabled=true;downloader=new AudioDownloader();try{current.Result=await downloader.Run(current.Url,current.Folder,Output,current.EntirePlaylist);if(current.Result.NeedsSignIn&&!current.Result.Cancelled)await RetrySignedIn(current);player.Find<Button>("DownloadSignIn").Visibility=current.NeedsSignIn?Visibility.Visible:Visibility.Collapsed;if(current.Result.Files.Count>0){if(current.Result.Files.Count==1&&!String.IsNullOrWhiteSpace(current.MetadataTitle)&&SongTags.CanEdit(current.Result.Files[0]))await Task.Run(delegate{var tagged=new Track{Path=current.Result.Files[0]};SongTags.ReadInto(tagged);tagged.SongTitle=current.MetadataTitle;tagged.Artist=current.MetadataArtist??"";SongTags.SaveIdentity(tagged);});await player.Import(current.Result.Files.ToArray(),true);var imported=current.Result.Files.Select(p=>player.Data.Tracks.FirstOrDefault(t=>String.Equals(t.Path,p,StringComparison.OrdinalIgnoreCase))).Where(t=>t!=null).ToList();player.QueueRecognition(imported,current.Result.Hints,current.MetadataTitle,current.MetadataArtist,false);if(current.CreatePlaylist||current.Destination!=null)player.AddDownloadedTracks(current.Name,current.Folder,imported,current.Destination);current.State=current.Result.Cancelled?"Cancelled · finished files kept":current.NeedsSignIn?"Completed · "+current.Result.SignInIds.Count+" need YouTube sign-in":current.Result.Errors>0||current.Result.ExitCode!=0&&current.Result.ExitCode!=101?"Completed with warnings":"Completed";Status(current.NeedsSignIn?current.Result.Files.Count+" downloaded. Some songs are age-restricted: sign in to YouTube, then press Retry.":current.Result.Files.Count==1?"Downloaded.":current.Result.Files.Count+" songs downloaded.");}else if(current.NeedsSignIn&&!current.Result.Cancelled){current.State="Needs YouTube sign-in";Status("This song is age-restricted on YouTube. Sign in once, then press Retry.");}else current.State=current.Result.Cancelled?"Cancelled":"Failed";}catch(Exception e){current.Error=e.Message;current.State="Failed";Status("Download failed: "+e.Message);}finally{var finished=current;current=null;downloader=null;player.Find<Button>("DownloadCancel").IsEnabled=false;RefreshPlaylists();RefreshJobs();finished.Completion.TrySetResult(finished.State.StartsWith("Completed"));}}
            pumping=false;// A short summary for batches (a Spotify import, several links); a sign-in request stays on screen instead.
            if(batch.Count>1&&!batch.Any(j=>j.NeedsSignIn))Status(batch.Count(j=>j.State.StartsWith("Completed"))+" of "+batch.Count+" done"+(batch.Any(j=>j.State=="Failed")?" · "+batch.Count(j=>j.State=="Failed")+" failed":"")+".");
            else if(batch.Count>1)Status("Some songs are age-restricted on YouTube. Sign in once, then press Retry on them.");
        }
        // YouTube wanted a signed-in account. Try again with the viewer's sign-in (if any): the whole link for one song, or just the refused songs of a playlist.
        private async Task RetrySignedIn(DownloadJob job)
        {
            var first=job.Result;string signIn=Path.Combine(PortableFolders.Current.Cache,"temp","yt-signin-"+Guid.NewGuid().ToString("N")+".txt");
            bool signedIn;try{signedIn=await youtube.WriteSignIn(signIn);}catch(Exception){signedIn=false;}
            if(!signedIn){job.NeedsSignIn=true;return;}
            try
            {
                Status("Age-restricted · trying again with your YouTube sign-in…");
                var targets=job.EntirePlaylist&&first.SignInIds.Count>0?first.SignInIds.Select(id=>"https://www.youtube.com/watch?v="+id).ToList():new List<string>{job.Url};
                var refused=new List<string>();
                foreach(string target in targets)
                {
                    if(first.Cancelled)break;
                    downloader=new AudioDownloader();var again=await downloader.Run(target,job.Folder,Output,job.EntirePlaylist&&first.SignInIds.Count==0,signIn);
                    first.Files.AddRange(again.Files);foreach(var hint in again.Hints)first.Hints[hint.Key]=hint.Value;
                    first.Cancelled|=again.Cancelled;first.Errors=Math.Max(0,first.Errors-again.Files.Count);if(!job.EntirePlaylist)first.ExitCode=again.ExitCode;
                    if(again.NeedsSignIn)refused.AddRange(again.SignInIds.Count>0?again.SignInIds:new List<string>{target});
                }
                job.NeedsSignIn=refused.Count>0;first.SignInIds.Clear();first.SignInIds.AddRange(refused);
            }
            finally{try{File.Delete(signIn);}catch(Exception){}}
        }
        private void Output(string line){player.Window.Dispatcher.BeginInvoke(new Action(delegate{if(line.StartsWith("STILL_PROGRESS:",StringComparison.Ordinal)){double percent;if(Double.TryParse(line.Substring(15).Trim().TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out percent)){player.Find<ProgressBar>("DownloadProgress").IsIndeterminate=false;player.Find<ProgressBar>("DownloadProgress").Value=Math.Max(0,Math.Min(100,percent));Status("Downloading audio · "+percent.ToString("0",CultureInfo.InvariantCulture)+"%");}return;}if(line.StartsWith("STILL_FILE:",StringComparison.Ordinal)||line.StartsWith("STILL_META:",StringComparison.Ordinal))return;var log=player.Find<TextBox>("DownloadLog");string next=log.Text+(line.Length>1500?line.Substring(0,1500):line)+Environment.NewLine;log.Text=next.Length>10000?next.Substring(next.Length-10000):next;log.ScrollToEnd();}));}
        private DownloadJob SelectedJob(){return player.Find<ListBox>("DownloadJobs").SelectedItem as DownloadJob;}
        private void RefreshJobs(){var list=player.Find<ListBox>("DownloadJobs");var selected=list.SelectedItem;list.ItemsSource=null;list.ItemsSource=jobs.ToList();if(selected!=null)list.SelectedItem=selected;}
        private void RetrySelected(){var chosen=SelectedJob();if(chosen==null||chosen==current||chosen.State=="Pending"||chosen.State=="Downloading")return;var retry=chosen.Copy();jobs.Add(retry);pending.Enqueue(retry);RefreshJobs();ActiveTask=retry.Completion.Task;if(!pumping){pumping=true;var ignored=Pump();}}
        internal void CancelSelected(){var chosen=SelectedJob();if(chosen==null)return;if(chosen==current){Cancel();return;}if(chosen.State=="Pending"){var before=pending.ToList();int position=before.IndexOf(chosen);var kept=before.Where(j=>j!=chosen).ToArray();pending.Clear();foreach(var job in kept)pending.Enqueue(job);chosen.State="Cancelled";chosen.Completion.TrySetResult(false);RefreshJobs();player.ShowUndoNotice("Removed a pending download from the queue.",delegate{var restored=chosen.Copy();restored.State="Pending";jobs.Add(restored);var ordered=pending.ToList();ordered.Insert(Math.Max(0,Math.Min(position,ordered.Count)),restored);pending.Clear();foreach(var job in ordered)pending.Enqueue(job);RefreshJobs();ActiveTask=restored.Completion.Task;if(!pumping){pumping=true;var ignored=Pump();}Status("Pending download restored.");});}}
        private void OpenSelectedFolder(){var chosen=SelectedJob();if(chosen==null||!LocalFiles.IsLocal(chosen.Folder)||!Directory.Exists(chosen.Folder))return;try{Process.Start(new ProcessStartInfo(chosen.Folder){UseShellExecute=true});}catch(Exception e){Status("Could not open folder: "+e.Message);}}
        private void UseYouTubePage(bool playlist){string url=youtube.CurrentUrl;if(String.IsNullOrWhiteSpace(url)){Status("Open a YouTube video or playlist first.");return;}ActiveTask=Enqueue(url,playlist,playlist?"YouTube playlist":null,youtube.SelectedPlaylist);player.ShowDownloads(true);}
    }}
