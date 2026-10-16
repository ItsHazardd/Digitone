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
        private static string Fixture(string name) { using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Fixture." + name)) using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd(); }
        private static Dictionary<string, string> FileTags(string path) { var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); object format, values; var probe = SongTags.Probe(path); if (probe.TryGetValue("format", out format) && ((Dictionary<string, object>)format).TryGetValue("tags", out values)) foreach (var tag in (Dictionary<string, object>)values) tags[tag.Key] = Convert.ToString(tag.Value); return tags; }
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
            PortableFolders.UseRoot(run);
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
                string updatePrompt=DigitoneUpdates.Prompt("v4.4.5");Check(updatePrompt.Contains("Digitone 4.4.5 is available")&&updatePrompt.Contains("userdata")&&!updatePrompt.ToLowerInvariant().Contains("changelog")&&!updatePrompt.ToLowerInvariant().Contains("new changes")&&!updatePrompt.ToLowerInvariant().Contains("youtube"),"Update prompt contains installation facts without changelog spoilers or feature teasers");
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
                Check(YouTubeBrowser.Trusted("https://www.youtube.com/watch?v=abc")&&YouTubeBrowser.Trusted("https://youtu.be/abc")&&!YouTubeBrowser.Trusted("http://youtube.com/watch?v=abc")&&!YouTubeBrowser.Trusted("https://youtube.com.example.net/watch?v=abc"),"Embedded YouTube browsing accepts only secure YouTube pages");
                SpotifyLink spotifyTrack=SpotifyLink.Parse("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC?si=test"),spotifyAlbum=SpotifyLink.Parse("spotify:album:1ATL5GLyefJaxhQzSPVrLX");Check(spotifyTrack!=null&&spotifyTrack.Kind=="track"&&spotifyTrack.Id=="4uLU6hMCjMI75M1A2tKUQC"&&spotifyAlbum!=null&&spotifyAlbum.Kind=="album"&&SpotifyLink.Parse("https://open.spotify.com.example.net/track/4uLU6hMCjMI75M1A2tKUQC")==null&&SpotifyLink.Parse("file:///spotify/track/4uLU6hMCjMI75M1A2tKUQC")==null,"Spotify importer accepts only valid Spotify track, album, and playlist identifiers");
                string spotifyEmbed="<script id=\"__NEXT_DATA__\" type=\"application/json\">{\"props\":{\"pageProps\":{\"state\":{\"data\":{\"entity\":{\"type\":\"playlist\",\"trackList\":[{\"uri\":\"spotify:track:4uLU6hMCjMI75M1A2tKUQC\",\"title\":\"Never Gonna Give You Up\",\"subtitle\":\"Rick Astley\",\"duration\":213573}]}}}}}}</script>";var sharedTracks=SpotifySharedLinks.ParseEmbed(spotifyEmbed);Check(sharedTracks.Count==1&&sharedTracks[0].Title=="Never Gonna Give You Up"&&sharedTracks[0].Artist=="Rick Astley"&&sharedTracks[0].DurationMs==213573,"Spotify shared pages expose track metadata without an account, token, Client ID, or secret");
                Check(SpotifySharedLinks.IsShortLink("https://spotify.link/example")&&!SpotifySharedLinks.IsShortLink("https://spotify.link.example.com/trap")&&!SpotifySharedLinks.IsShortLink("http://spotify.link/example"),"Spotify short links accept only the exact secure Spotify redirect host");
                string namedImport=SpotifyImportWindow.ImportName(run,new DateTime(2026,9,5,15,24,0));Check(namedImport=="Spotify Import (9-5-2026 at 3-24)","Spotify imports use a Windows-safe local date and time folder name");Directory.CreateDirectory(System.IO.Path.Combine(run,namedImport));Check(SpotifyImportWindow.ImportName(run,new DateTime(2026,9,5,15,24,0))==namedImport+" (2)","Spotify import folders remain unique when two batches start in the same minute");
                var scoreTrack=new SpotifyTrackCandidate{Artist="Daft Punk",Title="Harder Better Faster Stronger",DurationMs=224000};Check(YouTubeMatcher.Score(scoreTrack,"Daft Punk - Harder Better Faster Stronger (Official Audio)",224)>.9&&YouTubeMatcher.Score(scoreTrack,"Unrelated ten hour rain sounds",36000)<.2,"YouTube matching scores artist, title, and duration without metadata guesswork");
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
                Check(PortableFolders.DetectRoot(@"D:\Digi\app\") == @"D:\Digi" && PortableFolders.DetectRoot(@"D:\Digi") == @"D:\Digi", "Portable root is the folder above app, or the program folder itself");
                var portable = new PortableFolders(System.IO.Path.Combine(run, "Portable A"));
                Check(portable.ToStored(System.IO.Path.Combine(portable.Music, "x.mp3")) == @"Music\x.mp3" && portable.ToStored(@"D:\Elsewhere\x.mp3") == @"D:\Elsewhere\x.mp3" && portable.FromStored(@"..\..\escape.mp3") == null && portable.FromStored(@"Music\x.mp3") == System.IO.Path.Combine(portable.Music, "x.mp3"), "Userdata paths are stored relative and cannot escape userdata");
                string legacyRoot = System.IO.Path.Combine(run, "Legacy install"), legacyMusic = System.IO.Path.Combine(legacyRoot, "DigiMusic"), legacyAlbum = System.IO.Path.Combine(legacyMusic, "Album"), legacyData = System.IO.Path.Combine(legacyRoot, "Data"), legacySong = System.IO.Path.Combine(legacyAlbum, "song.wav");
                Directory.CreateDirectory(legacyAlbum); Directory.CreateDirectory(legacyData); Tone(legacySong, 440);
                var legacyLibrary = new Library { MainMusicFolder = legacyMusic, DownloadFolder = legacyMusic, MusicFolders = new List<string> { legacyMusic }, ThemeName = "Pride" };
                legacyLibrary.Tracks.Add(new Track { Path = legacySong, Lyrics = "kept lyrics" }); legacyLibrary.Playlists.Add(new Playlist { Name = "Road", Paths = new List<string> { legacySong }, FolderPath = legacyAlbum });
                File.WriteAllText(System.IO.Path.Combine(legacyData, "library.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(legacyLibrary));
                var legacyFolders = new PortableFolders(legacyRoot); string migrated = legacyFolders.Migrate();
                var legacyStore = new LibraryStore(legacyFolders.LibraryFile, legacyFolders); var migratedLibrary = legacyStore.Load();
                string newSong = System.IO.Path.Combine(legacyFolders.Music, "Album", "song.wav");
                Check(migrated != null && !Directory.Exists(legacyMusic) && !Directory.Exists(legacyData) && File.Exists(newSong) && migratedLibrary.Tracks.Single().Path == newSong && migratedLibrary.Tracks.Single().Lyrics == "kept lyrics" && migratedLibrary.Playlists[0].Paths.Single() == newSong && migratedLibrary.Playlists[0].FolderPath == System.IO.Path.Combine(legacyFolders.Music, "Album") && migratedLibrary.MainMusicFolder == legacyFolders.Music && migratedLibrary.ThemeName == "Pride", "Legacy Data and DigiMusic move into userdata with every library path and setting kept");
                legacyStore.Save(migratedLibrary); string savedJson = File.ReadAllText(legacyFolders.LibraryFile);
                Check(!savedJson.Contains(legacyRoot.Replace("\\", "\\\\")) && savedJson.Contains("Music\\\\Album\\\\song.wav"), "Saved library contains no absolute userdata paths");
                Check(legacyFolders.Migrate() == null, "Migration runs only once");
                string movedRoot = System.IO.Path.Combine(run, "Moved copy"); Directory.Move(legacyRoot, movedRoot);
                var movedFolders = new PortableFolders(movedRoot); var movedLibrary = new LibraryStore(movedFolders.LibraryFile, movedFolders).Load();
                string movedSong = System.IO.Path.Combine(movedFolders.Music, "Album", "song.wav");
                Check(File.Exists(movedSong) && movedLibrary.Tracks.Single().Path == movedSong && movedLibrary.Playlists[0].Paths.Single() == movedSong && movedLibrary.DownloadFolder == movedFolders.Music && movedLibrary.MusicFolders.Single() == movedFolders.Music && movedLibrary.ThemeName == "Pride", "A moved Digitone folder finds its music, playlists, and settings");
                Check(movedFolders.FromStored(@"C:\Digitone-old-place-" + Guid.NewGuid().ToString("N") + @"\userdata\Music\Album\song.wav") == movedSong, "Absolute paths from a previously moved copy are recovered inside userdata");
                string lockedRoot = System.IO.Path.Combine(run, "Locked install"), lockedFile = System.IO.Path.Combine(lockedRoot, "Data", "library.json"), keptSong = System.IO.Path.Combine(lockedRoot, "DigiMusic", "keep.wav");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(lockedFile)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(keptSong)); File.WriteAllText(lockedFile, "{}"); Tone(keptSong, 500);
                bool lockedRefused = false;
                using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None)) { try { new PortableFolders(lockedRoot).Migrate(); } catch (IOException) { lockedRefused = true; } }
                Check(lockedRefused && File.Exists(keptSong) && File.Exists(lockedFile) && !Directory.Exists(System.IO.Path.Combine(lockedRoot, "userdata", "Music")), "A blocked migration puts everything back where it was");
                string exportSource = System.IO.Path.Combine(run, "Export source"), exportApp = System.IO.Path.Combine(exportSource, "app"), exeName = PortableExport.ExeName; var exportFolders = new PortableFolders(exportSource);
                Directory.CreateDirectory(exportApp);
                foreach (string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory).Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) File.Copy(file, System.IO.Path.Combine(exportApp, System.IO.Path.GetFileName(file)));
                File.Copy(System.IO.Path.Combine(exportApp, exeName), System.IO.Path.Combine(exportSource, exeName));
                string insideSong = System.IO.Path.Combine(exportFolders.Music, "Inside", "inside.wav"), outsideFolder = System.IO.Path.Combine(run, "Outside music"), outsideSong = System.IO.Path.Combine(outsideFolder, "Live", "outside.wav");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(insideSong)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outsideSong)); Tone(insideSong, 600); Tone(outsideSong, 700);
                foreach (string junk in new[] { System.IO.Path.Combine(exportFolders.YouTubeBrowser, "Default", "Cookies"), System.IO.Path.Combine(exportFolders.YouTubeBrowser, "Default", "Cache", "junk.bin"), System.IO.Path.Combine(exportFolders.Cache, "temp", "junk.tmp") }) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(junk)); File.WriteAllText(junk, "x"); }
                var exportLibrary = new Library { SetupCompleted = true, ThemeName = "Pride", MainMusicFolder = exportFolders.Music, DownloadFolder = exportFolders.Music, MusicFolders = new List<string> { exportFolders.Music, outsideFolder } };
                exportLibrary.Tracks.Add(new Track { Path = insideSong }); exportLibrary.Tracks.Add(new Track { Path = outsideSong, Lyrics = "outside lyrics" }); exportLibrary.Playlists.Add(new Playlist { Name = "Both", Paths = new List<string> { insideSong, outsideSong } });
                new LibraryStore(exportFolders.LibraryFile, exportFolders).Save(exportLibrary);
                bool insideRefused = false; try { PortableExport.Export(exportFolders, exportApp, exeName, exportSource, delegate { }); } catch (IOException) { insideRefused = true; }
                Check(insideRefused, "Export refuses to copy Digitone into its own folder");
                string exportParent = System.IO.Path.Combine(run, "Exports"); Directory.CreateDirectory(exportParent);
                var exported = PortableExport.Export(exportFolders, exportApp, exeName, exportParent, delegate { }); var exportedFolders = new PortableFolders(exported.Destination);
                Check(File.Exists(System.IO.Path.Combine(exported.Destination, exeName)) && File.Exists(System.IO.Path.Combine(exported.Destination, "app", exeName)) && File.Exists(System.IO.Path.Combine(exported.Destination, "README.txt")) && File.Exists(System.IO.Path.Combine(exportedFolders.Music, "Inside", "inside.wav")) && File.Exists(System.IO.Path.Combine(exportedFolders.Music, "Imported - Outside music", "Live", "outside.wav")) && exported.OutsideSongs == 1, "Export copies the launcher, program, userdata music, and outside music into one folder");
                Check(File.Exists(System.IO.Path.Combine(exportedFolders.YouTubeBrowser, "Default", "Cookies")) && !Directory.Exists(System.IO.Path.Combine(exportedFolders.YouTubeBrowser, "Default", "Cache")) && !File.Exists(System.IO.Path.Combine(exportedFolders.Cache, "temp", "junk.tmp")), "Export keeps browser sign-in data but skips throwaway caches");
                Check(!File.ReadAllText(exportedFolders.LibraryFile).Contains(run.Replace("\\", "\\\\")) && File.Exists(outsideSong) && File.Exists(insideSong), "Exported library has no absolute paths and the originals are untouched");
                string movedExport = System.IO.Path.Combine(run, "USB stick", "Digitone"); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(movedExport)); Directory.Move(exported.Destination, movedExport);
                var movedCheck = PortableExport.RunCheck(movedExport, exeName, System.IO.Path.Combine(run, "portable-check.json"));
                Check(movedCheck.PassedFor(exported) && movedCheck.Tracks == 2 && movedCheck.TracksFound == 2 && movedCheck.Playlists == 1 && movedCheck.Root == movedExport, "A moved portable copy starts and finds all its songs, playlists, and settings");
                Check(PortableExport.LauncherName("DigitoneApp.exe") == "Digitone.exe" && PortableExport.LauncherName("Digitone.dev.exe") == "Digitone.dev.exe", "Release launcher is Digitone.exe; the dev launcher keeps the dev name");
                string repairArguments = PlayerApp.RepairArguments(@"D:\Digitone\Repair-Digitone.ps1", 4242, "5.4.14");
                Check(repairArguments.Contains("-File \"D:\\Digitone\\Repair-Digitone.ps1\"") && repairArguments.Contains("-WaitForPid 4242") && repairArguments.Contains("-Version \"5.4.14\" -Yes") && !PlayerApp.RepairArguments(@"D:\Digitone\Repair-Digitone.ps1", 1, null).Contains("-Version"), "In-app updates and repairs hand off to Repair Digitone with the right version");
                Check(SongRecognizer.CleanTitle("Starship - We Built This City (Official Music Video)") == "Starship - We Built This City" && SongRecognizer.CleanTitle("Night Drive 2 (Lyrics)") == "Night Drive 2" && SongRecognizer.CleanTitle("Song [Acoustic]") == "Song [Acoustic]" && SongRecognizer.CleanTitle("Free Bird (Audio)") == "Free Bird", "Recognition strips YouTube video labels but keeps real version names");
                var ytHints = SongRecognizer.ParseHints(new SongHints { Title = "Starship - We Built This City (Official Music Video)", Channel = "StarshipVEVO" });
                Check(ytHints[0].Artist == "Starship" && ytHints[0].Title == "We Built This City", "YouTube 'Artist - Title' titles are split and cleaned");
                var musicHints = SongRecognizer.ParseHints(new SongHints { Title = "We Built This City", Channel = "Starship - Topic", Description = "Provided to YouTube by Sony Music\n\nWe Built This City · Starship\n\nKnee Deep in the Hoopla\n\n℗ 1985 RCA\n\nReleased on: 1985-09-10\n\nAuto-generated by YouTube." });
                Check(musicHints[0].Kind == "YouTube Music description" && musicHints[0].Artist == "Starship" && musicHints[0].Album == "Knee Deep in the Hoopla" && musicHints[0].Year == "1985" && musicHints.Count == 1 && SongRecognizer.ParseHints(new SongHints { Title = "We Built This City", Channel = "Starship - Topic" }).Single().Quality == 72, "YouTube Music descriptions and Topic channels give title, artist, album, and year");
                Check(SongRecognizer.WithoutArtist("Lynyrd Skynyrd - Free Bird", "Lynyrd Skynyrd") == "Free Bird" && SongRecognizer.WithoutArtist("Sara - Live", "Starship") == "Sara - Live", "A title that repeats the artist name is cleaned without breaking real subtitles");
                var instrumental = new RecognitionCandidate { Title = "Let's Groove (instrumental)", Confidence = 99 }; SongRecognizer.PenalizeVersion(instrumental, new SongHints { Title = "Earth, Wind & Fire - Let's Groove (Official HD Video)" });
                var wantedLive = new RecognitionCandidate { Title = "Free Bird (live)", Confidence = 99 }; SongRecognizer.PenalizeVersion(wantedLive, new SongHints { Title = "Lynyrd Skynyrd - Free Bird (Live at Knebworth)" });
                Check(SongRecognizer.VersionKey("Let's Groove (instrumental)") != SongRecognizer.VersionKey("Let's Groove") && SongRecognizer.Compact("Free Bird") == SongRecognizer.Compact("Freebird") && instrumental.Confidence == 84 && wantedLive.Confidence == 99, "Instrumental, live, and remix versions stay separate and are flagged unless the download says so");
                Check(SongRecognizer.CleanArtist("Nova Reed, Nova Reed, Nova Reed") == "Nova Reed" && SongRecognizer.ParseHints(new SongHints { TagTitle = "Ivy Lane & The Static - Paper Moons", TagArtist = "4400 LANTERN" }).Any(h => h.Artist == "Ivy Lane & The Static" && h.Title == "Paper Moons"), "Repeated artists are merged and an artist inside the title is found");
                Check(SongRecognizer.Similarity("Kai Moreno, 4everblue", "Kai Moreno & 4everblue") >= .9 && SongRecognizer.Similarity("We Built This City", "Sara") == 0, "Title and artist comparison ignores punctuation and joiners");
                bool badKey = false; try { SongRecognizer.ParseAcoustId(Fixture("acoustid-badkey.json")); } catch (InvalidOperationException error) { badKey = error.Message.Contains("not valid"); }
                Check(badKey, "A wrong AcoustID key gives a clear message (real AcoustID reply)");
                var fpMatches = SongRecognizer.ParseAcoustId(Fixture("acoustid-ok.json"));
                Check(fpMatches.Count == 3 && fpMatches[0].Score > .96 && fpMatches[0].Artist == "Starship" && fpMatches[2].Title == "Sara", "AcoustID results are read with scores, recordings, and artists");
                var mbRecording = SongRecognizer.ParseRecordingJson(Fixture("mb-recording.json")); var mbRelease = SongRecognizer.PickRelease(mbRecording.Releases, null);
                Check(mbRecording.Title == "We Built This City" && mbRecording.Artist == "Starship" && Math.Abs(mbRecording.Length - 293.68) < .01 && mbRecording.Releases.Count == 25 && mbRelease.Title == "Knee Deep in the Hoopla" && mbRelease.TrackNumber == "1", "MusicBrainz details pick the original album over singles and use a plain track number (real MusicBrainz reply)");
                var mbSearch = SongRecognizer.ParseSearchJson(Fixture("mb-search.json"));
                Check(mbSearch.Count >= 5 && mbSearch[0].SearchScore == 100 && mbSearch.All(r => r.Title != null), "MusicBrainz search results are read (real MusicBrainz reply)");
                SongRecognizer.HttpOverride = delegate(string url, string post) { if (url.Contains("acoustid")) return Fixture("acoustid-ok.json"); if (url.Contains("/recording/")) return Fixture("mb-recording.json"); if (url.Contains("/recording?")) return Fixture("mb-search.json"); throw new WebException("offline"); };
                try
                {
                    var known = new RecognitionResult { Fingerprint = "AQADtest", FileDuration = 293.9 };
                    var strong = SongRecognizer.Recognize(null, new SongHints { Title = "Starship - We Built This City (Official Music Video)", Channel = "StarshipVEVO" }, "testkey", System.Threading.CancellationToken.None, known);
                    Check(strong.Best.Title == "We Built This City" && strong.Best.Album == "Knee Deep in the Hoopla" && strong.Best.Confidence >= SongRecognizer.HighConfidence && strong.Best.RecordingId == "7b2a32ca-cac6-4f22-ac44-5c77ad99f155" && strong.Best.AcoustId != null && strong.Best.Source == "Audio fingerprint", "A strong fingerprint match with a matching length is high confidence");
                    var wrongLength = SongRecognizer.Recognize(null, null, "testkey", System.Threading.CancellationToken.None, new RecognitionResult { Fingerprint = "AQADtest", FileDuration = 200 });
                    Check(wrongLength.Best.Confidence < SongRecognizer.HighConfidence && wrongLength.Best.Notes.Any(n => n.Contains("Length")), "A length mismatch sends the song to review");
                    var conflict = SongRecognizer.Recognize(null, new SongHints { Title = "Daft Punk - One More Time" }, "testkey", System.Threading.CancellationToken.None, known);
                    var conflicted = conflict.Candidates.First(c => c.Source == "Audio fingerprint");
                    Check(conflicted.Confidence < SongRecognizer.HighConfidence && conflicted.Notes.Any(n => n.Contains("Differs")), "A fingerprint that disagrees with the YouTube title is flagged for review");
                    var noKey = SongRecognizer.Recognize(null, new SongHints { Title = "Starship - We Built This City (Official Music Video)" }, "", System.Threading.CancellationToken.None, known);
                    Check(noKey.Candidates.Any(c => c.RecordingId != null) && noKey.Candidates.All(c => c.Confidence < SongRecognizer.HighConfidence) && noKey.Error.Contains("No AcoustID key"), "Without a key, YouTube info is checked on MusicBrainz but never auto-applied");
                    SongRecognizer.HttpOverride = delegate { throw new WebException("offline"); };
                    var offline = SongRecognizer.Recognize(null, new SongHints { Title = "Starship - We Built This City" }, "testkey", System.Threading.CancellationToken.None, known);
                    Check(offline.Error != null && offline.Best != null && offline.Best.Confidence <= 45 && offline.Best.Title == "We Built This City" && offline.Best.RecordingId == null, "Offline, the cleaned YouTube title is offered for review only");
                }
                finally { SongRecognizer.HttpOverride = null; }
                string idMp3 = System.IO.Path.Combine(run, "Recognized.mp3");
                SongTags.Run("ffmpeg.exe", new[] { "-v", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "sine=frequency=330:duration=20", "-c:a", "libmp3lame", "-q:a", "4", idMp3 });
                double fpLength; string realPrint = SongRecognizer.Fingerprint(idMp3, out fpLength);
                Check(realPrint.StartsWith("AQA") && Math.Abs(fpLength - 20) < 1, "The bundled FFmpeg makes AcoustID fingerprints (no extra tool needed)");
                var idTrack = new Track { Path = idMp3 }; SongTags.ReadInto(idTrack); idTrack.SongTitle = "We Built This City"; idTrack.Artist = "Starship"; idTrack.MusicBrainzRecordingId = "7b2a32ca-cac6-4f22-ac44-5c77ad99f155"; idTrack.AcoustId = "1f6d1c1b-5a0b-4a6e-9a8c-2c3c0b1d6f11"; idTrack.MusicBrainzReleaseId = "not-an-id";
                string audioBefore = SongTags.AudioHash(idMp3); SongTags.Save(idTrack, null, false, SongTags.IdentityTags(idTrack)); var idTags = FileTags(idMp3);
                Check(idTags["title"] == "We Built This City" && idTags["MusicBrainz Recording Id"] == "7b2a32ca-cac6-4f22-ac44-5c77ad99f155" && idTags["Acoustid Id"] == "1f6d1c1b-5a0b-4a6e-9a8c-2c3c0b1d6f11" && !idTags.ContainsKey("MusicBrainz Album Id") && SongTags.AudioHash(idMp3) == audioBefore, "Recognized IDs are written to the file's tags without touching the audio; invalid IDs are skipped");
                var recognitionCacheFile = System.IO.Path.Combine(run, "recognition-cache.json"); var cachedResult = new RecognitionResult { AudioHash = "abc", Fingerprint = "AQADx", FileDuration = 12, Hints = new SongHints { Title = "Cached title" } }; cachedResult.Candidates.Add(new RecognitionCandidate { Title = "T", Artist = "A", Confidence = 90 });
                new RecognitionCache(recognitionCacheFile).Put(cachedResult); var rereadResult = new RecognitionCache(recognitionCacheFile).Get("abc");
                Check(rereadResult != null && rereadResult.Candidates[0].Title == "T" && rereadResult.FileDuration == 12 && rereadResult.Hints.Title == "Cached title", "Recognition results are cached on disk and reused offline");
                SongHints downloadHints; string described = AudioDownloader.CompletedHints("STILL_META:" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "filepath", idMp3 }, { "title", "Starship - We Built This City (Official Music Video)" }, { "channel", "StarshipVEVO" }, { "duration", 294.0 }, { "description", "Provided to YouTube by RCA" } }), run, out downloadHints);
                Check(described == idMp3 && downloadHints.Title.StartsWith("Starship") && downloadHints.Channel == "StarshipVEVO" && downloadHints.Duration == 294 && AudioDownloader.CompletedHints("STILL_META:{\"filepath\":\"C:\\\\elsewhere.mp3\"}", run, out downloadHints) == null, "Downloads keep YouTube's title, channel, and description for each finished file");
                Check(AudioDownloader.Arguments("https://example.com/v", music).Any(a => a.StartsWith("after_move:STILL_META:")), "Downloads ask yt-dlp for the video details recognition needs");
                LyricsEngine.HttpOverride = delegate(string url) { if (url.Contains("lrclib.net/api/get")) return url.Contains("Free%20Bird") ? Fixture("lrclib-get.json") : null; if (url.Contains("lrclib.net/api/search")) return url.Contains("We%20Built") ? Fixture("lrclib-search.json") : "[]"; if (url.Contains("lyrics.ovh/v1/Starship")) return Fixture("lyricsovh-ok.json"); return null; };
                try
                {
                    var freeBird = LyricsEngine.Find(new LyricsQuery { Title = "Free Bird", Artist = "Lynyrd Skynyrd", Duration = 550, Confirmed = true }, System.Threading.CancellationToken.None);
                    Check(freeBird.Best.Source == "LRCLIB" && freeBird.Best.Synced && freeBird.Best.Confidence >= LyricsEngine.HighConfidence && LyricsFiles.IsSynced(freeBird.Best.Text), "LRCLIB time-synced lyrics with matching title, artist, and length are high confidence (real LRCLIB reply)");
                    var wrongVersion = LyricsEngine.Find(new LyricsQuery { Title = "Free Bird", Artist = "Lynyrd Skynyrd", Duration = 300, Confirmed = true }, System.Threading.CancellationToken.None);
                    Check(wrongVersion.Candidates.All(c => c.Source != "LRCLIB"), "Lyrics for a version with a different length are rejected");
                    var city = LyricsEngine.Find(new LyricsQuery { Title = "We Built This City", Artist = "Starship", Duration = 278, Confirmed = true }, System.Threading.CancellationToken.None);
                    Check(city.Best.Source == "LRCLIB" && city.Best.Synced && Math.Abs(city.Best.Duration - 278) <= 2 && city.Best.Confidence >= LyricsEngine.HighConfidence && city.Candidates.All(c => c.Duration <= 0 || Math.Abs(c.Duration - 278) <= 10), "Among many LRCLIB versions, only ones with the same length are kept and the closest wins");
                    var unconfirmed = LyricsEngine.Find(new LyricsQuery { Title = "We Built This City", Artist = "Starship", Duration = 278, Confirmed = false }, System.Threading.CancellationToken.None);
                    Check(unconfirmed.Best.Confidence <= city.Best.Confidence - 9 && unconfirmed.Best.Notes.Any(n => n.Contains("not confirmed")), "Lyrics for songs that were not identified count for less");
                    var noLength = LyricsEngine.Find(new LyricsQuery { Title = "We Built This City", Artist = "Starship", Duration = 0, Confirmed = true }, System.Threading.CancellationToken.None);
                    Check(noLength.Best.Confidence < LyricsEngine.HighConfidence && noLength.Candidates.Any(c => c.Source == "lyrics.ovh"), "Without a length to compare, lyrics are never saved automatically and lyrics.ovh is used as backup");
                    var instrumentalResult = new LyricsCandidate { Source = "LRCLIB", Instrumental = true, Text = "", Title = "Let's Groove", Artist = "Earth, Wind & Fire", Duration = 235 };
                    var wantedInstrumental = new LyricsCandidate { Source = "LRCLIB", Instrumental = true, Text = "", Title = "Let's Groove (Instrumental)", Artist = "Earth, Wind & Fire", Duration = 235 };
                    LyricsEngine.Score(instrumentalResult, new LyricsQuery { Title = "Let's Groove", Artist = "Earth, Wind & Fire", Duration = 235, Confirmed = true });
                    LyricsEngine.Score(wantedInstrumental, new LyricsQuery { Title = "Let's Groove (Instrumental)", Artist = "Earth, Wind & Fire", Duration = 235, Confirmed = true });
                    Check(instrumentalResult.Confidence < LyricsEngine.CheckConfidence && wantedInstrumental.Confidence >= LyricsEngine.HighConfidence, "An 'instrumental' result never replaces a sung song's lyrics unless the song itself is the instrumental");
                    var nothing = LyricsEngine.Find(new LyricsQuery { Title = "Not A Real Song 123", Artist = "Nobody Known", Duration = 100, Confirmed = true }, System.Threading.CancellationToken.None);
                    Check(nothing.Best == null, "A song with no lyrics anywhere gets nothing instead of another song's lyrics");
                }
                finally { LyricsEngine.HttpOverride = null; }
                var lrc = LrcFormat.Parse("[ar:Test]\n[offset:+500]\n[00:10.50][01:10.00]Chorus line\n[00:05.00]<00:05.00>Hello <00:05.60>there\n[00:20]Plain stamp\nno timestamp line");
                Check(lrc.Count == 4 && Math.Abs(lrc[0].Time - 4.5) < .001 && lrc[0].Words.Count == 2 && Math.Abs(lrc[0].Words[1].Time - 5.1) < .001 && lrc[0].Text == "Hello there" && lrc[1].Text == "Chorus line" && Math.Abs(lrc[1].Time - 10) < .001 && lrc[2].Text == "Plain stamp" && Math.Abs(lrc[3].Time - 69.5) < .001, "LRC reader handles offsets, repeated chorus times, word timing, and loose lines");
                var realTiming = LrcFormat.Parse(LyricsEngine.ParseLrclibGet(Fixture("lrclib-get.json")).Text);
                Check(realTiming.Count >= 20 && Math.Abs(realTiming[0].Time - 69.24) < .01 && realTiming.Zip(realTiming.Skip(1), (a, b) => a.Time <= b.Time).All(x => x), "LRCLIB's real line timing (Free Bird) becomes karaoke lines in order");
                var lrcAgain = LrcFormat.Parse(LrcFormat.Write(lrc));
                Check(lrcAgain.Count == 4 && lrcAgain.Zip(lrc, (a, b) => Math.Abs(a.Time - b.Time) < .006 && a.Text == b.Text && a.Words.Count == b.Words.Count).All(x => x), "LRC written by Digitone reads back identically, including word timing");
                Check(LrcFormat.ActiveIndex(lrc, 0) == -1 && LrcFormat.ActiveIndex(lrc, 4.6) == 0 && LrcFormat.ActiveIndex(lrc, 30) == 2 && LrcFormat.ActiveIndex(lrc, 500) == 3, "The highlighted karaoke line follows the playback time");
                var gaps = new List<LyricLine> { new LyricLine { Time = 0 }, new LyricLine(), new LyricLine(), new LyricLine { Time = 9 } }; LrcFormat.FillGaps(gaps);
                double stamped; Check(Math.Abs(gaps[1].Time - 3) < .001 && Math.Abs(gaps[2].Time - 6) < .001 && LrcFormat.TryStamp("01:02.5", out stamped) && Math.Abs(stamped - 62.5) < .001 && !LrcFormat.TryStamp("abc", out stamped) && LrcFormat.Stamp(62.5) == "01:02.50", "Half-finished timing fills the gaps, and typed times are checked");
                var artPixels = new WriteableBitmap(64, 64, 96, 96, PixelFormats.Bgr32, null); var artFill = new int[64 * 64]; for (int i = 0; i < artFill.Length; i++) artFill[i] = (i % 64) * 3 << 16 | (i / 64) * 3; artPixels.WritePixels(new Int32Rect(0, 0, 64, 64), artFill, 64 * 4, 0);
                var artEncoder = new JpegBitmapEncoder(); artEncoder.Frames.Add(BitmapFrame.Create(artPixels)); byte[] artJpeg; using (var artStream = new MemoryStream()) { artEncoder.Save(artStream); artJpeg = artStream.ToArray(); }
                var artCalls = new List<string>();
                AlbumArt.Fetch = delegate(Uri uri, System.Threading.CancellationToken token) { artCalls.Add(uri.AbsoluteUri); return artJpeg; };
                try
                {
                    var exactArt = AlbumArt.Find(new Track { Path = first, MusicBrainzReleaseId = "7b2a32ca-cac6-4f22-ac44-5c77ad99f155" }, true, System.Threading.CancellationToken.None);
                    Check(exactArt != null && exactArt.Source.Contains("exact album") && artCalls.Count == 1 && artCalls[0].Contains("coverartarchive.org/release/7b2a32ca-cac6-4f22-ac44-5c77ad99f155/front-500"), "Album art comes from the Cover Art Archive for the exact album recognition found");
                    artCalls.Clear();
                    Check(AlbumArt.Find(new Track { Path = first, Artist = "Someone", Album = "Something" }, false, System.Threading.CancellationToken.None) == null && artCalls.Count == 0, "Songs that are not identified get no guessed cover and no online lookup");
                    string coverFolder = System.IO.Path.Combine(run, "Cover folder"); Directory.CreateDirectory(coverFolder); string coverSong = System.IO.Path.Combine(coverFolder, "song.wav"); File.Copy(first, coverSong); File.WriteAllBytes(System.IO.Path.Combine(coverFolder, "cover.jpg"), artJpeg);
                    var folderArt = AlbumArt.Find(new Track { Path = coverSong, MusicBrainzReleaseId = "7b2a32ca-cac6-4f22-ac44-5c77ad99f155" }, true, System.Threading.CancellationToken.None);
                    Check(folderArt != null && folderArt.Source.Contains("folder") && artCalls.Count == 0, "A cover picture in the song's folder is used first, without going online");
                }
                finally { AlbumArt.Fetch = ArtworkFinder.Request; }
                var lyricFolders = new PortableFolders(System.IO.Path.Combine(run, "Lyrics store")); var lyricStore = new LibraryStore(lyricFolders.LibraryFile, lyricFolders);
                var lyricLibrary = new Library(); var syncedTrack = new Track { Path = @"C:\x\a.mp3", Artist = "A/B", SongTitle = "Song: One", Lyrics = "[00:01.00] one\n[00:02.00] two\n[00:03.00] three" }; var plainTrack = new Track { Path = @"C:\x\b.mp3", Artist = "C", SongTitle = "Two", Lyrics = "plain words" };
                lyricLibrary.Tracks.Add(syncedTrack); lyricLibrary.Tracks.Add(plainTrack); lyricStore.Save(lyricLibrary);
                string lyricLibraryJson = File.ReadAllText(lyricFolders.LibraryFile); var lyricReload = new LibraryStore(lyricFolders.LibraryFile, lyricFolders).Load();
                Check(syncedTrack.LyricsFile.EndsWith(".lrc") && plainTrack.LyricsFile.EndsWith(".txt") && File.Exists(System.IO.Path.Combine(lyricFolders.Lyrics, syncedTrack.LyricsFile)) && syncedTrack.LyricsFile.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 && !lyricLibraryJson.Contains("plain words") && lyricReload.Tracks.Single(t => t.SongTitle == "Two").Lyrics == "plain words" && lyricReload.Tracks.Single(t => t.SongTitle == "Song: One").Lyrics.Contains("three"), "Lyrics are stored as .lrc/.txt files in userdata\\lyrics with safe names and reload correctly");
                File.WriteAllText(lyricFolders.LibraryFile, "{\"Tracks\":[{\"Path\":\"C:\\\\x\\\\c.mp3\",\"Lyrics\":\"old inline words\"}],\"Playlists\":[]}");
                var inlineStore = new LibraryStore(lyricFolders.LibraryFile, lyricFolders); var inlineLibrary = inlineStore.Load(); inlineStore.Save(inlineLibrary);
                Check(inlineLibrary.Tracks[0].Lyrics == "old inline words" && !String.IsNullOrEmpty(inlineLibrary.Tracks[0].LyricsFile) && !File.ReadAllText(lyricFolders.LibraryFile).Contains("old inline words"), "Lyrics from older libraries move out of library.json into lyrics files");
                string clearedLyrics = System.IO.Path.Combine(lyricFolders.Lyrics, inlineLibrary.Tracks[0].LyricsFile); inlineLibrary.Tracks[0].Lyrics = null; inlineStore.Save(inlineLibrary);
                Check(!File.Exists(clearedLyrics) && inlineLibrary.Tracks[0].LyricsFile == null, "Clearing lyrics removes their file");
                var processInfo = AudioDownloader.KeepCachesPortable(new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false });
                Check(processInfo.EnvironmentVariables["DENO_DIR"].StartsWith(PortableFolders.Current.UserData + "\\") && processInfo.EnvironmentVariables["TEMP"].StartsWith(PortableFolders.Current.UserData + "\\"), "Download tools cache inside userdata instead of the Windows profile");
                var store = new LibraryStore(System.IO.Path.Combine(run, "Data", "library.json"));
                Check(store.Load().Tracks.Count == 0, "First launch starts empty");
                var testLibrary = store.Load(); testLibrary.GlobalMediaKeys=false; testLibrary.AutoLyrics=false; testLibrary.AutoRecognize=false; testLibrary.AutoLyricsDefaultApplied=true; testLibrary.AutoArt=false; testLibrary.BetaAutoQualityOff=true;
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
                        Check(player.Data.Tracks.Count==0&&player.Data.MusicFolders.SequenceEqual(new[]{PlayerApp.DigiMusicFolder})&&player.Data.MainMusicFolder==PlayerApp.DigiMusicFolder&&player.Data.DownloadFolder==PlayerApp.DigiMusicFolder&&Directory.Exists(PlayerApp.DigiMusicFolder)&&player.Data.AutoLyrics==true,"First-run creates and selects DigiMusic without scanning, with automatic lyrics on by default");
                        player.Data.AutoLyrics=false;
                        Check(player.Data.LastSeenVersion==AppInfo.Version&&!player.ShouldShowWhatsNew,"Fresh installs get Welcome only, not What's new");
                        player.Data.LastSeenVersion="5.3.5";
                        Check(player.ShouldShowWhatsNew,"What's new shows after an update");
                        player.HoldBackgroundWork();
                        Check(!player.BackgroundWorkAllowed.IsCompleted,"Background fixes wait while What's new is open");
                        player.Data.AutoRecognize=false; player.Data.AutoArt=true;
                        var news=player.CreateWhatsNew();
                        news.Loaded+=delegate{
                            var toggles=Elements(news).OfType<CheckBox>().ToList();
                            Check(toggles.First(x=>x.Name=="WhatsNewNames").IsChecked==false&&toggles.First(x=>x.Name=="WhatsNewLyrics").IsChecked==false&&toggles.First(x=>x.Name=="WhatsNewArt").IsChecked==true,"What's new switches start from the current settings");
                            Check(Elements(news).OfType<TextBlock>().Count(x=>x.Name=="WhatsNewItem")==AppInfo.WhatsNew.Length&&AppInfo.WhatsNew.Length>=3&&AppInfo.WhatsNew.Length<=6,"What's new lists a few short highlights");
                            Check(Elements(news).OfType<Button>().Any(x=>x.Name=="WhatsNewBeta"&&x.Visibility==Visibility.Visible)&&Elements(news).OfType<Button>().Any(x=>x.Name=="WhatsNewChangelog"),"What's new offers the full changelog and the Beta look");
                            Snapshot(news,System.IO.Path.Combine(run,"whats-new.png"));
                            toggles.First(x=>x.Name=="WhatsNewArt").IsChecked=false;
                            Elements(news).OfType<Button>().First(x=>x.Name=="WhatsNewDone").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        };
                        news.ShowDialog();
                        Check(player.BackgroundWorkAllowed.IsCompleted,"Closing What's new lets background fixes start");
                        Check(player.Data.AutoArt==false&&player.Data.AutoLyrics==false&&player.Data.AutoRecognize==false&&store.Load().AutoArt==false,"What's new switches are saved when it closes");
                        Check(store.Load().LastSeenVersion==AppInfo.Version&&!player.ShouldShowWhatsNew,"What's new shows only once per version");
                        player.Data.LastSeenVersion=null;
                        var betaNews=player.CreateWhatsNew();
                        betaNews.Loaded+=delegate{ Elements(betaNews).OfType<Button>().First(x=>x.Name=="WhatsNewBeta").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
                        betaNews.ShowDialog();
                        for(int i=0;i<40&&!player.Data.BetaTheme;i++) await Task.Delay(25);
                        Check(player.Data.BetaTheme&&player.Data.LastSeenVersion==AppInfo.Version,"Try the Beta look turns Beta on and closes What's new");
                        player.SetBeta(false);
                        Check(!player.Data.Playlists.Any(p=>p.FolderPath==PlayerApp.DigiMusicFolder),"DigiMusic is never represented as a playlist card");
                        Check(NativeChrome.IsDark(player.Window), "Windows title bar is in dark mode");
                        Check(((SolidColorBrush)player.Find<Border>("RecordArt").Background).Color.A == 0, "Record backdrop is transparent");
                        Check(((SolidColorBrush)player.Find<Grid>("DownloadPane").Background).Color.A == 0, "Downloads does not obscure ambient geometry");
                        if (SystemParameters.ClientAreaAnimation)
                        {
                            var pane = player.Find<Grid>("LibraryPane"); int glidesBefore = player.PageGlideCount; player.SelectCollection(null, true);
                            Check(pane.Opacity == 0 && pane.CacheMode is BitmapCache, "Page change hides the new page before it is drawn, then glides it in as one cached picture (no flash, no snapshot)");
                            await Task.Delay(60); var transition=BetaMotion.Parts(pane).Move;
                            Check(player.PageGlideCount > glidesBefore && transition.HasAnimatedProperties && transition.Y > 0 && transition.Y < PlayerApp.GlideRise, "The page glide starts after layout and runs through an animation clock");
                            await Task.Delay(PlayerApp.GlideMs + 150);
                            Check(Math.Abs(BetaMotion.Parts(pane).Move.X) < .01 && Math.Abs(BetaMotion.Parts(pane).Move.Y) < .01 && pane.Opacity == 1 && pane.CacheMode == null && player.Find<Border>("NowPane").Opacity == 1, "The page glide lands exactly in place, back to crisp live drawing, and leaves still panels alone");
                            player.SelectCollection(null, false); await Task.Delay(PlayerApp.GlideMs + 150);
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
                        var titlePlate = (Border)((Viewbox)player.Find<TextBlock>("CollectionTitle").Parent).Parent;
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
                        Func<int,bool,string,Func<LyricsQuery,System.Threading.CancellationToken,LyricsSearch>> fakeLyrics=(confidence,synced,words)=>(q,c)=>{var fakeSearch=new LyricsSearch{Query=q};fakeSearch.Candidates.Add(new LyricsCandidate{Source="LRCLIB",Synced=synced,Text=synced?"[00:01.00] "+words+"\n[00:02.00] Line two\n[00:03.00] Line three":words,Confidence=confidence});return fakeSearch;};
                        player.Data.AutoLyrics=true; player.FindLyrics=fakeLyrics(95,true,"Automatically found test words"); player.AutoFindLyrics(autoTrack);
                        for(int wait=0;wait<40 && String.IsNullOrEmpty(autoTrack.Lyrics);wait++) await Task.Delay(100);
                        string libraryText=File.ReadAllText(System.IO.Path.Combine(run,"Data","library.json"));
                        Check(autoTrack.Lyrics!=null && autoTrack.Lyrics.Contains("Automatically found test words") && autoTrack.LyricsStatus=="Accepted" && autoTrack.LyricsSynced && store.Load().Tracks[1].Lyrics==autoTrack.Lyrics && autoTrack.LyricsFile.EndsWith(".lrc") && File.Exists(System.IO.Path.Combine(PortableFolders.Current.Lyrics,autoTrack.LyricsFile)) && !libraryText.Contains("Automatically found"),"Confident time-synced lyrics are saved as an .lrc file in userdata\\lyrics, not inside the library");
                        player.FindLyrics=fakeLyrics(95,false,"Replacement"); player.AutoFindLyrics(autoTrack); await Task.Delay(50);
                        Check(autoTrack.Lyrics.Contains("Automatically found test words"),"Automatic lookup preserves existing lyrics");
                        var reviewLyricsTrack=player.Data.Tracks[0]; string keptWords=reviewLyricsTrack.Lyrics,keptArtist=reviewLyricsTrack.Artist; reviewLyricsTrack.Lyrics=null; reviewLyricsTrack.LyricsStatus=null; if(String.IsNullOrWhiteSpace(reviewLyricsTrack.Artist)) reviewLyricsTrack.Artist="Review artist";
                        player.FindLyrics=fakeLyrics(62,false,"Uncertain test words"); player.AutoFindLyrics(reviewLyricsTrack);
                        for(int wait=0;wait<40 && reviewLyricsTrack.LyricsStatus!="Review";wait++) await Task.Delay(100);
                        Check(String.IsNullOrEmpty(reviewLyricsTrack.Lyrics) && reviewLyricsTrack.LyricsStatus=="Review" && player.LyricsReviewCount==1,"Uncertain lyrics are never saved automatically and wait for review");
                        var reviewLyricsDialog=player.CreateLyrics(reviewLyricsTrack); reviewLyricsDialog.Show(); reviewLyricsDialog.UpdateLayout(); await Task.Delay(100);
                        var lyricsChoice=Elements(reviewLyricsDialog).OfType<ComboBox>().First(x=>x.Name=="LyricsChoice");
                        Check(lyricsChoice.Visibility==Visibility.Visible && lyricsChoice.Items.Count==1 && Elements(reviewLyricsDialog).OfType<TextBox>().First(x=>x.Name=="LyricsText").Text=="Uncertain test words","The lyrics window shows waiting results with a preview and choices");
                        Elements(reviewLyricsDialog).OfType<Button>().First(x=>x.Name=="SaveLyrics").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(reviewLyricsTrack.Lyrics=="Uncertain test words" && reviewLyricsTrack.LyricsStatus=="Accepted" && reviewLyricsTrack.LyricsSource=="LRCLIB" && reviewLyricsTrack.LyricsSummary.StartsWith("Chosen by you") && player.LyricsReviewCount==0,"Saving a reviewed result accepts it and records where it came from");
                        reviewLyricsDialog.Close();
                        var lyricsReview=player.ShowLyricsReview(autoTrack); await Task.Delay(250);
                        var lyricsReviewList=Elements(lyricsReview).OfType<ListBox>().First(x=>x.Name=="LyricsReviewList");
                        Check(lyricsReviewList.SelectedItem!=null && lyricsReviewList.SelectedItem.ToString().StartsWith("✓ synced") && Elements(lyricsReview).OfType<TextBox>().First(x=>x.Name=="ReviewLyricsText").Text==autoTrack.Lyrics,"Lyrics review opens on the chosen song and shows its attached time-synced lyrics");
                        Elements(lyricsReview).OfType<Button>().First(b=>b.Name=="TimingMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                        Check(Elements(lyricsReview).OfType<ListBox>().First(b=>b.Name=="TimingRows").Items.Count==3,"The timing editor lists every timed line");
                        Elements(lyricsReview).OfType<Button>().First(b=>Convert.ToString(b.Content)=="+").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Snapshot(lyricsReview,System.IO.Path.Combine(run,"lyrics-review-timing.png"));
                        Elements(lyricsReview).OfType<Button>().First(b=>b.Name=="AcceptLyricsReview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(autoTrack.Lyrics.Contains("[00:01.10]") && autoTrack.LyricsStatus=="Accepted" && autoTrack.LyricsSource=="Edited by you" && store.Load().Tracks[1].Lyrics.Contains("[00:01.10]"),"Nudging a line's time and accepting saves the corrected timing");
                        lyricsReview.Close(); reviewLyricsTrack.Lyrics=keptWords; reviewLyricsTrack.Artist=keptArtist; store.Save(player.Data);
                        player.Data.AutoLyrics=false; player.FindLyrics=LyricsEngine.Find; autoTrack.Artist=originalArtist;
                        Check(!player.Find<Button>("PlayButton").IsEnabled, "Import alone does not enable Play");
                        player.Find<TextBox>("Search").Text = "golden";
                        Check(player.Find<ListBox>("Tracks").Items.Count == 1, "Search filters by filename");
                        player.Find<TextBox>("Search").Text = "no matches";
                        Check(player.Find<StackPanel>("EmptyState").Visibility == Visibility.Visible, "Search has a meaningful empty state");
                        player.Find<TextBox>("Search").Text = "";
                        player.Find<ListBox>("Tracks").SelectedIndex = 0; Press(player, "FavoriteButton"); Press(player, "FavoritesButton");
                        Check(player.Find<ListBox>("Tracks").Items.Count == 1 && player.Data.Tracks[0].Favorite, "Favorite action and favorites collection");
                        player.Find<ListBox>("Tracks").SelectedIndex=0;((MenuItem)player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<object>().First(i=>Convert.ToString(((MenuItem)i).Header)=="Remove from this collection")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(!player.Data.Tracks[0].Favorite&&player.UndoAvailable,"Removing from Favorites keeps the song in the library and offers Undo");
                        var undoNotice=Elements(player.Window).OfType<Border>().Single(b=>b.Name=="UndoNotice");var undoButton=Elements(undoNotice).OfType<Button>().Single(b=>b.Name=="UndoActionButton");var undoBar=Elements(undoNotice).OfType<Border>().Single(b=>b.Name=="UndoCountdownBar");player.Window.UpdateLayout();var undoPosition=undoNotice.TranslatePoint(new Point(0,0),(UIElement)player.Window.Content);await Task.Delay(120);double remaining=((ScaleTransform)undoBar.RenderTransform).ScaleX;Check(undoNotice.VerticalAlignment==VerticalAlignment.Top&&Grid.GetRowSpan(undoNotice)==((Grid)player.Window.Content).RowDefinitions.Count&&Panel.GetZIndex(undoNotice)>=10000&&undoNotice.IsVisible&&undoNotice.ActualWidth>=340&&undoNotice.ActualWidth<=580&&undoNotice.ActualHeight>30&&undoPosition.Y>=70&&undoPosition.Y<110&&undoPosition.Y+undoNotice.ActualHeight<player.Window.ActualHeight&&undoNotice.Effect is DropShadowEffect&&undoButton.Effect is DropShadowEffect&&undoBar.Height==3&&undoBar.RenderTransform.HasAnimatedProperties&&remaining<1&&remaining>0,"Undo is a compact high overlay with a live draining countdown bar");Snapshot(player.Window,System.IO.Path.Combine(run,"undo-visible-after-removal.png"));player.UndoLastRemoval();Check(player.Data.Tracks[0].Favorite,"Undo restores a song to Favorites");
                        Press(player, "LibraryButton");
                        var playlist = player.CreatePlaylist("A quieter place", player.Data.Tracks);
                        Check(player.Find<ListBox>("Playlists").Items.Count == 0 && !playlist.Favorite && playlist.Paths.Count == 2, "New manual playlist stays out of quick access by default");
                        var quickPlaylistCard=player.Find<WrapPanel>("PlaylistGrid").Children.OfType<Button>().Single(b=>b.Tag==playlist);var quickAccess=(MenuItem)quickPlaylistCard.ContextMenu.Items.Cast<object>().First(i=>Convert.ToString(((MenuItem)i).Header)=="Add to quick access");quickAccess.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(playlist.Favorite&&player.Find<ListBox>("Playlists").Items.Count==1,"Playlist enters quick access only after the explicit menu action");
                        var contextTarget=new Playlist{Name="Context target",Paths=new List<string>(),Favorite=false};player.Data.Playlists.Add(contextTarget);player.Find<ListBox>("Tracks").SelectedItems.Clear();foreach(Track track in player.Data.Tracks)player.Find<ListBox>("Tracks").SelectedItems.Add(track);var trackContext=player.Find<ListBox>("Tracks").ContextMenu;trackContext.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));var addSubmenu=trackContext.Items.Cast<MenuItem>().Single(i=>Convert.ToString(i.Header)=="Add to playlist");addSubmenu.ApplyTemplate();Check(addSubmenu.Items.Cast<MenuItem>().Any(i=>Convert.ToString(i.Header)==contextTarget.Name)&&addSubmenu.Template.FindName("PART_Popup",addSubmenu)!=null,"Song context menu exposes a working playlist dropdown");((MenuItem)addSubmenu.Items.Cast<MenuItem>().First(i=>Convert.ToString(i.Header)==contextTarget.Name)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(contextTarget.Paths.Count==2,"Ctrl-style multi-selection batch adds every selected song to a playlist");player.Data.Playlists.Remove(contextTarget);store.Save(player.Data);
                        var contextMenu = player.Find<ListBox>("Playlists").ContextMenu; contextMenu.PlacementTarget = player.Find<ListBox>("Playlists"); contextMenu.IsOpen = true; await Task.Delay(100); contextMenu.UpdateLayout();
                        var menuBitmap = new RenderTargetBitmap((int)contextMenu.ActualWidth, (int)contextMenu.ActualHeight, 96, 96, PixelFormats.Pbgra32); menuBitmap.Render(contextMenu); var menuEncoder = new PngBitmapEncoder(); menuEncoder.Frames.Add(BitmapFrame.Create(menuBitmap)); using (var menuStream = File.Create(System.IO.Path.Combine(run, "playlist-menu.png"))) menuEncoder.Save(menuStream);
                        var menuItem = (MenuItem)contextMenu.Items[0]; menuItem.ApplyTemplate(); Check(menuItem.Template.FindName("MenuSurface", menuItem) != null, "Playlist context menu uses the custom dark template without an icon gutter"); contextMenu.IsOpen = false;
                        player.Find<ListBox>("Tracks").SelectedItems.Clear();player.Find<ListBox>("Tracks").SelectedIndex = 0;var draggedTrack=(Track)player.Find<ListBox>("Tracks").SelectedItem;Check(player.ReorderPlaylistTracks(new[]{draggedTrack},2)&&playlist.Paths[1]==first,"Playlist drag can place a song at the exact end");Check(player.ReorderPlaylistTracks(new[]{draggedTrack},0)&&playlist.Paths[0]==first,"Playlist drag can place a song at the exact beginning");Check(player.ReorderPlaylistTracks(new[]{draggedTrack},2)&&playlist.Paths[1]==first,"Playlist track ordering");
                        ((MenuItem)player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<object>().First(i=>Convert.ToString(((MenuItem)i).Header)=="Remove from this collection")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        Check(playlist.Paths.Count == 1 && player.Data.Tracks.Count == 2 && File.Exists(first), "Playlist removal preserves the library and original audio");
                        Check(player.UndoAvailable,"Playlist removal shows a temporary Undo notification");player.UndoLastRemoval();Check(playlist.Paths.Count==2,"Undo restores a playlist song to its original position");
                        int libraryIndex=player.Data.Tracks.IndexOf(draggedTrack),membershipIndex=playlist.Paths.IndexOf(draggedTrack.Path);player.RemoveTracksFromLibrary(new List<Track>{draggedTrack});store.Save(player.Data);Check(!player.Data.Tracks.Contains(draggedTrack)&&player.Data.RemovedPaths.Contains(draggedTrack.Path,StringComparer.OrdinalIgnoreCase)&&!playlist.Paths.Contains(draggedTrack.Path,StringComparer.OrdinalIgnoreCase),"Library removal preserves the file but records an intentional rescan exclusion");await player.Import(new[]{draggedTrack.Path},true);Check(!player.Data.Tracks.Contains(draggedTrack),"Library rescan does not reimport an intentionally removed song");player.UndoLastRemoval();Check(player.Data.Tracks.IndexOf(draggedTrack)==libraryIndex&&playlist.Paths.IndexOf(draggedTrack.Path)==membershipIndex&&!player.Data.RemovedPaths.Contains(draggedTrack.Path,StringComparer.OrdinalIgnoreCase),"Library Undo restores metadata, ordering, playlist membership, and clears the exclusion");
                        player.Find<ListBox>("Tracks").SelectedItem=draggedTrack;((MenuItem)player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<object>().First(i=>Convert.ToString(((MenuItem)i).Header)=="Remove from this collection")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));var restored = store.Load();
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
                        var karaokeTrack=player.Data.Tracks.First(t=>t.Title==player.CurrentTitle); string karaokeBefore=karaokeTrack.Lyrics;
                        karaokeTrack.Lyrics="[00:00.00]First line\n[00:00.20]<00:00.20>Second <00:00.40>line\n[00:30.00]Later line\n[00:40.00]Last line";
                        Press(player,"LyricsButton"); Check(player.LyricsVisible,"Lyrics toggles an in-app display on");
                        await Task.Delay(500);
                        Check(player.KaraokeLines!=null && player.KaraokeLines.Count==4 && player.KaraokeActive>=0 && player.KaraokeActive<=1 && player.KaraokeLines[1].Words.Count==2,"Time-synced lyrics play karaoke-style with the sung line highlighted");
                        Snapshot(player.Window,System.IO.Path.Combine(run,"lyrics-display.png"));
                        Press(player,"LyricsButton"); Check(!player.LyricsVisible,"Lyrics toggles the display off");
                        karaokeTrack.Lyrics="One\nTwo\nThree"; store.Save(player.Data);
                        var timingWindow=player.ShowLyricsReview(karaokeTrack); await Task.Delay(250);
                        Elements(timingWindow).OfType<Button>().First(b=>b.Name=="TimingMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                        var timingRows=Elements(timingWindow).OfType<ListBox>().First(b=>b.Name=="TimingRows"); timingRows.SelectedIndex=0;
                        double stampAt=player.Position; Elements(timingWindow).OfType<Button>().First(b=>b.Name=="StampLine").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(timingRows.Items.Count==3 && timingRows.SelectedIndex==1 && stampAt>0,"Tap timing stamps the selected line at the playing position and moves to the next line");
                        Elements(timingWindow).OfType<Button>().First(b=>b.Name=="AcceptLyricsReview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var timedLines=LrcFormat.Parse(karaokeTrack.Lyrics);
                        Check(karaokeTrack.LyricsSynced && karaokeTrack.LyricsSource=="Timed by you" && timedLines.Count==3 && Math.Abs(timedLines[0].Time-stampAt)<1 && timedLines[1].Time>timedLines[0].Time,"Timing plain lyrics by tapping saves them as karaoke lyrics");
                        timingWindow.Close(); karaokeTrack.Lyrics=karaokeBefore; karaokeTrack.LyricsSource=null; karaokeTrack.LyricsStatus=null; store.Save(player.Data);
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
                        Press(player,"QueueToggleButton"); Check(player.Find<Border>("QueueDrawer").Visibility == Visibility.Visible,"Queue opens in its dedicated drawer");int queuedBefore=player.QueueLength;player.Find<ListBox>("Queue").SelectedIndex=0;Press(player,"QueueRemoveButton");Check(player.QueueLength==queuedBefore-1&&player.UndoAvailable,"Queue removal shows Undo without affecting the current song");player.UndoLastRemoval();Check(player.QueueLength==queuedBefore,"Undo restores the queued song to its original position");Press(player,"CloseQueueButton"); Check(player.Find<Border>("QueueDrawer").Visibility == Visibility.Collapsed,"Queue drawer closes without changing playback");
                        player.Window.Width = 920; player.Window.Height = 660; await Task.Delay(100);

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
                        player.Next(true);
                        Press(player, "RepeatButton"); Press(player,"ShuffleButton"); player.Next(true);
                        Check(player.IsPlaying && player.CurrentTitle == "01 - Golden hour", "All Music restarts from its first song after natural queue completion");
                        var playlistCycle=playlist.Paths.Select(path=>player.Data.Tracks.First(t=>t.Path==path)).ToList();player.StartQueue(playlistCycle,playlistCycle.Last());player.Next(true);Check(player.IsPlaying&&player.CurrentTitle==playlistCycle[0].Title,"Saved playlists restart from their first song after natural queue completion");var unavailable=new Track{Path=System.IO.Path.Combine(run,"missing-queue-track.wav"),SongTitle="Unavailable queue fixture"};player.StartQueue(new[]{unavailable,playlistCycle[0]},unavailable);for(int i=0;i<100&&player.CurrentTitle!=playlistCycle[0].Title;i++)await Task.Delay(30);Check(player.CurrentTitle==playlistCycle[0].Title&&playlistCycle[0].IsPlaying&&!unavailable.IsPlaying,"Unavailable queued files advance to the next playable song");
                        string corruptPath = System.IO.Path.Combine(run, "corrupt.json"); File.WriteAllText(corruptPath, "{ broken");
                        bool rejected = false; try { new LibraryStore(corruptPath).Load(); } catch { rejected = true; }
                        Check(rejected && File.ReadAllText(corruptPath) == "{ broken", "Corrupt library is rejected without overwriting it");
                        Press(player, "DownloadsButton");
                        Check(player.Find<Grid>("DownloadPane").Visibility == Visibility.Visible && player.Find<Grid>("LibraryPane").Visibility == Visibility.Collapsed, "Downloads tab switches without replacing playback controls");Check(player.Find<Button>("SpotifyImportButton")!=null,"Downloads exposes the Spotify Import Assistant separately from direct links");Press(player,"SpotifyImportButton");await Task.Delay(100);var spotifyWindow=Application.Current.Windows.Cast<Window>().Single(w=>w.Title.StartsWith("Spotify Import Assistant"));Check(spotifyWindow.ResizeMode==ResizeMode.CanResize&&!Elements(spotifyWindow).OfType<Button>().Any(b=>Convert.ToString(b.Content).Contains("Connect Spotify"))&&Elements(spotifyWindow).OfType<Button>().Any(b=>Convert.ToString(b.Content)=="Load shared link")&&!Elements(spotifyWindow).OfType<Button>().Any(b=>Convert.ToString(b.Content)=="Find YouTube matches")&&Elements(spotifyWindow).OfType<Button>().Any(b=>Convert.ToString(b.Content)=="Queue selected downloads")&&!Elements(spotifyWindow).OfType<ComboBox>().Any()&&Elements(spotifyWindow).OfType<ListBox>().Any(),"Loading a Spotify shared link owns matching automatically and every approved batch gets its own folder");Snapshot(spotifyWindow,System.IO.Path.Combine(run,"spotify-import-assistant.png"));spotifyWindow.Close();
                        var initialTargets=player.Find<ComboBox>("DownloadPlaylistTarget").Items.Cast<DownloadTarget>().ToList();Check(initialTargets.Count>=1&&initialTargets[0].Label=="All music only"&&player.Find<StackPanel>("DownloadExistingControls").Visibility==Visibility.Visible&&player.Find<WrapPanel>("DownloadNewControls").Visibility==Visibility.Collapsed&&player.Find<TextBox>("DownloadLog").Visibility==Visibility.Collapsed&&player.Find<Button>("YouTubeToggle").Content.ToString().Contains("YouTube")&&YouTubeBrowser.AllowedNavigation("https://accounts.google.com/"),"Downloads exposes playlist destinations and a sign-in-capable pop-out YouTube viewer");
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
                            Check(downloaded != null && player.Find<TextBlock>("DownloadStatus").Text.Contains("ownloaded"), "Finished MP3 automatically joins the library");
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
                            store.Save(player.Data); Check(store.Load().ThemeName == Themes.Names.Last(), "Theme selection persists across library reloads");
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
                            var prideDark=ThemeScenes.Build("Pride",600,null,false,true);var prideLight=ThemeScenes.Build("Pride",600,null,true,true);var prideNeon=ThemeScenes.Build("Pride",600,null,false,true,true);var prideBannerArt=ThemeScenes.PrideBanner();var prideFlags=Elements(prideBannerArt).OfType<Canvas>().Where(e=>e.Name.StartsWith("PrideFlag")&&!e.Name.StartsWith("PrideFlagArt")).ToList();var prideLines=Elements(prideDark).OfType<Canvas>().Single(e=>e.Name=="PrideNeonLines");var prideSpectrum=Elements(prideDark).OfType<Canvas>().Single(e=>e.Name=="PrideSpectrum");Check(prideFlags.Count==9&&prideLines.Children.OfType<System.Windows.Shapes.Path>().Count()==6&&prideSpectrum.Children.OfType<System.Windows.Shapes.Rectangle>().Count(e=>e.Name=="PrideSpectrumBar")==48,"Pride theme contains all nine flags, six rainbow lines, and an expanded spectrum");Check(Elements(prideFlags[8]).OfType<System.Windows.Shapes.Rectangle>().Any(r=>r.Fill!=null)&&Elements(prideFlags[8]).OfType<System.Windows.Shapes.Ellipse>().Any()&&prideBannerArt.ColumnDefinitions.Count==9&&prideBannerArt.Children.OfType<Viewbox>().Count()==9&&prideBannerArt.Children.OfType<Viewbox>().All(v=>v.Stretch==Stretch.Uniform&&Double.IsNaN(v.Width))&&ThemeScenes.PrideBanner(true).Children.OfType<Viewbox>().Count()==9&&prideFlags.All(f=>f.Children.OfType<System.Windows.Shapes.Rectangle>().Any(r=>r.Name=="PrideFlagNeonBorder"&&r.Effect is DropShadowEffect)),"All nine Pride flags form one spaced header banner with proportional neon borders in dark and light modes");Check(Elements(prideNeon).OfType<Canvas>().Single(e=>e.Name=="PrideSpectrum").Effect is DropShadowEffect&&((DropShadowEffect)Elements(prideNeon).OfType<Canvas>().Single(e=>e.Name=="PrideSpectrum").Effect).BlurRadius>=18,"Pride Neon strengthens the rainbow spectrum glow with one shared effect");int prideAnimationCount=0;ThemeScenes.Build("Pride",600,delegate(System.Windows.Media.Animation.IAnimatable target,DependencyProperty property,double from,double to,double seconds){prideAnimationCount++;},false,true,true);Check(prideAnimationCount<=11,"Pride scene keeps flowing lines within its optimized animation budget");player.ApplyTheme("Pride");var prideQueueButton=player.Find<Button>("QueueToggleButton");var prideSettingsButton=player.Find<Button>("ThemeButton");Check(player.PrideVisualizerActive&&player.Find<Grid>("TopHeaderBackdrop").Children.OfType<Grid>().Any(g=>g.Name=="PrideHeaderBannerArt")&&!Elements(prideDark).OfType<Canvas>().Any(e=>e.Name.StartsWith("PrideFlag"))&&((SolidColorBrush)prideQueueButton.Background).Color.A<255&&((SolidColorBrush)prideSettingsButton.Background).Color.A<255&&prideQueueButton.Style==player.Find<Button>("LibraryButton").Style&&prideSettingsButton.Style==player.Find<Button>("LibraryButton").Style&&prideQueueButton.FontSize==17&&prideSettingsButton.Padding==new Thickness(12,10,12,10),"Pride applies moving rainbow visualizers, header flags, and matching navigation-style header buttons");player.ApplyTheme("Overworld");Check(((SolidColorBrush)prideQueueButton.Background).Color.A==255&&((SolidColorBrush)prideSettingsButton.Background).Color.A==255,"Pride header transparency does not affect other themes");player.ApplyTheme("Pride");double prideWindowWidth=player.Window.Width,prideWindowHeight=player.Window.Height;player.Window.Width=2400;player.Window.Height=790;await Task.Delay(100);Snapshot(player.Window,System.IO.Path.Combine(run,"pride-ultrawide.png"));player.Window.Width=prideWindowWidth;player.Window.Height=prideWindowHeight;await Task.Delay(80);
                            var sanctuaryLight=ThemeScenes.Build("Sanctuary",600,null,true,true);var sanctuaryDark=ThemeScenes.Build("Sanctuary",600,null,false,true);var sanctuaryImage=Elements(sanctuaryDark).OfType<Image>().Single(i=>i.Name=="SanctuaryPlant");var sanctuaryBitmap=(BitmapSource)sanctuaryImage.Source;var sanctuaryPixel=new byte[4];sanctuaryBitmap.CopyPixels(new Int32Rect(0,0,1,1),sanctuaryPixel,4,0);Check(Elements(sanctuaryLight).OfType<System.Windows.Shapes.Ellipse>().Any(e=>e.Name=="SanctuarySun")&&!Elements(sanctuaryDark).OfType<System.Windows.Shapes.Ellipse>().Any(e=>e.Name=="SanctuarySun")&&Elements(sanctuaryDark).OfType<Canvas>().Any(e=>e.Name=="SanctuaryStars"),"Tree House Sanctuary switches between a light-mode sun and dark-mode night sky");var sanctuaryShrubs=Elements(sanctuaryDark).OfType<Canvas>().Single(e=>e.Name=="SanctuaryShrubs");var wideSanctuary=ThemeScenes.Build("Sanctuary",340,null,false,true);var wideShrubs=Elements(wideSanctuary).OfType<Canvas>().Single(e=>e.Name=="SanctuaryShrubs");Check(sanctuaryShrubs.Children.OfType<System.Windows.Shapes.Ellipse>().Any(e=>Canvas.GetTop(e)<520)&&wideShrubs.Children.OfType<System.Windows.Shapes.Ellipse>().Any(e=>Canvas.GetTop(e)<260),"Sanctuary shrub canopy remains visible in portrait and landscape scenes");Check(Elements(sanctuaryLight).OfType<Image>().Count(i=>i.Name=="SanctuaryPlant")==1&&Elements(sanctuaryDark).OfType<Image>().Count(i=>i.Name=="SanctuaryPlant")==1&&sanctuaryPixel[3]==0,"Sanctuary uses exactly one transparently processed supplied plant");player.Data.AppearanceMode="Dark";player.ApplyTheme("Sanctuary");Check(((FontFamily)player.Window.Resources["ThemeDisplayFont"]).Source.Contains("Segoe Print"),"Tree House Sanctuary uses lo-fi display typography");player.Data.AppearanceMode="Light";player.ApplyTheme("Sanctuary");await Task.Delay(80);Snapshot(player.Window,System.IO.Path.Combine(run,"sanctuary-light.png"));player.Data.AppearanceMode="Dark";player.ApplyTheme("Sanctuary");await Task.Delay(80);Snapshot(player.Window,System.IO.Path.Combine(run,"sanctuary-dark.png"));
                            var sanctuaryLights=Elements(sanctuaryDark).OfType<Canvas>().Single(e=>e.Name=="SanctuaryStringLights");var sanctuaryNeonLights=Elements(ThemeScenes.Build("Sanctuary",600,null,false,true,true)).OfType<Canvas>().Single(e=>e.Name=="SanctuaryStringLights");var normalBulbs=Elements(sanctuaryLights).OfType<System.Windows.Shapes.Ellipse>().Where(e=>e.Name=="SanctuaryStringBulb").ToList();var neonBulbs=Elements(sanctuaryNeonLights).OfType<System.Windows.Shapes.Ellipse>().Where(e=>e.Name=="SanctuaryStringBulb").ToList();Check(normalBulbs.Count==13&&neonBulbs.Count==13&&sanctuaryLights.Children.OfType<System.Windows.Shapes.Polyline>().Count()==1&&sanctuaryLights.Children.OfType<System.Windows.Shapes.Rectangle>().Count(r=>r.Width==2&&r.Height==18)==13&&normalBulbs.All(b=>b.Effect is DropShadowEffect)&&neonBulbs.All(b=>((DropShadowEffect)b.Effect).BlurRadius>((DropShadowEffect)normalBulbs[0].Effect).BlurRadius),"Tree House Sanctuary has attached, softly glowing string lights with a stronger Neon state");Func<Color,double> sanctuaryLum=delegate(Color value){return .2126*value.R+.7152*value.G+.0722*value.B;};player.Data.AppearanceMode="Dark";player.ApplyTheme("Sanctuary");Color sanctuaryDarkPrimary=((SolidColorBrush)player.Window.FindResource("BEEEFE8")).Color,sanctuaryDarkMuted=((SolidColorBrush)player.Window.FindResource("B919A92")).Color,sanctuaryDarkSurface=((SolidColorBrush)player.Window.FindResource("B161917")).Color;player.Data.AppearanceMode="Light";player.ApplyTheme("Sanctuary");Color sanctuaryLightPrimary=((SolidColorBrush)player.Window.FindResource("BEEEFE8")).Color,sanctuaryLightMuted=((SolidColorBrush)player.Window.FindResource("B919A92")).Color,sanctuaryLightSurface=((SolidColorBrush)player.Window.FindResource("B161917")).Color;Check(sanctuaryLum(sanctuaryDarkPrimary)-sanctuaryLum(sanctuaryDarkSurface)>140&&sanctuaryLum(sanctuaryDarkMuted)-sanctuaryLum(sanctuaryDarkSurface)>110&&sanctuaryLum(sanctuaryLightSurface)-sanctuaryLum(sanctuaryLightPrimary)>140&&sanctuaryLum(sanctuaryLightSurface)-sanctuaryLum(sanctuaryLightMuted)>110,"Tree House Sanctuary keeps primary and muted text clearly separated from light and dark surfaces");player.Data.AppearanceMode="Dark";player.ApplyTheme("Sanctuary");var downloadInstruction=Elements(player.Find<Grid>("DownloadPane")).OfType<TextBlock>().Single(t=>t.Text=="Downloads");Check(downloadInstruction.Effect is DropShadowEffect&&((DropShadowEffect)downloadInstruction.Effect).Opacity>.8&&sanctuaryLum(((SolidColorBrush)downloadInstruction.Foreground).Color)>220,"Sanctuary download heading uses a bright foreground and protective shadow");player.ShowDownloads(true);await Task.Delay(80);Snapshot(player.Window,System.IO.Path.Combine(run,"sanctuary-downloads-dark.png"));player.ShowDownloads(false);var olympusLight=ThemeScenes.Build("Olympus",340,null,true,false,false);var olympusDark=ThemeScenes.Build("Olympus",340,null,false,false,false);var olympusNeon=ThemeScenes.Build("Olympus",340,null,false,false,true);var olympusTall=ThemeScenes.Build("Olympus",1600,null,false,false,false);Check(Elements(olympusLight).OfType<System.Windows.Shapes.Ellipse>().Any(e=>e.Name=="OlympusSun")&&!Elements(olympusDark).OfType<System.Windows.Shapes.Ellipse>().Any(e=>e.Name=="OlympusSun")&&Elements(olympusDark).OfType<Canvas>().Any(e=>e.Name=="OlympusStars"),"Mount Olympus switches between divine daylight and a stormy star field");var olympusMusic=Elements(olympusDark).OfType<Canvas>().Single(e=>e.Name=="OlympusMusic");Check(olympusMusic.Children.OfType<System.Windows.Shapes.Path>().Count()>=5&&olympusMusic.Children.OfType<Canvas>().Count()>=13&&new[]{"OlympusTemple","OlympusMountain","OlympusStairs","OlympusFog"}.All(name=>Elements(olympusDark).OfType<Canvas>().Any(e=>e.Name==name))&&!Elements(olympusDark).OfType<Canvas>().Any(e=>e.Name=="OlympusReferences"),"Mount Olympus contains flowing music, summit architecture, layered cliffs, stairs, and fog without the removed symbols");var tallTemple=Elements(olympusTall).OfType<Canvas>().Single(e=>e.Name=="OlympusTemple");Check(tallTemple.RenderTransform is TranslateTransform&&((TranslateTransform)tallTemple.RenderTransform).X>=180&&tallTemple.Children.OfType<System.Windows.Shapes.Rectangle>().Where(r=>r.Width==24).All(r=>r.Height<230),"Olympus keeps classical temple proportions and clears the centered record in tall layouts");Check(Elements(olympusDark).OfType<Canvas>().Any(e=>e.Name=="AthenaOwlRelief")&&Elements(olympusNeon).OfType<Canvas>().Single(e=>e.Name=="OlympusMusic").Effect is DropShadowEffect,"Olympus embeds Athena’s owl in the temple and lights musical strands in Neon mode");player.Data.AppearanceMode="Light";player.ApplyTheme("Olympus");Check(((FontFamily)player.Window.Resources["ThemeDisplayFont"]).Source.Contains("Palatino"),"Mount Olympus uses classical display typography");await Task.Delay(80);Snapshot(player.Window,System.IO.Path.Combine(run,"olympus-light.png"));player.Data.AppearanceMode="Dark";player.ApplyTheme("Olympus");await Task.Delay(80);Snapshot(player.Window,System.IO.Path.Combine(run,"olympus-dark.png"));
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
                            var settingsWindow = player.CreateSettings(); Check(settingsWindow.ResizeMode==ResizeMode.NoResize&&settingsWindow.Width>=860&&settingsWindow.Height>=700&&settingsWindow.Width<=SystemParameters.WorkArea.Width&&settingsWindow.Height<=SystemParameters.WorkArea.Height,"Settings window is larger, stays on screen, and removes minimize and maximize controls"); settingsWindow.Show(); await Task.Delay(150); Snapshot(settingsWindow, System.IO.Path.Combine(run, "settings.png")); Check(settingsWindow.Icon != null, "Settings and main window use the Digitone application icon"); var hexInput = Elements(settingsWindow).OfType<TextBox>().Single(e => e.Name == "CustomColorHex");
                            var applyCustom = Elements(settingsWindow).OfType<Button>().Single(e => e.Name == "ApplyCustomColor");
                            hexInput.Text = "#nothex"; applyCustom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(player.Data.ThemeName == "Overworld", "Invalid custom colors leave the active theme unchanged");
                            hexInput.Text = "#b4e"; applyCustom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Check(player.Data.ThemeName == "Custom" && store.Load().CustomThemeColor == "#BB44EE" && store.Load().ThemeName == "Custom", "Custom hex color applies and persists in normalized form");
                            foreach (string extreme in new[] { "#000000", "#FFFFFF", "#FF0000" }) { Color mapped = Themes.Map((Color)ColorConverter.ConvertFromString("#C1D3A8"),"Custom",extreme); Check(.2126*mapped.R + .7152*mapped.G + .0722*mapped.B >= 180, "Custom accent stays readable for " + extreme); }
                            var cards = Elements(settingsWindow).OfType<Button>().Where(b => b.Tag is string && Themes.Names.Contains((string)b.Tag)).ToArray();
                            var themeScroll=Elements(settingsWindow).OfType<ScrollViewer>().Single(s=>s.Name=="ThemePreviewScroll");
                            Check(cards.Length == 7 && cards.All(b => Math.Abs(b.ActualHeight-158)<1&&Math.Abs(b.ActualWidth-118)<1) && themeScroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Auto, "Theme cards retain fixed dimensions in a horizontal scroller");
                            var themeStrip=(StackPanel)themeScroll.Content;Check(themeStrip.Margin.Left>=16&&cards.All(b=>((Border)((StackPanel)b.Content).Children[0]).Margin.Left>=7),"Slanted theme previews have enough left clearance to avoid clipping");
                            Check(cards.All(b=>{var label=((StackPanel)b.Content).Children[1] as TextBlock;return label!=null&&label.TextWrapping==TextWrapping.Wrap&&label.TextTrimming==TextTrimming.None;}),"Theme names wrap without being cut off");
                            Check(cards.All(b => { var frame = ((StackPanel)b.Content).Children[0] as Border; return frame != null && frame.BorderThickness.Left == 2 && ((SolidColorBrush)frame.BorderBrush).Color == Colors.Black && ((SkewTransform)frame.RenderTransform).AngleX == -5; }), "Theme previews match the card angle with a black separation border");
                            var appearanceSections=Elements(settingsWindow).OfType<StackPanel>().Where(p=>p.Name.EndsWith("SettingsSection")).ToList();Check(appearanceSections.Select(p=>p.Name).SequenceEqual(new[]{"ThemeSettingsSection","CustomColorSettingsSection","SurfaceModeSettingsSection","NeonSettingsSection","BetaThemeSettingsSection"}),"Appearance contains only theme, custom color, surface, and Neon controls");
                            var neonToggle=Elements(settingsWindow).OfType<CheckBox>().Single(c=>c.Name=="NeonToggle");Check(neonToggle.IsChecked==false&&player.Find<Slider>("Seek").Effect==null,"Neon defaults off with no accent rendering effect");neonToggle.IsChecked=true;neonToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var glowingSanctuary=ThemeScenes.Build("Sanctuary",600,null,false,true,true);Check(player.Data.NeonEnabled&&store.Load().NeonEnabled&&player.Find<Slider>("Seek").Effect is DropShadowEffect&&player.Find<Grid>("RecordDisc").Effect is DropShadowEffect&&player.Find<System.Windows.Shapes.Ellipse>("AmbientRing").Effect is DropShadowEffect&&Elements(glowingSanctuary).OfType<Canvas>().Single(c=>c.Name=="SanctuaryStars").Effect is DropShadowEffect,"Neon toggle persists and extends to moving theme accents and Sanctuary stars");player.ShowExpanded(true);Check(Elements(player.Find<Canvas>("ExpandedWaveform")).OfType<System.Windows.Shapes.Polyline>().Any(p=>p.Effect is DropShadowEffect),"Neon extends to fullscreen visualizer rendering");player.ShowExpanded(false);Snapshot(player.Window,System.IO.Path.Combine(run,"neon-accents.png"));var neonReload=new PlayerApp(store,store.Load());Check(neonReload.Data.NeonEnabled&&neonReload.Find<Slider>("Seek").Effect is DropShadowEffect&&neonReload.Find<System.Windows.Shapes.Ellipse>("AmbientOrbit").Effect is DropShadowEffect,"Reopened player restores Neon controls and background accents");neonReload.Window.Close();neonToggle.IsChecked=false;neonToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(!player.Data.NeonEnabled&&player.Find<Slider>("Seek").Effect==null&&player.Find<System.Windows.Shapes.Ellipse>("AmbientRing").Effect==null,"Turning Neon off removes its rendering effects");
                            var audioNav=Elements(settingsWindow).OfType<Button>().Single(b=>Convert.ToString(b.Content)=="Audio");audioNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);var audioSections=Elements(settingsWindow).OfType<StackPanel>().Where(p=>p.Name.EndsWith("SettingsSection")).ToList();var outputSelector=Elements(settingsWindow).OfType<ComboBox>().Single(c=>c.Name=="AudioOutputSelector");var eqSettings=audioSections.Single(p=>p.Name=="EqualizerSettingsSection");Check(audioSections.Select(p=>p.Name).SequenceEqual(new[]{"AudioOutputSettingsSection","EqualizerSettingsSection"})&&outputSelector.Items.Count>=1&&outputSelector.SelectedIndex>=0&&Elements(eqSettings).OfType<Slider>().Count()==5&&Elements(eqSettings).OfType<Slider>().All(s=>s.IsMoveToPointEnabled),"Audio tab contains output selection and five direct-drag equalizer bands");
                            var generalNav=Elements(settingsWindow).OfType<Button>().Single(b=>Convert.ToString(b.Content)=="General");generalNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);var changeBullets=AppInfo.Changelog.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Where(line=>line.StartsWith("•")).ToArray();Check(Elements(settingsWindow).OfType<TextBlock>().Any(t=>t.Name=="VersionLabel"&&t.Text=="Version "+AppInfo.Version)&&Elements(settingsWindow).OfType<Button>().Any(b=>b.Name=="ChangelogButton")&&Elements(settingsWindow).OfType<CheckBox>().Any(c=>Convert.ToString(c.Content)=="Global media keys")&&Elements(settingsWindow).OfType<CheckBox>().Any(c=>Convert.ToString(c.Content).StartsWith("Automatically find"))&&AppInfo.ChangelogVersions.Length==AppInfo.Changelogs.Length&&AppInfo.ChangelogVersions.Length>=2&&AppInfo.Release1Changelog.Contains("CORE PLAYER")&&changeBullets.Length==changeBullets.Distinct().Count()&&AppInfo.Changelogs.SelectMany(log=>log.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Where(line=>line.StartsWith("•"))).GroupBy(line=>line).All(group=>group.Count()==1),"General tab contains controls, updates, tools, versioning, and retained changelogs");Check(!Elements(settingsWindow).OfType<TextBox>().Any(e=>e.Name=="SpotifyClientId")&&!Elements(settingsWindow).OfType<Button>().Any(e=>e.Name=="SaveSpotifyClientId"),"Spotify shared link imports require no account settings or credentials");
                            var settingsScroll=Elements(settingsWindow).OfType<ScrollViewer>().First(s=>s.Content is StackPanel&&Elements((DependencyObject)s.Content).OfType<StackPanel>().Any(p=>p.Name=="VersionSettingsSection"));settingsScroll.ScrollToEnd();await Task.Delay(80);Snapshot(settingsWindow,System.IO.Path.Combine(run,"settings-footer.png"));Check(Elements(settingsWindow).OfType<StackPanel>().Single(p=>p.Name=="VersionSettingsSection").IsVisible,"Version and changelog controls remain at the absolute bottom of the General page");
                            Snapshot(settingsWindow, System.IO.Path.Combine(run, "settings-custom.png"));
                            player.ApplyTheme("Overworld"); ((Button)settingsWindow.Tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100); Snapshot(settingsWindow, System.IO.Path.Combine(run, "settings-folders.png")); settingsWindow.Close();

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
                                var tags = new Track { Path = editable, SongTitle = "Golden hour", Artist = "Test artist", Album = "A quieter place", AlbumArtist = "Various artists", Genre = "Ambient", Year = "2026-08-27", TrackNumber = "2/10", DiscNumber = "1/2", Comment = "Unicode café test" };
                                batchTracks.Add(tags);batchAudioHashes[editable]=hash;
                                string backup = await Task.Run(delegate { return SongTags.Save(tags, coverBytes, true); });
                                var reread = new Track { Path = editable }; SongTags.ReadInto(reread);
                                Check(reread.SongTitle == tags.SongTitle && reread.Artist == tags.Artist && reread.Comment == tags.Comment && !String.IsNullOrEmpty(reread.CoverData), extension + " tags and artwork round-trip through the actual file");
                                Check(backup == null && SongTags.AudioHash(editable) == hash && !Directory.GetFiles(System.IO.Path.GetDirectoryName(editable), System.IO.Path.GetFileName(editable) + ".digitone-backup*").Any(), extension + " editing preserves compressed audio without song backups");
                                await Task.Run(delegate { SongTags.Save(tags, null, true); }); SongTags.ReadInto(reread);
                                Check(String.IsNullOrEmpty(reread.CoverData), extension + " cover art removal is persisted");
                                if(extension==".mp3"){var identityOnly=new Track{Path=editable,SongTitle="Spotify title",Artist="Spotify artist",Year="this field must be ignored"};await Task.Run(delegate{SongTags.SaveIdentity(identityOnly);});SongTags.ReadInto(reread);Check(reread.SongTitle=="Spotify title"&&reread.Artist=="Spotify artist"&&reread.Year==tags.Year&&SongTags.AudioHash(editable)==hash,"Spotify metadata saves only title and artist without failing on unrelated fields");identityOnly.SongTitle=tags.SongTitle;identityOnly.Artist=tags.Artist;await Task.Run(delegate{SongTags.SaveIdentity(identityOnly);});}
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
                                Press(player,"DownloadNewMode");player.Find<CheckBox>("DownloadPlaylist").IsChecked = true; player.Find<TextBox>("DownloadPlaylistName").Text = "Test downloaded playlist";
                                player.Find<TextBox>("DownloadUrl").Text = server.Url.Replace("sample.wav", "playlist.html"); Press(player, "DownloadStart");
                                var completed = await Task.WhenAny(player.Downloads.ActiveTask, Task.Delay(60000));
                                if (completed != player.Downloads.ActiveTask) { player.Downloads.Cancel(); await player.Downloads.ActiveTask; throw new Exception("Playlist integration test timed out"); }
                                await player.Downloads.ActiveTask;
                                var savedPlaylist = player.Data.Playlists.FirstOrDefault(p => p.Name == "Test downloaded playlist");
                                Check(savedPlaylist != null && !savedPlaylist.Favorite && savedPlaylist.Paths.Count == 2 && savedPlaylist.Paths.All(File.Exists)&&String.Equals(savedPlaylist.FolderPath,System.IO.Path.Combine(destination,"Test downloaded playlist"),StringComparison.OrdinalIgnoreCase), "Actual multi-entry download creates a non-favorite folder-backed library playlist");
                                Check(savedPlaylist.Paths.All(p => System.IO.Path.GetDirectoryName(p) == System.IO.Path.Combine(destination, "Test downloaded playlist")), "Playlist downloads are saved inside their named folder");
                                player.Downloads.RefreshPlaylists();var existingTarget=player.Find<ComboBox>("DownloadPlaylistTarget").Items.Cast<DownloadTarget>().Single(t=>t.Playlist==savedPlaylist);Check(existingTarget.Label==savedPlaylist.Name,"Existing playlists appear in the download destination dropdown");
                                Press(player,"DownloadExistingMode");player.Find<ComboBox>("DownloadPlaylistTarget").SelectedItem=existingTarget;player.Find<CheckBox>("DownloadPlaylist").IsChecked=false;player.Find<TextBox>("DownloadUrl").Text=server.Url;Press(player,"DownloadStart");var queuedFirst=player.Downloads.ActiveTask;var cancelledTask=player.Downloads.Enqueue(server.Url,false,null,savedPlaylist);var pendingJob=player.Find<ListBox>("DownloadJobs").Items.Cast<DownloadJob>().Last(j=>j.State=="Pending");player.Find<ListBox>("DownloadJobs").SelectedItem=pendingJob;player.Downloads.CancelSelected();Check(pendingJob.State=="Cancelled"&&player.UndoAvailable,"Removing a pending download offers Undo");player.UndoLastRemoval();var restoredDownload=player.Downloads.ActiveTask;var queuedPair=Task.WhenAll(queuedFirst,restoredDownload);var existingCompleted=await Task.WhenAny(queuedPair,Task.Delay(90000));if(existingCompleted!=queuedPair){player.Downloads.Cancel();throw new Exception("Existing-playlist queue test timed out");}await queuedPair;Check(savedPlaylist.Paths.Count==4&&savedPlaylist.Paths.All(File.Exists)&&savedPlaylist.Paths.All(p=>System.IO.Path.GetDirectoryName(p)==System.IO.Path.Combine(destination,"Test downloaded playlist"))&&player.Find<ListBox>("DownloadJobs").Items.Cast<DownloadJob>().Count(j=>j.State.StartsWith("Completed"))>=3,"Queued downloads run in order, restore pending removals, use the selected playlist folder, and remain in completed history");Press(player,"DownloadDetailsToggle");Check(player.Find<TextBox>("DownloadLog").Visibility==Visibility.Visible,"Download diagnostics expand only when Show details is requested");Press(player,"DownloadDetailsToggle");
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
                        Check(player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<MenuItem>().Any(i=>Convert.ToString(i.Header).StartsWith("Edit song"))&&player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<MenuItem>().All(i=>!Convert.ToString(i.Header).StartsWith("Bulk clean song"))&&player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<MenuItem>().Any(i=>Convert.ToString(i.Header).StartsWith("Set artwork"))&&player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<MenuItem>().Any(i=>Convert.ToString(i.Header)=="Remove from this collection")&&Elements(player.Window).OfType<Button>().All(b=>b.Name!="RemoveButton"),"Song editing, artwork, and removal remain in the track context menu without automatic metadata cleanup or a bottom Remove button");
                        player.SetLibraryArtworkBusy(true);Check(player.Find<Grid>("LibraryPane").IsEnabled&&!player.Find<Grid>("LibraryPane").IsHitTestVisible&&player.Find<ListBox>("Tracks").Background==Brushes.Transparent,"Batch artwork blocks library input without replacing its themed surface");player.SetLibraryArtworkBusy(false);Check(player.Find<Grid>("LibraryPane").IsHitTestVisible,"Batch artwork restores library interaction after writing");
                        Press(player,"PlaylistsButton");Check(player.Find<Grid>("PlaylistPane").Visibility==Visibility.Visible&&player.Find<Grid>("LibraryPane").Visibility==Visibility.Collapsed&&player.Find<WrapPanel>("PlaylistGrid").Children.Count==player.Data.Playlists.Count,"Playlists opens as a separate card grid");var playlistCard=player.Find<WrapPanel>("PlaylistGrid").Children.OfType<Button>().First();playlistCard.ApplyTemplate();var playlistSurface=(Border)playlistCard.Template.FindName("PlaylistCardSurface",playlistCard);var playlistStack=(StackPanel)playlistCard.Content;playlistCard.UpdateLayout();var playlistTitle=(TextBlock)playlistStack.Children[1];Check(!(playlistSurface.RenderTransform is SkewTransform)&&((Grid)playlistStack.Children[0]).Width==((Grid)playlistStack.Children[0]).Height&&playlistCard.Height>=280&&((TextBlock)playlistStack.Children[2]).ActualHeight>0,"Playlist grid uses straight square cover cards with a visible song count");Check(playlistTitle.Width==164&&playlistTitle.TextWrapping==TextWrapping.Wrap&&playlistTitle.TextTrimming==TextTrimming.None&&Convert.ToString(playlistTitle.ToolTip)==playlistTitle.Text,"Playlist cards wrap full names and retain the complete title as a tooltip");Snapshot(player.Window,System.IO.Path.Combine(run,"playlist-grid.png"));
                        player.Data.AppearanceMode="Light";player.Data.CustomThemeColor="#FF3366";player.ApplyTheme("Custom");Color redSurface=((SolidColorBrush)player.Window.FindResource("B161917")).Color,redAccent=((SolidColorBrush)player.Window.FindResource("BC1D3A8")).Color,searchSurface=((SolidColorBrush)player.Find<TextBox>("Search").Background).Color;player.Data.CustomThemeColor="#3366FF";player.ApplyTheme("Custom");Color blueSurface=((SolidColorBrush)player.Window.FindResource("B161917")).Color,blueAccent=((SolidColorBrush)player.Window.FindResource("BC1D3A8")).Color,mutedText=((SolidColorBrush)player.Window.FindResource("B919A92")).Color,primaryText=((SolidColorBrush)player.Window.FindResource("BEEEFE8")).Color;Check(searchSurface.R<245&&redSurface!=Color.FromRgb(250,242,220)&&redSurface!=blueSurface&&redAccent.R>redAccent.B&&blueAccent.B>blueAccent.R&&(.2126*mutedText.R+.7152*mutedText.G+.0722*mutedText.B)<110&&(.2126*primaryText.R+.7152*primaryText.G+.0722*primaryText.B)<70,"Light mode carries the custom color while keeping primary and muted text dark");Snapshot(player.Window,System.IO.Path.Combine(run,"light-custom-color.png"));var lightSettings=player.CreateSettings();lightSettings.Show();await Task.Delay(80);Elements(lightSettings).OfType<Button>().Single(b=>Convert.ToString(b.Content)=="General").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);Check(Elements(lightSettings).OfType<TextBlock>().Any(t=>t.Name=="SupportMessage"&&t.Text=="This program will always be free, its made for you. If you would like to tip to support my programs, please use the link below." )&&Elements(lightSettings).OfType<Button>().Any(b=>b.Name=="SupportLinkButton"&&Convert.ToString(b.Content)=="Support on Ko-fi"&&Convert.ToString(b.ToolTip)=="https://ko-fi.com/itshazzy"),"Made for music contains the free-program support message and Ko-fi link");var lightSettingsScroll=Elements(lightSettings).OfType<ScrollViewer>().First(s=>s.Content is StackPanel&&Elements((DependencyObject)s.Content).OfType<StackPanel>().Any(p=>p.Name=="VersionSettingsSection"));lightSettingsScroll.ScrollToEnd();await Task.Delay(80);Snapshot(lightSettings,System.IO.Path.Combine(run,"light-settings-footer.png"));Elements(lightSettings).OfType<Button>().Single(b=>b.Name=="FutureSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);lightSettingsScroll.ScrollToEnd();await Task.Delay(80);var futureCard=Elements(lightSettings).OfType<Border>().Single(b=>b.Name=="FutureNeonCard");var futureBrush=futureCard.BorderBrush as LinearGradientBrush;Check(futureCard.Effect is System.Windows.Media.Effects.DropShadowEffect&&futureBrush!=null&&futureBrush.GradientStops.Count==4&&Elements(lightSettings).OfType<TextBlock>().Any(t=>t.Text=="PLEASE READ")&&Elements(lightSettings).OfType<Button>().Any(b=>b.Name=="FutureSupportLink"&&Convert.ToString(b.ToolTip)=="https://ko-fi.com/itshazzy")&&Elements(lightSettings).OfType<Border>().Any(i=>i.Name=="HazzySignature"&&i.OpacityMask is ImageBrush&&i.Background is LinearGradientBrush&&((LinearGradientBrush)i.Background).GradientStops.Count==3),"The Future tab keeps its animated neon presentation, Ko-fi link, and supplied Hazzy signature");Snapshot(lightSettings,System.IO.Path.Combine(run,"settings-future.png"));lightSettings.Close();player.StartQueue(new[]{player.Data.Tracks.First(t=>String.IsNullOrEmpty(t.CoverData))},null);await Task.Delay(100);var recordLogo=player.Find<Image>("RecordLogo");var logoBitmap=recordLogo.Source as BitmapSource;Check(logoBitmap!=null&&logoBitmap.PixelWidth==1119&&recordLogo.Visibility==Visibility.Visible&&player.Find<System.Windows.Shapes.Ellipse>("RecordLabel").Visibility==Visibility.Visible&&recordLogo.Source!=player.Window.Icon,"Coverless songs use only the supplied lowercase d mark");Snapshot(player.Window,System.IO.Path.Combine(run,"coverless-logo.png"));player.Data.AppearanceMode="Dark";player.ApplyTheme("Custom");
                        string folderImport=System.IO.Path.Combine(run,"Folder playlist fixture");Directory.CreateDirectory(folderImport);string folderSong=System.IO.Path.Combine(folderImport,"folder-song.wav");File.Copy(first,folderSong);await player.Import(new[]{folderImport},false,true);var folderPlaylist=player.Data.Playlists.FirstOrDefault(p=>String.Equals(p.FolderPath,folderImport,StringComparison.OrdinalIgnoreCase));Check(folderPlaylist!=null&&!folderPlaylist.Favorite&&folderPlaylist.Paths.SequenceEqual(new[]{folderSong}),"Add Folder creates a non-favorite synchronized folder playlist");player.SelectCollection(folderPlaylist,false);player.Find<ListBox>("Tracks").SelectedIndex=0;((MenuItem)player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<object>().First(i=>Convert.ToString(((MenuItem)i).Header)=="Remove from this collection")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(folderPlaylist.Paths.Count==0&&folderPlaylist.ExcludedPaths.Contains(folderSong,StringComparer.OrdinalIgnoreCase),"Folder-playlist removal records a persistent rescan exclusion");await player.Import(new[]{folderImport},true,true);Check(folderPlaylist.Paths.Count==0,"Rescan does not add an intentionally removed folder song back");player.UndoLastRemoval();Check(folderPlaylist.Paths.SequenceEqual(new[]{folderSong})&&!folderPlaylist.ExcludedPaths.Contains(folderSong,StringComparer.OrdinalIgnoreCase),"Undo restores a folder song and clears its rescan exclusion");File.Delete(folderSong);Directory.Delete(folderImport);await player.RescanMusicFolders();Check(!player.Data.Tracks.Any(t=>t.Path==folderSong)&&!player.Data.Playlists.Contains(folderPlaylist),"Rescan removes missing folder tracks and its generated playlist");
                        player.Data.AppearanceMode="Dark";player.Data.CustomThemeColor="#FF3366";player.ApplyTheme("Custom");Color saturatedCustom=((SolidColorBrush)player.Window.FindResource("B161917")).Color;Check(new[]{saturatedCustom.R,saturatedCustom.G,saturatedCustom.B}.Max()-new[]{saturatedCustom.R,saturatedCustom.G,saturatedCustom.B}.Min()>=25,"Custom Color dark mode carries visibly saturated selected color");
                        var setupWindow=player.CreateFirstRun();Check(setupWindow.ResizeMode==ResizeMode.NoResize,"First-run window removes minimize and maximize controls");setupWindow.Show();await Task.Delay(80);var setupBrowse=Elements(setupWindow).OfType<Button>().First(b=>Convert.ToString(b.Content).StartsWith("Choose music folder"));Check(setupBrowse.Margin.Left>=10,"First-run folder button has left-side border clearance");setupWindow.Close();
                        Press(player,"LibraryButton");Press(player,"MainSpectrumButton");Check(player.Find<Canvas>("MainSpectrum").Height<=72&&player.Find<Border>("RecordArt").MinHeight==0&&Double.IsPositiveInfinity(player.Find<Viewbox>("DefaultArt").MaxHeight),"Main spectrum leaves the record fluid instead of imposing a fixed size");player.Window.Width=1000;player.Window.Height=660;await Task.Delay(120);var nowPane=player.Find<Border>("NowPane");var recordView=player.Find<Viewbox>("DefaultArt");Rect recordBounds=recordView.TransformToAncestor(nowPane).TransformBounds(new Rect(recordView.RenderSize));Rect spectrumBounds=player.Find<Canvas>("MainSpectrum").TransformToAncestor(nowPane).TransformBounds(new Rect(player.Find<Canvas>("MainSpectrum").RenderSize));Check(player.Find<Canvas>("MainSpectrum").Height<=52&&recordBounds.Top>=-1&&recordBounds.Bottom<=spectrumBounds.Top+1&&spectrumBounds.Bottom<=nowPane.ActualHeight+1,"Fluid record remains above the information and spectrum area at minimum size");double compactRecordHeight=recordView.ActualHeight;Snapshot(player.Window,System.IO.Path.Combine(run,"main-spectrum-small.png"));player.Window.Width=1180;player.Window.Height=790;await Task.Delay(80);Press(player,"VisualizerButton");Check(player.Find<Border>("RecordArt").MinHeight==0&&Double.IsPositiveInfinity(player.Find<Viewbox>("DefaultArt").MaxHeight)&&recordView.ActualHeight>compactRecordHeight,"Record grows fluidly when its available aspect-ratio space increases");
                        var sweptTrack = player.Data.Tracks.First(); string sweptArtist = sweptTrack.Artist, sweptWords = sweptTrack.Lyrics; sweptTrack.Lyrics = null; sweptTrack.LyricsStatus = null; if (String.IsNullOrWhiteSpace(sweptTrack.Artist)) sweptTrack.Artist = "Sweep artist";
                        player.Data.AutoLyrics = true; player.FindLyrics = (q, c) => { var probable = new LyricsSearch { Query = q }; probable.Candidates.Add(new LyricsCandidate { Source = "LRCLIB", Synced = true, Text = "[00:01.00] Probably right words\n[00:02.00] Two\n[00:03.00] Three", Confidence = 78 }); return probable; };
                        player.QueueLyricsLookup(player.Data.Tracks); await player.LyricsSweepIdle;
                        Check(sweptTrack.LyricsStatus == "Check" && sweptTrack.Lyrics != null && sweptTrack.Lyrics.Contains("Probably right words") && player.LyricsCheckCount >= 1 && sweptTrack.LyricsCheckedUtc != null, "Background lookup fills in missing lyrics on its own; 75-84% matches are added and listed to check");
                        var checkReview = player.ShowLyricsReview(null); await Task.Delay(250);
                        Check(Elements(checkReview).OfType<ComboBox>().First(x => x.Name == "LyricsFilter").SelectedIndex == 6 && Elements(checkReview).OfType<ListBox>().First(x => x.Name == "LyricsReviewList").Items.Cast<object>().Any(i => i.ToString().StartsWith("◐ check 78%")), "Lyrics review opens on the automatically added lyrics to check");
                        checkReview.Close(); player.Data.AutoLyrics = false; player.FindLyrics = LyricsEngine.Find; sweptTrack.Artist = sweptArtist; sweptTrack.Lyrics = sweptWords; sweptTrack.LyricsStatus = null; store.Save(player.Data);
                        var keptTrack = player.Data.Tracks[0]; string keptStatusBefore = keptTrack.RecognitionStatus; keptTrack.RecognitionStatus = "Kept";
                        var neverIdentified = player.Data.Tracks.Where(t => String.IsNullOrEmpty(t.RecognitionStatus) && File.Exists(t.Path)).ToList();
                        SongRecognizer.HttpOverride = delegate { throw new WebException("offline"); };
                        player.Data.AutoRecognize = true; int identifyQueued = player.QueueUnidentified(); await player.RecognitionIdle;
                        Check(identifyQueued == neverIdentified.Count && identifyQueued > 0 && neverIdentified.All(t => !String.IsNullOrEmpty(t.RecognitionStatus)) && keptTrack.RecognitionStatus == "Kept" && player.QueueUnidentified() == 0, "Songs already in the library are identified in the background once; songs you edited yourself are left alone");
                        SongRecognizer.HttpOverride = null; player.Data.AutoRecognize = false; keptTrack.RecognitionStatus = keptStatusBefore;
                        foreach (var t in neverIdentified) { t.RecognitionStatus = null; t.RecognitionConfidence = 0; t.RecognitionSummary = null; }
                        store.Save(player.Data);
                        var artSongs = player.Data.Tracks.Where(t => t.Path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && File.Exists(t.Path)).Take(4).ToList();
                        if (artSongs.Count >= 4)
                        {
                            var artSaved = artSongs.Select(t => new[] { t.CoverData, t.ArtworkSource, t.ArtworkCheckedUtc, t.MusicBrainzReleaseId }).ToList();
                            var autoSong = artSongs[0]; var removedSong = artSongs[1]; var ownSong = artSongs[2]; var raceSong = artSongs[3]; string userCover = Convert.ToBase64String(new byte[] { 1, 2, 3 });
                            foreach (var t in artSongs) { t.CoverData = null; t.ArtworkSource = null; t.ArtworkCheckedUtc = null; t.MusicBrainzReleaseId = "7b2a32ca-cac6-4f22-ac44-5c77ad99f155"; }
                            removedSong.ArtworkSource = "UserRemoved"; ownSong.CoverData = userCover; ownSong.ArtworkSource = "User";
                            byte[] testCover = null; var coverMaker = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgr32, null); var coverEncoder = new JpegBitmapEncoder(); coverEncoder.Frames.Add(BitmapFrame.Create(coverMaker)); using (var coverStream = new MemoryStream()) { coverEncoder.Save(coverStream); testCover = coverStream.ToArray(); }
                            player.Data.AutoArt = true;
                            Check(player.NeedsArt(autoSong, false) && !player.NeedsArt(removedSong, true) && !player.NeedsArt(ownSong, true), "Automatic art skips songs whose art you set or removed");
                            AlbumArt.Fetch = delegate { return testCover; };
                            string artResult = await player.FillArt(autoSong, System.Threading.CancellationToken.None);
                            Check(artResult != null && autoSong.CoverData != null && autoSong.ArtworkSource.StartsWith("Auto") && store.Load().Tracks.First(t => t.Path == autoSong.Path).CoverData == autoSong.CoverData, "Automatic album art is applied to songs without art and remembered");
                            AlbumArt.Fetch = delegate { raceSong.CoverData = userCover; raceSong.ArtworkSource = "User"; return testCover; };
                            string raceResult = await player.FillArt(raceSong, System.Threading.CancellationToken.None);
                            Check(raceResult == null && raceSong.CoverData == userCover && raceSong.ArtworkSource == "User" && ownSong.CoverData == userCover && removedSong.CoverData == null, "Art you choose always wins, even if you set it while a lookup is running");
                            AlbumArt.Fetch = ArtworkFinder.Request; player.Data.AutoArt = false;
                            for (int i = 0; i < artSongs.Count; i++) { artSongs[i].CoverData = artSaved[i][0]; artSongs[i].ArtworkSource = artSaved[i][1]; artSongs[i].ArtworkCheckedUtc = artSaved[i][2]; artSongs[i].MusicBrainzReleaseId = artSaved[i][3]; }
                            store.Save(player.Data);
                        }
                        else Check(false, "Album art test needs four local WAV songs");
                        var reviewTrack = player.Data.Tracks.First(); reviewTrack.AudioHash = "review-test-hash"; reviewTrack.RecognitionStatus = "Review"; reviewTrack.RecognitionConfidence = 70;
                        var reviewResult = new RecognitionResult { AudioHash = "review-test-hash", FileDuration = 8, Hints = new SongHints { Title = "Some Upload - wrong name (Official Video)", Channel = "Uploader" } };
                        reviewResult.Candidates.Add(new RecognitionCandidate { Title = "Golden Hour", Artist = "Test Artist", Album = "Mornings", Year = "2024", TrackNumber = "1", RecordingId = "11111111-2222-3333-4444-555555555555", Source = "Audio fingerprint", Confidence = 72, Notes = new List<string> { "Audio fingerprint match: 72%." } });
                        reviewResult.Candidates.Add(new RecognitionCandidate { Title = "Other Song", Artist = "Someone", Source = "YouTube title", Confidence = 30 });
                        player.RecognitionResults.Put(reviewResult);
                        Check(player.ReviewCount == 1 && player.Find<ListBox>("Tracks").ContextMenu.Items.Cast<MenuItem>().Any(i => Convert.ToString(i.Header).StartsWith("Identify song")), "Songs needing review are counted and Identify song is in the track menu");
                        var reviewWindow = player.ShowRecognitionReview(null); await Task.Delay(200);
                        var reviewChoices = Elements(reviewWindow).OfType<ComboBox>().Single(c => c.Name == "CandidateChoice");
                        Check(reviewChoices.Items.Count == 2 && Elements(reviewWindow).OfType<TextBox>().Single(t => t.Name == "ReviewTitle").Text == "Golden Hour" && Elements(reviewWindow).OfType<TextBlock>().Any(t => t.Text.StartsWith("72% · medium")), "Review shows the detected match, its confidence, and alternatives");
                        Snapshot(reviewWindow, System.IO.Path.Combine(run, "song-review.png"));
                        Elements(reviewWindow).OfType<Button>().Single(b => b.Name == "ApplyRecognition").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        for (int wait = 0; wait < 50 && reviewTrack.RecognitionStatus != "Confirmed"; wait++) await Task.Delay(100);
                        Check(reviewTrack.RecognitionStatus == "Confirmed" && reviewTrack.SongTitle == "Golden Hour" && reviewTrack.Artist == "Test Artist" && reviewTrack.Album == "Mornings" && reviewTrack.MusicBrainzRecordingId == "11111111-2222-3333-4444-555555555555" && player.ReviewCount == 0 && store.Load().Tracks.Any(t => t.SongTitle == "Golden Hour" && t.RecognitionStatus == "Confirmed"), "Applying a reviewed match updates the song and library and clears it from review");
                        reviewWindow.Close();
                        string themeBefore = player.Data.ThemeName, brandFontBefore = ((TextBlock)player.Find<Viewbox>("BrandTitleFit").Child).FontFamily.Source, navFontBefore = player.Find<Button>("LibraryButton").FontFamily.Source; var playEffectBefore = player.Find<Button>("PlayButton").Effect;
                        player.SetBeta(true); await Task.Delay(900);
                        Check(player.BetaActive && player.BetaBackdrop.Visibility == Visibility.Visible && player.BetaBackdrop.Children.Count > 8 && store.Load().BetaTheme && player.Find<Button>("LibraryButton").FontFamily.Source == "Bahnschrift", "Beta Theme turns on, draws its code-made living background, and is remembered");
                        bool gpu3D = PlayerApp.HardwareFor3D && SystemParameters.ClientAreaAnimation;
                        Check(!gpu3D || (player.Beta3D && player.BetaCrystalCount == 10 && player.BetaTileShown && player.Find<Viewbox>("DefaultArt").Visibility == Visibility.Collapsed), "Beta 3D shows lit glass crystals and a floating 3D album tile in place of the flat record" + (gpu3D ? "" : " (skipped: no 3D graphics here)"));
                        if (gpu3D) { var tileBox = player.BetaTileBounds; var tileArea = player.BetaTileArea; Check(!tileBox.IsEmpty && tileBox.Width > 50 && tileBox.Left >= 0 && tileBox.Top >= 0 && tileBox.Right <= tileArea.Width + .5 && tileBox.Bottom <= tileArea.Height + .5 && Math.Abs(tileBox.Width - tileBox.Height) < 1, "The 3D album tile is square and stays inside its area"); }
                        if (gpu3D)
                        {
                            var cdTracks = player.Data.Tracks.Where(t => File.Exists(t.Path)).Take(3).ToList(); player.StartQueue(cdTracks, cdTracks[0]); bool together = false;
                            for (int i = 0; i < 3; i++) { player.PlayAt(i); for (int w = 0; w < 16; w++) { await Task.Delay(40); if (player.Find<Viewbox>("DefaultArt").IsVisible && player.BetaTileShown) together = true; } }
                            Check(!together && player.Find<Viewbox>("DefaultArt").Visibility == Visibility.Collapsed && player.BetaTileShown, "Beta never shows the record and the 3D tile together, even across song and cover changes");
                        }
                        bool themeShapes = true;
                        foreach (string betaTheme in Themes.Names.Concat(new[] { "Custom" })) { player.ApplyTheme(betaTheme); await Task.Delay(450); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-" + betaTheme + ".png")); if (gpu3D && (player.BetaWorldKind != ThemeShapes.KindFor(betaTheme) || player.BetaCrystalCount == 0)) themeShapes = false; }
                        Check(themeShapes, "Each theme builds its own 3D world (blocks, cards, stars, fireflies, marble, prisms, crystals)");
                        Check(MinecraftTextures.Blocks.Concat(MinecraftTextures.Discs).All(n => MinecraftTextures.Get(n) != null && MinecraftTextures.Get(n).PixelWidth == 64 && MinecraftTextures.Get(n).PixelHeight == 64) && MinecraftTextures.Sharp("dirt", 6).PixelWidth == 384, "All Minecraft theme textures are bundled at 64x64 and enlarge by whole pixels");
                        var grassPixels = new byte[64 * 64 * 4]; new FormatConvertedBitmap(MinecraftTextures.Get("grass_block_top"), PixelFormats.Bgra32, null, 0).CopyPixels(grassPixels, 256, 0); long greenSum = 0, redSum = 0; for (int i = 0; i < grassPixels.Length; i += 4) { greenSum += grassPixels[i + 1]; redSum += grassPixels[i + 2]; }
                        Check(greenSum > redSum * 1.2, "The grass top gets Minecraft's green tint");
                        var minecraftScene = ThemeScenes.Build("Overworld", 600);
                        Check(Elements(minecraftScene).OfType<Canvas>().Any(g => g.Name == "MinecraftGround" && g.Children.OfType<System.Windows.Shapes.Rectangle>().Count(r => r.Fill is ImageBrush) >= 18) && Elements(minecraftScene).OfType<System.Windows.Shapes.Rectangle>().Count(r => r.Fill is ImageBrush && r.RenderTransform is MatrixTransform) >= 9, "The Minecraft background uses real textured grass blocks and three textured floating blocks");
                        if (gpu3D)
                        {
                            player.ApplyTheme("Overworld"); await Task.Delay(500);
                            Check(player.BetaWorldTextures.Any(t => t.StartsWith("music_disc_")) && player.BetaWorldTextures.Count(t => !t.StartsWith("music_disc_")) >= 6, "Beta's Minecraft world floats real textured blocks and spinning music discs (" + String.Join(", ", player.BetaWorldTextures.Distinct()) + ")");
                            var artless = player.Data.Tracks.FirstOrDefault(t => File.Exists(t.Path) && String.IsNullOrEmpty(t.CoverData));
                            if (artless != null) { player.StartQueue(new[] { artless }, artless); for (int w = 0; w < 30 && player.BetaTileDisc == null; w++) await Task.Delay(100); Check(player.BetaTileDisc != null && MinecraftTextures.Discs.Contains(player.BetaTileDisc), "In the Minecraft theme, a song without album art shows a music disc on the 3D tile"); }
                            player.ApplyTheme("Midnight"); await Task.Delay(300);
                            Check(player.BetaTileDisc == null, "Other themes keep their own tile face");
                        }
                        var betaRows = player.Find<ListBox>("Tracks");
                        Check(betaRows.ItemContainerStyle == player.Window.FindResource("BetaTrackRow"), "Beta song rows drop their left bars so the slanted cursor alone marks the selected song");
                        if (gpu3D)
                        {
                            player.ApplyTheme("Midnight"); string visualizerBefore = player.ActiveVisualizer; player.ShowExpanded(true); bool everyMode = true;
                            foreach (string mode in PlayerApp.VisualizerModes) { player.SetVisualizer(mode); await Task.Delay(450); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-visualizer-" + mode + ".png")); if (player.BetaVisualizerMode != mode || player.Find<Canvas>("ExpandedWaveform").Opacity != 0) everyMode = false; }
                            player.ApplyTheme("Pride"); player.SetVisualizer("Spectrum"); await Task.Delay(450); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-visualizer-pride.png"));
                            Check(everyMode && player.BetaVisualizerMode == "Spectrum", "Each expanded visualizer has a 3D Beta version, and it follows the theme");
                            Check(PlayerApp.VisualizerModes.All(m => Convert.ToString(player.Find<Button>("Mode" + m).Content) == Visualizer3D.StageName(m)) && Visualizer3D.StageName("Waveform") == "Laser Show" && Visualizer3D.StageName("Pulse") == "Drop", "In Beta 3D the visualizer buttons name the rave set-ups (Laser Show, LED Wall, Halo, Moving Heads, Drop)");
                            player.SetVisualizer(visualizerBefore); player.ShowExpanded(false); player.ApplyTheme("Midnight"); await Task.Delay(150);
                        }
                        player.Data.AppearanceMode = "Light"; player.ApplyTheme("Midnight"); await Task.Delay(450); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-light.png")); player.Data.AppearanceMode = "Dark"; player.ApplyTheme("Midnight"); await Task.Delay(400);
                        Press(player, "PlaylistsButton"); await Task.Delay(260); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-playlists-moving.png")); await Task.Delay(900); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-playlists.png"));
                        Check(player.Find<Grid>("ContentStage").Opacity == 1 && Elements(player.Window).OfType<System.Windows.Controls.Viewport3D>().Count(v => v.Visibility == Visibility.Visible && v.Children.Count > 1 && Grid.GetRow(v) == 2) == 0, "The 3D page transition finishes and leaves the new page fully visible");
                        if (gpu3D)
                        {
                            player.SetBetaQuality("Ultra"); await Task.Delay(300); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-ultra.png"));
                            Check(player.BetaCrystalCount == 18, "Ultra 3D adds more crystals");
                            player.SetBetaQuality("Flat"); await Task.Delay(200);
                            Check(!player.Beta3D && !player.BetaTileShown && player.Find<Viewbox>("DefaultArt").Visibility == Visibility.Visible, "Flat quality turns the 3D layer off and brings the record back");
                            player.SetBetaQuality(null); await Task.Delay(200);
                        }
                        Press(player, "LibraryButton"); await Task.Delay(120); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-wipe.png")); await Task.Delay(700); Snapshot(player.Window, System.IO.Path.Combine(run, "beta-library.png"));
                        var betaTracks = player.Find<ListBox>("Tracks"); betaTracks.SelectedIndex = 1; await Task.Delay(450);
                        var betaCursorShape = Elements(player.Window).OfType<System.Windows.Shapes.Polygon>().FirstOrDefault(p => p.Visibility == Visibility.Visible && p.Points.Count == 4 && p.Points[1].X == 13);
                        Check(betaCursorShape != null, "The slanted cursor slides to the selected song");
                        Snapshot(player.Window, System.IO.Path.Combine(run, "beta-selected.png"));
                        var betaSettings = player.CreateSettings(); betaSettings.Show(); await Task.Delay(140); Snapshot(betaSettings, System.IO.Path.Combine(run, "beta-settings-moving.png")); await Task.Delay(900); Snapshot(betaSettings, System.IO.Path.Combine(run, "beta-settings.png"));
                        Check(Elements(betaSettings).OfType<CheckBox>().Any(c => c.Name == "BetaThemeToggle" && c.IsChecked == true), "Settings shows the Beta Theme switch");
                        Check(Elements(betaSettings).OfType<CheckBox>().Any(c => c.Name == "BetaAutoQualityToggle"), "Settings shows the automatic 3D depth switch");
                        Check(Elements(betaSettings).OfType<CheckBox>().Any(c => c.Name == "VisualizerFlashesToggle" && c.IsChecked == true), "Settings has a switch for flashing lights in the visualizers (on, limited)");
                        betaSettings.Close();
                        Check(player.BetaAverageFrameMs > 0 && player.BetaAverageFrameMs < 6, "The living background stays light (average " + player.BetaAverageFrameMs.ToString("0.00") + " ms per frame)");
                        var bar = ((Grid)player.Window.Content).Children.OfType<Border>().First(c => Grid.GetRow(c) == 3);
                        Check((player.BetaBarShadow != null) != (bar.Effect is DropShadowEffect) && (player.BetaBarShadow == null || (player.BetaBarShadow.CacheMode is BitmapCache && Grid.GetRow(player.BetaBarShadow) == 3)), "The player bar floats on exactly one shadow, drawn once and reused when the bar is solid");
                        var spectrumWave = new double[384]; var spectrumRandom = new Random(3); for (int i = 0; i < spectrumWave.Length; i++) spectrumWave[i] = spectrumRandom.NextDouble() - .5;
                        Func<double[], int, double[]> directSpectrum = (wave, bands) => { var r = new double[bands]; int n = wave.Length; for (int b = 0; b < bands; b++) { double hz = 55 * Math.Pow(3600 / 55.0, b / (double)(bands - 1)), w = 2 * Math.PI * hz / 8000, re = 0, im = 0; for (int i = 0; i < n; i++) { double win = .5 - .5 * Math.Cos(2 * Math.PI * i / (n - 1)); re += wave[i] * win * Math.Cos(w * i); im += wave[i] * win * Math.Sin(w * i); } r[b] = Math.Sqrt(re * re + im * im) / n * 4; } return r; };
                        Check(new[] { 40, 64, 12 }.All(bands => { var fast = Visualizer3D.Spectrum(spectrumWave, bands); var slow = directSpectrum(spectrumWave, bands); return fast.Zip(slow, (a, b) => Math.Abs(a - b)).Max() < 1e-9; }), "The faster 3D spectrum gives exactly the same bars as before");
                        double stepped = 0, once = 0; for (int i = 0; i < 8; i++) stepped += (1 - stepped) * BetaMotion.Ease(.1, .1 / 8); once = BetaMotion.Ease(.1, .1);
                        Check(Math.Abs(stepped - once) < 1e-9 && Math.Abs(BetaMotion.Ease(.45, 1 / BetaMotion.TuningRate) - .45) < 1e-9 && Math.Abs(BetaMotion.Decay(.996, 1 / BetaMotion.TuningRate) - .996) < 1e-9, "Beta motion feels the same at any frame rate (smoothing is measured in seconds, not frames)");
                        var searchBox = player.Find<TextBox>("Search"); var searchFill = searchBox.Background as SolidColorBrush;
                        Check(searchFill == null || searchFill.Color.A != 255 ? searchBox.Effect is DropShadowEffect : searchBox.Effect == null && ((Grid)VisualTreeHelper.GetParent(searchBox)).Children.OfType<Grid>().Any(g => g.Name == "BetaSearchShadow" && g.CacheMode is BitmapCache), "The search box floats on a shadow that is drawn once and reused");
                        Check(player.Find<TextBlock>("NowTitle").Effect == null && player.CachedCaptionGroups == 0, "Themes without a visible caption shadow carry no shadow effect on their labels");
                        player.ApplyTheme("Sanctuary"); await Task.Delay(300);
                        Check(player.Find<TextBlock>("NowTitle").Effect is DropShadowEffect && player.CachedCaptionGroups > 0, "Sanctuary keeps its caption shadows, with still caption groups cached (" + player.CachedCaptionGroups + " groups)");
                        player.ApplyTheme("Olympus"); await Task.Delay(200); Check(player.CachedCaptionGroups == 0 && player.Find<TextBlock>("NowTitle").Effect == null && !Elements(player.Window).OfType<TextBlock>().Any(t => t.Effect != null), "Leaving Sanctuary removes its caption shadows and caches from every label");
                        player.ApplyTheme("Midnight"); await Task.Delay(200);
                        Check(AudioDownloader.NeedsSignIn("ERROR: [youtube] igZfECgnxNw: Sign in to confirm your age. Use --cookies-from-browser or --cookies for the authentication.") && AudioDownloader.NeedsSignIn("ERROR: [youtube] abcdefghijk: Sign in to confirm you’re not a bot.") && !AudioDownloader.NeedsSignIn("ERROR: [youtube] abcdefghijk: Video unavailable") && !AudioDownloader.NeedsSignIn("WARNING: Sign in to confirm your age") && AudioDownloader.SignInVideo("ERROR: [youtube] igZfECgnxNw: Sign in to confirm your age.") == "igZfECgnxNw", "Age-restricted and sign-in refusals from YouTube are recognized, with the refused video");
                        var signedArgs = AudioDownloader.Arguments("https://music.youtube.com/watch?v=igZfECgnxNw", @"C:\x", false, @"C:\x\signin.txt"); var plainArgs = AudioDownloader.Arguments("https://music.youtube.com/watch?v=igZfECgnxNw", @"C:\x");
                        Check(plainArgs.Contains("--no-cookies") && !plainArgs.Contains("--cookies") && !signedArgs.Contains("--no-cookies") && signedArgs[signedArgs.IndexOf("--cookies") + 1] == @"C:\x\signin.txt" && signedArgs.Contains("--no-cookies-from-browser") && signedArgs.Last() == plainArgs.Last(), "Downloads stay signed out, except a retry that uses Digitone's own viewer sign-in (never another browser)");
                        string cookieFile = YouTubeBrowser.CookieFile(new[] { new[] { ".youtube.com", "/", "TRUE", "1893456000", "LOGIN_INFO", "abc" }, new[] { "www.youtube.com", "", "FALSE", "0", "PREF", "f6=1" }, new[] { ".youtube.com", "/", "TRUE", "0", "BAD", "x\ty" } });
                        Check(cookieFile.StartsWith("# Netscape HTTP Cookie File\n") && cookieFile.Contains(".youtube.com\tTRUE\t/\tTRUE\t1893456000\tLOGIN_INFO\tabc\n") && cookieFile.Contains("www.youtube.com\tFALSE\t/\tFALSE\t0\tPREF\tf6=1\n") && !cookieFile.Contains("BAD"), "The viewer sign-in is written in the cookie format yt-dlp reads");
                        Check(player.Find<Button>("DownloadSignIn").Visibility == Visibility.Collapsed && YouTubeBrowser.AllowedNavigation(YouTubeBrowser.SignInPage), "The YouTube sign-in button only appears when a download needs it, and opens Google sign-in in the viewer");
                        Check(AudioDownloader.IsCollectionLink("https://music.youtube.com/browse/MPREb_K8qWMWVqXGi") && AudioDownloader.IsCollectionLink("https://music.youtube.com/playlist?list=OLAK5uy_kNhM2yaBTOVwrcZJepB1C9P3") && AudioDownloader.IsCollectionLink("https://www.youtube.com/playlist?list=PL123") && !AudioDownloader.IsCollectionLink("https://music.youtube.com/watch?v=igZfECgnxNw&list=RDAMVM1") && !AudioDownloader.IsCollectionLink("https://youtu.be/igZfECgnxNw") && !AudioDownloader.IsCollectionLink("https://example.com/playlist?list=1"), "Album and playlist links are told apart from songs inside a playlist");
                        Check(AudioDownloader.FolderName("Random Access Memories") == "Random Access Memories" && AudioDownloader.FolderName("AC/DC: Back in Black?") == "AC DC Back in Black" && AudioDownloader.FolderName("CON") == "YouTube album" && AudioDownloader.FolderName(new string('a', 90)).Length == 60, "Album names become safe playlist folder names");
                        Check(player.Find<Button>("LibraryButton").Content.ToString() == "ALL MUSIC" && player.Find<TextBlock>("SearchHint").Text == "Search" && player.Find<TextBlock>("CollectionSubtitle").Text.Contains(" song") && !Elements(player.Window).OfType<TextBlock>().Any(t => t.IsVisible && (t.Text == "NOW PLAYING" || t.Text == "FAVORITE PLAYLISTS /" || t.Text == "DIGITONE / LIVE SIGNAL" || t.Text.Contains("follows the current audio"))), "The main window keeps labels short and drops filler text");
                        var tidySettings = player.CreateSettings(); tidySettings.Show(); await Task.Delay(120);
                        var tidyTexts = Elements(tidySettings).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
                        Check(!tidyTexts.Contains("PREFERENCES") && !tidyTexts.Contains("Changes save automatically") && !tidyTexts.Contains("Choose the color of your signal.") && Elements(tidySettings).OfType<TextBlock>().Any(t => t.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text.Contains("ⓘ")) && t.ToolTip is ToolTip), "Settings drop page subtitles and move explanations into ⓘ tooltips");
                        Elements(tidySettings).OfType<Button>().Single(b => Convert.ToString(b.Content) == "General").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
                        Check(Elements(tidySettings).OfType<FrameworkElement>().Any(e => e.ToolTip is ToolTip && ((ToolTip)e.ToolTip).Content is TextBlock && ((TextBlock)((ToolTip)e.ToolTip).Content).Text.Contains("sending only the artist, title, and length")) && !Elements(tidySettings).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("sending only the artist, title, and length")), "The lyrics privacy note is kept as a tooltip instead of a paragraph");
                        tidySettings.Close();
                        // The beat and bass detector, fed a made-up 120 BPM track (60 updates a second).
                        var raveTest = new RaveClock(); double ravePosition = 0;
                        Action<double, Func<double, double[]>> playFor = (seconds, bass) => { for (int f = 0; f < seconds * 60; f++) { ravePosition += 1 / 60.0; var b = bass(ravePosition); raveTest.Feed(1 / 60.0, b[0], b[1], ravePosition, true); } };
                        Func<double, double[]> drums = t => { double inBeat = t % .5; return inBeat < .04 ? new[] { .30, .14 } : new[] { .03, .10 }; };
                        playFor(12, drums);
                        Check(Math.Abs(raveTest.Bpm - 120) < 4 && raveTest.Kicks >= 21 && raveTest.Kicks <= 25 && raveTest.Beat >= 20 && raveTest.Bar == raveTest.Beat / 4, "The beat detector finds kicks and the tempo (" + raveTest.Bpm.ToString("0") + " BPM, " + raveTest.Kicks + " kicks in 12 s of a 120 BPM track)");
                        var subTest = new RaveClock(); double subPosition = 0; for (int f = 0; f < 240; f++) { subPosition += 1 / 60.0; subTest.Feed(1 / 60.0, .2, .2, subPosition, true); }
                        Check(subTest.Sub > .6 && subTest.Kicks <= 1, "A steady bass line shows as sub-bass, not as a stream of kicks");
                        playFor(9, t => new[] { .01, .012 }); double buildLevel = raveTest.Build; bool dropped = false;
                        for (int f = 0; f < 90 && !dropped; f++) { ravePosition += 1 / 60.0; double inBeat = ravePosition % .5; raveTest.Feed(1 / 60.0, inBeat < .04 ? .4 : .03, inBeat < .04 ? .3 : .14, ravePosition, true); dropped |= raveTest.DropNow; }
                        Check(buildLevel > .3 && dropped && raveTest.Drop > .3, "A build (the bass drops out) followed by a big kick is caught as the drop");
                        raveTest.Feed(1 / 60.0, .03, .1, ravePosition + 30, true); Check(raveTest.Kicks == 0 && raveTest.Bpm == 0, "Seeking or a new song makes the beat detector learn the tempo again");
                        // The bass filter on real audio: a 60 Hz tone is bass, a 1 kHz tone is not.
                        string lowTone = System.IO.Path.Combine(run, "low-tone.wav"), highTone = System.IO.Path.Combine(run, "high-tone.wav"); Tone(lowTone, 60); Tone(highTone, 1000);
                        using (var lowWave = new LiveWaveSource(lowTone)) using (var highWave = new LiveWaveSource(highTone))
                        {
                            double[] lowBass = null, highBass = null; for (int w = 0; w < 100 && (lowBass == null || highBass == null); w++) { lowBass = lowWave.SampleLow(1.5); highBass = highWave.SampleLow(1.5); if (lowBass == null || highBass == null) await Task.Delay(100); }
                            Check(lowBass != null && highBass != null && lowBass[1] > highBass[1] * 20 && lowBass[0] > .05, "The bass detector hears the real low end (60 Hz) and ignores higher sounds (1 kHz)");
                        }
                        // The flash limiter: kicks arriving five times a second still flash at most about three times a second, and never with flashes off.
                        foreach (bool flashing in new[] { true, false })
                        {
                            var stage = new Visualizer3D(); stage.Build("Pulse", Colors.DeepSkyBlue, Colors.Navy, false, "Midnight"); var stageClock = new RaveClock(); double stagePosition = 0;
                            for (int f = 0; f < 120; f++) { stagePosition += 1 / 60.0; double inKick = stagePosition % .2; stageClock.Feed(1 / 60.0, inKick < .03 ? .4 : .02, .1, stagePosition, true); stage.Step(1 / 60.0, null, .5, stageClock, flashing); }
                            Check(flashing ? stage.Flashes >= 4 && stage.Flashes <= 7 : stage.Flashes == 0, flashing ? "Kicks flash the stage at most about three times a second (" + stage.Flashes + " flashes in 2 s of rapid kicks)" : "With flashing lights off, kicks never flash the stage");
                        }
                        var dropStage = new Visualizer3D(); dropStage.Build("Pulse", Colors.DeepSkyBlue, Colors.Navy, false, "Midnight"); var dropClock = new RaveClock(); double dropPosition = 0;
                        for (int f = 0; f < 60 * 16 && dropStage.Sparks < 50; f++) { dropPosition += 1 / 60.0; double inBeat = dropPosition % .5; bool thinPart = dropPosition > 6 && dropPosition < 14; dropClock.Feed(1 / 60.0, thinPart ? .01 : inBeat < .04 ? .4 : .03, thinPart ? .012 : inBeat < .04 ? .3 : .14, dropPosition, true); dropStage.Step(1 / 60.0, null, .5, dropClock, true); }
                        Check(dropStage.Sparks >= 50, "The Drop stage bursts into confetti when the drop hits");
                        var discMesh = ThemeShapes.PixelCutout(MinecraftTextures.Get("music_disc_cat"), 1); double half = .5;
                        Check(discMesh.Positions.Count > 0 && !discMesh.Positions.Any(p => Math.Abs(p.X) > half - .02 && Math.Abs(p.Y) > half - .02), "Music discs are cut out to their real shape (no square plate around them)");
                        var card = ThemeShapes.RoundedCard(1.1, 1.54, .04, .1, Brushes.Red, Brushes.Black, Brushes.Gold);
                        Check(card.Children.Count == 3 && ((System.Windows.Media.Media3D.MeshGeometry3D)((System.Windows.Media.Media3D.GeometryModel3D)card.Children[0]).Geometry).TextureCoordinates.All(t => t.X >= 0 && t.X <= 1 && t.Y >= 0 && t.Y <= 1), "Spire cards have rounded corners, a picture front, a dark back, and a plain gold edge");
                        Check(ThemeShapes.CardFace(new Random(1), Colors.Red, Colors.Navy) is ImageBrush && ThemeShapes.LeafFace(Colors.Green) is ImageBrush && ThemeShapes.Marble(new Random(2)) is ImageBrush, "Code-drawn 3D textures are painted once into pictures");
                        if (gpu3D)
                        {
                            player.Data.BetaAutoQualityOff = false; player.SetBetaQuality("Ultra"); await Task.Delay(300); player.BetaGuardWake();
                            bool firstSlow = player.BetaGuardFeed(110, 33, true), secondSlow = player.BetaGuardFeed(110, 33, true);
                            Check(!firstSlow && secondSlow && player.Data.BetaQuality == null && store.Load().BetaQuality == null && player.BetaGuardMessage.Contains("Balanced"), "Auto quality guard steps Ultra down to Balanced only after stutter lasts, and says so");
                            player.BetaGuardWake(); Check(!player.BetaGuardFeed(40, 33, true) && !player.BetaGuardFeed(40, 33, true) && !player.BetaGuardFeed(110, 33, false) && !player.BetaGuardFeed(110, 33, false) && player.Data.BetaQuality == null, "The guard ignores smooth frames and background windows");
                            player.Data.BetaAutoQualityOff = true; player.BetaGuardWake(); Check(!player.BetaGuardFeed(110, 33, true) && !player.BetaGuardFeed(110, 33, true) && player.Data.BetaQuality == null, "Turning the guard off leaves 3D depth alone");
                            player.Data.BetaAutoQualityOff = false; player.BetaGuardWake(); Check(!player.BetaGuardFeed(12, 6.06, true) && !player.BetaGuardFeed(18, 6.06, true) && player.Data.BetaQuality == null, "The guard never steps down a fast-refresh screen that is still above ~45 fps");
                            player.Data.BetaAutoQualityOff = true;
                            var tileBounds = player.BetaTileBounds; var shadowBounds = player.BetaTileShadowBounds; await Task.Delay(100); shadowBounds = player.BetaTileShadowBounds;
                            Check(!shadowBounds.IsEmpty && shadowBounds.Width > tileBounds.Width * .4 && Math.Abs((shadowBounds.Left + shadowBounds.Width / 2) - (tileBounds.Left + tileBounds.Width / 2)) < tileBounds.Width * .25 && shadowBounds.Top + shadowBounds.Height / 2 > tileBounds.Top + tileBounds.Height / 2, "The 3D tile casts its soft shadow under itself, following its tilt, without a live blur");
                            // Beta should update on (nearly) every frame Windows draws, however many that is right now (a covered or idle screen draws fewer).
                            PlayerApp.FocusOverride = true; await Task.Delay(200); int drawn = 0; TimeSpan lastDrawn = TimeSpan.Zero;
                            EventHandler countFrames = delegate(object sender, EventArgs e) { var frame = e as System.Windows.Media.RenderingEventArgs; if (frame != null && frame.RenderingTime != lastDrawn) { lastDrawn = frame.RenderingTime; drawn++; } };
                            System.Windows.Media.CompositionTarget.Rendering += countFrames; int ticksBefore = player.BetaTickCount; await Task.Delay(1000); int perSecond = player.BetaTickCount - ticksBefore; System.Windows.Media.CompositionTarget.Rendering -= countFrames; PlayerApp.FocusOverride = null;
                            Check(drawn > 0 && perSecond >= drawn * .8, "Beta animates on every frame Windows draws (" + perSecond + " updates for " + drawn + " frames in one second; the old timer managed ~21)");
                            player.SetBetaQuality(null); await Task.Delay(200);
                        }
                        Func<byte[], int> peak = wave => { int top = 0; for (int i = 44; i + 1 < wave.Length; i += 2) top = Math.Max(top, Math.Abs((int)BitConverter.ToInt16(wave, i))); return top; };
                        Check(UiSounds.Names.All(n => { var loud = UiSounds.Make(n, false); var soft = UiSounds.Make(n, true); return Encoding.ASCII.GetString(loud, 0, 4) == "RIFF" && loud.Length > 1000 && loud.Length < 44100 && peak(loud) < 32767 * .2 && peak(soft) < peak(loud); }), "Menu sounds are generated by code as short, quiet clips that get softer while music plays");
                        player.SetBeta(false); await Task.Delay(250);
                        var navTransform = player.Find<Button>("LibraryButton").RenderTransform; var titleTransform = player.Find<TextBlock>("NowTitle").RenderTransform;
                        Check(!player.BetaActive && player.BetaBackdrop.Visibility == Visibility.Collapsed && ((TextBlock)player.Find<Viewbox>("BrandTitleFit").Child).FontFamily.Source == brandFontBefore && player.Find<Button>("LibraryButton").FontFamily.Source == navFontBefore && player.Find<Button>("PlayButton").Effect == playEffectBefore && navTransform.Value.IsIdentity && titleTransform.Value.IsIdentity && !store.Load().BetaTheme && !player.Beta3D && !player.BetaTileShown && player.Find<Viewbox>("DefaultArt").Visibility == Visibility.Visible && player.Find<Grid>("ContentStage").Opacity == 1 && player.Find<Grid>("ContentStage").RenderTransform.Value.IsIdentity && player.BetaVisualizerMode == null && player.Find<Canvas>("ExpandedWaveform").Opacity == 1, "Turning Beta off restores the exact original look");
                        Snapshot(player.Window, System.IO.Path.Combine(run, "beta-off.png"));
                        Check(player.Find<ListBox>("Tracks").ReadLocalValue(ItemsControl.ItemContainerStyleProperty) == DependencyProperty.UnsetValue, "Turning Beta off brings back the normal song rows");
                        Check(PlayerApp.VisualizerModes.All(m => Convert.ToString(player.Find<Button>("Mode" + m).Content) == m), "Turning Beta off brings back the normal visualizer names");
                        Check(player.BetaBarShadow == null && !((Grid)player.Window.Content).Children.OfType<Grid>().Any(g => g.Name == "BetaBarShadow"), "Turning Beta off removes the player bar shadow");
                        player.ApplyTheme(themeBefore);
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



