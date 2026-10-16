using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Digitone
{
    // Recognition results by audio hash, kept in userdata\cache so they survive moves and work offline.
    internal sealed class RecognitionCache
    {
        private readonly string path; private readonly object gate = new object(); private Dictionary<string, RecognitionResult> items;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        internal RecognitionCache(string path) { this.path = path; }
        private void Ensure()
        {
            if (items != null) return;
            try { if (File.Exists(path)) items = json.Deserialize<Dictionary<string, RecognitionResult>>(File.ReadAllText(path)); } catch { items = null; }
            if (items == null) items = new Dictionary<string, RecognitionResult>(StringComparer.OrdinalIgnoreCase);
        }
        internal RecognitionResult Get(string hash) { lock (gate) { Ensure(); RecognitionResult result; return !String.IsNullOrEmpty(hash) && items.TryGetValue(hash, out result) ? result : null; } }
        internal void Put(RecognitionResult result)
        {
            if (result == null || String.IsNullOrEmpty(result.AudioHash)) return;
            lock (gate)
            {
                Ensure(); items[result.AudioHash] = result;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp"; File.WriteAllText(temporary, json.Serialize(items));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
        }
    }

    public sealed partial class PlayerApp
    {
        private sealed class RecognitionJob { internal Track Track; internal SongHints Hints; internal bool Manual; }
        private readonly Queue<RecognitionJob> recognitionJobs = new Queue<RecognitionJob>();
        private readonly HashSet<Track> pendingTagWrites = new HashSet<Track>();
        private readonly CancellationTokenSource recognitionCancel = new CancellationTokenSource();
        private TaskCompletionSource<bool> recognitionIdle = CompletedIdle();
        private bool recognitionRunning; private RecognitionCache recognitionCache; private DispatcherTimer tagWriteTimer;
        private static TaskCompletionSource<bool> CompletedIdle() { var done = new TaskCompletionSource<bool>(); done.SetResult(true); return done; }
        internal Task RecognitionIdle { get { return recognitionIdle.Task; } }
        internal string AcoustIdKeyInUse { get { return String.IsNullOrWhiteSpace(Data.AcoustIdKey) ? AppInfo.AcoustIdKey : Data.AcoustIdKey.Trim(); } }
        internal RecognitionCache RecognitionResults { get { return recognitionCache ?? (recognitionCache = new RecognitionCache(Path.Combine(PortableFolders.Current.Cache, "recognition.json"))); } }
        internal int ReviewCount { get { return Data.Tracks.Count(t => t.RecognitionStatus == "Review"); } }

        private void SetupRecognition()
        {
            Find<Button>("DownloadReviewSongs").Click += delegate { ShowRecognitionReview(null); };
            tagWriteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            tagWriteTimer.Tick += async delegate
            {
                foreach (var track in pendingTagWrites.ToList()) if (track != current && !Downloads.Busy && Data.Tracks.Contains(track)) Status(await WriteRecognitionTags(track));
                foreach (var track in pendingArtWrites.ToList()) if (track != current && !Downloads.Busy && Data.Tracks.Contains(track)) await EmbedArt(track);
                if (pendingTagWrites.Count == 0 && pendingArtWrites.Count == 0) tagWriteTimer.Stop();
            };
            Window.Closed += delegate { recognitionCancel.Cancel(); lyricsSweepCancel.Cancel(); tagWriteTimer.Stop(); };
            UpdateRecognitionBadge();
            // Lyrics are automatic: switch it on once for libraries made before this was the default, then keep the user's choice.
            if (!Data.AutoLyricsDefaultApplied) { Data.AutoLyrics = true; Data.AutoLyricsDefaultApplied = true; Save(); }
            if (!Environment.GetCommandLineArgs().Any(a => a == "--self-test"))
                Window.Loaded += async delegate
                {
                    await Task.Delay(5000); await BackgroundWorkAllowed; if (closed || !Data.SetupCompleted) return;
                    QueueUnidentified(); // identified songs then look up their own lyrics and art with the corrected names
                    await Task.Delay(3000); if (!closed) QueueLyricsLookup(Data.Tracks);
                };
        }
        private void UpdateRecognitionBadge()
        {
            var button = Find<Button>("DownloadReviewSongs"); int count = ReviewCount;
            button.Content = "Review songs (" + count + ")"; button.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        // Songs never identified before (including ones already in the library) are identified in the background.
        // 85%+ matches are applied; details you edited yourself ("Kept") and confirmed songs are never touched.
        internal int QueueUnidentified()
        {
            if (Data.AutoRecognize == false) return 0;
            var todo = Data.Tracks.Where(t => String.IsNullOrEmpty(t.RecognitionStatus) && File.Exists(t.Path) && !recognitionJobs.Any(j => j.Track == t)).ToList();
            if (todo.Count > 0) QueueRecognition(todo, null, null, null, false);
            return todo.Count;
        }
        private void IdentifySelected()
        {
            var selected = Selected();
            if (selected.Count == 0) { Status("Select the songs you want to identify."); return; }
            QueueRecognition(selected, null, null, null, true);
        }
        internal void QueueRecognition(IEnumerable<Track> tracks, IDictionary<string, SongHints> hints, string spotifyTitle, string spotifyArtist, bool manual)
        {
            if (!manual && Data.AutoRecognize == false) return;
            foreach (var track in tracks)
            {
                if (track == null || !File.Exists(track.Path) || recognitionJobs.Any(j => j.Track == track && (!manual || j.Manual))) continue;
                SongHints hint = null; if (hints != null) hints.TryGetValue(track.Path, out hint);
                hint = hint ?? new SongHints();
                if (!String.IsNullOrWhiteSpace(spotifyTitle)) { hint.SpotifyTitle = spotifyTitle; hint.SpotifyArtist = spotifyArtist; }
                hint.FileName = Path.GetFileName(track.Path); hint.TagTitle = track.SongTitle; hint.TagArtist = track.Artist; hint.TagAlbum = track.Album;
                recognitionJobs.Enqueue(new RecognitionJob { Track = track, Hints = hint, Manual = manual });
            }
            if (!recognitionRunning && recognitionJobs.Count > 0) PumpRecognition();
        }
        private async void PumpRecognition()
        {
            recognitionRunning = true; if (recognitionIdle.Task.IsCompleted) recognitionIdle = new TaskCompletionSource<bool>();
            var manualDone = new List<Track>(); int applied = 0, review = 0;
            try
            {
                while (recognitionJobs.Count > 0 && !closed)
                {
                    var job = recognitionJobs.Dequeue(); var track = job.Track; string key = AcoustIdKeyInUse, path = track.Path; var token = recognitionCancel.Token;
                    if (!Data.Tracks.Contains(track)) continue;
                    RecognitionResult result;
                    try { result = await Task.Run(delegate { return Recognize(path, job.Hints, job.Manual, key, token); }); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception error) { result = new RecognitionResult { Error = error.Message }; }
                    if (closed) break;
                    if (!String.IsNullOrEmpty(result.AudioHash)) track.AudioHash = result.AudioHash;
                    string outcome = await Decide(track, result, job.Manual);
                    if (outcome == "Applied") applied++; else if (outcome == "Review") review++;
                    if (outcome != "Applied") QueueLyricsLookup(new[] { track }); // identified songs search on their own; the rest try with what they have
                    if (job.Manual) manualDone.Add(track);
                    Save(); RefreshTracks(); UpdateRecognitionBadge();
                }
            }
            finally { recognitionRunning = false; recognitionIdle.TrySetResult(true); }
            if (closed) return;
            if (applied + review > 0) Status((applied > 0 ? "Identified " + applied + (applied == 1 ? " song" : " songs") : "") + (applied > 0 && review > 0 ? " · " : "") + (review > 0 ? review + " to review in Downloads" : "") + ".");
            if (manualDone.Count > 0) ShowRecognitionReview(manualDone);
        }
        // Runs off the UI thread: reuse a cached result unless asked again, otherwise fingerprint and look up.
        private RecognitionResult Recognize(string path, SongHints hints, bool fresh, string key, CancellationToken token)
        {
            string hash = null; try { hash = SongTags.AudioHash(path); } catch { }
            var cached = RecognitionResults.Get(hash);
            if (cached != null && cached.Hints != null && hints != null) { if (String.IsNullOrEmpty(hints.Title) && String.IsNullOrEmpty(hints.Description)) { cached.Hints.TagTitle = hints.TagTitle; cached.Hints.TagArtist = hints.TagArtist; cached.Hints.TagAlbum = hints.TagAlbum; cached.Hints.FileName = hints.FileName; if (hints.SpotifyTitle != null) { cached.Hints.SpotifyTitle = hints.SpotifyTitle; cached.Hints.SpotifyArtist = hints.SpotifyArtist; } hints = cached.Hints; } }
            if (cached != null && !fresh && String.IsNullOrEmpty(cached.Error) && cached.Candidates.Count > 0) return cached;
            var result = SongRecognizer.Recognize(path, hints, key, token, cached);
            result.AudioHash = hash;
            // Offline or key problems should not throw away a better earlier answer.
            if (cached != null && cached.Best != null && (result.Best == null || result.Best.Confidence < cached.Best.Confidence) && !String.IsNullOrEmpty(result.Error)) { cached.Error = result.Error; return cached; }
            RecognitionResults.Put(result);
            return result;
        }
        private async Task<string> Decide(Track track, RecognitionResult result, bool manual)
        {
            var best = result.Best;
            if (best == null) { track.RecognitionStatus = "NoMatch"; track.RecognitionConfidence = 0; track.RecognitionSummary = result.Error ?? "No match was found."; return "NoMatch"; }
            if (!manual && (track.RecognitionStatus == "Confirmed" || track.RecognitionStatus == "Kept")) return "Skipped";
            if (!manual && best.Confidence >= SongRecognizer.HighConfidence && Data.AutoApplyRecognized != false)
            {
                Status(await ApplyRecognition(track, best, "Applied", false));
                return "Applied";
            }
            track.RecognitionStatus = "Review"; track.RecognitionConfidence = best.Confidence;
            track.RecognitionSummary = "Best guess: " + best.Title + " — " + best.Artist + " (" + best.Confidence + "%, " + best.Source + ")";
            return "Review";
        }
        // Puts recognized details into the library, then into the file's tags (later if the song is playing).
        internal async Task<string> ApplyRecognition(Track track, RecognitionCandidate candidate, string status, bool replaceEmpty)
        {
            track.SongTitle = candidate.Title; track.Artist = candidate.Artist;
            if (replaceEmpty || !String.IsNullOrWhiteSpace(candidate.Album)) track.Album = candidate.Album ?? "";
            if (replaceEmpty || !String.IsNullOrWhiteSpace(candidate.AlbumArtist)) track.AlbumArtist = candidate.AlbumArtist ?? "";
            if (replaceEmpty || !String.IsNullOrWhiteSpace(candidate.Year)) track.Year = candidate.Year ?? "";
            if (replaceEmpty || !String.IsNullOrWhiteSpace(candidate.TrackNumber)) track.TrackNumber = candidate.TrackNumber ?? "";
            track.MusicBrainzRecordingId = candidate.RecordingId; track.MusicBrainzReleaseId = candidate.ReleaseId; track.MusicBrainzReleaseGroupId = candidate.ReleaseGroupId; track.MusicBrainzArtistId = candidate.ArtistId; track.AcoustId = candidate.AcoustId;
            track.RecognitionStatus = status; track.RecognitionConfidence = candidate.Confidence;
            track.RecognitionSummary = (status == "Confirmed" ? "Confirmed by you" : "Identified automatically") + " · " + candidate.Source + " · " + candidate.Confidence + "%";
            Save(); RefreshTracks(); UpdateRecognitionBadge();
            string written = await WriteRecognitionTags(track);
            // Now that the song is identified, look for its lyrics with the correct title, artist, and length.
            if (String.IsNullOrWhiteSpace(track.Lyrics) && (track.LyricsStatus == null || track.LyricsStatus == "NotFound" || track.LyricsStatus == "Review"))
            {
                if (track.LyricsStatus == "Review" || track.LyricsStatus == "NotFound") track.LyricsStatus = null;
                lyricsAttempted.Remove(track.Path); AutoFindLyrics(track, false);
            }
            // The exact album is known now, so look for its cover (never replacing art the user chose).
            if (String.IsNullOrEmpty(track.CoverData) && !UserOwnsArt(track)) { track.ArtworkCheckedUtc = null; QueueLyricsLookup(new[] { track }); }
            return "“" + track.Title + "” by " + track.Artist + ": " + written;
        }
        private async Task<string> WriteRecognitionTags(Track track)
        {
            if (!SongTags.CanEdit(track.Path) || !File.Exists(track.Path)) return "saved in your library (this file type keeps details in the library only).";
            if (track == current) { pendingTagWrites.Add(track); if (tagWriteTimer != null) tagWriteTimer.Start(); return "saved; the file's tags are written once it stops playing."; }
            var snapshot = new Track { Path = track.Path, SongTitle = track.SongTitle, Artist = track.Artist, Album = track.Album, AlbumArtist = track.AlbumArtist, Year = track.Year, TrackNumber = track.TrackNumber, MusicBrainzRecordingId = track.MusicBrainzRecordingId, MusicBrainzReleaseId = track.MusicBrainzReleaseId, MusicBrainzReleaseGroupId = track.MusicBrainzReleaseGroupId, MusicBrainzArtistId = track.MusicBrainzArtistId, AcoustId = track.AcoustId };
            try
            {
                await Task.Run(delegate
                {
                    var onDisk = new Track { Path = snapshot.Path }; SongTags.ReadInto(onDisk);
                    onDisk.SongTitle = snapshot.SongTitle ?? ""; onDisk.Artist = snapshot.Artist ?? ""; onDisk.Album = snapshot.Album ?? ""; onDisk.AlbumArtist = snapshot.AlbumArtist ?? ""; onDisk.Year = snapshot.Year ?? ""; onDisk.TrackNumber = snapshot.TrackNumber ?? "";
                    onDisk.MusicBrainzRecordingId = snapshot.MusicBrainzRecordingId; onDisk.MusicBrainzReleaseId = snapshot.MusicBrainzReleaseId; onDisk.MusicBrainzReleaseGroupId = snapshot.MusicBrainzReleaseGroupId; onDisk.MusicBrainzArtistId = snapshot.MusicBrainzArtistId; onDisk.AcoustId = snapshot.AcoustId;
                    SongTags.Save(onDisk, null, false, SongTags.IdentityTags(onDisk));
                });
                pendingTagWrites.Remove(track);
                return "saved to the file's tags and your library.";
            }
            catch (Exception error) { pendingTagWrites.Remove(track); return "saved in your library, but the file's tags could not be updated: " + error.Message; }
        }

        private StackPanel RecognitionSettingsPanel()
        {
            var panel = new StackPanel { Name = "RecognitionSettingsSection" };
            panel.Children.Add(SettingsText("Song recognition", 16));
            panel.Children.Add(SettingsText("Digitone identifies your songs (new downloads and songs already in your library) from the audio itself (AcoustID + MusicBrainz), so wrong YouTube titles get fixed automatically. Only an audio fingerprint and song names are sent, never the file. Results are cached in userdata, so they keep working offline.", 12, true));
            var auto = new CheckBox { Name = "AutoRecognizeToggle", Content = "Identify songs automatically (downloads and songs already in your library)", IsChecked = Data.AutoRecognize != false, Margin = new Thickness(0, 10, 0, 4) };
            var apply = new CheckBox { Name = "AutoApplyRecognizedToggle", Content = "Apply matches of " + SongRecognizer.HighConfidence + "% or higher without asking", IsChecked = Data.AutoApplyRecognized != false, Margin = new Thickness(0, 0, 0, 4) };
            panel.Children.Add(auto); panel.Children.Add(apply);
            panel.Children.Add(SettingsText("Anything below " + SongRecognizer.HighConfidence + "% always waits for you in Review songs.", 11, true));
            auto.Click += delegate { Data.AutoRecognize = auto.IsChecked == true; Save(); if (Data.AutoRecognize == true) QueueUnidentified(); };
            apply.Click += delegate { Data.AutoApplyRecognized = apply.IsChecked == true; Save(); };
            var keyLabel = SettingsText("AcoustID application key", 12); keyLabel.Margin = new Thickness(0, 12, 0, 4); panel.Children.Add(keyLabel);
            var key = new TextBox { Name = "AcoustIdKeyBox", Text = Data.AcoustIdKey ?? "", MaxLength = 64, MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(key);
            var keyStatus = SettingsText("", 11, true); panel.Children.Add(keyStatus);
            Action showKey = delegate { keyStatus.Text = !String.IsNullOrWhiteSpace(Data.AcoustIdKey) ? "Using your key." : !String.IsNullOrWhiteSpace(AppInfo.AcoustIdKey) ? "Leave empty to use Digitone's built-in key." : "No key yet. Get a free one at acoustid.org/new-application. Until then Digitone uses YouTube info checked against MusicBrainz, and every result waits for review."; };
            key.LostFocus += delegate { string value = key.Text.Trim(); if (value.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9]{4,64}$")) { keyStatus.Text = "That does not look like an AcoustID key (letters and numbers only)."; return; } Data.AcoustIdKey = value.Length == 0 ? null : value; Save(); showKey(); };
            showKey();
            var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var review = new Button { Content = "Review songs…", Margin = new Thickness(0, 0, 8, 0) }; var all = new Button { Content = "Identify my whole library…" };
            buttons.Children.Add(review); buttons.Children.Add(all); panel.Children.Add(buttons);
            review.Click += delegate { ShowRecognitionReview(null); };
            all.Click += delegate
            {
                var todo = Data.Tracks.Where(t => String.IsNullOrEmpty(t.RecognitionStatus) || t.RecognitionStatus == "NoMatch").ToList();
                if (todo.Count == 0) { MessageBox.Show(Window, "Every song has already been identified or reviewed.", "Song recognition"); return; }
                if (MessageBox.Show(Window, "Identify " + todo.Count + " songs in the background? This takes about 2 seconds per song. Matches of " + SongRecognizer.HighConfidence + "%+ are applied" + (Data.AutoApplyRecognized != false ? "" : " only after review") + "; the rest wait in Review songs.", "Song recognition", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                QueueRecognition(todo, null, null, null, false);
            };
            return panel;
        }

        private sealed class ReviewItem
        {
            internal Track Track;
            public override string ToString()
            {
                string mark = Track.RecognitionStatus == "Review" ? (Track.RecognitionConfidence >= SongRecognizer.MediumConfidence ? "◐ " : "⚠ ") : Track.RecognitionStatus == "Confirmed" || Track.RecognitionStatus == "Applied" ? "✓ " : Track.RecognitionStatus == "Kept" ? "• " : "? ";
                return mark + (Track.RecognitionConfidence > 0 && Track.RecognitionStatus == "Review" ? Track.RecognitionConfidence + "% · " : "") + Track.Title + (String.IsNullOrWhiteSpace(Track.Artist) ? "" : " — " + Track.Artist);
            }
        }
        private Window recognitionReview;
        internal Window ShowRecognitionReview(List<Track> focus)
        {
            var tracks = focus ?? Data.Tracks.Where(t => t.RecognitionStatus == "Review" || t.RecognitionStatus == "NoMatch").OrderBy(t => t.RecognitionConfidence).ToList();
            if (tracks.Count == 0) { MessageBox.Show(Window, "No songs are waiting for review.", "Review songs"); return null; }
            if (recognitionReview != null) recognitionReview.Close();
            var dialog = new Window { Owner = Window, Title = "Review songs · Digitone", Width = 1040, Height = 720, MinWidth = 760, MinHeight = 520, Resources = Window.Resources, FontFamily = Window.FontFamily, Icon = Window.Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            recognitionReview = dialog; dialog.Closed += delegate { if (recognitionReview == dialog) recognitionReview = null; };
            dialog.SetResourceReference(Control.BackgroundProperty, "B161917"); dialog.SetResourceReference(Control.ForegroundProperty, "BEEEFE8");
            var root = new Grid { Margin = new Thickness(20) }; dialog.Content = root;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) }); root.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) }; root.Children.Add(left);
            var leftHeader = SettingsText("Songs to check", 18); DockPanel.SetDock(leftHeader, Dock.Top); left.Children.Add(leftHeader);
            var legend = SettingsText("⚠ low · ◐ medium · ✓ done · • kept as is", 11, true); DockPanel.SetDock(legend, Dock.Top); left.Children.Add(legend);
            var list = new ListBox { Name = "ReviewList", Margin = new Thickness(0, 8, 0, 0) }; left.Children.Add(list);
            var items = tracks.Select(t => new ReviewItem { Track = t }).ToList(); list.ItemsSource = items;
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); root.Children.Add(scroll);
            var body = new StackPanel(); scroll.Content = body;
            var heading = SettingsText("", 20); var file = SettingsText("", 11, true); var source = SettingsText("", 12, true);
            body.Children.Add(heading); body.Children.Add(file); body.Children.Add(source);
            body.Children.Add(Spaced(SettingsText("Detected matches", 12), 14));
            var candidates = new ComboBox { Name = "CandidateChoice", Margin = new Thickness(0, 4, 0, 6) }; body.Children.Add(candidates);
            var confidence = SettingsText("", 22); var notes = SettingsText("", 12, true); body.Children.Add(confidence); body.Children.Add(notes);
            var form = new Grid { Margin = new Thickness(0, 14, 0, 0) }; body.Children.Add(form);
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); form.ColumnDefinitions.Add(new ColumnDefinition());
            string[] labels = { "Title", "Artist", "Album", "Album artist", "Year", "Track #" }; var boxes = new TextBox[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = SettingsText(labels[i], 12); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(label, i); form.Children.Add(label);
                boxes[i] = new TextBox { Name = "Review" + labels[i].Replace(" ", "").Replace("#", "Number"), MaxLength = 250, Margin = new Thickness(0, 3, 0, 3) }; Grid.SetRow(boxes[i], i); Grid.SetColumn(boxes[i], 1); form.Children.Add(boxes[i]);
            }
            var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; body.Children.Add(actions);
            var applyButton = new Button { Name = "ApplyRecognition", Content = "✓ Apply these details", Margin = new Thickness(0, 0, 8, 8) };
            var keepButton = new Button { Name = "KeepCurrent", Content = "Keep current details", Margin = new Thickness(0, 0, 8, 8) };
            var searchButton = new Button { Content = "Search MusicBrainz for the title/artist above", Margin = new Thickness(0, 0, 8, 8) };
            var againButton = new Button { Content = "Identify again", Margin = new Thickness(0, 0, 8, 8) };
            actions.Children.Add(applyButton); actions.Children.Add(keepButton); actions.Children.Add(searchButton); actions.Children.Add(againButton);
            var status = SettingsText("", 12, true); body.Children.Add(status);

            ReviewItem selected = null; RecognitionResult result = null; bool busy = false;
            Action<RecognitionCandidate> fill = delegate(RecognitionCandidate c)
            {
                string[] values = c == null ? new[] { selected.Track.SongTitle ?? selected.Track.Title, selected.Track.Artist, selected.Track.Album, selected.Track.AlbumArtist, selected.Track.Year, selected.Track.TrackNumber } : new[] { c.Title, c.Artist, c.Album, c.AlbumArtist, c.Year, c.TrackNumber };
                for (int i = 0; i < boxes.Length; i++) boxes[i].Text = values[i] ?? "";
                if (c == null) { confidence.Text = ""; notes.Text = ""; return; }
                confidence.Text = c.Confidence + "% · " + (c.Confidence >= SongRecognizer.HighConfidence ? "high confidence" : c.Confidence >= SongRecognizer.MediumConfidence ? "medium: please check" : "low: check carefully");
                confidence.Foreground = c.Confidence >= SongRecognizer.HighConfidence ? Brushes.MediumSeaGreen : c.Confidence >= SongRecognizer.MediumConfidence ? Brushes.Goldenrod : Brushes.IndianRed;
                notes.Text = String.Join("\n", c.Notes.Select(n => "• " + n));
            };
            Action<List<RecognitionCandidate>> showCandidates = delegate(List<RecognitionCandidate> list2)
            {
                candidates.ItemsSource = list2; candidates.IsEnabled = list2.Count > 0;
                if (list2.Count > 0) candidates.SelectedIndex = 0; else fill(null);
            };
            Action load = delegate
            {
                selected = list.SelectedItem as ReviewItem; if (selected == null) return;
                var track = selected.Track; result = RecognitionResults.Get(track.AudioHash);
                heading.Text = track.Title + (String.IsNullOrWhiteSpace(track.Artist) ? "" : " — " + track.Artist);
                file.Text = Path.GetFileName(track.Path) + (result != null && result.FileDuration > 0 ? " · " + SongRecognizer.Clock(result.FileDuration) : "");
                var hints = result != null ? result.Hints : null;
                source.Text = hints != null && !String.IsNullOrWhiteSpace(hints.Title) ? "YouTube said: “" + hints.Title + "”" + (String.IsNullOrWhiteSpace(hints.Channel ?? hints.Uploader) ? "" : " · channel " + (hints.Channel ?? hints.Uploader)) : hints != null && !String.IsNullOrWhiteSpace(hints.SpotifyTitle) ? "Spotify said: " + hints.SpotifyTitle + " — " + hints.SpotifyArtist : "";
                if (result != null && !String.IsNullOrWhiteSpace(result.Error)) source.Text += (source.Text.Length > 0 ? "\n" : "") + "Note: " + result.Error;
                status.Text = track.RecognitionSummary ?? "";
                showCandidates(result != null ? result.Candidates : new List<RecognitionCandidate>());
                if (result == null) status.Text = "This song has not been identified yet. Choose Identify again.";
            };
            Action next = delegate
            {
                list.Items.Refresh();
                int index = list.SelectedIndex; var following = items.Skip(index + 1).Concat(items.Take(index)).FirstOrDefault(i => i.Track.RecognitionStatus == "Review" || i.Track.RecognitionStatus == "NoMatch" || String.IsNullOrEmpty(i.Track.RecognitionStatus));
                if (following != null) list.SelectedItem = following; else load();
            };
            list.SelectionChanged += delegate { load(); };
            candidates.SelectionChanged += delegate { fill(candidates.SelectedItem as RecognitionCandidate); };
            Action<bool> setBusy = delegate(bool value) { busy = value; foreach (var button in new[] { applyButton, keepButton, searchButton, againButton }) button.IsEnabled = !value; list.IsEnabled = !value; };
            applyButton.Click += async delegate
            {
                if (selected == null || busy) return;
                if (String.IsNullOrWhiteSpace(boxes[0].Text) || String.IsNullOrWhiteSpace(boxes[1].Text)) { status.Text = "Enter at least a title and an artist."; return; }
                var chosen = candidates.SelectedItem as RecognitionCandidate;
                var final = new RecognitionCandidate { Title = boxes[0].Text.Trim(), Artist = boxes[1].Text.Trim(), Album = boxes[2].Text.Trim(), AlbumArtist = boxes[3].Text.Trim(), Year = boxes[4].Text.Trim(), TrackNumber = boxes[5].Text.Trim(), Source = chosen != null ? chosen.Source : "Typed by you", Confidence = 100 };
                bool sameSong = chosen != null && String.Equals(chosen.Title ?? "", final.Title, StringComparison.CurrentCultureIgnoreCase) && String.Equals(chosen.Artist ?? "", final.Artist, StringComparison.CurrentCultureIgnoreCase);
                if (sameSong) { final.RecordingId = chosen.RecordingId; final.ReleaseId = String.Equals(chosen.Album ?? "", final.Album, StringComparison.CurrentCultureIgnoreCase) ? chosen.ReleaseId : null; final.ReleaseGroupId = final.ReleaseId != null ? chosen.ReleaseGroupId : null; final.ArtistId = chosen.ArtistId; final.AcoustId = chosen.AcoustId; }
                else if (chosen != null) final.Source = "Edited by you";
                setBusy(true); status.Text = "Saving…";
                try { status.Text = await ApplyRecognition(selected.Track, final, "Confirmed", true); next(); }
                finally { setBusy(false); }
            };
            keepButton.Click += delegate
            {
                if (selected == null || busy) return;
                selected.Track.RecognitionStatus = "Kept"; selected.Track.RecognitionSummary = "You kept the existing details."; Save(); UpdateRecognitionBadge(); status.Text = "Kept the current details."; next();
            };
            searchButton.Click += async delegate
            {
                if (selected == null || busy) return;
                if (String.IsNullOrWhiteSpace(boxes[0].Text)) { status.Text = "Type a title (and ideally an artist) first."; return; }
                string artist = boxes[1].Text.Trim(), title = boxes[0].Text.Trim(); double length = result != null ? result.FileDuration : 0;
                setBusy(true); status.Text = "Searching MusicBrainz…";
                try
                {
                    var found = await Task.Run(delegate { return SongRecognizer.Search(artist, title, length, recognitionCancel.Token); });
                    var merged = found.Concat(result != null ? result.Candidates : new List<RecognitionCandidate>()).GroupBy(c => c.RecordingId ?? Guid.NewGuid().ToString()).Select(g => g.OrderByDescending(c => c.Confidence).First()).ToList();
                    showCandidates(merged); status.Text = found.Count == 0 ? "MusicBrainz found nothing for that. Check the spelling, or type the details and Apply." : found.Count + " results from MusicBrainz. Pick one, check it, then Apply.";
                }
                catch (Exception error) { status.Text = "Search failed: " + error.Message; }
                finally { setBusy(false); }
            };
            againButton.Click += async delegate
            {
                if (selected == null || busy) return;
                var track = selected.Track; string key = AcoustIdKeyInUse, path = track.Path;
                var hints = result != null && result.Hints != null ? result.Hints : new SongHints { FileName = Path.GetFileName(path), TagTitle = track.SongTitle, TagArtist = track.Artist, TagAlbum = track.Album };
                setBusy(true); status.Text = "Identifying from the audio…";
                try
                {
                    var fresh = await Task.Run(delegate { return Recognize(path, hints, true, key, recognitionCancel.Token); });
                    if (!String.IsNullOrEmpty(fresh.AudioHash)) track.AudioHash = fresh.AudioHash;
                    if (track.RecognitionStatus != "Confirmed") { var best = fresh.Best; track.RecognitionStatus = best == null ? "NoMatch" : "Review"; track.RecognitionConfidence = best == null ? 0 : best.Confidence; }
                    Save(); load(); list.Items.Refresh(); UpdateRecognitionBadge();
                }
                catch (Exception error) { status.Text = "Could not identify: " + error.Message; }
                finally { setBusy(false); }
            };
            dialog.Loaded += delegate { list.SelectedIndex = 0; };
            NativeChrome.Apply(dialog);
            dialog.Show();
            return dialog;
        }
        private static FrameworkElement Spaced(FrameworkElement element, double top) { element.Margin = new Thickness(0, top, 0, 0); return element; }
    }
}
