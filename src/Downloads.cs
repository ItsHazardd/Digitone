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
        private async Task Show()
        {
            if(disposed)return;if(window!=null){window.Show();window.Activate();return;}BuildWindow();window.Show();status.Text="Opening YouTube…";
            try
            {
                string bundled=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Tools","WebView2"),data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data","WebView2","YouTube");Directory.CreateDirectory(data);var environment=await CoreWebView2Environment.CreateAsync(Directory.Exists(bundled)?bundled:null,data);if(disposed||browser==null)return;await browser.EnsureCoreWebView2Async(environment);if(disposed||browser==null)return;
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=true;browser.CoreWebView2.Settings.AreDevToolsEnabled=false;browser.CoreWebView2.Settings.AreHostObjectsAllowed=false;browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled=true;browser.CoreWebView2.Settings.IsGeneralAutofillEnabled=true;
                browser.CoreWebView2.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};browser.CoreWebView2.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;if(status!=null)status.Text="Use Digitone's Download button instead of the browser download.";};
                browser.CoreWebView2.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){if(!AllowedNavigation(e.Uri)){e.Cancel=true;if(status!=null)status.Text="The viewer blocked a page outside YouTube sign-in.";}};
                browser.CoreWebView2.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;if(AllowedNavigation(e.Uri))browser.CoreWebView2.Navigate(e.Uri);else if(status!=null)status.Text="The viewer blocked a pop-up outside YouTube sign-in.";};
                browser.CoreWebView2.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){if(status!=null)status.Text=e.IsSuccess?"Choose a video or playlist, then use Download this page.":"YouTube could not load. Check your connection and try again.";};browser.CoreWebView2.Navigate("https://www.youtube.com/");
            }
            catch(Exception error){if(status!=null)status.Text="The YouTube viewer could not start: "+error.Message;}
        }
        private void BuildWindow()
        {
            browser=new WebView2();window=new Window{Owner=player.Window,Title="YouTube · Digitone",Width=1120,Height=760,MinWidth=760,MinHeight=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,Resources=player.Window.Resources,FontFamily=player.Window.FontFamily,Icon=player.Window.Icon,UseLayoutRounding=true};window.SetResourceReference(Control.BackgroundProperty,"B161917");var root=new Grid{Margin=new Thickness(14)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});window.Content=root;var bar=new WrapPanel{Margin=new Thickness(0,0,0,10)};root.Children.Add(bar);Button back=new Button{Content="←",ToolTip="Back",Padding=new Thickness(12,7,12,7)},forward=new Button{Content="→",ToolTip="Forward",Padding=new Thickness(12,7,12,7)},home=new Button{Content="YouTube home",Padding=new Thickness(12,7,12,7)};download=new Button{Content="↓ Audio only · current video",Padding=new Thickness(12,7,12,7)};downloadPlaylist=new Button{Content="↓ Audio only · entire playlist",Padding=new Thickness(12,7,12,7)};playlistChoice=new ComboBox{MinWidth=220,Margin=new Thickness(8,0,0,0)};download.SetResourceReference(Control.BackgroundProperty,"BC1D3A8");download.SetResourceReference(Control.ForegroundProperty,"B20291E");bar.Children.Add(back);bar.Children.Add(forward);bar.Children.Add(home);bar.Children.Add(download);bar.Children.Add(downloadPlaylist);bar.Children.Add(playlistChoice);back.Click+=delegate{if(browser.CanGoBack)browser.GoBack();};forward.Click+=delegate{if(browser.CanGoForward)browser.GoForward();};home.Click+=delegate{if(browser.CoreWebView2!=null)browser.CoreWebView2.Navigate("https://www.youtube.com/");};download.Click+=delegate{useCurrent(false);};downloadPlaylist.Click+=delegate{useCurrent(true);};RefreshPlaylists(player.Data.Playlists);Grid.SetRow(browser,1);root.Children.Add(browser);status=new TextBlock{Text="Choose All music or an existing playlist above. Sign-in cookies stay in Digitone's local Data folder.",FontSize=11,Margin=new Thickness(0,9,0,0),TextWrapping=TextWrapping.Wrap};status.SetResourceReference(TextBlock.ForegroundProperty,"B919A92");Grid.SetRow(status,2);root.Children.Add(status);window.Closed+=delegate{if(browser!=null)browser.Dispose();browser=null;window=null;status=null;download=null;downloadPlaylist=null;playlistChoice=null;};NativeChrome.Apply(window);
        }
        public void Dispose(){if(disposed)return;disposed=true;if(window!=null)window.Close();else if(browser!=null)browser.Dispose();}
    }
    internal sealed class DownloadJob
    {
        internal string Url,Folder,Name,State="Pending",Error;internal bool EntirePlaylist,CreatePlaylist;internal Playlist Destination;internal DownloadResult Result;internal readonly TaskCompletionSource<bool> Completion=new TaskCompletionSource<bool>();
        public string Display{get{return State+" · "+(EntirePlaylist?"Playlist · ":"")+(Destination!=null?Destination.Name:CreatePlaylist?Name:"All music")+" · "+Url;}}
        internal DownloadJob Copy(){return new DownloadJob{Url=Url,Folder=Folder,Name=Name,EntirePlaylist=EntirePlaylist,CreatePlaylist=CreatePlaylist,Destination=Destination};}
    }
    internal sealed class DownloadController
    {
        private readonly PlayerApp player;private readonly Action save;private readonly List<DownloadJob> jobs=new List<DownloadJob>();private readonly Queue<DownloadJob> pending=new Queue<DownloadJob>();private AudioDownloader downloader;private DownloadJob current;private readonly YouTubeBrowser youtube;private Playlist selectedDestination;private bool createDestination,refreshingDestinations,pumping;
        internal bool Busy{get{return current!=null;}}internal Task ActiveTask{get;private set;}
        internal DownloadController(PlayerApp player,Action save)
        {
            this.player=player;this.save=save;if(!String.IsNullOrEmpty(player.Data.DownloadFolder)&&LocalFiles.IsLocal(player.Data.DownloadFolder))player.Find<TextBlock>("DownloadFolder").Text=player.Data.DownloadFolder;
            player.Find<Button>("DownloadBrowse").Click+=delegate{using(var dialog=new Forms.FolderBrowserDialog{Description="Choose where Digitone should save downloaded MP3 files.",ShowNewFolderButton=true})if(dialog.ShowDialog()==Forms.DialogResult.OK){if(!LocalFiles.IsLocal(dialog.SelectedPath)){Status("Choose a local folder, not a network, cloud placeholder, or linked folder.");return;}player.Data.DownloadFolder=dialog.SelectedPath;player.Find<TextBlock>("DownloadFolder").Text=dialog.SelectedPath;save();}};
            player.Find<Button>("DownloadStart").Click+=delegate{ActiveTask=EnqueueFromForm();};player.Find<Button>("DownloadCancel").Click+=delegate{Cancel();};player.Find<Button>("DownloadExistingMode").Click+=delegate{SetMode(false);};player.Find<Button>("DownloadNewMode").Click+=delegate{SetMode(true);};
            player.Find<ComboBox>("DownloadPlaylistTarget").SelectionChanged+=delegate{if(!refreshingDestinations){var target=player.Find<ComboBox>("DownloadPlaylistTarget").SelectedItem as DownloadTarget;selectedDestination=target==null?null:target.Playlist;}UpdateTargetControls();};player.Find<CheckBox>("DownloadPlaylist").Click+=delegate{UpdateTargetControls();};
            player.Find<Button>("DownloadRetry").Click+=delegate{RetrySelected();};player.Find<Button>("DownloadJobCancel").Click+=delegate{CancelSelected();};player.Find<Button>("DownloadOpenFolder").Click+=delegate{OpenSelectedFolder();};player.Find<Button>("DownloadDetailsToggle").Click+=delegate{var log=player.Find<TextBox>("DownloadLog");bool show=log.Visibility!=Visibility.Visible;log.Visibility=show?Visibility.Visible:Visibility.Collapsed;player.Find<Button>("DownloadDetailsToggle").Content=show?"Hide details":"Show details";};
            youtube=new YouTubeBrowser(player,UseYouTubePage);RefreshTools();RefreshPlaylists();SetMode(false);RefreshJobs();
        }
        internal void RefreshPlaylists(){var box=player.Find<ComboBox>("DownloadPlaylistTarget");var targets=new List<DownloadTarget>{new DownloadTarget{Label="All music only"}};targets.AddRange(player.Data.Playlists.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).Select(p=>new DownloadTarget{Label=p.Name,Playlist=p}));refreshingDestinations=true;box.ItemsSource=targets;box.SelectedItem=selectedDestination==null?targets[0]:targets.FirstOrDefault(t=>t.Playlist==selectedDestination)??targets[0];refreshingDestinations=false;UpdateTargetControls();youtube.RefreshPlaylists(player.Data.Playlists);}
        internal void RefreshTools(){player.Find<TextBlock>("DownloadToolsStatus").Text=AudioDownloader.ToolsReady?"yt-dlp + FFmpeg ready":"Download tools missing · run Install-DownloadTools.ps1.";}
        private void SetMode(bool create){createDestination=create;player.Find<StackPanel>("DownloadExistingControls").Visibility=create?Visibility.Collapsed:Visibility.Visible;player.Find<WrapPanel>("DownloadNewControls").Visibility=create?Visibility.Visible:Visibility.Collapsed;player.Find<Button>("DownloadExistingMode").SetResourceReference(Control.BackgroundProperty,create?"B1C211D":"B354233");player.Find<Button>("DownloadNewMode").SetResourceReference(Control.BackgroundProperty,create?"B354233":"B1C211D");UpdateTargetControls();}
        private void UpdateTargetControls(){player.Find<TextBox>("DownloadPlaylistName").IsEnabled=createDestination;player.Find<ComboBox>("DownloadPlaylistTarget").IsEnabled=!createDestination;}
        private void Status(string text){player.Find<TextBlock>("DownloadStatus").Text=text;}
        internal void Cancel(){if(downloader!=null&&current!=null){current.State="Cancelling";Status("Cancelling active download and conversion…");downloader.Cancel();RefreshJobs();}}
        private Task EnqueueFromForm(){return Enqueue(player.Find<TextBox>("DownloadUrl").Text.Trim(),createDestination&&player.Find<CheckBox>("DownloadPlaylist").IsChecked==true,createDestination?player.Find<TextBox>("DownloadPlaylistName").Text.Trim():null,createDestination?null:selectedDestination);}
        internal Task Enqueue(string url,bool entirePlaylist,string name,Playlist destination)
        {
            string root=player.Data.DownloadFolder;if(!AudioDownloader.ValidUrl(url)){Status("Paste an HTTP or HTTPS link first.");return Task.FromResult(false);}if(!LocalFiles.IsLocal(root)||!Directory.Exists(root)){Status("Choose an existing local folder first.");return Task.FromResult(false);}var job=new DownloadJob{Url=url,EntirePlaylist=entirePlaylist,Name=name,Destination=destination,CreatePlaylist=destination==null&&(!String.IsNullOrWhiteSpace(name)||entirePlaylist)};try{job.Folder=job.CreatePlaylist||destination!=null?AudioDownloader.PlaylistFolder(root,destination!=null?destination.Name:name):root;}catch(Exception e){Status(e.Message);return Task.FromResult(false);}jobs.Add(job);pending.Enqueue(job);RefreshJobs();Status("Added to download queue · "+pending.Count+" waiting.");if(!pumping){pumping=true;var ignored=Pump();}return job.Completion.Task;
        }
        private async Task Pump()
        {
            while(pending.Count>0){current=pending.Dequeue();current.State="Downloading";RefreshJobs();player.Find<Button>("DownloadCancel").IsEnabled=true;downloader=new AudioDownloader();try{current.Result=await downloader.Run(current.Url,current.Folder,Output,current.EntirePlaylist);if(current.Result.Files.Count>0){await player.Import(current.Result.Files.ToArray(),true);var imported=current.Result.Files.Select(p=>player.Data.Tracks.FirstOrDefault(t=>String.Equals(t.Path,p,StringComparison.OrdinalIgnoreCase))).Where(t=>t!=null).ToList();if(current.CreatePlaylist||current.Destination!=null)player.AddDownloadedTracks(current.Name,current.Folder,imported,current.Destination);current.State=current.Result.Cancelled?"Cancelled · finished files kept":current.Result.Errors>0||current.Result.ExitCode!=0&&current.Result.ExitCode!=101?"Completed with warnings":"Completed";Status(current.Result.Files.Count+" tracks finished · "+current.State+".");}else current.State=current.Result.Cancelled?"Cancelled":"Failed";}catch(Exception e){current.Error=e.Message;current.State="Failed";Status("Download failed: "+e.Message);}finally{var finished=current;current=null;downloader=null;player.Find<Button>("DownloadCancel").IsEnabled=false;RefreshPlaylists();RefreshJobs();finished.Completion.TrySetResult(finished.State.StartsWith("Completed"));}}
            pumping=false;Status(jobs.Count(j=>j.State.StartsWith("Completed"))+" completed · "+jobs.Count(j=>j.State=="Failed")+" failed · queue finished.");
        }
        private void Output(string line){player.Window.Dispatcher.BeginInvoke(new Action(delegate{if(line.StartsWith("STILL_PROGRESS:",StringComparison.Ordinal)){double percent;if(Double.TryParse(line.Substring(15).Trim().TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out percent)){player.Find<ProgressBar>("DownloadProgress").IsIndeterminate=false;player.Find<ProgressBar>("DownloadProgress").Value=Math.Max(0,Math.Min(100,percent));Status("Downloading audio · "+percent.ToString("0",CultureInfo.InvariantCulture)+"%");}return;}if(line.StartsWith("STILL_FILE:",StringComparison.Ordinal))return;var log=player.Find<TextBox>("DownloadLog");string next=log.Text+(line.Length>1500?line.Substring(0,1500):line)+Environment.NewLine;log.Text=next.Length>10000?next.Substring(next.Length-10000):next;log.ScrollToEnd();}));}
        private DownloadJob SelectedJob(){return player.Find<ListBox>("DownloadJobs").SelectedItem as DownloadJob;}
        private void RefreshJobs(){var list=player.Find<ListBox>("DownloadJobs");var selected=list.SelectedItem;list.ItemsSource=null;list.ItemsSource=jobs.ToList();if(selected!=null)list.SelectedItem=selected;}
        private void RetrySelected(){var chosen=SelectedJob();if(chosen==null||chosen==current||chosen.State=="Pending"||chosen.State=="Downloading")return;var retry=chosen.Copy();jobs.Add(retry);pending.Enqueue(retry);RefreshJobs();ActiveTask=retry.Completion.Task;if(!pumping){pumping=true;var ignored=Pump();}}
        internal void CancelSelected(){var chosen=SelectedJob();if(chosen==null)return;if(chosen==current){Cancel();return;}if(chosen.State=="Pending"){var before=pending.ToList();int position=before.IndexOf(chosen);var kept=before.Where(j=>j!=chosen).ToArray();pending.Clear();foreach(var job in kept)pending.Enqueue(job);chosen.State="Cancelled";chosen.Completion.TrySetResult(false);RefreshJobs();player.ShowUndoNotice("Removed a pending download from the queue.",delegate{var restored=chosen.Copy();restored.State="Pending";jobs.Add(restored);var ordered=pending.ToList();ordered.Insert(Math.Max(0,Math.Min(position,ordered.Count)),restored);pending.Clear();foreach(var job in ordered)pending.Enqueue(job);RefreshJobs();ActiveTask=restored.Completion.Task;if(!pumping){pumping=true;var ignored=Pump();}Status("Pending download restored.");});}}
        private void OpenSelectedFolder(){var chosen=SelectedJob();if(chosen==null||!LocalFiles.IsLocal(chosen.Folder)||!Directory.Exists(chosen.Folder))return;try{Process.Start(new ProcessStartInfo(chosen.Folder){UseShellExecute=true});}catch(Exception e){Status("Could not open folder: "+e.Message);}}
        private void UseYouTubePage(bool playlist){string url=youtube.CurrentUrl;if(String.IsNullOrWhiteSpace(url)){Status("Open a YouTube video or playlist first.");return;}ActiveTask=Enqueue(url,playlist,playlist?"YouTube playlist":null,youtube.SelectedPlaylist);player.ShowDownloads(true);}
    }}
