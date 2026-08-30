using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Digitone
{
    public sealed class Track
    {
        public string Path { get; set; }
        public bool Favorite { get; set; }
        public string SongTitle { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string AlbumArtist { get; set; }
        public string Genre { get; set; }
        public string Year { get; set; }
        public string TrackNumber { get; set; }
        public string DiscNumber { get; set; }
        public string Comment { get; set; }
        public string CoverData { get; set; }
        public string Lyrics { get; set; }
        public string Title { get { return CleanTitle(String.IsNullOrWhiteSpace(SongTitle) ? System.IO.Path.GetFileNameWithoutExtension(Path) : SongTitle); } }
        public static string CleanTitle(string title) { return System.Text.RegularExpressions.Regex.Replace(title ?? "", @"\s+\[[A-Za-z0-9_-]{11}\]$", ""); }
        public string Detail { get { return !String.IsNullOrWhiteSpace(Artist) ? Artist + (String.IsNullOrWhiteSpace(Album) ? "" : " · " + Album) : new DirectoryInfo(System.IO.Path.GetDirectoryName(Path)).Name; } }
        public string Format { get { return System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant(); } }
    }
    public sealed class Playlist
    {
        public string Name { get; set; }
        public List<string> Paths { get; set; }
        public bool Favorite { get; set; }
        public string CoverData { get; set; }
        public string FolderPath { get; set; }
        public bool FolderPreferenceInitialized { get; set; }
        public Playlist() { Paths = new List<string>(); }
    }
    internal sealed class BatchArtworkResult
    {
        internal readonly List<string> Succeeded=new List<string>();
        internal readonly Dictionary<string,string> Failed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Unsupported=new List<string>();
    }
    public sealed class Library
    {
        public List<Track> Tracks { get; set; }
        public List<Playlist> Playlists { get; set; }
        public double Volume { get; set; }
        public bool? GlobalMediaKeys { get; set; }
        public bool? AutoLyrics { get; set; }
        public bool SetupCompleted { get; set; }
        public string DownloadFolder { get; set; }
        public string ThemeName { get; set; }
        public string CustomThemeColor { get; set; }
        public string VisualizerMode { get; set; }
        public string MainVisualizerMode { get; set; }
        public string AppearanceMode { get; set; }
        public bool EqualizerEnabled { get; set; }
        public double[] EqualizerGains { get; set; }
        public bool NeonEnabled { get; set; }
        public bool AutoCheckUpdates { get; set; }
        public string LastUpdateCheckUtc { get; set; }
        public string AudioOutputId { get; set; }
        public string MainMusicFolder { get; set; }
        public List<string> MusicFolders { get; set; }
        public Library() { Tracks = new List<Track>(); Playlists = new List<Playlist>(); Volume = 0.7; AutoCheckUpdates = true; }
    }
    public static class LocalFiles
    {
        public static readonly string[] Extensions = { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".aif", ".aiff" };
        // Reject network paths before any filesystem operation. Reparse points could escape a chosen folder.
        public static bool IsLocal(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length < 3 || !Char.IsLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/')) return false;
            if (path.IndexOf(':', 2) >= 0) return false;
            try
            {
                string full = System.IO.Path.GetFullPath(path);
                DriveInfo drive = new DriveInfo(System.IO.Path.GetPathRoot(full));
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable && drive.DriveType != DriveType.CDRom && drive.DriveType != DriveType.Ram) return false;
                string part = full;
                while (!String.IsNullOrEmpty(part))
                {
                    if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) return false;
                    part = System.IO.Path.GetDirectoryName(part);
                }
                return true;
            }
            catch { return false; }
        }
        public static bool IsAudio(string path) { return Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
        public static List<string> Scan(IEnumerable<string> selected, out int skipped)
        {
            int failures = 0;
            HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stack<string> pending = new Stack<string>(selected);
            while (pending.Count > 0)
            {
                string path = pending.Pop();
                if (!IsLocal(path)) { failures++; continue; }
                path = System.IO.Path.GetFullPath(path);
                if (!visited.Add(path)) continue;
                if (File.Exists(path)) { if (IsAudio(path)) found.Add(path); continue; }
                if (!Directory.Exists(path)) { failures++; continue; }
                try
                {
                    foreach (string file in Directory.EnumerateFiles(path))
                        if (IsAudio(file)) { if (IsLocal(file)) found.Add(file); else failures++; }
                    foreach (string folder in Directory.EnumerateDirectories(path)) pending.Push(folder);
                }
                catch (UnauthorizedAccessException) { failures++; }
                catch (IOException) { failures++; }
            }
            skipped = failures;
            return found.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static void Shuffle<T>(IList<T> values, Random random)
        {
            for (int i = values.Count - 1; i > 0; i--) { int j = random.Next(i + 1); T temp = values[i]; values[i] = values[j]; values[j] = temp; }
        }
    }
    public sealed class LibraryStore
    {
        private readonly string path;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        public LibraryStore(string path) { this.path = path; }
        public Library Load()
        {
            if (!File.Exists(path)) return new Library();
            Library data = json.Deserialize<Library>(File.ReadAllText(path));
            if (data == null || data.Tracks == null || data.Playlists == null || data.Tracks.Any(t => t == null || String.IsNullOrEmpty(t.Path)) || data.Playlists.Any(p => p == null || p.Paths == null || String.IsNullOrEmpty(p.Name)))
                throw new InvalidDataException("The saved library is invalid. It has not been overwritten.");
            data.Tracks = data.Tracks.Where(t => LocalFiles.IsLocal(t.Path) && LocalFiles.IsAudio(t.Path)).GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            var allowed = new HashSet<string>(data.Tracks.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
            foreach (Playlist playlist in data.Playlists) playlist.Paths = playlist.Paths.Where(p => p != null && allowed.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            data.Volume = Math.Max(0, Math.Min(1, data.Volume));
            return data;
        }
        public void Save(Library library)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json.Serialize(library));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }
    public static class WaveReader
    {
        // Bounded sampling for a genuine amplitude waveform, without loading the entire song into RAM.
        public static double[] Read(string path, int count)
        {
            if (!LocalFiles.IsLocal(path) || !String.Equals(System.IO.Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (new string(reader.ReadChars(4)) != "RIFF") return null;
                    reader.ReadUInt32();
                    if (new string(reader.ReadChars(4)) != "WAVE") return null;
                    ushort format = 0, channels = 0, bits = 0, align = 0;
                    long start = 0, length = 0;
                    while (stream.Position + 8 <= stream.Length)
                    {
                        string id = new string(reader.ReadChars(4)); uint size = reader.ReadUInt32(); long next = stream.Position + size + (size % 2);
                        if (next > stream.Length + 1) return null;
                        if (id == "fmt " && size >= 16) { format = reader.ReadUInt16(); channels = reader.ReadUInt16(); reader.ReadUInt32(); reader.ReadUInt32(); align = reader.ReadUInt16(); bits = reader.ReadUInt16(); }
                        if (id == "data") { start = stream.Position; length = Math.Min(size, stream.Length - start); }
                        stream.Position = Math.Min(next, stream.Length);
                    }
                    if (start == 0 || channels == 0 || align == 0 || length < align || !(format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32) || format == 3 && bits == 32) || align != channels * (bits / 8)) return null;
                    long frames = length / align;
                    var peaks = new double[count];
                    for (int bin = 0; bin < count; bin++)
                    {
                        long first = bin * frames / count;
                        long last = Math.Max(first + 1, (bin + 1) * frames / count);
                        for (int sample = 0; sample < 128; sample++)
                        {
                            long frame = Math.Min(frames - 1, first + (last - first) * sample / 128);
                            stream.Position = start + frame * align;
                            for (int channel = 0; channel < channels; channel++)
                            {
                                double value;
                                if (format == 3) value = reader.ReadSingle();
                                else if (bits == 8) value = (reader.ReadByte() - 128) / 128.0;
                                else if (bits == 16) value = reader.ReadInt16() / 32768.0;
                                else if (bits == 24) { int raw = reader.ReadByte() | reader.ReadByte() << 8 | reader.ReadByte() << 16; if ((raw & 0x800000) != 0) raw |= unchecked((int)0xff000000); value = raw / 8388608.0; }
                                else value = reader.ReadInt32() / 2147483648.0;
                                if (!Double.IsNaN(value) && !Double.IsInfinity(value)) peaks[bin] = Math.Max(peaks[bin], Math.Min(1, Math.Abs(value)));
                            }
                        }
                    }
                    return peaks;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
    public sealed partial class PlayerApp
    {
        internal const string StartupGreeting = "Howdy Krazy";
        internal readonly Window Window;
        internal readonly Library Data;
        private readonly LibraryStore store;
        private readonly AudioPlayback media = new AudioPlayback();
        private readonly Random random = new Random();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly DispatcherTimer volumeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer seekResumeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly List<Track> queue = new List<Track>();


        private Brush accent = new SolidColorBrush(Color.FromRgb(193, 211, 168));
        private Brush dim = new SolidColorBrush(Color.FromRgb(67, 82, 66));
        private Playlist selectedPlaylist;
        private bool favorites, playlistsView, playing, ready, opening, updatingSeek, shuffle, importing, closed, seekWasPlaying;
        private int repeat, queueIndex = -1, waveVersion;
        private Track current;
        private Point trackDragStart;
        private ListBoxItem playlistDropItem;
        internal DownloadController Downloads;
        private List<Track> visible = new List<Track>();
        internal bool MediaReady { get { return ready; } }
        internal bool IsPlaying { get { return playing; } }
        internal bool IsImporting { get { return importing; } }
        internal int QueueLength { get { return queue.Count; } }
        internal string CurrentTitle { get { return current == null ? null : current.Title; } }
        internal static string DigiMusicFolder { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DigiMusic"); } }
        internal double Position { get { return media.Position.TotalSeconds; } }
        internal T Find<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }
        private void Text(string name, string text) { Find<TextBlock>(name).Text = text; }
        private void Status(string text) { Text("Status", text); }
        private void Click(string name, Action action) { Find<Button>(name).Click += delegate { action(); }; }
        public PlayerApp(LibraryStore store, Library data)
        {
            NativeChrome.Register();
            this.store = store; Data = data;if(RemoveMainFolderPlaylist())try{store.Save(Data);}catch{}
            using (Stream xaml = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) Window = (Window)XamlReader.Load(xaml);
            SetupExpandedWaveform();
            SetupThemes();
            SetupSettings();
            SetupUpdates();
            using(var markStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DigitoneMark.png")){var mark=new BitmapImage();mark.BeginInit();mark.CacheOption=BitmapCacheOption.OnLoad;mark.StreamSource=markStream;mark.EndInit();mark.Freeze();Find<Image>("RecordLogo").Source=mark;}
            SetupMotion();
            SetupMediaKeys();
            Click("LyricsButton", delegate { ShowLyrics(false); });
            Click("ExpandedLyricsButton", delegate { ShowLyrics(true); });
            Downloads = new DownloadController(this, delegate { Save(); });
            Click("DownloadsButton", delegate { ShowDownloads(true); });
            Click("QueueToggleButton", delegate { var drawer = Find<Border>("QueueDrawer"); drawer.Visibility = drawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; });
            Click("CloseQueueButton", delegate { Find<Border>("QueueDrawer").Visibility = Visibility.Collapsed; });
            Click("AddFolderButton", delegate { using (var dialog = new Forms.FolderBrowserDialog { Description = "Choose a local music folder. Digitone only reads the folder you choose and its subfolders.", ShowNewFolderButton = false }) if (dialog.ShowDialog() == Forms.DialogResult.OK) { var ignored = Import(new[] { dialog.SelectedPath },false,true); } });
            Click("AddFilesButton", delegate { var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Choose local audio files", Filter = "Audio files|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.aif;*.aiff" }; if (dialog.ShowDialog(Window) == true) { var ignored = Import(dialog.FileNames); } });
            Click("LibraryButton", delegate { SelectCollection(null, false); });
            Click("FavoritesButton", delegate { SelectCollection(null, true); });
            Click("PlaylistsButton", ShowPlaylists);
            Click("NewPlaylistButton", delegate { string name = Prompt("New playlist", "Give this collection a name.", "Untitled playlist"); if (name != null) CreatePlaylist(name, Selected()); });
            Click("AddToPlaylistButton", AddToPlaylist);
            Click("AddQueueButton", delegate { EnqueueSelected(false); });
            Click("FavoriteButton", ToggleFavorite);
            Click("VisualizerButton", delegate { SetMainVisualizer("Waveform"); });
            Click("MainSpectrumButton", delegate { SetMainVisualizer("Spectrum"); });
            Click("MainVisualizerOffButton", delegate { SetMainVisualizer("Off"); });
            Click("RemoveButton", RemoveSelected);
            Click("DeletePlaylistButton", DeletePlaylist);
            Click("PlayAllButton", delegate { StartQueue(visible, null); });
            Click("PlayButton", TogglePlay);
            Click("NextButton", delegate { Next(false); });
            Click("PreviousButton", delegate { if (ready && media.Position.TotalSeconds > 3) media.Position = TimeSpan.Zero; else if (queueIndex > 0) PlayAt(queueIndex - 1); });
            Click("ShuffleButton", delegate { shuffle = !shuffle; Find<Button>("ShuffleButton").Foreground = shuffle ? accent : Brushes.Gray; });
            Click("RepeatButton", delegate { repeat = (repeat + 1) % 3; Find<Button>("RepeatButton").Content = repeat == 2 ? "↻ 1" : "↻"; Find<Button>("RepeatButton").Foreground = repeat == 0 ? Brushes.Gray : accent; Status(new[] { "Repeat off.", "Repeat queue.", "Repeat current track." }[repeat]); });
            Click("MuteButton", delegate { media.IsMuted = !media.IsMuted; Find<Button>("MuteButton").Content = media.IsMuted ? "×" : "♪"; });
            Find<Slider>("Volume").Value = data.Volume;
            media.Volume = data.Volume;
            volumeSaveTimer.Tick += delegate { volumeSaveTimer.Stop(); Save(); };
            Window.Closed += delegate { volumeSaveTimer.Stop(); };
            Find<Slider>("Volume").ValueChanged += delegate { double value = Find<Slider>("Volume").Value; media.Volume = value; Data.Volume = value; volumeSaveTimer.Stop(); volumeSaveTimer.Start(); };
            Find<Slider>("Seek").ValueChanged += delegate { if (!updatingSeek && ready) media.Position = TimeSpan.FromSeconds(Find<Slider>("Seek").Value); };
            seekResumeTimer.Tick+=delegate{seekResumeTimer.Stop();if(ready&&playing&&seekWasPlaying)media.Play();};
            EnableDirectSlider(Find<Slider>("Seek"),BeginSeekInteraction,EndSeekInteraction); EnableDirectSlider(Find<Slider>("Volume"));
            Find<TextBox>("Search").TextChanged += delegate { Find<TextBlock>("SearchHint").Visibility = Find<TextBox>("Search").Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RefreshTracks(); };
            Find<ListBox>("Playlists").SelectionChanged += delegate { var playlist = Find<ListBox>("Playlists").SelectedItem as Playlist; if (playlist != null) SelectCollection(playlist, false); };
            Find<ListBox>("Tracks").SelectionChanged += delegate { UpdateTransport(); };
            Find<ListBox>("Tracks").MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e) { if (ItemAt(e.OriginalSource as DependencyObject) && Find<ListBox>("Tracks").SelectedItem != null) StartQueue(visible, (Track)Find<ListBox>("Tracks").SelectedItem); };
            Find<ListBox>("Tracks").PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){trackDragStart=e.GetPosition(Find<ListBox>("Tracks"));DependencyObject source=e.OriginalSource as DependencyObject;while(source!=null&&!(source is ListBoxItem))source=source is Visual?VisualTreeHelper.GetParent(source):null;var item=source as ListBoxItem;if(item!=null&&!item.IsSelected&&Keyboard.Modifiers==ModifierKeys.None){Find<ListBox>("Tracks").SelectedItems.Clear();item.IsSelected=true;}};
            Find<ListBox>("Tracks").PreviewMouseMove+=delegate(object sender,MouseEventArgs e){if(e.LeftButton!=MouseButtonState.Pressed)return;Point at=e.GetPosition(Find<ListBox>("Tracks"));if(Math.Abs(at.X-trackDragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(at.Y-trackDragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;var tracks=Selected();if(tracks.Count>0){DragDrop.DoDragDrop(Find<ListBox>("Tracks"),new DataObject("DigitoneTracks",tracks.ToArray()),selectedPlaylist!=null?DragDropEffects.Move:DragDropEffects.Copy);ClearPlaylistDrop();}};
            Find<ListBox>("Tracks").AllowDrop=true;
            Find<ListBox>("Tracks").DragOver+=delegate(object sender,DragEventArgs e){if(selectedPlaylist!=null&&String.IsNullOrEmpty(Find<TextBox>("Search").Text)&&e.Data.GetDataPresent("DigitoneTracks")){var item=TrackItemAt(e.OriginalSource as DependencyObject);bool after=item!=null&&e.GetPosition(item).Y>=item.ActualHeight/2;ShowPlaylistDrop(item,after);e.Effects=DragDropEffects.Move;e.Handled=true;}else ClearPlaylistDrop();};
            Find<ListBox>("Tracks").DragLeave+=delegate{ClearPlaylistDrop();};
            Find<ListBox>("Tracks").Drop+=delegate(object sender,DragEventArgs e){if(selectedPlaylist==null||!String.IsNullOrEmpty(Find<TextBox>("Search").Text))return;var tracks=e.Data.GetData("DigitoneTracks") as Track[];if(tracks==null||tracks.Length==0)return;var item=TrackItemAt(e.OriginalSource as DependencyObject);int insertion=item==null?selectedPlaylist.Paths.Count:visible.IndexOf((Track)item.DataContext)+(e.GetPosition(item).Y>=item.ActualHeight/2?1:0);ClearPlaylistDrop();ReorderPlaylistTracks(tracks,insertion);e.Handled=true;};
            Find<ListBox>("Tracks").KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && Find<ListBox>("Tracks").SelectedItem != null) { StartQueue(visible, (Track)Find<ListBox>("Tracks").SelectedItem); e.Handled = true; } };
            Find<ListBox>("Queue").MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e) { if (ItemAt(e.OriginalSource as DependencyObject) && Find<ListBox>("Queue").SelectedIndex >= 0) PlayAt(queueIndex + 1 + Find<ListBox>("Queue").SelectedIndex); };
            Find<ListBox>("Queue").AllowDrop=true;Find<ListBox>("Queue").DragOver+=delegate(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent("DigitoneTracks")?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};Find<ListBox>("Queue").Drop+=delegate(object sender,DragEventArgs e){var tracks=e.Data.GetData("DigitoneTracks") as Track[];if(tracks==null||tracks.Length==0)return;if(queueIndex<0)StartQueue(tracks,null);else{queue.AddRange(tracks);RefreshQueue();Status(tracks.Length+" tracks added to the queue.");}e.Handled=true;};
            Click("QueueRemoveButton", RemoveQueued);
            Click("QueueUpButton", delegate { MoveQueued(-1); });
            Click("QueueDownButton", delegate { MoveQueued(1); });
            var trackMenu = new ContextMenu();
            Menu(trackMenu, "Play next", delegate { EnqueueSelected(true); });
            Menu(trackMenu, "Add to queue", delegate { EnqueueSelected(false); });
            var trackPlaylistMenu=new MenuItem{Header="Add to playlist",Style=(Style)Window.FindResource(typeof(MenuItem))};trackMenu.Items.Add(trackPlaylistMenu);trackMenu.Opened+=delegate{RefreshTrackPlaylistMenu(trackPlaylistMenu);};
            Menu(trackMenu, "Add / remove favorite", ToggleFavorite);
            Menu(trackMenu, "Edit song details…", EditSelectedTrack);
            Menu(trackMenu, "Set artwork for selected songs…", SetBatchArtwork);
            Find<ListBox>("Tracks").ContextMenu = trackMenu;
            Find<ListBox>("Tracks").PreviewMouseRightButtonDown+=delegate(object sender,MouseButtonEventArgs e){DependencyObject source=e.OriginalSource as DependencyObject;while(source!=null&&!(source is ListBoxItem))source=source is Visual?VisualTreeHelper.GetParent(source):null;var item=source as ListBoxItem;if(item!=null&&!item.IsSelected){Find<ListBox>("Tracks").SelectedItems.Clear();item.IsSelected=true;}};
            var playlistMenu = new ContextMenu();
            Menu(playlistMenu, "Rename playlist", delegate { if (selectedPlaylist == null) return; string name = Prompt("Rename playlist", "Make it yours.", selectedPlaylist.Name); if (name != null) { selectedPlaylist.Name = name; Save(); RefreshPlaylists(); RefreshTracks(); } });
            Menu(playlistMenu, "Add / remove quick access", delegate { if (selectedPlaylist == null) return; selectedPlaylist.Favorite = !selectedPlaylist.Favorite; Save(); RefreshPlaylists(); });
            Menu(playlistMenu, "Delete playlist…", DeletePlaylist);
            Find<ListBox>("Playlists").ContextMenu = playlistMenu;
            Find<ListBox>("Playlists").PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e) { DependencyObject source = e.OriginalSource as DependencyObject; while (source != null && !(source is ListBoxItem)) source = source is Visual ? VisualTreeHelper.GetParent(source) : null; if (source is ListBoxItem) ((ListBoxItem)source).IsSelected = true; };
            Window.AllowDrop = true;
            Window.DragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Window.Drop += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { var ignored = Import((string[])e.Data.GetData(DataFormats.FileDrop)); } };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space && !(Keyboard.FocusedElement is TextBox) && !(Keyboard.FocusedElement is Button) && !(Keyboard.FocusedElement is Slider)) { TogglePlay(); e.Handled = true; } };
            media.MediaEnded += delegate { if (repeat == 2) { media.Position = TimeSpan.Zero; media.Play(); } else Next(true); };
            media.MediaFailed += delegate(Exception e) { opening = false; ready = false; playing = false; media.Close(); UpdatePlayButton(); Status("Cannot play this file. It may be missing, damaged, or unsupported by your Windows codecs. Try another track."); };
            Find<Canvas>("Waveform").SizeChanged += delegate { LayoutWave(); };
            Window.SizeChanged += delegate(object sender, SizeChangedEventArgs e) { ApplyCompactLayout(e.NewSize.Height, e.NewSize.Width); };
            SetupWaveRendering();
            timer.Tick += delegate { ((RotateTransform)Find<System.Windows.Shapes.Ellipse>("AmbientOrbit").RenderTransform).Angle-=.28; UpdateLyricsDisplay(); if (!ready) return; updatingSeek = true; Find<Slider>("Seek").Value = media.Position.TotalSeconds; updatingSeek = false; Text("Elapsed", FormatTime(media.Position.TotalSeconds)); };
            Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (Downloads.Busy) { e.Cancel = true; ShowDownloads(true); if (MessageBox.Show(Window, "Cancel the active download? After it stops, you can close Digitone.", "Download in progress", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Downloads.Cancel(); return; } if (importing) { e.Cancel = true; Status("Finishing this import. Please close Digitone again in a moment."); return; } if (!Save()) { if (MessageBox.Show(Window, "The library could not be saved. Close and lose unsaved changes?", "Close Digitone?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) e.Cancel = true; } };
            Window.Closed += delegate { closed = true; waveVersion++; if (liveWave != null) liveWave.Dispose(); timer.Stop(); seekResumeTimer.Stop(); media.Close(); };
            RefreshPlaylists(); RefreshTracks(); UpdateTransport(); timer.Start();
        }
        private static bool ItemAt(DependencyObject source)
        {
            while (source != null) { if (source is ListBoxItem) return true; if (!(source is Visual)) return false; source = VisualTreeHelper.GetParent(source); } return false;
        }
        private Track TrackAt(DependencyObject source)
        {
            var item=TrackItemAt(source);return item==null?null:item.DataContext as Track;
        }
        private static ListBoxItem TrackItemAt(DependencyObject source){while(source!=null&&!(source is ListBoxItem))source=source is Visual?VisualTreeHelper.GetParent(source):null;return source as ListBoxItem;}
        private void ClearPlaylistDrop(){if(playlistDropItem==null)return;playlistDropItem.BorderBrush=Brushes.Transparent;playlistDropItem.BorderThickness=new Thickness(0);playlistDropItem.Effect=null;playlistDropItem=null;}
        private void ShowPlaylistDrop(ListBoxItem item,bool after){if(playlistDropItem!=item)ClearPlaylistDrop();playlistDropItem=item;if(item==null)return;item.BorderBrush=accent;item.BorderThickness=after?new Thickness(0,0,0,3):new Thickness(0,3,0,0);item.Effect=NeonGlow();}
        internal void ApplyCompactLayout(double height, double width = 0)
        {
            bool compact = height < 720;
            if (width <= 0) width = Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width;
            bool portrait = height > width * 1.2;
            var stage = Find<Grid>("ContentStage");
            stage.RowDefinitions[0].Height = portrait ? new GridLength(Math.Min(600, Math.Max(330, (height-260)*.44))) : new GridLength(1,GridUnitType.Star);
            stage.RowDefinitions[1].Height = portrait ? new GridLength(1,GridUnitType.Star) : new GridLength(0);
            var now = Find<Border>("NowPane"); var library = Find<Grid>("LibraryPane");
            Grid.SetColumnSpan(now, portrait ? 2 : 1); Grid.SetColumn(library, portrait ? 0 : 1); Grid.SetColumnSpan(library, portrait ? 2 : 1); Grid.SetRow(library, portrait ? 1 : 0);
            now.Margin = portrait ? new Thickness(24,0,24,12) : new Thickness(22,0,10,12);
            library.Margin = portrait ? new Thickness(24,0,24,12) : new Thickness(14,0,24,12);
            Find<Border>("RecordArt").Height = Double.NaN;
            Find<TextBlock>("CollectionTitle").FontSize = compact ? 40 : 52;
            Find<StackPanel>("CollectionHeader").Margin = new Thickness(0,compact ? 14 : 24,0,compact ? 12 : 22);
            Find<Canvas>("Waveform").Height = compact ? 40 : 58;
            Find<Canvas>("MainSpectrum").Height = Data.MainVisualizerMode=="Spectrum" ? (compact?52:72) : (compact?40:58);
            Find<Viewbox>("DefaultArt").MaxHeight = Double.PositiveInfinity;
            Find<Border>("RecordArt").MinHeight = Data.MainVisualizerMode=="Spectrum" ? (compact?150:190) : 90;
            foreach(string name in new[]{"LibraryButton","FavoritesButton","DownloadsButton","PlaylistsButton"}){ var button=Find<Button>(name); button.FontSize=width<1250?12:17; button.Padding=width<1250?new Thickness(8,8,8,8):new Thickness(12,10,12,10); }
        }
        private void Menu(ContextMenu menu, string label, Action action) { menu.Style = (Style)Window.FindResource(typeof(ContextMenu)); var item = new MenuItem { Header = label, Style = (Style)Window.FindResource(typeof(MenuItem)) }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        private List<Track> Selected() { return Find<ListBox>("Tracks").SelectedItems.Cast<Track>().ToList(); }
        private void EnableDirectSlider(Slider slider,Action begin=null,Action end=null)
        {
            slider.IsMoveToPointEnabled=true;
            bool dragging=false;
            Action<MouseEventArgs> set=delegate(MouseEventArgs e) { double width=slider.ActualWidth; if(width<=0)return; double fraction=Math.Max(0,Math.Min(1,e.GetPosition(slider).X/width)); slider.Value=slider.Minimum+fraction*(slider.Maximum-slider.Minimum); };
            slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler(delegate(object sender,MouseButtonEventArgs e){ dragging=true;if(begin!=null)begin();set(e); slider.CaptureMouse(); e.Handled=true; }),true);
            slider.AddHandler(UIElement.PreviewMouseMoveEvent,new MouseEventHandler(delegate(object sender,MouseEventArgs e){ if(dragging && e.LeftButton==MouseButtonState.Pressed){set(e);e.Handled=true;} }),true);
            slider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler(delegate(object sender,MouseButtonEventArgs e){ if(!dragging)return; set(e); dragging=false; slider.ReleaseMouseCapture();if(end!=null)end(); e.Handled=true; }),true);
        }
        internal void BeginSeekInteraction(){seekResumeTimer.Stop();seekWasPlaying=playing;if(ready)media.Pause();}
        internal void EndSeekInteraction(){if(seekWasPlaying&&playing)seekResumeTimer.Start();}
        private bool Save() { try { store.Save(Data); return true; } catch (Exception e) { Status("Could not save your library: " + e.Message); return false; } }
        internal async Task Import(string[] paths, bool keepCollection = false, bool createFolderPlaylist = false)
        {
            if (Downloads.Busy && !keepCollection) { Status("Finish or cancel the download before importing more files."); return; }
            if (importing) { Status("An import is already in progress."); return; }
            importing = true; Find<Button>("AddFolderButton").IsEnabled = false; Find<Button>("AddFilesButton").IsEnabled = false; Status("Reading the files you chose…");
            try
            {
                int skipped = 0;
                RememberMusicFolders(paths);
                List<string> found = await Task.Run(delegate { int count; var result = LocalFiles.Scan(paths, out count); skipped = count; return result; });
                var known = new HashSet<string>(Data.Tracks.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
                int added = 0;
                foreach (string path in found) if (known.Add(path)) { Track track = await Task.Run(delegate { var t = new Track { Path = path }; try { SongTags.ReadInto(t); } catch { } return t; }); Data.Tracks.Add(track); added++; }
                RemoveMainFolderPlaylist();foreach(string folder in paths.Where(Directory.Exists))if(!SameFolder(folder,Data.MainMusicFolder)&&(createFolderPlaylist||Data.Playlists.Any(p=>SameFolder(p.FolderPath,folder))))EnsureFolderPlaylist(folder,found);
                if (keepCollection) RefreshTracks(); else SelectCollection(null, false);
                if (Save()) Status(String.Format("{0} tracks added · {1} already in your library{2}. Your original files are untouched.", added, found.Count - added, skipped == 0 ? "" : " · " + skipped + " inaccessible, linked, or nonlocal paths skipped"));
            }
            catch (Exception e) { Status("Import could not finish: " + e.Message); }
            finally { importing = false; Find<Button>("AddFolderButton").IsEnabled = true; Find<Button>("AddFilesButton").IsEnabled = true; }
        }
        internal async Task RescanMusicFolders()
        {
            var roots=Data.MusicFolders.Where(p=>!String.IsNullOrWhiteSpace(p)).Select(p=>System.IO.Path.GetFullPath(p).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar).ToArray();
            var missing=new HashSet<string>(Data.Tracks.Where(t=>roots.Any(r=>t.Path.StartsWith(r,StringComparison.OrdinalIgnoreCase))&&!File.Exists(t.Path)).Select(t=>t.Path),StringComparer.OrdinalIgnoreCase);
            if(current!=null&&missing.Contains(current.Path))StopAndClear();
            Data.Tracks.RemoveAll(t=>missing.Contains(t.Path));foreach(Playlist p in Data.Playlists)p.Paths.RemoveAll(missing.Contains);
            Data.Playlists.RemoveAll(p=>!String.IsNullOrEmpty(p.FolderPath)&&!Directory.Exists(p.FolderPath));
            RemoveMainFolderPlaylist();
            Save();RefreshPlaylists();RefreshPlaylistGrid();RefreshTracks();
            var existing=Data.MusicFolders.Where(Directory.Exists).ToArray();if(existing.Length>0)await Import(existing,true);
            Status(missing.Count+" missing tracks removed · folders rescanned.");
        }
        private void EnsureFolderPlaylist(string folder,IEnumerable<string> found)
        {
            string full=System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar); string name=new DirectoryInfo(full).Name;
            if(SameFolder(full,Data.MainMusicFolder)){RemoveMainFolderPlaylist();RefreshPlaylists();RefreshPlaylistGrid();return;}
            var paths=found.Where(p=>p.StartsWith(full+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(paths.Count==0)return;
            var playlist=Data.Playlists.FirstOrDefault(p=>String.Equals(p.FolderPath,full,StringComparison.OrdinalIgnoreCase));
            if(playlist==null){string baseName=name;int suffix=2;while(Data.Playlists.Any(p=>String.Equals(p.Name,name,StringComparison.CurrentCultureIgnoreCase)))name=baseName+" ("+(suffix++)+")";playlist=new Playlist{Name=name,Favorite=false,FolderPath=full,FolderPreferenceInitialized=true}; Data.Playlists.Add(playlist); }
            playlist.FolderPath=full;if(!playlist.FolderPreferenceInitialized){playlist.Favorite=false;playlist.FolderPreferenceInitialized=true;}playlist.Paths=paths;
            RefreshPlaylists(); RefreshPlaylistGrid();
        }
        private static bool SameFolder(string first,string second){if(String.IsNullOrWhiteSpace(first)||String.IsNullOrWhiteSpace(second))return false;try{return String.Equals(System.IO.Path.GetFullPath(first).TrimEnd(System.IO.Path.DirectorySeparatorChar),System.IO.Path.GetFullPath(second).TrimEnd(System.IO.Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
        internal bool RemoveMainFolderPlaylist(){if(String.IsNullOrWhiteSpace(Data.MainMusicFolder))return false;return Data.Playlists.RemoveAll(p=>!String.IsNullOrWhiteSpace(p.FolderPath)&&SameFolder(p.FolderPath,Data.MainMusicFolder))>0;}
        internal void SelectCollection(Playlist playlist, bool onlyFavorites)
        {
            ShowDownloads(false);
            Find<Grid>("PlaylistPane").Visibility=Visibility.Collapsed;
            selectedPlaylist = playlist; favorites = onlyFavorites; playlistsView=false;
            if (playlist == null) Find<ListBox>("Playlists").SelectedItem = null;
            Find<Button>("LibraryButton").Background = playlist == null && !favorites ? dim : Brushes.Transparent;
            Find<Button>("FavoritesButton").Background = favorites ? dim : Brushes.Transparent;
            Find<Button>("PlaylistsButton").Background = Brushes.Transparent;
            RefreshTracks();
            UpdateNavigationOutline(); Motion.Enter(Find<StackPanel>("CollectionHeader"),-18,8);
        }
        private void ShowPlaylists()
        {
            ShowDownloads(false); playlistsView=true; favorites=false; selectedPlaylist=null;
            Find<Border>("NowPane").Visibility=Visibility.Collapsed;Find<Grid>("LibraryPane").Visibility=Visibility.Collapsed;Find<Grid>("PlaylistPane").Visibility=Visibility.Visible;RefreshPlaylistGrid();
            Find<Button>("LibraryButton").Background=Brushes.Transparent; Find<Button>("FavoritesButton").Background=Brushes.Transparent; Find<Button>("PlaylistsButton").Background=dim; UpdateNavigationOutline();Motion.Enter(Find<Grid>("PlaylistPane"),28,0);
        }
        internal void ShowDownloads(bool show)
        {
            Find<Grid>("DownloadPane").Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Find<Grid>("PlaylistPane").Visibility = Visibility.Collapsed; playlistsView=false;
            Find<Grid>("LibraryPane").Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            Find<Border>("NowPane").Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            Find<Button>("DownloadsButton").Background = show ? dim : Brushes.Transparent;
            UpdateNavigationOutline(); Motion.Enter(show ? (FrameworkElement)Find<Grid>("DownloadPane") : Find<Grid>("LibraryPane"), 28, 0);
            if (show) { Find<Button>("LibraryButton").Background = Brushes.Transparent; Find<Button>("FavoritesButton").Background = Brushes.Transparent; Find<ListBox>("Playlists").SelectedItem = null; Downloads.RefreshTools(); }
        }
        private void RefreshPlaylists() { var list = Find<ListBox>("Playlists"); list.ItemsSource = null; list.ItemsSource = Data.Playlists.Where(p=>p.Favorite).ToList(); if (selectedPlaylist != null) list.SelectedItem = selectedPlaylist; }
        private void RefreshPlaylistGrid()
        {
            var grid=Find<WrapPanel>("PlaylistGrid");grid.Children.Clear();Find<TextBlock>("PlaylistGridEmpty").Visibility=Data.Playlists.Count==0?Visibility.Visible:Visibility.Collapsed;
            var straightCard=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='PlaylistCardSurface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border></ControlTemplate>");
            foreach(Playlist playlist in Data.Playlists.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase))
            {
                var stack=new StackPanel();var art=new Grid{Width=164,Height=164,Background=dim};var image=new Image{Source=SongTags.ImageFromData(playlist.CoverData),Stretch=Stretch.UniformToFill};art.Children.Add(image);if(image.Source==null)art.Children.Add(new TextBlock{Text=(playlist.Name??"?").Substring(0,1).ToUpperInvariant(),FontFamily=(FontFamily)Window.Resources["ThemeDisplayFont"],FontSize=62,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=accent});stack.Children.Add(art);stack.Children.Add(new TextBlock{Text=playlist.Name,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,10,0,2),TextTrimming=TextTrimming.CharacterEllipsis});stack.Children.Add(new TextBlock{Text=playlist.Paths.Count+" tracks",Foreground=(Brush)Window.FindResource("B919A92"),FontSize=10});
                var card=new Button{Content=stack,Tag=playlist,Template=straightCard,Width=196,Height=252,Padding=new Thickness(14),Margin=new Thickness(0,0,16,16),HorizontalContentAlignment=HorizontalAlignment.Left,VerticalContentAlignment=VerticalAlignment.Top,Background=Themes.Brush(Window,"1C211D"),BorderBrush=Themes.Brush(Window,"354233")};card.Click+=delegate{SelectCollection(playlist,false);};
                var menu=new ContextMenu();Menu(menu,"Change square cover…",delegate{ChangePlaylistCover(playlist);});Menu(menu,"Remove square cover",delegate{playlist.CoverData=null;Save();RefreshPlaylistGrid();});Menu(menu,playlist.Favorite?"Remove from quick access":"Add to quick access",delegate{playlist.Favorite=!playlist.Favorite;Save();RefreshPlaylists();RefreshPlaylistGrid();});Menu(menu,"Delete playlist…",delegate{selectedPlaylist=playlist;DeletePlaylist();});card.ContextMenu=menu;grid.Children.Add(card);
            }
        }
        private void ChangePlaylistCover(Playlist playlist){var picker=new Microsoft.Win32.OpenFileDialog{Title="Choose a square cover for "+playlist.Name,Filter="Image files|*.jpg;*.jpeg;*.png;*.bmp;*.webp"};if(picker.ShowDialog(Window)!=true)return;try{playlist.CoverData=Convert.ToBase64String(SongTags.LoadCover(picker.FileName));Save();RefreshPlaylistGrid();}catch(Exception e){Status("Could not use that playlist cover: "+e.Message);}}
        private void RefreshTracks()
        {
            var selected = Selected();
            IEnumerable<Track> tracks = Data.Tracks;
            if (selectedPlaylist != null) { var byPath = Data.Tracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase); tracks = selectedPlaylist.Paths.Where(byPath.ContainsKey).Select(p => byPath[p]); }
            else { if (favorites) tracks = tracks.Where(t => t.Favorite); tracks = tracks.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase); }
            string query = Find<TextBox>("Search").Text.Trim();
            visible = tracks.Where(t => (t.Title + " " + t.Detail + " " + t.Genre).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            Find<ListBox>("Tracks").ItemsSource = visible;
            foreach (Track track in selected) if (visible.Contains(track)) Find<ListBox>("Tracks").SelectedItems.Add(track);
            Text("CollectionTitle", selectedPlaylist != null ? selectedPlaylist.Name : favorites ? "Favorites" : "Library");
            Text("CollectionSubtitle", visible.Count == 0 && Data.Tracks.Count == 0 ? "Bring your music. Leave everything else behind." : visible.Count + " tracks · " + (selectedPlaylist != null ? "Handpicked by you." : favorites ? "The ones you come back to." : "Your music collection."));
            Find<StackPanel>("EmptyState").Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Text("EmptyTitle", query.Length > 0 ? "No matching music" : selectedPlaylist != null ? "A little room for your favorites" : favorites ? "Keep the good ones close" : "Your collection starts here");
            Text("EmptyDescription", query.Length > 0 ? "Try another filename or folder name." : selectedPlaylist != null ? "Select songs in All music, then choose To playlist." : favorites ? "Select a song and choose Favorite to save it here." : "Add a folder or drop audio files into this window.\nYour originals stay exactly where they are.");
            Find<Button>("DeletePlaylistButton").Visibility = selectedPlaylist == null ? Visibility.Collapsed : Visibility.Visible;
            Find<Button>("PlayAllButton").IsEnabled = visible.Count > 0;
            Text("PlayerDetail", current == null ? "A good place to slow down." : current.Detail);
        }
        internal Playlist CreatePlaylist(string name, IEnumerable<Track> tracks)
        {
            var playlist = new Playlist { Name = name.Trim(), Favorite = false, Paths = tracks.Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            Data.Playlists.Add(playlist); selectedPlaylist = playlist; favorites = false; RefreshPlaylists(); RefreshPlaylistGrid(); RefreshTracks(); Save(); return playlist;
        }
        private void MakeMix()
        {
            if (visible.Count == 0) return;
            string name = Prompt("Make a mix", "Give your shuffled playlist a name.", DateTime.Now.Hour < 12 ? "Morning mix" : DateTime.Now.Hour < 18 ? "Afternoon mix" : "Evening mix");
            if (name == null) return;
            var mixed = visible.ToList(); LocalFiles.Shuffle(mixed, random); CreatePlaylist(name, mixed); StartQueue(mixed, mixed[0]); Status("Mix saved to your playlists. This is a listening queue, not an exported audio file.");
        }
        private void AddToPlaylist()
        {
            var tracks = Selected(); if (tracks.Count == 0) { Status("Select one or more tracks first. Hold Ctrl or Shift to select several."); return; }
            if (Data.Playlists.Count == 0) { string name = Prompt("New playlist", "Save your selected tracks together.", "My playlist"); if (name != null) CreatePlaylist(name, tracks); return; }
            var dialog = Dialog("Add to playlist", 380, 270); var panel = DialogPanel(dialog, "Choose a playlist for " + tracks.Count + " tracks.");
            var choices = new ComboBox { ItemsSource = Data.Playlists.OrderByDescending(p=>p.Favorite).ThenBy(p=>p.Name).ToList(), DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 18, 0, 22), FontSize = 15, Padding = new Thickness(8) }; panel.Children.Add(choices);
            var accept = new Button { Content = "Add tracks", IsDefault = true }; accept.Click += delegate { dialog.DialogResult = true; }; panel.Children.Add(accept);
            if (dialog.ShowDialog() == true) AddSelectedToPlaylist((Playlist)choices.SelectedItem);
        }
        private void RefreshTrackPlaylistMenu(MenuItem parent)
        {
            parent.Items.Clear();var playlists=Data.Playlists.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).ToList();parent.IsEnabled=playlists.Count>0;if(playlists.Count==0){parent.Items.Add(new MenuItem{Header="No playlists yet",IsEnabled=false,Style=(Style)Window.FindResource(typeof(MenuItem))});return;}foreach(var playlist in playlists){var item=new MenuItem{Header=playlist.Name,Style=(Style)Window.FindResource(typeof(MenuItem))};Playlist target=playlist;item.Click+=delegate{AddSelectedToPlaylist(target);};parent.Items.Add(item);}
        }
        internal int AddSelectedToPlaylist(Playlist playlist)
        {
            if(playlist==null)return 0;var tracks=Selected();if(tracks.Count==0){Status("Select one or more tracks first. Hold Ctrl or Shift to select several.");return 0;}int added=0;foreach(Track track in tracks)if(!playlist.Paths.Contains(track.Path,StringComparer.OrdinalIgnoreCase)){playlist.Paths.Add(track.Path);added++;}if(Save())Status(added==0?"Those songs are already in "+playlist.Name+".":"Added "+added+" song"+(added==1?"":"s")+" to "+playlist.Name+".");RefreshTracks();return added;
        }
        private void ToggleFavorite()
        {
            var tracks = Selected(); if (tracks.Count == 0) { Status("Select tracks to add or remove favorites."); return; } bool value = tracks.Any(t => !t.Favorite); foreach (Track track in tracks) track.Favorite = value; RefreshTracks(); if (Save()) Status(value ? "Saved to Favorites." : "Removed from Favorites.");
        }
        private async void SetBatchArtwork()
        {
            var tracks=Selected(); if(tracks.Count==0){ Status("Select one or more tracks first."); return; }
            byte[] cover=ChooseBatchArtwork(tracks); if(cover==null)return;
            if(MessageBox.Show(Window,"Set this artwork on "+tracks.Count+" selected songs?\n\nEach song will be written and verified individually. A failed or unsupported file will not stop the remaining songs. No song backups will be created.","Set album artwork",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            if(current!=null && tracks.Contains(current))StopAndClear();
            importing=true; SetLibraryArtworkBusy(true);
            try
            {
                var result=await ApplyBatchArtworkDetailed(tracks,cover,delegate(int done){Status("Writing artwork · "+Math.Min(tracks.Count,done+1)+" / "+tracks.Count);});
                Save();RefreshTracks();UpdateCover();Status("Artwork updated on "+result.Succeeded.Count+" of "+tracks.Count+" songs.");
                if(result.Failed.Count>0||result.Unsupported.Count>0){var lines=new List<string>{result.Succeeded.Count+" updated · "+result.Failed.Count+" failed · "+result.Unsupported.Count+" unsupported"};if(result.Failed.Count>0){lines.Add("");lines.Add("Failed:");lines.AddRange(result.Failed.Select(pair=>System.IO.Path.GetFileName(pair.Key)+" — "+pair.Value));}if(result.Unsupported.Count>0){lines.Add("");lines.Add("Unsupported (MP3, FLAC, and M4A can store verified artwork):");lines.AddRange(result.Unsupported.Select(System.IO.Path.GetFileName));}MessageBox.Show(Window,String.Join("\n",lines),"Batch artwork results",MessageBoxButton.OK,result.Failed.Count>0?MessageBoxImage.Warning:MessageBoxImage.Information);}
            }
            catch(Exception e){Save();RefreshTracks();Status("Batch artwork could not continue: "+e.Message);}
            finally{ importing=false; SetLibraryArtworkBusy(false); }
        }
        internal void SetLibraryArtworkBusy(bool busy)
        {
            var pane=Find<Grid>("LibraryPane");
            // Disabling a WPF ListBox lets the Windows disabled-control theme paint its
            // default white surface. Block input without changing the themed visuals.
            pane.IsHitTestVisible=!busy;
            pane.Cursor=busy?Cursors.Wait:null;
        }
        internal async Task<int> ApplyBatchArtwork(IList<Track> tracks,byte[] cover,Action<int> progress=null)
        {
            return (await ApplyBatchArtworkDetailed(tracks,cover,progress)).Succeeded.Count;
        }
        internal async Task<BatchArtworkResult> ApplyBatchArtworkDetailed(IList<Track> tracks,byte[] cover,Action<int> progress=null)
        {
            if(cover==null||cover.Length==0)throw new ArgumentException("Choose a valid artwork image.");var result=new BatchArtworkResult();int processed=0;
            foreach(Track track in tracks)
            {
                if(progress!=null)progress(processed);processed++;
                if(track==null||!SongTags.CanEdit(track.Path)){result.Unsupported.Add(track==null?"Unknown song":track.Path);continue;}
                Track selected=track;
                try{await Task.Run(delegate{SongTags.SaveArtwork(selected.Path,cover);var reread=new Track{Path=selected.Path};SongTags.ReadInto(reread);if(String.IsNullOrEmpty(reread.CoverData)||SongTags.ArtworkDistance(cover,Convert.FromBase64String(reread.CoverData))>18)throw new IOException("Embedded artwork did not match the selected picture.");selected.CoverData=reread.CoverData;});result.Succeeded.Add(selected.Path);}
                catch(Exception e){result.Failed[selected.Path]=e.Message;}
            }
            return result;
        }
        private byte[] ChooseBatchArtwork(List<Track> tracks)
        {
            var dialog=Dialog("Set artwork for "+tracks.Count+" songs",540,520);var panel=DialogPanel(dialog,"Use the same cover for every selected file.");byte[] chosen=null;
            var preview=new Image{Width=220,Height=220,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,16,0,14)};panel.Children.Add(preview);var actions=new WrapPanel();panel.Children.Add(actions);
            var file=new Button{Content="Choose picture…"};var browse=new Button{Content="Browse covers…"};var folder=new Button{Content="Use folder cover"};actions.Children.Add(file);actions.Children.Add(browse);actions.Children.Add(folder);
            var status=SettingsText("Choose artwork to continue.",11,true);panel.Children.Add(status);var apply=new Button{Content="Use this artwork",IsDefault=true,IsEnabled=false,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0)};panel.Children.Add(apply);
            Action<byte[],string> select=delegate(byte[] image,string description){chosen=image;preview.Source=SongTags.ImageFromData(Convert.ToBase64String(image));status.Text=description;apply.IsEnabled=true;};
            file.Click+=delegate{var picker=new Microsoft.Win32.OpenFileDialog{Title="Choose album artwork",Filter="Image files|*.jpg;*.jpeg;*.png;*.bmp;*.webp"};if(picker.ShowDialog(dialog)==true)try{select(SongTags.LoadCover(picker.FileName),"Local picture: "+System.IO.Path.GetFileName(picker.FileName));}catch(Exception e){status.Text=e.Message;}};
            browse.Click+=delegate{var gallery=new CoverGallery(dialog,tracks.Select(t=>t.Artist).FirstOrDefault(a=>!String.IsNullOrWhiteSpace(a))??"");if(gallery.ShowDialog()==true&&gallery.Selected!=null)select(gallery.Selected.Image,gallery.Selected.Description);};
            folder.Click+=async delegate{folder.IsEnabled=false;try{var match=await Task.Run(delegate{return ArtworkFinder.Local(tracks[0].Path);});if(match==null)status.Text="No folder cover was found beside the first selected song.";else select(match.Image,match.Description);}catch(Exception e){status.Text=e.Message;}finally{folder.IsEnabled=true;}};
            apply.Click+=delegate{dialog.DialogResult=true;};return dialog.ShowDialog()==true?chosen:null;
        }
        private void RemoveSelected()
        {
            var tracks = Selected(); if (tracks.Count == 0) { Status("Select the tracks you want to remove."); return; }
            if (selectedPlaylist != null) { foreach (Track track in tracks) selectedPlaylist.Paths.Remove(track.Path); }
            else
            {
                if (MessageBox.Show(Window, "Remove " + tracks.Count + " tracks from Digitone and its playlists?\n\nYour audio files will NOT be deleted.", "Remove from library", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                foreach (Track track in tracks) { Data.Tracks.Remove(track); foreach (Playlist playlist in Data.Playlists) playlist.Paths.Remove(track.Path); }
                if (current != null && tracks.Contains(current)) StopAndClear();
                else { Track playingTrack = current; queue.RemoveAll(tracks.Contains); queueIndex = playingTrack == null ? -1 : queue.IndexOf(playingTrack); RefreshQueue(); }
            }
            RefreshTracks(); if (Save()) Status("Removed from this collection. No audio files were deleted.");
        }
        private async void DeletePlaylist()
        {
            if (selectedPlaylist == null) return;
            if (importing || Downloads.Busy) { Status("Finish the current file operation first."); return; }
            Playlist playlist = selectedPlaylist; string[] paths = playlist.Paths.ToArray();
            int shared = paths.Count(p => Data.Playlists.Any(other => other != playlist && other.Paths.Contains(p, StringComparer.OrdinalIgnoreCase)));
            var dialog = new DeletePlaylistDialog(Window, playlist.Name, paths.Length, shared);
            if (dialog.ShowDialog() != true) return;
            if (dialog.Mode == PlaylistRemoval.KeepFiles) { Data.Playlists.Remove(playlist); SelectCollection(null, false); RefreshPlaylists(); RefreshPlaylistGrid(); Save(); return; }
            importing = true;
            if (current != null && paths.Contains(current.Path, StringComparer.OrdinalIgnoreCase)) StopAndClear();
            Status("Updating playlist files…"); Find<Grid>("LibraryPane").IsEnabled = false;
            string[] allowed = Data.Tracks.Select(t => t.Path).ToArray();
            try
            {
                PlaylistRemovalResult result = await Task.Run(delegate { return PlaylistFiles.Remove(paths, allowed, dialog.Mode, dialog.DeletedRoot); });
                ApplyRemovedFiles(playlist, result);
                if (Save()) Status(result.Errors.Count == 0 ? result.Removed.Count + " entries removed." + (result.Folder == null ? "" : " Files moved to " + result.Folder) : "Some files could not be changed; those entries remain. " + String.Join(" · ", result.Errors.Take(3)));
            }
            catch (Exception e) { Status("Could not finish deleting the playlist: " + e.Message); }
            finally { importing = false; Find<Grid>("LibraryPane").IsEnabled = true; }
        }
        internal void ApplyRemovedFiles(Playlist playlist, PlaylistRemovalResult result)
        {
            var removed = new HashSet<string>(result.Removed, StringComparer.OrdinalIgnoreCase);
            Data.Tracks.RemoveAll(t => removed.Contains(t.Path)); foreach (Playlist item in Data.Playlists) item.Paths.RemoveAll(removed.Contains);
            queue.RemoveAll(t => removed.Contains(t.Path)); queueIndex = current == null ? -1 : queue.IndexOf(current); RefreshQueue();
            if (result.Errors.Count == 0) Data.Playlists.Remove(playlist);
            SelectCollection(result.Errors.Count == 0 ? null : playlist, false); RefreshPlaylists(); RefreshPlaylistGrid();
        }
        internal bool ReorderPlaylistTracks(IEnumerable<Track> moving,int insertion)
        {
            if(selectedPlaylist==null||moving==null||!String.IsNullOrEmpty(Find<TextBox>("Search").Text))return false;
            var paths=moving.Select(t=>t.Path).Where(p=>selectedPlaylist.Paths.Contains(p,StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(paths.Count==0)return false;
            insertion=Math.Max(0,Math.Min(selectedPlaylist.Paths.Count,insertion));
            int removedBefore=selectedPlaylist.Paths.Take(insertion).Count(p=>paths.Contains(p,StringComparer.OrdinalIgnoreCase));
            selectedPlaylist.Paths.RemoveAll(p=>paths.Contains(p,StringComparer.OrdinalIgnoreCase));insertion=Math.Max(0,Math.Min(selectedPlaylist.Paths.Count,insertion-removedBefore));
            selectedPlaylist.Paths.InsertRange(insertion,paths);RefreshTracks();foreach(var track in moving)if(visible.Contains(track))Find<ListBox>("Tracks").SelectedItems.Add(track);Save();Status(paths.Count==1?"Song moved in playlist.":paths.Count+" songs moved in playlist.");return true;
        }
        internal void StartQueue(IEnumerable<Track> tracks, Track first)
        {
            var nextQueue = tracks.ToList(); if (nextQueue.Count == 0) return;
            if (shuffle && first == null) LocalFiles.Shuffle(nextQueue, random);
            queue.Clear(); queue.AddRange(nextQueue); PlayAt(first == null ? 0 : Math.Max(0, queue.IndexOf(first)));
        }
        private void EnqueueSelected(bool next)
        {
            var tracks = Selected(); if (tracks.Count == 0) return;
            if (queueIndex < 0) { StartQueue(tracks, null); return; }
            if (next) queue.InsertRange(queueIndex + 1, tracks); else queue.AddRange(tracks); RefreshQueue(); Status(tracks.Count + " tracks added to the queue.");
        }
        private void RemoveQueued(){ int selected=Find<ListBox>("Queue").SelectedIndex; int actual=queueIndex+1+selected; if(selected<0||actual<0||actual>=queue.Count)return; queue.RemoveAt(actual); RefreshQueue(); }
        private void MoveQueued(int direction){ int selected=Find<ListBox>("Queue").SelectedIndex; int actual=queueIndex+1+selected,next=actual+direction; if(selected<0||next<=queueIndex||next>=queue.Count)return; Track item=queue[actual]; queue.RemoveAt(actual); queue.Insert(next,item); RefreshQueue(); Find<ListBox>("Queue").SelectedIndex=selected+direction; }
        internal void PlayAt(int index)
        {
            if (index < 0 || index >= queue.Count) return;
            opening = false; media.Close(); ready = false; playing = false; queueIndex = index; current = queue[index]; waveVersion++; if (liveWave != null) liveWave.Dispose();
            Text("NowTitle", current.Title); Text("NowDetail", current.Detail + " · " + current.Format); Text("PlayerTitle", current.Title); Text("PlayerDetail", current.Detail); Text("Elapsed", "0:00"); Text("Duration", "0:00");
            AutoFindLyrics(current);
            UpdateCover();
            Motion.Enter(Find<Border>("RecordArt"), -18, 0);
            if (SystemParameters.ClientAreaAnimation) Find<TextBlock>("NowTitle").BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(240)));
            updatingSeek = true; Find<Slider>("Seek").Value = 0; Find<Slider>("Seek").Maximum = 1; updatingSeek = false;
             ResetLiveWave(); Text("WaveLabel", "Preparing waveform…"); RefreshQueue(); UpdatePlayButton();
            if (!LocalFiles.IsLocal(current.Path) || !File.Exists(current.Path)) { Status("This local file is unavailable. Reconnect its drive or add it again from its new location."); return; }
            try { opening = true; playing = true; string openingPath=current.Path; int openingVersion=waveVersion; UpdatePlayButton(); Status("Opening local audio…"); Window.Dispatcher.BeginInvoke(new Action(delegate{if(closed||openingVersion!=waveVersion)return;OpenCurrentAudio(openingPath,openingVersion,0,true);})); var ignored = LoadWave(current.Path, waveVersion); }
            catch (Exception e) { opening = false; playing = false; UpdatePlayButton(); Status("Unable to open this track: " + e.Message); }
        }
        private void OpenCurrentAudio(string path,int version,double position,bool resume)
        {
            if(closed||version!=waveVersion||current==null)return;
            playing=resume;if(!media.Open(new Uri(path,UriKind.Absolute),Data.EqualizerEnabled,Data.EqualizerGains,Data.AudioOutputId))return;
            if(closed||version!=waveVersion){media.Close();return;}opening=false;ready=true;media.Volume=Data.Volume;Find<Slider>("Volume").Value=Data.Volume;Find<Slider>("Seek").Maximum=Math.Max(1,media.Duration.TotalSeconds);Text("Duration",FormatTime(Find<Slider>("Seek").Maximum));if(position>0)media.Position=TimeSpan.FromSeconds(Math.Min(position,media.Duration.TotalSeconds));if(resume)media.Play();else media.Pause();UpdateTransport();UpdatePlayButton();Status("Playing from your selected output · "+current.Format);
        }
        internal void ChangeAudioOutput(string id)
        {
            Data.AudioOutputId=id??"";Save();if(current==null||(!ready&&!opening))return;double position=ready?media.Position.TotalSeconds:0;bool resume=playing;int version=waveVersion;string path=current.Path;opening=true;ready=false;media.Close();UpdatePlayButton();OpenCurrentAudio(path,version,position,resume);
        }
        private Task LoadWave(string path, int version)
        {
            liveWave = new LiveWaveSource(path); liveWave.Sample(0); return Task.FromResult(0);
        }
        private void LayoutWave() { DrawLiveWave(lastWave); }
        internal void SeekWaveform(double fraction) { if (ready && !Double.IsNaN(fraction)) media.Position = TimeSpan.FromSeconds(Math.Max(0, Math.Min(1, fraction)) * Find<Slider>("Seek").Maximum); }
        internal void TogglePlay()
        {
            if (opening) return;
            if (current == null) { var selected = Find<ListBox>("Tracks").SelectedItem as Track; if (selected != null) StartQueue(visible, selected); return; }
            if (!ready && !playing) { PlayAt(queueIndex); return; }
            playing = !playing; if (playing) media.Play(); else media.Pause(); UpdatePlayButton();
        }
        internal void Next(bool ended)
        {
            if (queue.Count == 0) return;
            int next = queueIndex + 1;
            if (next >= queue.Count) { if (ended||repeat==1) next=0;else return; }
            if (shuffle && next < queue.Count - 1) { int pick = random.Next(next, queue.Count); Track temp = queue[next]; queue[next] = queue[pick]; queue[pick] = temp; }
            PlayAt(next);
        }
        private void RefreshQueue() { Find<ListBox>("Queue").ItemsSource = queue.Skip(queueIndex + 1).ToList(); Text("QueueCount", Math.Max(0, queue.Count - queueIndex - 1) + " tracks"); }
        private void UpdatePlayButton() { UpdateTransport(); Find<Button>("PlayButton").Content = playing ? "Ⅱ" : "▶"; UpdateRecordSpin(); if (!playing) UpdateLiveWave(); }
        private void UpdateCover()
        {
            UpdateExpandedTitles();
            var bitmap = SongTags.ImageFromData(current == null ? null : current.CoverData);
            Find<Image>("CoverArt").Source = bitmap;
            Find<Image>("CoverArt").Visibility = Visibility.Collapsed;
            Find<Ellipse>("RecordImage").Fill = bitmap == null ? null : new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            Find<Image>("RecordLogo").Visibility = bitmap==null?Visibility.Visible:Visibility.Collapsed;
            Find<Ellipse>("RecordLabel").Visibility = bitmap==null?Visibility.Visible:Visibility.Collapsed;
            Find<Viewbox>("DefaultArt").Visibility = Visibility.Visible;
        }
        private async void EditSelectedTrack()
        {
            var selected = Selected();
            if (selected.Count != 1) { Status("Select one song to edit its details and cover art."); return; }
            if (Downloads.Busy || importing) { Status("Wait for the current import or download to finish first."); return; }
            Track track = selected[0];
            if (!SongTags.CanEdit(track.Path)) { Status("File editing supports MP3, FLAC, and M4A. Other formats can still be played."); return; }
            bool wasCurrent = current == track; double position = wasCurrent && ready ? media.Position.TotalSeconds : 0; bool resume = wasCurrent && playing;
            var editor = new SongEditor(Window, track, delegate { if (wasCurrent) { if (liveWave != null) liveWave.Dispose(); opening = false; media.Close(); ready = false; playing = false; UpdatePlayButton(); } });
            editor.ShowDialog(); RefreshTracks(); Save();
            if (wasCurrent && editor.ReleasedPlayback)
            {
                PlayAt(queueIndex); int version = waveVersion;
                for (int i = 0; i < 100 && !ready && version == waveVersion && !closed; i++) await Task.Delay(50);
                if (ready && version == waveVersion && !closed) {media.Position = TimeSpan.FromSeconds(position);if(!resume){playing=false;media.Pause();UpdatePlayButton();}}
            }
        }
        private void StopAndClear() { if (liveWave != null) liveWave.Dispose(); opening = false; media.Close(); ready = false; playing = false; current = null; queue.Clear(); queueIndex = -1; waveVersion++;  UpdateCover(); ResetLiveWave(); UpdatePlayButton(); RefreshQueue(); Text("NowTitle", "Find your quiet."); Text("NowDetail", "Choose something worth listening to."); Text("PlayerTitle", "Nothing playing. Yet."); Text("Elapsed", "0:00"); Text("Duration", "0:00"); updatingSeek = true; Find<Slider>("Seek").Value = 0; updatingSeek = false; }
        private static string FormatTime(double seconds) { return TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss"); }
        private Window Dialog(string title, int width, int height) { return new Window { Owner = Window, Title = title, Width = width, Height = height, Background = Window.Background, Foreground = Window.Foreground, FontFamily = Window.FontFamily, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Resources = Window.Resources }; }
        private static StackPanel DialogPanel(Window dialog, string description) { var panel = new StackPanel { Margin = new Thickness(25) }; panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 14 }); dialog.Content = panel; return panel; }
        internal void ShowKrazyGreeting()
        {
            var dialog=Dialog(StartupGreeting,380,190);var panel=DialogPanel(dialog,StartupGreeting);
            var close=new Button{Content="Howdy!",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,24,0,0),Padding=new Thickness(18,8,18,8)};
            close.Click+=delegate{dialog.DialogResult=true;};panel.Children.Add(close);dialog.Loaded+=delegate{close.Focus();};dialog.ShowDialog();
        }
        private string Prompt(string title, string description, string initial)
        {
            var dialog = Dialog(title, 420, 270); var panel = DialogPanel(dialog, description);
            var input = new TextBox { Text = initial, MaxLength = 64, Margin = new Thickness(0, 18, 0, 18) }; panel.Children.Add(input);
            var accept = new Button { Content = "Save playlist", IsDefault = true }; accept.Click += delegate { if (!String.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; }; panel.Children.Add(accept);
            dialog.Loaded += delegate { input.Focus(); input.SelectAll(); }; return dialog.ShowDialog() == true ? input.Text.Trim() : null;
        }
    }
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if(args.Length>1&&args[0]=="--apply-update")return DigitoneUpdates.ApplyUpdate(args[1]);
#if !RELEASE
            if (args.Length > 0 && args[0] == "--cover-browser-test") return CoverBrowserTest.Run();
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args.Length > 1 ? args[1] : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestResults"));
#endif
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string healthMarker=args.Length>1&&args[0]=="--update-health"?args[1]:null,stagingRoot=args.Length>2&&args[0]=="--update-health"?args[2]:null;
            bool created;
            string id = Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace('/', '_').Replace('+', '-');
            using (var mutex = new Mutex(true, "Local\\Digitone_" + id, out created))
            {
                if (!created) { MessageBox.Show("Digitone is already running from this folder.", "Digitone"); return 0; }
                try
                {
                    var app = new Application();
                    Directory.CreateDirectory(PlayerApp.DigiMusicFolder);
                    var store = new LibraryStore(System.IO.Path.Combine(root, "Data", "library.json"));
                    var player = new PlayerApp(store, store.Load());
                    player.Window.Loaded += async delegate {
                        if(!String.IsNullOrWhiteSpace(healthMarker))try{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(healthMarker));File.WriteAllText(healthMarker,AppInfo.Version);if(!String.IsNullOrWhiteSpace(stagingRoot))ThreadPool.QueueUserWorkItem(delegate{Thread.Sleep(5000);try{string full=System.IO.Path.GetFullPath(stagingRoot),safe=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Digitone","Updates")+System.IO.Path.DirectorySeparatorChar;if(full.StartsWith(safe,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(full))Directory.Delete(full,true);}catch{}});}catch{}
                        if(!player.Data.SetupCompleted) {
                            if(player.CreateFirstRun().ShowDialog()!=true) { player.Window.Close(); return; }
                            if(!String.IsNullOrEmpty(player.Data.MainMusicFolder)) await player.Import(new[]{player.Data.MainMusicFolder});
                        }
                        player.ShowKrazyGreeting();
                    };
                    app.Run(player.Window); return 0;
                }
                catch (Exception e) { MessageBox.Show("Digitone could not start. Your music is untouched.\n\n" + e.Message + "\n\nIf your library file is damaged, a previous copy may be in Data\\library.json.bak.", "Digitone", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
            }
        }
    }
}




