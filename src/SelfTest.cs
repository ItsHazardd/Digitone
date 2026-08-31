using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Digitone
{
    internal static class SelfTest
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
        private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root,i); var element = child as FrameworkElement;
                if (element != null) yield return element;
                foreach (var nested in Elements(child)) yield return nested;
            }
        }
        private sealed class LocalAudioServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly byte[] audio;
            private volatile bool stopped;
            private readonly bool slow;
            internal string Url;
            internal LocalAudioServer(string file, bool slow)
            {
                this.slow = slow; audio = File.ReadAllBytes(file);
                listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/sample.wav";
                Task.Run((Action)Serve);
            }
            private void Serve()
            {
                while (!stopped)
                {
                    try
                    {
                        using (TcpClient client = listener.AcceptTcpClient())
                        using (NetworkStream stream = client.GetStream())
                        {
                            stream.ReadTimeout = 5000;
                            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                            string request = reader.ReadLine(), header;
                            while (!String.IsNullOrEmpty(header = reader.ReadLine())) { }
                            bool playlist = request != null && request.Contains("/playlist.html");
                            byte[] payload = playlist ? Encoding.UTF8.GetBytes("<html><head><title>Digitone test playlist</title></head><body><audio controls src=\"/first.wav\"></audio><audio controls src=\"/second.wav\"></audio></body></html>") : audio;
                            byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: " + (playlist ? "text/html" : "audio/wav") + "\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");
                            stream.Write(headers, 0, headers.Length);
                            if (request != null && !request.StartsWith("HEAD "))
                                for (int i = 0; i < payload.Length && !stopped; i += 4096) { stream.Write(payload, i, Math.Min(4096, payload.Length - i)); if (slow) System.Threading.Thread.Sleep(40); }
                        }
                    }
                    catch (Exception) { if (stopped) break; }
                }
            }
            public void Dispose() { stopped = true; listener.Stop(); }
        }
        private static readonly List<string> results = new List<string>();
        private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        private static void Press(PlayerApp player, string name) { player.Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static void Tone(string path, double frequency)
        {
            const int rate = 44100, seconds = 8, bytes = rate * seconds * 2;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((ushort)2); writer.Write((ushort)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                for (int i = 0; i < rate * seconds; i++) { double envelope = 0.18 + 0.12 * Math.Sin(i / (double)rate * 2); writer.Write((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * envelope * 32767)); }
            }
        }
        private static void Snapshot(Window window, string path)
        {
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
        private static void AcceptDialog(PlayerApp player, string text)
        {
            player.Window.Dispatcher.BeginInvoke(new Action(delegate
            {
                var dialog = player.Window.OwnedWindows.Cast<Window>().FirstOrDefault();
                if (dialog == null) return;
                var panel = (StackPanel)dialog.Content;
                var input = panel.Children.OfType<TextBox>().FirstOrDefault();
                if (input != null) input.Text = text;
                panel.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
        }
        public static int Run(string output)
        {
            Directory.CreateDirectory(output);
            string run = System.IO.Path.Combine(output, DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(run);
            string music = System.IO.Path.Combine(run, "Music"); Directory.CreateDirectory(music);
            string nested = System.IO.Path.Combine(music, "A quieter place"); Directory.CreateDirectory(nested);
            string first = System.IO.Path.Combine(nested, "01 - Golden hour.wav"); string second = System.IO.Path.Combine(nested, "02 - Slow mornings.wav");
            Tone(first, 220); Tone(second, 330); File.WriteAllText(System.IO.Path.Combine(music, "notes.txt"), "not audio");
            var app = new Application(); int exitCode = 1; PlayerApp player = null;
            try
            {
                Check(!LocalFiles.IsLocal("https://example.com/audio.mp3"), "Reject remote URL before filesystem access");
                DigitoneUpdates.EnsureTls();Check((ServicePointManager.SecurityProtocol&(SecurityProtocolType)3072)!=0,"GitHub update checks explicitly enable TLS 1.2");
                var audioOutputs=AudioPlayback.Outputs();Check(audioOutputs.Length>=1&&audioOutputs[0].Id==""&&audioOutputs[0].Name.StartsWith("Windows default"),"Audio output discovery always provides a safe Windows-default choice");
                var updateFixture=DigitoneUpdates.Parse("{\"tag_name\":\"v2.8.30\",\"body\":\"Test notes\",\"html_url\":\"https://github.com/ItsHazardd/Digitone/releases/tag/v2.8.30\",\"prerelease\":false,\"assets\":[{\"name\":\"Digitone-2.8.30-Windows-x64.zip\",\"browser_download_url\":\"https://github.com/ItsHazardd/Digitone/releases/download/v2.8.30/Digitone-2.8.30-Windows-x64.zip\"},{\"name\":\"Digitone-2.8.30-SHA256.txt\",\"browser_download_url\":\"https://github.com/ItsHazardd/Digitone/releases/download/v2.8.30/Digitone-2.8.30-SHA256.txt\"}]}");Check(updateFixture.Version=="v2.8.30"&&DigitoneUpdates.IsNewer(updateFixture.Version,"2.7.30")&&DigitoneUpdates.Installable(updateFixture)&&!DigitoneUpdates.IsNewer("2.7.30","2.7.30"),"GitHub release metadata finds only the versioned install assets without network access");
                var evilUpdate=new DigitoneRelease{Version="v2.8.30",ZipUrl="https://example.com/Digitone-2.8.30-Windows-x64.zip",ChecksumUrl=updateFixture.ChecksumUrl};Check(!DigitoneUpdates.Installable(evilUpdate),"Updater rejects release assets outside the official Digitone GitHub path");
                Check(!LocalFiles.IsLocal(@"\\server\music\song.mp3"), "Reject UNC network share");
                Check(!LocalFiles.IsLocal("file:///C:/song.mp3"), "Reject URI input");
                Check(!LocalFiles.IsLocal("relative.mp3"), "Reject relative paths");
                Check(!LocalFiles.IsLocal(first + ":alternate"), "Reject alternate data streams");
                Check(Track.CleanTitle("505 - Arctic Monkeys [MrmPDUvKyLs]") == "505 - Arctic Monkeys" && Track.CleanTitle("Song [Acoustic]") == "Song [Acoustic]", "Legacy YouTube ID suffixes are hidden without removing ordinary version labels");
                var playlistArguments = AudioDownloader.Arguments("https://example.com/playlist", music, true);
                Check(playlistArguments.Contains("--yes-playlist") && !playlistArguments.Contains("--max-downloads") && !playlistArguments.Contains("--playlist-items") && !playlistArguments.Any(a => a.Contains("%(id)")), "Playlist mode removes single-track limits and URL IDs from output names");
                string playlistFolder = AudioDownloader.PlaylistFolder(run, "Night drive");
                Check(Directory.Exists(playlistFolder) && System.IO.Path.GetDirectoryName(playlistFolder) == run && AudioDownloader.PlaylistFolder(run, "Night drive") == playlistFolder, "Playlist name creates and reuses a dedicated child folder");
                foreach (string invalidName in new[] { "", "..", "../escape", "C:\\escape", "CON", "mix." }) { bool refused = false; try { AudioDownloader.PlaylistFolder(run, invalidName); } catch (ArgumentException) { refused = true; } Check(refused, "Reject unsafe playlist folder name: " + invalidName); }
                Check(AudioDownloader.ValidUrl("https://example.com/watch?v=abc&list=def") && !AudioDownloader.ValidUrl("file:///C:/secret") && !AudioDownloader.ValidUrl("--exec calc.exe") && !AudioDownloader.ValidUrl("https://user:password@example.com/song") && !AudioDownloader.ValidUrl("https://example.com/\n--exec"), "Download links reject local files, command options, embedded credentials, and control characters");
                Check(AudioDownloader.Quote("a\"b") == "\"a\\\"b\"" && AudioDownloader.Quote("C:\\music\\") == "\"C:\\music\\\\\"" && AudioDownloader.Quote("") == "\"\"", "Downloader argument quoting handles quotes, trailing slashes, and empty strings");
                var arguments = AudioDownloader.Arguments("https://example.com/?a=1&b=2", music);
                Check(arguments.Contains("--ignore-config") && arguments.Contains("--no-plugin-dirs") && arguments.Contains("--no-cookies-from-browser") && arguments.Contains("--no-remote-components") && arguments[arguments.Count - 2] == "--", "Downloader ignores external configs, browser logins, plugins, and remote components");
                Check(AudioDownloader.CompletedFile("STILL_FILE:\"C:\\\\elsewhere.mp3\"", music) == null && AudioDownloader.CompletedFile("STILL_FILE:not-json", music) == null, "Completed output validates location and JSON before import");
                int skipped; var found = LocalFiles.Scan(new[] { music, first }, out skipped);
                Check(found.Count == 2 && skipped == 0, "Recursive import, non-audio exclusion, and duplicate suppression");
                var missing = LocalFiles.Scan(new[] { System.IO.Path.Combine(music, "missing.wav") }, out skipped);
                Check(missing.Count == 0 && skipped == 1, "Missing files skipped safely");
                double[] peaks = WaveReader.Read(first, 64);
                Check(peaks != null && peaks.Length == 64 && peaks.Max() > .25 && peaks.Max() < .31 && peaks.Min() < .12, "Waveform reflects actual PCM amplitude");
                string badWave = System.IO.Path.Combine(run, "invalid.wav"); File.WriteAllText(badWave, "no audio");
                Check(WaveReader.Read(badWave, 64) == null, "Malformed WAV handled without crashing");
                string updateRoot=System.IO.Path.Combine(run,"Updater fixtures"),installRoot=System.IO.Path.Combine(updateRoot,"install"),packageRoot=System.IO.Path.Combine(updateRoot,"package"),backupRoot=System.IO.Path.Combine(updateRoot,"rollback");Directory.CreateDirectory(installRoot);Directory.CreateDirectory(packageRoot);Directory.CreateDirectory(System.IO.Path.Combine(installRoot,"Data"));Directory.CreateDirectory(System.IO.Path.Combine(installRoot,"DigiMusic"));File.WriteAllText(System.IO.Path.Combine(installRoot,"Digitone.exe"),"old-exe");File.WriteAllText(System.IO.Path.Combine(installRoot,"runtime.dll"),"old-runtime");File.WriteAllText(System.IO.Path.Combine(installRoot,"personal-note.txt"),"mine");File.WriteAllText(System.IO.Path.Combine(installRoot,"Data","library.json"),"personal-library");File.WriteAllText(System.IO.Path.Combine(installRoot,"DigiMusic","song.mp3"),"personal-song");File.WriteAllText(System.IO.Path.Combine(packageRoot,"Digitone.exe"),"new-exe");File.WriteAllText(System.IO.Path.Combine(packageRoot,"runtime.dll"),"new-runtime");File.WriteAllText(System.IO.Path.Combine(packageRoot,"new-runtime.dll"),"new-file");var updateTransaction=new UpdateTransaction(installRoot,backupRoot);updateTransaction.Apply(packageRoot);Check(File.ReadAllText(System.IO.Path.Combine(installRoot,"Digitone.exe"))=="new-exe"&&File.ReadAllText(System.IO.Path.Combine(installRoot,"Data","library.json"))=="personal-library"&&File.ReadAllText(System.IO.Path.Combine(installRoot,"DigiMusic","song.mp3"))=="personal-song"&&File.ReadAllText(System.IO.Path.Combine(installRoot,"personal-note.txt"))=="mine","Updater replaces packaged files while preserving personal and unknown files");updateTransaction.Rollback();Check(File.ReadAllText(System.IO.Path.Combine(installRoot,"Digitone.exe"))=="old-exe"&&File.ReadAllText(System.IO.Path.Combine(installRoot,"runtime.dll"))=="old-runtime"&&!File.Exists(System.IO.Path.Combine(installRoot,"new-runtime.dll")),"Failed update transaction restores replaced files and removes newly created runtime files");
                string protectedPackage=System.IO.Path.Combine(updateRoot,"protected-package");Directory.CreateDirectory(System.IO.Path.Combine(protectedPackage,"Data"));File.WriteAllText(System.IO.Path.Combine(protectedPackage,"Data","library.json"),"attack");bool protectedRejected=false;var protectedTransaction=new UpdateTransaction(installRoot,System.IO.Path.Combine(updateRoot,"protected-backup"));try{protectedTransaction.Apply(protectedPackage);}catch(InvalidDataException){protectedRejected=true;protectedTransaction.Rollback();}Check(protectedRejected&&File.ReadAllText(System.IO.Path.Combine(installRoot,"Data","library.json"))=="personal-library","Updater refuses packages that contain protected personal-data folders");
                string safeZip=System.IO.Path.Combine(updateRoot,"safe.zip"),extractRoot=System.IO.Path.Combine(updateRoot,"extracted");using(var archive=new ZipArchive(File.Create(safeZip),ZipArchiveMode.Create)){var exe=archive.CreateEntry("Digitone-9.0.0/Digitone.exe");using(var writer=new StreamWriter(exe.Open()))writer.Write("safe-exe");var dll=archive.CreateEntry("Digitone-9.0.0/runtime.dll");using(var writer=new StreamWriter(dll.Open()))writer.Write("safe-dll");}string extractedPackage=DigitoneUpdates.ExtractPackage(safeZip,extractRoot);Check(File.Exists(System.IO.Path.Combine(extractedPackage,"Digitone.exe"))&&System.IO.Path.GetFileName(extractedPackage)=="Digitone-9.0.0","Updater safely extracts a single wrapped release package");string checksumFile=System.IO.Path.Combine(updateRoot,"SHA256.txt"),safeHash=DigitoneUpdates.HashFile(safeZip);File.WriteAllText(checksumFile,safeHash+"  Digitone-9.0.0-Windows-x64.zip");Check(DigitoneUpdates.ExpectedHash(checksumFile,"Digitone-9.0.0-Windows-x64.zip")==safeHash,"Updater verifies the checksum entry for the exact release ZIP");
                string unsafeZip=System.IO.Path.Combine(updateRoot,"unsafe.zip");using(var archive=new ZipArchive(File.Create(unsafeZip),ZipArchiveMode.Create)){var escape=archive.CreateEntry("../escape.txt");using(var writer=new StreamWriter(escape.Open()))writer.Write("escape");}bool unsafeRejected=false;try{DigitoneUpdates.ExtractPackage(unsafeZip,System.IO.Path.Combine(updateRoot,"unsafe-extract"));}catch(InvalidDataException){unsafeRejected=true;}Check(unsafeRejected&&!File.Exists(System.IO.Path.Combine(updateRoot,"escape.txt")),"Updater blocks ZIP path traversal before writing outside staging");
                var ordered = Enumerable.Range(0, 40).ToList(); var mixed = ordered.ToList(); LocalFiles.Shuffle(mixed, new Random(42));
                Check(mixed.OrderBy(i => i).SequenceEqual(ordered) && !mixed.SequenceEqual(ordered), "Mix preserves every track and changes order");
                var store = new LibraryStore(System.IO.Path.Combine(run, "Data", "library.json"));
                Check(store.Load().Tracks.Count == 0, "First launch starts empty");
                var testLibrary = store.Load(); testLibrary.GlobalMediaKeys=false; testLibrary.AutoLyrics=false;
                player = new PlayerApp(store, testLibrary);
                player.Window.ShowActivated = false;
                player.Window.Loaded += async delegate
                {
                    try
                    {
                        Snapshot(player.Window, System.IO.Path.Combine(run, "empty.png"));
                        var setup=player.CreateFirstRun();
                        setup.Loaded+=delegate {
                            Elements(setup).OfType<CheckBox>().First(x=>x.Name=="SetupMediaKeys").IsChecked=false;
                            Snapshot(setup,System.IO.Path.Combine(run,"first-run.png"));
                            Elements(setup).OfType<Button>().First(x=>x.Name=="FinishSetup").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        };
                        Check(setup.ShowDialog()==true && store.Load().SetupCompleted,"First-run setup persists completion");
                        Check(player.Data.Tracks.Count==0&&player.Data.MusicFolders.SequenceEqual(new[]{PlayerApp.DigiMusicFolder})&&player.Data.MainMusicFolder==PlayerApp.DigiMusicFolder&&player.Data.DownloadFolder==PlayerApp.DigiMusicFolder&&Directory.Exists(PlayerApp.DigiMusicFolder)&&player.Data.AutoLyrics==false,"First-run creates and selects DigiMusic without scanning or automatic online lookups");
                        Check(!player.Data.Playlists.Any(p=>p.FolderPath==PlayerApp.DigiMusicFolder),"DigiMusic is never represented as a playlist card");
                        Check(NativeChrome.IsDark(player.Window), "Windows title bar is in dark mode");
                        Check(((SolidColorBrush)player.Find<Border>("RecordArt").Background).Color.A == 0, "Record backdrop is transparent");
                        Check(((SolidColorBrush)player.Find<Grid>("DownloadPane").Background).Color.A == 0, "Downloads does not obscure ambient geometry");
                        if (SystemParameters.ClientAreaAnimation)
                        {
                            var pane = player.Find<Grid>("LibraryPane"); Motion.Enter(pane, 40, 0);
                            await Task.Delay(60); var transition=(TranslateTransform)pane.RenderTransform;
                            Check(transition.HasAnimatedProperties, "Panel transition runs through an animation clock");
                            await Task.Delay(350);
                            Check(Math.Abs(((TranslateTransform)pane.RenderTransform).X) < .01 && pane.Opacity == 1, "Panel transition settles without leaving an animation offset");
                            foreach (string name in new[] { "AmbientRing", "AmbientOrbit" })
                            {
                                var rotation = (RotateTransform)player.Find<System.Windows.Shapes.Ellipse>(name).RenderTransform;
                                double before = rotation.Angle; await Task.Delay(100);
                                Check(player.CurrentTitle == null && rotation.Angle != before, name + " moves before any song is loaded");
                            }
                        }
                        Check(player.Find<StackPanel>("EmptyState").Visibility == Visibility.Visible, "Empty state renders");
                        foreach(double hz in new[] { 100.0,1000.0,10000.0,18000.0 })
                        {
                            var signal=new double[FrequencyBands.WindowSize]; for(int i=0;i<signal.Length;i++) signal[i]=.4*Math.Sin(2*Math.PI*hz*i/FrequencyBands.SampleRate);
                            var bands=FrequencyBands.Analyze(signal); int expected=hz<250?0:hz<4000?1:2;
                            Check(bands[expected]>.2 && bands.Where((v,i)=>i!=expected).All(v=>v<.02),hz+" Hz drives only the correct Pulse band");
                        }
                        Check(FrequencyBands.Analyze(new double[FrequencyBands.WindowSize]).All(v=>v==0),"Silence produces zero energy in all Pulse bands");
                        Check(EqualizerSampleProvider.TestBandResponse(910,2,6)>5.5,"Equalizer applies a real six-decibel mid-band boost");
                        Check(Math.Abs(EqualizerSampleProvider.TestBandResponse(910,2,0))<.05,"Flat equalizer response remains neutral");
                        var pulseScene=new AudioScene { Mode="Pulse" }; pulseScene.SetFrame(new double[384],new[] { .2,0.0,0.0 });
                        Check(pulseScene.BandLevels[0]>0 && pulseScene.BandLevels[1]==0 && pulseScene.BandLevels[2]==0,"Pulse rings respond independently");
                        pulseScene.SetFrame(null); Check(pulseScene.BandLevels.All(v=>v==0),"Pause clears Pulse ring energy");
                        player.SetVisualizer("Ribbon"); Check(player.ActiveVisualizer=="Prism","Saved Ribbon selection migrates to Prism"); player.SetVisualizer("Waveform");
                        string trebleFixture=System.IO.Path.Combine(run,"treble-analysis.wav"); Tone(trebleFixture,10000);
                        using(var analyzer=new LiveWaveSource(trebleFixture))
                        {
                            for(int i=0;i<100 && analyzer.Sample(.5)==null;i++) await Task.Delay(50);
                            var bands=analyzer.SampleBands(.5);
                            Check(bands!=null && bands[2]>.08 && bands[0]<.01 && bands[1]<.01,"Actual decoded audio retains treble above the old 4 kHz limit");
                        }
                        string openedFolder = null;
                        var folderLink = PlayerApp.FolderLink(player.Window,music,path => openedFolder = path);
                        ((System.Windows.Documents.Hyperlink)folderLink.Inlines.FirstInline).RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
                        Check(openedFolder == music, "Folder links activate the exact local directory");
                        var titlePlate = (Border)player.Find<TextBlock>("CollectionTitle").Parent;
                        Check(titlePlate.HorizontalAlignment == HorizontalAlignment.Left && titlePlate.ActualWidth < player.Find<Grid>("LibraryPane").ActualWidth * .8, "Collection heading background fits its word instead of the panel");
                        Check(!player.Find<Button>("PlayButton").IsEnabled && !player.Find<Button>("NextButton").IsEnabled && !player.Find<Slider>("Seek").IsEnabled, "Transport is disabled before a song is selected");
                        player.TogglePlay(); Check(player.CurrentTitle == null, "Space does not choose a song implicitly");
                        await player.Import(new[] { music });
                        await player.Import(new[] { first });
                        Check(player.Data.Tracks.Count == 2, "UI folder import and repeated import do not duplicate tracks");
                        Check(player.Find<ListBox>("Tracks").Items.Count == 2, "Library renders imported tracks");
                        Check(LyricsLookup.Address("A & B","A/B").EndsWith("A%20%26%20B/A%2FB"),"Lyrics query safely encodes artist and title");
                        Check(LyricsLookup.Parse("{\"lyrics\":\"Test line one\\nTest line two\"}").Contains(Environment.NewLine),"Lyrics service response preserves line breaks");
                        bool emptyLyrics=false; try { LyricsLookup.Parse("{\"error\":\"missing\"}"); } catch(IOException) { emptyLyrics=true; }
                        Check(emptyLyrics,"Missing lyrics response is handled without replacing the preview");
                        var lyricsTrack=player.Data.Tracks[0]; var lyricsDialog=player.CreateLyrics(lyricsTrack); Check(lyricsDialog.ResizeMode==ResizeMode.NoResize,"Lyrics window removes minimize and maximize controls"); lyricsDialog.Show(); lyricsDialog.UpdateLayout();
                        var lyricsEditor=Elements(lyricsDialog).OfType<TextBox>().First(x=>x.Name=="LyricsText"); lyricsEditor.Text="Original test words";
                        Check(String.IsNullOrEmpty(lyricsTrack.Lyrics),"Lyrics preview does not save implicitly");
                        Elements(lyricsDialog).OfType<Button>().First(x=>x.Name=="SaveLyrics").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(store.Load().Tracks[0].Lyrics=="Original test words","Lyrics save persists with the selected track");
                        Snapshot(lyricsDialog,System.IO.Path.Combine(run,"lyrics.png")); lyricsDialog.Close();
                        var autoTrack=player.Data.Tracks[1]; string originalArtist=autoTrack.Artist; autoTrack.Artist="Test artist";
                        player.Data.AutoLyrics=true; player.FetchLyrics=(a,t,c)=>"Automatically found test words"; player.AutoFindLyrics(autoTrack);
                        for(int wait=0;wait<40 && String.IsNullOrEmpty(autoTrack.Lyrics);wait++) await Task.Delay(100);
                        Check(autoTrack.Lyrics=="Automatically found test words" && store.Load().Tracks[1].Lyrics==autoTrack.Lyrics,"Automatic lyrics lookup saves a result to the correct track");
                        player.FetchLyrics=(a,t,c)=>"Replacement"; player.AutoFindLyrics(autoTrack); await Task.Delay(50);
                        Check(autoTrack.Lyrics=="Automatically found test words","Automatic lookup preserves existing lyrics");
                        player.Data.AutoLyrics=false; player.FetchLyrics=LyricsLookup.Fetch; autoTrack.Artist=originalArtist;
                        Check(!player.Find<Button>("PlayButton").IsEnabled, "Import alone does not enable Play");
                        player.Find<TextBox>("Search").Text = "golden";
                        Check(player.Find<ListBox>("Tracks").Items.Count == 1, "Search filters by filename");
                        player.Find<TextBox>("Search").Text = "no matches";
                        Check(player.Find<StackPanel>("EmptyState").Visibility == Visibility.Visible, "Search has a meaningful empty state");
                        player.Find<TextBox>("Search").Text = "";
                        player.Find<ListBox>("Tracks").SelectedIndex = 0; Press(player, "FavoriteButton"); Press(player, "FavoritesButton");
                        Check(player.Find<ListBox>("Tracks").Items.Count == 1 && player.Data.Tracks[0].Favorite, "Favorite action and favorites collection");
                        Press(player, "LibraryButton");
                        var playlist = player.CreatePlaylist("A quieter place", player.Data.Tracks);
                        Check(player.Find<ListBox>("Playlists").Items.Count == 0 && !playlist.Favorite && playlist.Paths.Count == 2, "New manual playlist stays out of quick access by default");
                        var quickPlaylistCard=player.Find<WrapPanel>("PlaylistGrid").Children.OfType<Button>().Single(b=>b.Tag==playlist);var quickAccess=(MenuItem)quickPlaylistCard.ContextMenu.Items.Cast<object>().First(i=>Convert.ToString(((MenuItem)i).Header)=="Add to quick access");quickAccess.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(playlist.Favorite&&player.Find<ListBox>("Playlists").Items.Count==1,"Playlist enters quick access only after the explicit menu action");
                        var contextTarget=new Playlist{Name="Context target",Paths=new List<string>(),Favorite=false};player.Data.Playlists.Add(contextTarget);player.Find<ListBox>("Tracks").SelectedItems.Clear();foreach(Track track in player.Data.Tracks)player.Find<ListBox>("Tracks").SelectedItems.Add(track);var trackContext=player.Find<ListBox>("Tracks").ContextMenu;trackContext.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));var addSubmenu=trackContext.Items.Cast<MenuItem>().Single(i=>Convert.ToString(i.Header)=="Add to playlist");addSubmenu.ApplyTemplate();Check(addSubmenu.Items.Cast<MenuItem>().Any(i=>Convert.ToString(i.Header)==contextTarget.Name)&&addSubmenu.Template.FindName("PART_Popup",addSubmenu)!=null,"Song context menu exposes a working playlist dropdown");((MenuItem)addSubmenu.Items.Cast<MenuItem>().First(i=>Convert.ToString(i.Header)==contextTarget.Name)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(contextTarget.Paths.Count==2,"Ctrl-style multi-selection batch adds every selected song to a playlist");player.Data.Playlists.Remove(contextTarget);store.Save(player.Data);
                        var contextMenu = player.Find<ListBox>("Playlists").ContextMenu; contextMenu.PlacementTarget = player.Find<ListBox>("Playlists"); contextMenu.IsOpen = true; await Task.Delay(100); contextMenu.UpdateLayout();
                        var menuBitmap = new RenderTargetBitmap((int)contextMenu.ActualWidth, (int)contextMenu.ActualHeight, 96, 96, PixelFormats.Pbgra32); menuBitmap.Render(contextMenu); var menuEncoder = new PngBitmapEncoder(); menuEncoder.Frames.Add(BitmapFrame.Create(menuBitmap)); using (var menuStream = File.Create(System.IO.Path.Combine(run, "playlist-menu.png"))) menuEncoder.Save(menuStream);
                        var menuItem = (MenuItem)contextMenu.Items[0]; menuItem.ApplyTemplate(); Check(menuItem.Template.FindName("MenuSurface", menuItem) != null, "Playlist context menu uses the custom dark template without an icon gutter"); contextMenu.IsOpen = false;
                        player.Find<ListBox>("Tracks").SelectedItems.Clear();player.Find<ListBox>("Tracks").SelectedIndex = 0;var draggedTrack=(Track)player.Find<ListBox>("Tracks").SelectedItem;Check(player.ReorderPlaylistTracks(new[]{draggedTrack},2)&&playlist.Paths[1]==first,"Playlist drag can place a song at the exact end");Check(player.ReorderPlaylistTracks(new[]{draggedTrack},0)&&playlist.Paths[0]==first,"Playlist drag can place a song at the exact beginning");Check(player.ReorderPlaylistTracks(new[]{draggedTrack},2)&&playlist.Paths[1]==first,"Playlist track ordering");
                        Press(player, "RemoveButton");
                        Check(playlist.Paths.Count == 1 && player.Data.Tracks.Count == 2 && File.Exists(first), "Playlist removal preserves the library and original audio");
                        var restored = store.Load();
                        var restoredPlaylist=restored.Playlists.FirstOrDefault(p=>p.Name==playlist.Name);
                        Check(restoredPlaylist!=null && restoredPlaylist.Paths.SequenceEqual(playlist.Paths) && restored.Tracks[0].Favorite, "Playlists, ordering, and favorites persist to disk");
                        Check(File.Exists(System.IO.Path.Combine(run, "Data", "library.json.bak")), "Atomic save keeps a previous library backup");
                        Press(player, "LibraryButton");
                        player.Find<ListBox>("Tracks").SelectedIndex = 0;
                        AcceptDialog(player, ""); Press(player, "AddToPlaylistButton");
                        Check(playlist.Paths.Count == 2, "Add-to-playlist dialog adds selected tracks");
                        Press(player, "MuteButton");
                        player.Find<ListBox>("Tracks").SelectedIndex = 1;
                        string chosenTitle = ((Track)player.Find<ListBox>("Tracks").SelectedItem).Title;
                        Check(player.Find<Button>("PlayButton").IsEnabled, "Selecting a track enables Play");
                        Press(player, "PlayButton");
                        Check(!player.Find<Button>("PlayButton").IsEnabled, "Play is disabled while audio opens");
                        player.TogglePlay();
                        for (int i = 0; i < 100 && !player.MediaReady; i++) await Task.Delay(100);
                        await Task.Delay(350);
                        Check(player.CurrentTitle == chosenTitle && player.IsPlaying && player.Position > 0, "Play starts the selected track; repeated input while loading does not pause it");
                        player.DispatchMediaCommand(47); player.DispatchMediaCommand(47); Check(!player.IsPlaying,"Hardware Pause is idempotent");
                        player.DispatchMediaCommand(46); player.DispatchMediaCommand(46); Check(player.IsPlaying,"Hardware Play is idempotent");
                        player.DispatchMediaCommand(13); Check(!player.IsPlaying && player.Position<.2,"Hardware Stop pauses and rewinds without clearing the track");
                        player.DispatchMediaCommand(14); Check(player.IsPlaying,"Hardware play/pause resumes playback");
                        SendMessage(new System.Windows.Interop.WindowInteropHelper(player.Window).Handle,0x319,IntPtr.Zero,new IntPtr(47 << 16));
                        Check(!player.IsPlaying,"Native Windows media command reaches the player window");
                        player.DispatchMediaCommand(46);
                        Check(player.Window.FindName("MixButton")==null,"Shuffle mix button is removed");
                        Press(player,"LyricsButton"); Check(player.LyricsVisible,"Lyrics toggles an in-app display on");
                        Snapshot(player.Window,System.IO.Path.Combine(run,"lyrics-display.png"));
                        Press(player,"LyricsButton"); Check(!player.LyricsVisible,"Lyrics toggles the display off");
                        Check(PlayerApp.LyricsProgress(0,180)==0 && PlayerApp.LyricsProgress(90,180)==.5 && PlayerApp.LyricsProgress(180,180)==1,"Lyrics progress stays bounded and follows song duration");
                        Check(PlayerApp.DisplayLyrics("[00:12.50]Test words")=="Test words","Imported LRC timestamps are hidden in the reading view");
                        Press(player, "LibraryButton"); Press(player, "PlayAllButton");
                        for (int i = 0; i < 100 && !player.MediaReady; i++) await Task.Delay(100);
                        Check(player.MediaReady, "Windows audio engine opened a real local WAV");
                        await Task.Delay(500);
                        Check(player.IsPlaying && player.Position > 0, "Audio playback clock advances");
                        player.ChangeAudioOutput("missing-test-device");await Task.Delay(350);Check(player.MediaReady&&player.IsPlaying&&player.Position>0&&store.Load().AudioOutputId=="missing-test-device","Unavailable saved output falls back to Windows default without silencing playback");player.ChangeAudioOutput("");await Task.Delay(250);Check(player.MediaReady&&player.IsPlaying&&store.Load().AudioOutputId=="","Changing output reopens the current song and persists the selection");
                        Press(player, "PlayButton"); double position = player.Position; await Task.Delay(300);
                        Check(!player.IsPlaying && Math.Abs(player.Position - position) < .15, "Pause stops playback advancement");
                        player.Find<Slider>("Seek").Value = 3; await Task.Delay(200);
                        Check(Math.Abs(player.Position - 3) < .3, "Seek updates actual audio position");
                        Press(player, "PlayButton"); Check(player.IsPlaying, "Resume playback");
                        player.BeginSeekInteraction();player.Find<Slider>("Seek").Value=4;await Task.Delay(20);double heldSeek=player.Position;await Task.Delay(20);Check(Math.Abs(player.Position-heldSeek)<.12,"Scrubber pauses audio while its position is moving");player.EndSeekInteraction();await Task.Delay(20);Check(Math.Abs(player.Position-heldSeek)<.12,"Scrubber waits before resuming after release");await Task.Delay(90);Check(player.Position>heldSeek+.02,"Scrubber resumes playback after the 40 ms debounce");
                        player.DispatchMediaCommand(11);
                        for (int i = 0; i < 100 && !player.MediaReady; i++) await Task.Delay(100);
                        Check(player.MediaReady && player.CurrentTitle == "02 - Slow mornings", "Next selects and opens the next track");
                        player.DispatchMediaCommand(12);
                        for (int i = 0; i < 100 && !player.MediaReady; i++) await Task.Delay(100);
                        Check(player.CurrentTitle == "01 - Golden hour", "Previous returns to the preceding track");
                        player.Find<Slider>("Seek").Value=7.6;await Task.Delay(1200);Check(player.CurrentTitle=="02 - Slow mornings"&&player.IsPlaying,"Natural queue completion reliably advances to the next song");player.DispatchMediaCommand(12);for(int i=0;i<100&&(!player.MediaReady||player.CurrentTitle!="01 - Golden hour");i++)await Task.Delay(50);
                        Press(player, "ShuffleButton"); Press(player, "RepeatButton"); Press(player, "RepeatButton");
                        Check(player.Find<Button>("RepeatButton").Content.ToString() == "↻ 1", "Repeat cycles to repeat one");
                        player.Find<Slider>("Seek").Value = 7.6; await Task.Delay(1200);
                        Check(player.CurrentTitle == "01 - Golden hour" && player.IsPlaying && player.Position < 2, "Audio end event repeats the current track");
                        player.Find<Slider>("Volume").Value = .35;
                        Check(Math.Abs(player.Data.Volume - .35) < .001, "Volume control updates stored preference");
                        await Task.Delay(650);
                        Check(Math.Abs(store.Load().Volume - .35) < .001, "Volume persists automatically after adjustment");
                        var reopened = new PlayerApp(store, store.Load());
                        Check(Math.Abs(reopened.Find<Slider>("Volume").Value - .35) < .001, "Reopened player restores the saved volume"); reopened.Window.Close();
                        for (int i = 0; i < 100 && player.Find<Canvas>("Waveform").Children.OfType<System.Windows.Shapes.Polyline>().First().Points.Count < 384; i++) await Task.Delay(50);
                        Check(player.Find<Canvas>("Waveform").Children.OfType<System.Windows.Shapes.Polyline>().First().Points.Count == 384, "Live waveform renders actual signed audio samples");
                        Snapshot(player.Window, System.IO.Path.Combine(run, "playing.png"));
                        Check(player.Data.Tracks.Count(t=>t.IsPlaying)==1&&player.Data.Tracks.Single(t=>t.IsPlaying).Title==player.CurrentTitle,"Exactly one library row tracks the currently playing song");
                        if (SystemParameters.ClientAreaAnimation)
                        {
                            var ring = (RotateTransform)player.Find<System.Windows.Shapes.Ellipse>("AmbientRing").RenderTransform;
                            double angle = ring.Angle; await Task.Delay(100);
                            Check(ring.Angle != angle, "Ambient geometry moves during playback");
                            player.TogglePlay(); angle = ring.Angle; await Task.Delay(100); Check(ring.HasAnimatedProperties && ring.Angle != angle, "Ambient animation continues while paused"); player.TogglePlay();
                        }
                        Press(player,"QueueToggleButton"); Check(player.Find<Border>("QueueDrawer").Visibility == Visibility.Visible,"Queue opens in its dedicated drawer"); Press(player,"CloseQueueButton"); Check(player.Find<Border>("QueueDrawer").Visibility == Visibility.Collapsed,"Queue drawer closes without changing playback");
                        player.Window.Width = 920; player.Window.Height = 660; await Task.Delay(100);
                        // Explicitly lay out the client area: a hidden test process can defer native resize messages.
                        player.ApplyCompactLayout(660,920);
                        var content = (FrameworkElement)player.Window.Content;
                        content.Measure(new Size(904, 621)); content.Arrange(new Rect(0, 0, 904, 621));
                        Snapshot(player.Window, System.IO.Path.Combine(run, "compact.png"));
                        Check(player.Find<ListBox>("Tracks").ActualHeight > 60, "Minimum window size leaves a usable track list");
                        Check(player.Find<Border>("RecordArt").ClipToBounds&&Grid.GetRow(player.Find<Border>("RecordArt"))==1&&Grid.GetRowSpan(player.Find<Border>("RecordArt"))==1&&player.Find<Border>("RecordArt").MinHeight==0,"Record stays inside its dedicated fluid layout row without entering song information");
                        player.Window.Width = 1000; player.Window.Height = 1600; player.ApplyCompactLayout(1600,1000);
                        content.Measure(new Size(984,1561)); content.Arrange(new Rect(0,0,984,1561)); await Task.Delay(100);
                        player.ApplyCompactLayout(1600,1000); content.Measure(new Size(984,1561)); content.Arrange(new Rect(0,0,984,1561));
                        Check(Grid.GetRow(player.Find<Grid>("LibraryPane")) == 1 && Grid.GetColumnSpan(player.Find<Border>("NowPane")) == 2, "Portrait windows stack the player above the library");
                        Check(player.Find<Canvas>("AmbientCanvas").Children.Count > 6,"Portrait layout includes the preset scene");
                        var roundRing = player.Find<System.Windows.Shapes.Ellipse>("AmbientRing");
                        Check(Math.Abs(roundRing.ActualWidth-roundRing.ActualHeight)<1 && player.Find<Canvas>("AmbientCanvas").Parent == player.Find<Grid>("ContentStage"), "Ambient circles retain equal axes without a stretching viewbox");
                        Snapshot(player.Window,System.IO.Path.Combine(run,"portrait.png"));
                        player.Window.Width = 920; player.Window.Height = 660; player.ApplyCompactLayout(660,920); content.Measure(new Size(904,621)); content.Arrange(new Rect(0,0,904,621));
                        Check(Grid.GetRow(player.Find<Grid>("LibraryPane")) == 0 && Grid.GetColumn(player.Find<Grid>("LibraryPane")) == 1, "Landscape layout restores after portrait resizing");
                        player.ApplyCompactLayout(1440,2560);
                        Check(Grid.GetRow(player.Find<Grid>("LibraryPane")) == 0 && Grid.GetColumn(player.Find<Grid>("LibraryPane")) == 1 && Grid.GetColumnSpan(player.Find<Border>("NowPane")) == 1, "Large landscape uses actual dimensions instead of the smaller restored window width");
                        content.Measure(new Size(2544,1401)); content.Arrange(new Rect(0,0,2544,1401));
                        Snapshot(player.Window,System.IO.Path.Combine(run,"large-landscape.png"));
                        player.ApplyCompactLayout(660,920); content.Measure(new Size(904,621)); content.Arrange(new Rect(0,0,904,621));
                        Check(player.WaveRenderCount > 0, "Waveform updates through display rendering callbacks");
                        player.Next(true); // advance to the final track
                        Press(player, "RepeatButton"); Press(player,"ShuffleButton"); player.Next(true);
                        Check(player.IsPlaying && player.CurrentTitle == "01 - Golden hour", "All Music restarts from its first song after natural queue completion");
                        var playlistCycle=playlist.Paths.Select(path=>player.Data.Tracks.First(t=>t.Path==path)).ToList();player.StartQueue(playlistCycle,playlistCycle.Last());player.Next(true);Check(player.IsPlaying&&player.CurrentTitle==playlistCycle[0].Title,"Saved playlists restart from their first song after natural queue completion");var unavailable=new Track{Path=System.IO.Path.Combine(run,"missing-queue-track.wav"),SongTitle="Unavailable queue fixture"};player.StartQueue(new[]{unavailable,playlistCycle[0]},unavailable);for(int i=0;i<100&&player.CurrentTitle!=playlistCycle[0].Title;i++)await Task.Delay(30);Check(player.CurrentTitle==playlistCycle[0].Title&&playlistCycle[0].IsPlaying&&!unavailable.IsPlaying,"Unavailable queued files advance to the next playable song");
                        string corruptPath = System.IO.Path.Combine(run, "corrupt.json"); File.WriteAllText(corruptPath, "{ broken");
                        bool rejected = false; try { new LibraryStore(corruptPath).Load(); } catch { rejected = true; }
                        Check(rejected && File.ReadAllText(corruptPath) == "{ broken", "Corrupt library is rejected without overwriting it");
                        Press(player, "DownloadsButton");
                        Check(player.Find<Grid>("DownloadPane").Visibility == Visibility.Visible && player.Find<Grid>("LibraryPane").Visibility == Visibility.Collapsed, "Downloads tab switches without replacing playback controls");
                        player.Find<TextBox>("DownloadUrl").Text = "file:///C:/secret"; Press(player, "DownloadStart");
                        await player.Downloads.ActiveTask;
                        Check(!player.Downloads.Busy && player.Find<TextBlock>("DownloadStatus").Text.Contains("HTTP"), "Invalid download URL is rejected in the interface");
                        Snapshot(player.Window, System.IO.Path.Combine(run, "downloads.png"));
                        if (AudioDownloader.ToolsReady)
                        {
                            string destination = System.IO.Path.Combine(run, "Downloaded audio"); Directory.CreateDirectory(destination);
                            player.Data.DownloadFolder = destination; player.Find<TextBlock>("DownloadFolder").Text = destination;
                            using (var server = new LocalAudioServer(first, false))
                            {
                                player.Find<TextBox>("DownloadUrl").Text = server.Url; Press(player, "DownloadStart");
                                var completed = await Task.WhenAny(player.Downloads.ActiveTask, Task.Delay(45000));
                                if (completed != player.Downloads.ActiveTask) { player.Downloads.Cancel(); await player.Downloads.ActiveTask; throw new Exception("Local download integration test timed out"); }
                                await player.Downloads.ActiveTask;
                            }
                            File.WriteAllText(System.IO.Path.Combine(run,"download-debug.txt"),player.Find<TextBlock>("DownloadStatus").Text+Environment.NewLine+player.Find<TextBox>("DownloadLog").Text);
                            Check(!player.Downloads.Busy && Directory.GetFiles(destination, "*.mp3").Length == 1, "Actual yt-dlp and FFmpeg download a local fixture and produce MP3");
                            var downloaded = player.Data.Tracks.FirstOrDefault(t => t.Path.StartsWith(destination, StringComparison.OrdinalIgnoreCase));
                            Check(downloaded != null && player.Find<TextBlock>("DownloadStatus").Text.Contains("added to All music"), "Finished MP3 automatically joins the library");
                            player.StartQueue(new[] { downloaded }, null);
                            for (int i = 0; i < 100 && !player.MediaReady; i++) await Task.Delay(100);
                            Check(player.MediaReady && player.IsPlaying, "Converted MP3 plays in the Windows audio engine");
                            int hiddenFrameCount = player.WaveRenderCount; await Task.Delay(100);
                            Check(player.WaveRenderCount == hiddenFrameCount, "Hidden waveform skips rendering work on Downloads");
                            Press(player,"LibraryButton");
                            for (int i = 0; i < 100 && player.Find<Canvas>("Waveform").Children.OfType<System.Windows.Shapes.Polyline>().First().Points.Count < 384; i++) await Task.Delay(50);
                            Check(player.Find<Canvas>("Waveform").Children.OfType<System.Windows.Shapes.Polyline>().First().Points.Count == 384, "Downloaded MP3 displays a live line waveform");
                            Press(player, "ExpandWaveformButton"); await Task.Delay(100);
                            Check(player.Find<Grid>("ExpandedView").Visibility == Visibility.Visible && player.Find<Canvas>("ExpandedWaveform").Children.OfType<System.Windows.Shapes.Polyline>().Last().Points.Count == 384, "Expanded live waveform renders a glowing line instead of overview bars");
                            var hiddenPoints=player.Find<Canvas>("Waveform").Children.OfType<System.Windows.Shapes.Polyline>().First().Points;
                            var hiddenRotation=(RotateTransform)player.Find<Grid>("RecordDisc").RenderTransform;
                            var hiddenRing=(RotateTransform)player.Find<System.Windows.Shapes.Ellipse>("AmbientRing").RenderTransform;
                            double recordAngle=hiddenRotation.Angle,ambientAngle=hiddenRing.Angle; int framesBefore=player.WaveRenderCount;
                            await Task.Delay(120);
                            Check(Object.ReferenceEquals(hiddenPoints,player.Find<Canvas>("Waveform").Children.OfType<System.Windows.Shapes.Polyline>().First().Points),"Expanded view skips hidden main waveform geometry");
                            Check(hiddenRotation.Angle==recordAngle && Math.Abs(hiddenRing.Angle-ambientAngle)<.01,"Expanded view pauses hidden record and background motion");
                            Check(player.WaveRenderCount>framesBefore && player.IsPlaying,"Visible visualizer and audio keep updating while hidden visuals pause");
                            player.ShowExpanded(false); await Task.Delay(120); double resumed=hiddenRing.Angle; await Task.Delay(100);
                            Check(hiddenRotation.HasAnimatedProperties && hiddenRing.Angle!=resumed,"Returning to the player resumes record and background motion");
                            player.ShowExpanded(true); await Task.Delay(100);
                            player.Find<Slider>("Seek").Value = 4; await Task.Delay(150); Check(player.Position >= 3.8 && player.Position < 4.5, "Progress-bar seeking updates playback during live visualization");
                            Snapshot(player.Window, System.IO.Path.Combine(run, "expanded-waveform.png"));
                            foreach(string mode in PlayerApp.VisualizerModes.Where(m=>m!="Waveform"))
                            {
                                int rendered=player.SceneRenderCount; Press(player,"Mode"+mode); await Task.Delay(150);
                                Check(player.ActiveVisualizer==mode && player.SceneRenderCount>rendered,mode+" visualizer renders in the expanded view");
                                Check(store.Load().VisualizerMode==mode,mode+" visualizer selection persists");
                                Snapshot(player.Window,System.IO.Path.Combine(run,"visualizer-"+mode.ToLowerInvariant()+".png"));
                            }
                            Press(player,"ModeWaveform"); await Task.Delay(100);
                            foreach (string theme in Themes.Names)
                            {
                                player.ApplyTheme(theme); await Task.Delay(100); Color themeAccent = ((SolidColorBrush)Themes.Brush(player.Window, "C1D3A8")).Color;
                                Check(themeAccent == Themes.Map((Color)ColorConverter.ConvertFromString("#C1D3A8"),theme), theme + " theme updates accent resources");
                                Check(((SolidColorBrush)player.Window.Background).Color == Themes.Map((Color)ColorConverter.ConvertFromString("#161917"), theme), theme + " theme updates window background");
                                Check(((SolidColorBrush)player.Find<Button>("LibraryButton").BorderBrush).Color == themeAccent, theme + " theme gives active navigation an accent outline");
                                Snapshot(player.Window, System.IO.Path.Combine(run, "theme-" + theme.ToLowerInvariant() + ".png"));
                            }
                            store.Save(player.Data); Check(store.Load().ThemeName == "Sanctuary", "Theme selection persists across library reloads");
                            player.ShowExpanded(false);
                            foreach(string theme in Themes.Names.Concat(new[]{"Custom"}))
                            {
                                player.ApplyTheme(theme); await Task.Delay(80);
                                var actionButton=player.Find<Button>("LyricsButton"); actionButton.ApplyTemplate();
                                var surface=(Border)actionButton.Template.FindName("Surface",actionButton);
                                Check(((SkewTransform)surface.RenderTransform).AngleX==-8 && surface.CornerRadius==new CornerRadius(0),theme+" retains the original sharp slanted buttons");
                                Snapshot(player.Window,System.IO.Path.Combine(run,"layout-"+theme+".png"));
                            }
                            Check(Themes.Names.Select(n=>ThemeScenes.Build(n,600).Children.Count).Distinct().Count()>=3,"Presets use distinct composed scenes instead of repeated emblems");
                            var sanctuaryLight=ThemeScenes.Build("Sanctuary",600,null,true,true);var sanctuaryDark=ThemeScenes.Build("Sanctuary",600,null,false,true);var sanctuaryImage=Elements(sanctuaryDark).OfType<Image>().Single(i=>i.Name=="SanctuaryPlant");var sanctuaryBitmap=(BitmapSource)sanctuaryImage.Source;var sanctuaryPixel=new byte[4];sanctuaryBitmap.CopyPixels(new Int32Rect(0,0,1,1),sanctuaryPixel,4,0);Check(Elements(sanctuaryLight).OfType<System.Windows.Shapes.Ellipse>().Any(e=>e.Name=="SanctuarySun")&&!Elements(sanctuaryDark).OfType<System.Windows.Shapes.Ellipse>().Any(e=>e.Name=="SanctuarySun")&&Elements(sanctuaryDark).OfType<Canvas>().Any(e=>e.Name=="SanctuaryStars"),"Tree House Sanctuary switches between a light-mode sun and dark-mode night sky");var sanctuaryShrubs=Elements(sanctuaryDark).OfType<Canvas>().Single(e=>e.Name=="SanctuaryShrubs");var wideSanctuary=ThemeScenes.Build("Sanctuary",340,null,false,true);var wideShrubs=Elements(wideSanctuary).OfType<Canvas>().Single(e=>e.Name=="SanctuaryShrubs");Check(sanctuaryShrubs.Children.OfType<System.Windows.Shapes.Ellipse>().Any(e=>Canvas.GetTop(e)<520)&&wideShrubs.Children.OfType<System.Windows.Shapes.Ellipse>().Any(e=>Canvas.GetTop(e)<260),"Sanctuary shrub canopy remains visible in portrait and landscape scenes");Check(Elements(sanctuaryLight).OfType<Image>().Count(i=>i.Name=="SanctuaryPlant")==1&&Elements(sanctuaryDark).OfType<Image>().Count(i=>i.Name=="SanctuaryPlant")==1&&sanctuaryPixel[3]==0,"Sanctuary uses exactly one transparently processed supplied plant");player.Data.AppearanceMode="Dark";player.ApplyTheme("Sanctuary");Check(((FontFamily)player.Window.Resources["ThemeDisplayFont"]).Source.Contains("Segoe Print"),"Tree House Sanctuary uses lo-fi display typography");player.Data.AppearanceMode="Light";player.ApplyTheme("Sanctuary");await Task.Delay(80);Snapshot(player.Window,System.IO.Path.Combine(run,"sanctuary-light.png"));player.Data.AppearanceMode="Dark";player.ApplyTheme("Sanctuary");
                            player.ApplyTheme("Custom"); Check(player.Find<System.Windows.Shapes.Ellipse>("AmbientRing").Visibility==Visibility.Visible,"Custom restores original circle geometry");
                            player.ShowExpanded(true); player.ApplyTheme("Overworld");
                            WindowState previousState = player.Window.WindowState; WindowStyle previousStyle = player.Window.WindowStyle; Press(player, "FullscreenButton");
                            Check(player.Window.WindowState == WindowState.Maximized && player.Window.WindowStyle == WindowStyle.None, "Waveform enters borderless fullscreen");
                            Press(player, "FullscreenButton"); Check(player.Window.WindowState == previousState && player.Window.WindowStyle == previousStyle, "Leaving fullscreen restores the previous window state");
                            Press(player, "CloseExpandedButton"); Press(player, "MainVisualizerOffButton"); Check(player.Find<Canvas>("Waveform").Visibility == Visibility.Collapsed && player.Find<Canvas>("MainSpectrum").Visibility==Visibility.Collapsed, "Main visualizer can be turned off"); Press(player, "VisualizerButton");
                            player.TogglePlay();
                            Check(player.Find<Canvas>("Waveform").Children.OfType<System.Windows.Shapes.Polyline>().Single().Points.All(p => Math.Abs(p.Y - player.Find<Canvas>("Waveform").ActualHeight / 2) < .01), "Paused playback flattens the visible wave immediately");
                            var discRotation = (RotateTransform)player.Find<Grid>("RecordDisc").RenderTransform;
                            double pausedAngle = discRotation.Angle; await Task.Delay(100); Check(Math.Abs(pausedAngle - discRotation.Angle) < .01, "Turntable stops rotating while paused");
                            player.RememberMusicFolders(new[] { run, run }); player.Data.MainMusicFolder = run;player.Data.Playlists.Add(new Playlist{Name="Generated main root",FolderPath=run,Paths=player.Data.Tracks.Select(t=>t.Path).ToList()});Check(player.RemoveMainFolderPlaylist()&&!player.Data.Playlists.Any(p=>p.Name=="Generated main root"),"Main music root is removed from the playlist grid");await player.Import(new[]{run},true,true);Check(!player.Data.Playlists.Any(p=>!String.IsNullOrEmpty(p.FolderPath)&&String.Equals(System.IO.Path.GetFullPath(p.FolderPath).TrimEnd('\\'),System.IO.Path.GetFullPath(run).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)),"Importing the main music root never recreates its playlist");store.Save(player.Data);
                            Check(store.Load().MainMusicFolder == run && store.Load().MusicFolders.Count(p => p == run) == 1, "Music folder settings persist without duplicate roots");
                            var settingsWindow = player.CreateSettings(); Check(settingsWindow.ResizeMode==ResizeMode.NoResize,"Settings window removes minimize and maximize controls"); settingsWindow.Show(); await Task.Delay(150); Snapshot(settingsWindow, System.IO.Path.Combine(run, "settings.png")); Check(settingsWindow.Icon != null, "Settings and main window use the Digitone application icon"); var hexInput = Elements(settingsWindow).OfType<TextBox>().Single(e => e.Name == "CustomColorHex");
                            var applyCustom = Elements(settingsWindow).OfType<Button>().Single(e => e.Name == "ApplyCustomColor");
                            hexInput.Text = "#nothex"; applyCustom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(player.Data.ThemeName == "Overworld", "Invalid custom colors leave the active theme unchanged");
                            hexInput.Text = "#b4e"; applyCustom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Check(player.Data.ThemeName == "Custom" && store.Load().CustomThemeColor == "#BB44EE" && store.Load().ThemeName == "Custom", "Custom hex color applies and persists in normalized form");
                            foreach (string extreme in new[] { "#000000", "#FFFFFF", "#FF0000" }) { Color mapped = Themes.Map((Color)ColorConverter.ConvertFromString("#C1D3A8"),"Custom",extreme); Check(.2126*mapped.R + .7152*mapped.G + .0722*mapped.B >= 180, "Custom accent stays readable for " + extreme); }
                            var cards = Elements(settingsWindow).OfType<Button>().Where(b => b.Tag is string && Themes.Names.Contains((string)b.Tag)).ToArray();
                            var themeScroll=Elements(settingsWindow).OfType<ScrollViewer>().Single(s=>s.Name=="ThemePreviewScroll");
                            Check(cards.Length == 5 && cards.All(b => Math.Abs(b.ActualHeight-158)<1&&Math.Abs(b.ActualWidth-118)<1) && themeScroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Auto, "Theme cards retain fixed dimensions in a horizontal scroller");
                            var themeStrip=(StackPanel)themeScroll.Content;Check(themeStrip.Margin.Left>=16&&cards.All(b=>((Border)((StackPanel)b.Content).Children[0]).Margin.Left>=7),"Slanted theme previews have enough left clearance to avoid clipping");
                            Check(cards.All(b=>{var label=((StackPanel)b.Content).Children[1] as TextBlock;return label!=null&&label.TextWrapping==TextWrapping.Wrap&&label.TextTrimming==TextTrimming.None;}),"Theme names wrap without being cut off");
                            Check(cards.All(b => { var frame = ((StackPanel)b.Content).Children[0] as Border; return frame != null && frame.BorderThickness.Left == 2 && ((SolidColorBrush)frame.BorderBrush).Color == Colors.Black && ((SkewTransform)frame.RenderTransform).AngleX == -5; }), "Theme previews match the card angle with a black separation border");
                            var appearanceSections=Elements(settingsWindow).OfType<StackPanel>().Where(p=>p.Name.EndsWith("SettingsSection")).ToList();Check(appearanceSections.Select(p=>p.Name).SequenceEqual(new[]{"AudioOutputSettingsSection","ThemeSettingsSection","CustomColorSettingsSection","SurfaceModeSettingsSection","NeonSettingsSection","EqualizerSettingsSection","UpdateSettingsSection","CreditsSettingsSection"}),"Settings order includes audio output and updates while keeping credits last");
                            var outputSelector=Elements(settingsWindow).OfType<ComboBox>().Single(c=>c.Name=="AudioOutputSelector");Check(outputSelector.Items.Count>=1&&outputSelector.SelectedIndex>=0,"Settings exposes the available Windows audio outputs");
                            var eqSettings=appearanceSections.Single(p=>p.Name=="EqualizerSettingsSection");Check(Elements(eqSettings).OfType<Slider>().Count()==5&&Elements(eqSettings).OfType<Slider>().All(s=>s.IsMoveToPointEnabled),"Every equalizer band supports click-to-position dragging");
                            var neonToggle=Elements(settingsWindow).OfType<CheckBox>().Single(c=>c.Name=="NeonToggle");Check(neonToggle.IsChecked==false&&player.Find<Slider>("Seek").Effect==null,"Neon defaults off with no accent rendering effect");neonToggle.IsChecked=true;neonToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var glowingSanctuary=ThemeScenes.Build("Sanctuary",600,null,false,true,true);Check(player.Data.NeonEnabled&&store.Load().NeonEnabled&&player.Find<Slider>("Seek").Effect is DropShadowEffect&&player.Find<Grid>("RecordDisc").Effect is DropShadowEffect&&player.Find<System.Windows.Shapes.Ellipse>("AmbientRing").Effect is DropShadowEffect&&Elements(glowingSanctuary).OfType<Canvas>().Single(c=>c.Name=="SanctuaryStars").Effect is DropShadowEffect,"Neon toggle persists and extends to moving theme accents and Sanctuary stars");player.ShowExpanded(true);Check(Elements(player.Find<Canvas>("ExpandedWaveform")).OfType<System.Windows.Shapes.Polyline>().Any(p=>p.Effect is DropShadowEffect),"Neon extends to fullscreen visualizer rendering");player.ShowExpanded(false);Snapshot(player.Window,System.IO.Path.Combine(run,"neon-accents.png"));var neonReload=new PlayerApp(store,store.Load());Check(neonReload.Data.NeonEnabled&&neonReload.Find<Slider>("Seek").Effect is DropShadowEffect&&neonReload.Find<System.Windows.Shapes.Ellipse>("AmbientOrbit").Effect is DropShadowEffect,"Reopened player restores Neon controls and background accents");neonReload.Window.Close();neonToggle.IsChecked=false;neonToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(!player.Data.NeonEnabled&&player.Find<Slider>("Seek").Effect==null&&player.Find<System.Windows.Shapes.Ellipse>("AmbientRing").Effect==null,"Turning Neon off removes its rendering effects");
                            var changeBullets=AppInfo.Changelog.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Where(line=>line.StartsWith("•")).ToArray();Check(Elements(settingsWindow).OfType<TextBlock>().Any(t=>t.Name=="VersionLabel"&&t.Text=="Version "+AppInfo.Version)&&Elements(settingsWindow).OfType<Button>().Any(b=>b.Name=="ChangelogButton")&&AppInfo.ChangelogVersions.Length==AppInfo.Changelogs.Length&&AppInfo.ChangelogVersions.Length>=2&&AppInfo.Release1Changelog.Contains("PLAYBACK AND SOUND")&&changeBullets.Length==changeBullets.Distinct().Count(),"Settings footer exposes centralized versioning and retained changelog history");
                            var settingsScroll=Elements(settingsWindow).OfType<ScrollViewer>().First(s=>s.Content is StackPanel&&Elements((DependencyObject)s.Content).OfType<StackPanel>().Any(p=>p.Name=="CreditsSettingsSection"));settingsScroll.ScrollToEnd();await Task.Delay(80);Snapshot(settingsWindow,System.IO.Path.Combine(run,"settings-footer.png"));Check(Elements(settingsWindow).OfType<StackPanel>().Single(p=>p.Name=="CreditsSettingsSection").IsVisible,"Credits remain at the absolute bottom of the Appearance page");
                            Snapshot(settingsWindow, System.IO.Path.Combine(run, "settings-custom.png"));
                            player.ApplyTheme("Overworld"); ((Button)settingsWindow.Tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100); Snapshot(settingsWindow, System.IO.Path.Combine(run, "settings-folders.png")); settingsWindow.Close();
                            // Work on copies so editing checks cannot interfere with the playback fixture.
                            string editMp3 = System.IO.Path.Combine(run, "Edit fixture.mp3"); File.Copy(downloaded.Path, editMp3);
                            var coverBitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) context.DrawRectangle(Brushes.OliveDrab, null, new Rect(0, 0, 64, 64)); coverBitmap.Render(drawing);
                            var coverEncoder = new JpegBitmapEncoder(); coverEncoder.Frames.Add(BitmapFrame.Create(coverBitmap)); byte[] coverBytes; using (var memory = new MemoryStream()) { coverEncoder.Save(memory); coverBytes = memory.ToArray(); }
                            File.WriteAllBytes(System.IO.Path.Combine(run,"cover.jpg"),coverBytes);
                            Check(ArtworkFinder.Local(editMp3) != null, "Auto artwork finds a local folder cover without network access");
                            Check(!ArtworkFinder.Allowed(new Uri("https://archive.org.evil.example/image")) && !ArtworkFinder.Allowed(new Uri("http://musicbrainz.org/")) && !ArtworkFinder.Allowed(new Uri("https://localhost/image")), "Artwork requests reject unrelated, insecure and local endpoints");
                            Check(ArtworkFinder.Query("Artist", "Album", "Title").Contains("/release?") && ArtworkFinder.Query("Artist", "", "Title").Contains("/recording?"), "Artwork lookup uses album tags or falls back to song title");
                            string artworkJson = "{\"releases\":[{\"id\":\"76df3287-6cda-33eb-8e9a-044b5e15ffdd\",\"title\":\"Test album\",\"score\":100}]}";
                            var onlineArt = await Task.Run(delegate { return ArtworkFinder.Online("Test artist","Test album","",System.Threading.CancellationToken.None,delegate(Uri uri,System.Threading.CancellationToken token) { return uri.Host == "musicbrainz.org" ? System.Text.Encoding.UTF8.GetBytes(artworkJson) : coverBytes; }); });
                            Check(onlineArt != null && SongTags.ImageFromData(Convert.ToBase64String(onlineArt.Image)) != null && onlineArt.Description.Contains("Test album"), "Artwork search parses a match and sanitizes downloaded image bytes");
                            var noArt = await Task.Run(delegate { return ArtworkFinder.Online("Test artist","Test album","",System.Threading.CancellationToken.None,delegate(Uri uri,System.Threading.CancellationToken token) { return System.Text.Encoding.UTF8.GetBytes("{\"releases\":[]}"); }); });
                            Check(noArt == null, "Artwork lookup handles no matches without replacing a picture");
                            Check(ReleaseDate.Format("20260827") == "2026/08/27" && ReleaseDate.Format("202608") == "2026/08" && ReleaseDate.Storage("2026/08/27") == "2026-08-27", "Release dates insert slashes and save standard date tags");
                            Check(ReleaseDate.Valid("2024/02/29") && ReleaseDate.Valid("2026") && !ReleaseDate.Valid("2026/02/29") && !ReleaseDate.Valid("2026/13"), "Release date validation accepts partial dates and rejects impossible dates");
                            Check(CoverSearch.Address("Test Artist").Contains("artist=Test%20Artist") && !CoverSearch.Address("Test Artist").Contains("album="), "COV browser integration searches by artist only");
                            Check(CoverSearch.Picture("{\"type\":\"pick\",\"smallCoverUrl\":\"https://is1-ssl.mzstatic.com/test.jpg\"}") != null && CoverSearch.Picture("{\"type\":\"pick\",\"smallCoverUrl\":\"https://localhost/test.jpg\"}") == null, "COV selection validates image URLs before loading");
                            Check(CoverSearch.TrustedPage("https://covers.musichoarders.xyz/?artist=Test") && !CoverSearch.TrustedPage("https://covers.musichoarders.xyz.evil.example/"), "Cover bridge accepts only the actual COV website origin");
                            var batchTracks=new List<Track>();var batchAudioHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                            foreach (string extension in new[] { ".mp3", ".flac", ".m4a" })
                            {
                                string editable = extension == ".mp3" ? editMp3 : System.IO.Path.Combine(run, "Edit fixture" + extension);
                                if (extension != ".mp3") SongTags.Run("ffmpeg.exe", new[] { "-v", "error", "-y", "-i", first, editable });
                                string hash = SongTags.AudioHash(editable);
                                var tags = new Track { Path = editable, SongTitle = "Golden hour", Artist = "Test artist", Album = "A quieter place", AlbumArtist = "Various artists", Genre = "Ambient", Year = "2026-08-27", TrackNumber = "2/10", DiscNumber = "1/2", Comment = "Unicode café — test" };
                                batchTracks.Add(tags);batchAudioHashes[editable]=hash;
                                string backup = await Task.Run(delegate { return SongTags.Save(tags, coverBytes, true); });
                                var reread = new Track { Path = editable }; SongTags.ReadInto(reread);
                                Check(reread.SongTitle == tags.SongTitle && reread.Artist == tags.Artist && reread.Comment == tags.Comment && !String.IsNullOrEmpty(reread.CoverData), extension + " tags and artwork round-trip through the actual file");
                                Check(backup == null && SongTags.AudioHash(editable) == hash && !Directory.GetFiles(System.IO.Path.GetDirectoryName(editable), System.IO.Path.GetFileName(editable) + ".digitone-backup*").Any(), extension + " editing preserves compressed audio without song backups");
                                await Task.Run(delegate { SongTags.Save(tags, null, true); }); SongTags.ReadInto(reread);
                                Check(String.IsNullOrEmpty(reread.CoverData), extension + " cover art removal is persisted");
                                using (var live = new LiveWaveSource(editable))
                                {
                                    double[] frame = null; for (int i = 0; i < 100 && frame == null; i++) { frame = live.Sample(.21); if (frame == null) await Task.Delay(50); }
                                    Check(frame != null && frame.Length == 384 && frame.Max() > .02 && frame.Min() < -.02, extension + " live waveform uses positive and negative samples");
                                    double[] later = live.Sample(.317); Check(later != null && !later.SequenceEqual(frame), extension + " waveform changes with the current audio position");
                                    Check(live.Sample(.317).SequenceEqual(later), extension + " paused audio position holds the waveform steady");
                                    Check(live.Sample(0).All(value => value == 0), extension + " initial silent window renders a flat line");
                                }
                            }
                            var batchBitmap=new RenderTargetBitmap(96,96,96,96,PixelFormats.Pbgra32);var batchDrawing=new DrawingVisual();using(var context=batchDrawing.RenderOpen()){context.DrawRectangle(Brushes.DarkOrange,null,new Rect(0,0,96,96));context.DrawRectangle(Brushes.Navy,null,new Rect(16,16,64,64));}batchBitmap.Render(batchDrawing);var batchEncoder=new JpegBitmapEncoder{QualityLevel=92};batchEncoder.Frames.Add(BitmapFrame.Create(batchBitmap));byte[] batchCover;using(var memory=new MemoryStream()){batchEncoder.Save(memory);batchCover=memory.ToArray();}File.WriteAllBytes(System.IO.Path.Combine(run,"basic-batch-cover.jpg"),batchCover);
                            Check(batchTracks.All(t=>{var clean=new Track{Path=t.Path};SongTags.ReadInto(clean);return String.IsNullOrEmpty(clean.CoverData);}),"Batch artwork fixtures begin with no embedded cover");var batchYears=batchTracks.ToDictionary(t=>t.Path,t=>{var disk=new Track{Path=t.Path};SongTags.ReadInto(disk);return disk.Year;},StringComparer.OrdinalIgnoreCase);batchTracks[0].Year="2099/12/31";var batchInput=new List<Track>{batchTracks[0],new Track{Path=first},new Track{Path=System.IO.Path.Combine(run,"missing-but-supported.mp3")},batchTracks[1],batchTracks[2]};var batchResult=await player.ApplyBatchArtworkDetailed(batchInput,batchCover);Check(batchResult.Succeeded.Count==3&&batchResult.Unsupported.SequenceEqual(new[]{first})&&batchResult.Failed.Count==1,"Batch artwork processes every song individually and continues after unsupported and failed files");foreach(var track in batchTracks){var verifiedBatch=new Track{Path=track.Path};SongTags.ReadInto(verifiedBatch);Check(!String.IsNullOrEmpty(verifiedBatch.CoverData)&&SongTags.ArtworkDistance(batchCover,Convert.FromBase64String(verifiedBatch.CoverData))<18&&SongTags.AudioHash(track.Path)==batchAudioHashes[track.Path]&&verifiedBatch.Year==batchYears[track.Path],System.IO.Path.GetExtension(track.Path)+" artwork-only save embeds a cover without changing audio or Year");}
                            var artTags = new Track { Path = editMp3 }; SongTags.ReadInto(artTags); await Task.Run(delegate { SongTags.Save(artTags, coverBytes, true); });
                            await player.Import(new[] { editMp3 });
                            var editTrack = player.Data.Tracks.First(t => t.Path == editMp3);
                            player.StartQueue(new[] { editTrack }, null);
                            for (int i = 0; i < 100 && !player.MediaReady; i++) await Task.Delay(50);
                            player.TogglePlay(); player.Find<Slider>("Seek").Value = 3;
                            Check(player.Find<System.Windows.Shapes.Ellipse>("RecordImage").Fill is ImageBrush&&player.Find<Image>("RecordLogo").Visibility==Visibility.Collapsed&&player.Find<System.Windows.Shapes.Ellipse>("RecordLabel").Visibility==Visibility.Collapsed, "Turntable uses clean current-song artwork without a logo overlay");
                            player.TogglePlay(); double startAngle = ((RotateTransform)player.Find<Grid>("RecordDisc").RenderTransform).Angle; await Task.Delay(200);
                            Check(((RotateTransform)player.Find<Grid>("RecordDisc").RenderTransform).Angle > startAngle, "Artwork turntable rotates during playback");
                            Snapshot(player.Window, System.IO.Path.Combine(run, "artwork-turntable.png"));
                            player.ShowExpanded(true); Check(player.Find<Image>("ExpandedArt").Source != null,"Expanded visualizer displays the current song artwork"); Snapshot(player.Window,System.IO.Path.Combine(run,"visualizer-artwork.png")); player.ShowExpanded(false);
                            player.TogglePlay(); player.Find<Slider>("Seek").Value = 3;
                            player.Find<ListBox>("Tracks").SelectedItems.Clear(); player.Find<ListBox>("Tracks").SelectedItem = editTrack;
                            Exception editorFailure = null;
                            var editorAutomation = player.Window.Dispatcher.BeginInvoke(new Action(async delegate
                            {
                                var editor = player.Window.OwnedWindows.OfType<SongEditor>().FirstOrDefault();
                                try
                                {
                                    if (editor == null) throw new Exception("Editor did not open");
                                    for (int i = 0; i < 100 && !editor.SaveButton.IsEnabled; i++) await Task.Delay(50);
                                    Check(editor.SaveButton.IsEnabled && editor.Input("Artist").Text == "Test artist", "Song editor reads actual file tags into its fields");
                                    editor.FolderArtworkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                    for (int i = 0; i < 100 && !editor.SaveButton.IsEnabled; i++) await Task.Delay(50);
                                    Check(editor.HasPendingArtwork && editor.SaveButton.IsEnabled, "Editor auto artwork previews local art before the explicit file save");
                                    editor.Input("SongTitle").Text = "Edited in Digitone";
                                    editor.Input("Year").Text = "20260827"; Check(editor.Input("Year").Text == "2026/08/27", "Date Released field inserts separators in the editor");
                                    Snapshot(editor, System.IO.Path.Combine(run, "song-editor.png"));
                                    editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                    for (int i = 0; i < 200 && editor.IsVisible; i++) await Task.Delay(50);
                                    if (editor.IsVisible) throw new Exception("Editor save did not complete");
                                }
                                catch (Exception e) { editorFailure = e; if (editor != null) editor.Close(); }
                            }));
                            ((MenuItem)player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<object>().First(i=>Convert.ToString(((MenuItem)i).Header).StartsWith("Edit song"))).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                            if (editorFailure != null) throw editorFailure;
                            Check(editTrack.Title == "Edited in Digitone", "Editor saves and refreshes the library title");
                            for (int i = 0; i < 100 && (!player.MediaReady || player.Position < 2.5); i++) await Task.Delay(50);
                            Check(player.MediaReady && !player.IsPlaying && Math.Abs(player.Position - 3) < .4 && !String.IsNullOrEmpty(editTrack.CoverData), "Editing the current song restores paused position and preserves artwork");
                            using (var server = new LocalAudioServer(first, false))
                            {
                                player.Find<CheckBox>("DownloadPlaylist").IsChecked = true; player.Find<TextBox>("DownloadPlaylistName").Text = "Test downloaded playlist";
                                player.Find<TextBox>("DownloadUrl").Text = server.Url.Replace("sample.wav", "playlist.html"); Press(player, "DownloadStart");
                                var completed = await Task.WhenAny(player.Downloads.ActiveTask, Task.Delay(60000));
                                if (completed != player.Downloads.ActiveTask) { player.Downloads.Cancel(); await player.Downloads.ActiveTask; throw new Exception("Playlist integration test timed out"); }
                                await player.Downloads.ActiveTask;
                                var savedPlaylist = player.Data.Playlists.FirstOrDefault(p => p.Name == "Test downloaded playlist");
                                Check(savedPlaylist != null && !savedPlaylist.Favorite && savedPlaylist.Paths.Count == 2 && savedPlaylist.Paths.All(File.Exists), "Actual multi-entry download creates a non-favorite library playlist");
                                Check(savedPlaylist.Paths.All(p => System.IO.Path.GetDirectoryName(p) == System.IO.Path.Combine(destination, "Test downloaded playlist")), "Playlist downloads are saved inside their named folder");
                                player.Find<CheckBox>("DownloadPlaylist").IsChecked = false;
                            }
                            string cancelFolder = System.IO.Path.Combine(run, "Cancelled audio"); Directory.CreateDirectory(cancelFolder);
                            using (var server = new LocalAudioServer(first, true))
                            {
                                var runner = new AudioDownloader(); Task<DownloadResult> download = runner.Run(server.Url, cancelFolder, delegate { });
                                await Task.Delay(500); runner.Cancel();
                                Check(await Task.WhenAny(download, Task.Delay(5000)) == download && (await download).Cancelled, "Cancellation terminates the downloader job promptly");
                            }
                            Check(!player.Data.Tracks.Any(t => t.Path.StartsWith(cancelFolder)), "Cancelled downloads do not enter the library");
                            player.Window.Width = 1180; player.Window.Height = 790; player.ApplyCompactLayout(790,1180);
                            content.Measure(new Size(1164, 751)); content.Arrange(new Rect(0, 0, 1164, 751));
                            Snapshot(player.Window, System.IO.Path.Combine(run, "download-complete.png"));
                        }
                        else results.Add("SKIP: Optional downloader integration checks (tools not installed)");
                        Press(player, "LibraryButton");
                        Check(player.Find<Grid>("DownloadPane").Visibility == Visibility.Collapsed, "Returning to local library hides Downloads");
                        string deletionRoot = System.IO.Path.Combine(run, "Deletion fixtures"); Directory.CreateDirectory(deletionRoot);
                        string keepFile = System.IO.Path.Combine(deletionRoot, "keep.wav"); File.Copy(first, keepFile);
                        var kept = PlaylistFiles.Remove(new[] { keepFile }, new[] { keepFile }, PlaylistRemoval.KeepFiles, null); Check(File.Exists(keepFile) && kept.Removed.Count == 0, "Playlist-only deletion leaves audio files untouched");
                        bool rejectedDelete = false; try { PlaylistFiles.Remove(new[] { keepFile, second }, new[] { keepFile }, PlaylistRemoval.DeleteFiles, null); } catch (IOException) { rejectedDelete = true; }
                        Check(rejectedDelete && File.Exists(keepFile) && File.Exists(second), "Deletion preflight rejects unregistered paths before changing any file");
                        string moveFile = System.IO.Path.Combine(deletionRoot, "move.wav"); File.Copy(first, moveFile);
                        var moveTrack = new Track { Path = moveFile }; player.Data.Tracks.Add(moveTrack); var removePlaylist = player.CreatePlaylist("Remove test", new[] { moveTrack }); var sharedPlaylist = player.CreatePlaylist("Shared test", new[] { moveTrack });
                        var moved = PlaylistFiles.Remove(new[] { moveFile }, new[] { moveFile }, PlaylistRemoval.MoveFiles, System.IO.Path.Combine(deletionRoot, "Deleted")); player.ApplyRemovedFiles(removePlaylist, moved);
                        Check(!File.Exists(moveFile) && File.Exists(System.IO.Path.Combine(moved.Folder, "move.wav")), "Move-to-Deleted preserves the audio in a dedicated folder");
                        Check(!player.Data.Tracks.Contains(moveTrack) && !player.Data.Playlists.Contains(removePlaylist) && sharedPlaylist.Paths.Count == 0, "Moved files are removed from library and shared playlists");
                        string permanent = System.IO.Path.Combine(deletionRoot, "permanent.wav"); File.Copy(first, permanent);
                        var deleted = PlaylistFiles.Remove(new[] { permanent }, new[] { permanent }, PlaylistRemoval.DeleteFiles, null); Check(!File.Exists(permanent) && deleted.Removed.Count == 1, "Permanent deletion removes only the specified test audio file");
                        string locked = System.IO.Path.Combine(deletionRoot, "locked.wav"); File.Copy(first, locked);
                        using (var lockedStream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            var partial = PlaylistFiles.Remove(new[] { keepFile, locked }, new[] { keepFile, locked }, PlaylistRemoval.DeleteFiles, null);
                            Check(partial.Removed.SequenceEqual(new[] { keepFile }) && partial.Errors.Count == 1 && File.Exists(locked), "Locked files survive partial deletion with explicit failure reporting");
                        }
                        var deleteDialog = new DeletePlaylistDialog(player.Window, "Confirmation test", 2, 1); bool confirmationHeld = false;
                        var confirmationAutomation = player.Window.Dispatcher.BeginInvoke(new Action(delegate { deleteDialog.Delete.IsChecked = true; deleteDialog.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); confirmationHeld = deleteDialog.IsVisible; Snapshot(deleteDialog, System.IO.Path.Combine(run, "delete-dialog.png")); deleteDialog.DialogResult = false; }));
                        deleteDialog.ShowDialog(); Check(confirmationHeld&&deleteDialog.ResizeMode==ResizeMode.NoResize, "Permanent deletion requires typing DELETE before confirmation and uses secondary-window chrome");
                        Check(player.Find<Slider>("Seek").IsMoveToPointEnabled&&player.Find<Slider>("Volume").IsMoveToPointEnabled,"Seek and volume tracks accept click-to-position dragging");
                        Check(player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<MenuItem>().Any(i=>Convert.ToString(i.Header).StartsWith("Edit song"))&&player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<MenuItem>().Any(i=>Convert.ToString(i.Header).StartsWith("Set artwork")),"Song editing and batch artwork live in the track context menu");
                        player.SetLibraryArtworkBusy(true);Check(player.Find<Grid>("LibraryPane").IsEnabled&&!player.Find<Grid>("LibraryPane").IsHitTestVisible&&player.Find<ListBox>("Tracks").Background==Brushes.Transparent,"Batch artwork blocks library input without replacing its themed surface");player.SetLibraryArtworkBusy(false);Check(player.Find<Grid>("LibraryPane").IsHitTestVisible,"Batch artwork restores library interaction after writing");
                        Press(player,"PlaylistsButton");Check(player.Find<Grid>("PlaylistPane").Visibility==Visibility.Visible&&player.Find<Grid>("LibraryPane").Visibility==Visibility.Collapsed&&player.Find<WrapPanel>("PlaylistGrid").Children.Count==player.Data.Playlists.Count,"Playlists opens as a separate card grid");var playlistCard=player.Find<WrapPanel>("PlaylistGrid").Children.OfType<Button>().First();playlistCard.ApplyTemplate();var playlistSurface=(Border)playlistCard.Template.FindName("PlaylistCardSurface",playlistCard);var playlistStack=(StackPanel)playlistCard.Content;playlistCard.UpdateLayout();var playlistTitleFit=(Viewbox)playlistStack.Children[1];Check(!(playlistSurface.RenderTransform is SkewTransform)&&((Grid)playlistStack.Children[0]).Width==((Grid)playlistStack.Children[0]).Height&&playlistCard.Height>=252&&((TextBlock)playlistStack.Children[2]).ActualHeight>0,"Playlist grid uses straight square cover cards with a visible song count");Check(playlistTitleFit.Width==164&&playlistTitleFit.StretchDirection==StretchDirection.DownOnly&&((TextBlock)playlistTitleFit.Child).TextTrimming==TextTrimming.None,"Playlist names dynamically scale to fit their cards without truncation");Snapshot(player.Window,System.IO.Path.Combine(run,"playlist-grid.png"));
                        player.Data.AppearanceMode="Light";player.Data.CustomThemeColor="#FF3366";player.ApplyTheme("Custom");Color redSurface=((SolidColorBrush)player.Window.FindResource("B161917")).Color,redAccent=((SolidColorBrush)player.Window.FindResource("BC1D3A8")).Color,searchSurface=((SolidColorBrush)player.Find<TextBox>("Search").Background).Color;player.Data.CustomThemeColor="#3366FF";player.ApplyTheme("Custom");Color blueSurface=((SolidColorBrush)player.Window.FindResource("B161917")).Color,blueAccent=((SolidColorBrush)player.Window.FindResource("BC1D3A8")).Color,mutedText=((SolidColorBrush)player.Window.FindResource("B919A92")).Color,primaryText=((SolidColorBrush)player.Window.FindResource("BEEEFE8")).Color;Check(searchSurface.R<245&&redSurface!=Color.FromRgb(250,242,220)&&redSurface!=blueSurface&&redAccent.R>redAccent.B&&blueAccent.B>blueAccent.R&&(.2126*mutedText.R+.7152*mutedText.G+.0722*mutedText.B)<110&&(.2126*primaryText.R+.7152*primaryText.G+.0722*primaryText.B)<70,"Light mode carries the custom color while keeping primary and muted text dark");Snapshot(player.Window,System.IO.Path.Combine(run,"light-custom-color.png"));var lightSettings=player.CreateSettings();lightSettings.Show();await Task.Delay(80);var creditText=Elements(lightSettings).OfType<TextBlock>().First(t=>t.Text.StartsWith("Digitone was made by GEMMA"));Color creditColor=((SolidColorBrush)creditText.Foreground).Color;Check((.2126*creditColor.R+.7152*creditColor.G+.0722*creditColor.B)<110,"Light-mode Settings descriptions use dark readable text");var lightSettingsScroll=Elements(lightSettings).OfType<ScrollViewer>().First(s=>s.Content is StackPanel&&Elements((DependencyObject)s.Content).OfType<StackPanel>().Any(p=>p.Name=="CreditsSettingsSection"));lightSettingsScroll.ScrollToEnd();await Task.Delay(80);Snapshot(lightSettings,System.IO.Path.Combine(run,"light-settings-footer.png"));lightSettings.Close();player.StartQueue(new[]{player.Data.Tracks.First(t=>String.IsNullOrEmpty(t.CoverData))},null);await Task.Delay(100);var recordLogo=player.Find<Image>("RecordLogo");var logoBitmap=recordLogo.Source as BitmapSource;Check(logoBitmap!=null&&logoBitmap.PixelWidth==1119&&recordLogo.Visibility==Visibility.Visible&&player.Find<System.Windows.Shapes.Ellipse>("RecordLabel").Visibility==Visibility.Visible&&recordLogo.Source!=player.Window.Icon,"Coverless songs use only the supplied lowercase d mark");Snapshot(player.Window,System.IO.Path.Combine(run,"coverless-logo.png"));player.Data.AppearanceMode="Dark";player.ApplyTheme("Custom");
                        string folderImport=System.IO.Path.Combine(run,"Folder playlist fixture");Directory.CreateDirectory(folderImport);string folderSong=System.IO.Path.Combine(folderImport,"folder-song.wav");File.Copy(first,folderSong);await player.Import(new[]{folderImport},false,true);var folderPlaylist=player.Data.Playlists.FirstOrDefault(p=>String.Equals(p.FolderPath,folderImport,StringComparison.OrdinalIgnoreCase));Check(folderPlaylist!=null&&!folderPlaylist.Favorite&&folderPlaylist.Paths.SequenceEqual(new[]{folderSong}),"Add Folder creates a non-favorite synchronized folder playlist");File.Delete(folderSong);Directory.Delete(folderImport);await player.RescanMusicFolders();Check(!player.Data.Tracks.Any(t=>t.Path==folderSong)&&!player.Data.Playlists.Contains(folderPlaylist),"Rescan removes missing folder tracks and its generated playlist");
                        player.Data.AppearanceMode="Dark";player.Data.CustomThemeColor="#FF3366";player.ApplyTheme("Custom");Color saturatedCustom=((SolidColorBrush)player.Window.FindResource("B161917")).Color;Check(new[]{saturatedCustom.R,saturatedCustom.G,saturatedCustom.B}.Max()-new[]{saturatedCustom.R,saturatedCustom.G,saturatedCustom.B}.Min()>=25,"Custom Color dark mode carries visibly saturated selected color");
                        var setupWindow=player.CreateFirstRun();Check(setupWindow.ResizeMode==ResizeMode.NoResize,"First-run window removes minimize and maximize controls");setupWindow.Show();await Task.Delay(80);var setupBrowse=Elements(setupWindow).OfType<Button>().First(b=>Convert.ToString(b.Content).StartsWith("Choose music folder"));Check(setupBrowse.Margin.Left>=10,"First-run folder button has left-side border clearance");setupWindow.Close();
                        Press(player,"LibraryButton");Press(player,"MainSpectrumButton");Check(player.Find<Canvas>("MainSpectrum").Height<=72&&player.Find<Border>("RecordArt").MinHeight==0&&Double.IsPositiveInfinity(player.Find<Viewbox>("DefaultArt").MaxHeight),"Main spectrum leaves the record fluid instead of imposing a fixed size");player.Window.Width=1000;player.Window.Height=660;await Task.Delay(120);var nowPane=player.Find<Border>("NowPane");var recordView=player.Find<Viewbox>("DefaultArt");Rect recordBounds=recordView.TransformToAncestor(nowPane).TransformBounds(new Rect(recordView.RenderSize));Rect spectrumBounds=player.Find<Canvas>("MainSpectrum").TransformToAncestor(nowPane).TransformBounds(new Rect(player.Find<Canvas>("MainSpectrum").RenderSize));Check(player.Find<Canvas>("MainSpectrum").Height<=52&&recordBounds.Top>=-1&&recordBounds.Bottom<=spectrumBounds.Top+1&&spectrumBounds.Bottom<=nowPane.ActualHeight+1,"Fluid record remains above the information and spectrum area at minimum size");double compactRecordHeight=recordView.ActualHeight;Snapshot(player.Window,System.IO.Path.Combine(run,"main-spectrum-small.png"));player.Window.Width=1180;player.Window.Height=790;await Task.Delay(80);Press(player,"VisualizerButton");Check(player.Find<Border>("RecordArt").MinHeight==0&&Double.IsPositiveInfinity(player.Find<Viewbox>("DefaultArt").MaxHeight)&&recordView.ActualHeight>compactRecordHeight,"Record grows fluidly when its available aspect-ratio space increases");
                        exitCode = 0;
                    }
                    catch (Exception e) { results.Add(e.ToString()); }
                    finally { player.Window.Close(); app.Shutdown(); }
                };
                app.Run(player.Window);
            }
            catch (Exception e) { results.Add(e.ToString()); }
            results.Add(exitCode == 0 ? "ALL CHECKS PASSED" : "CHECKS FAILED");
            File.WriteAllLines(System.IO.Path.Combine(run, "results.txt"), results);
            File.WriteAllText(System.IO.Path.Combine(output, "latest.txt"), run);
            return exitCode;
        }
    }
}



