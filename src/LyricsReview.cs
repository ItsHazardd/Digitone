using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Digitone
{
    public sealed partial class PlayerApp
    {
        // Playback helpers for the timing editor.
        internal double PositionOf(Track track) { return current == track && ready ? media.Position.TotalSeconds : -1; }
        internal async Task PlayFrom(Track track, double seconds)
        {
            if (current != track) { StartQueue(new[] { track }, track); for (int i = 0; i < 80 && !(ready && current == track); i++) await Task.Delay(50); }
            if (current == track && ready) { media.Position = TimeSpan.FromSeconds(Math.Max(0, seconds)); if (!playing) TogglePlay(); }
        }
        internal void PauseFor(Track track) { if (current == track && ready && playing) TogglePlay(); }

        private sealed class LyricsReviewItem
        {
            internal Track Track;
            public override string ToString()
            {
                var t = Track; string state =
                    t.LyricsStatus == "Review" ? "⚠ waiting " + t.LyricsConfidence + "%" :
                    t.LyricsStatus == "Check" ? "◐ check " + t.LyricsConfidence + "%" :
                    t.LyricsStatus == "Rejected" ? "✗ rejected" :
                    !String.IsNullOrWhiteSpace(t.Lyrics) ? (LyricsFiles.IsSynced(t.Lyrics) ? "✓ synced" : "✓ plain") :
                    t.LyricsStatus == "Accepted" ? "✓ instrumental" : "– none";
                return state + " · " + t.Title + (String.IsNullOrWhiteSpace(t.Artist) ? "" : " — " + t.Artist);
            }
        }
        private sealed class TimingRow { internal LyricLine Line; internal TextBox Time; internal Border Frame; }
        private static readonly string[] LyricsFilters = { "Waiting for review", "All songs", "No lyrics", "Time-synced", "Plain text only", "Rejected", "Added automatically – please check" };
        internal int LyricsCheckCount { get { return Data.Tracks.Count(t => t.LyricsStatus == "Check"); } }
        private static bool LyricsFilter(Track t, int filter)
        {
            switch (filter)
            {
                case 0: return t.LyricsStatus == "Review";
                case 2: return String.IsNullOrWhiteSpace(t.Lyrics) && t.LyricsStatus != "Review";
                case 3: return LyricsFiles.IsSynced(t.Lyrics);
                case 4: return !String.IsNullOrWhiteSpace(t.Lyrics) && !LyricsFiles.IsSynced(t.Lyrics);
                case 5: return t.LyricsStatus == "Rejected";
                case 6: return t.LyricsStatus == "Check";
                default: return true;
            }
        }
        private static void Shift(LyricLine line, double seconds)
        {
            if (line.Time < 0) return;
            line.Time = Math.Max(0, line.Time + seconds);
            foreach (var word in line.Words) word.Time = Math.Max(0, word.Time + seconds);
        }

        // Lyrics Review: see what every song has, accept/reject/swap results, edit text, and fix timing.
        internal Window ShowLyricsReview(Track focus)
        {
            if (lyricsReviewWindow != null) lyricsReviewWindow.Close();
            var dialog = new Window { Owner = Window, Title = "Lyrics review · Digitone", Width = 1120, Height = 760, MinWidth = 820, MinHeight = 560, Resources = Window.Resources, FontFamily = Window.FontFamily, Icon = Window.Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            lyricsReviewWindow = dialog;
            dialog.SetResourceReference(Control.BackgroundProperty, "B161917"); dialog.SetResourceReference(Control.ForegroundProperty, "BEEEFE8");
            var root = new Grid { Margin = new Thickness(20) }; root.SetResourceReference(Panel.BackgroundProperty, "B161917"); dialog.Content = root;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) }); root.ColumnDefinitions.Add(new ColumnDefinition());

            var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) }; root.Children.Add(left);
            var leftHeader = new StackPanel(); DockPanel.SetDock(leftHeader, Dock.Top); left.Children.Add(leftHeader);
            leftHeader.Children.Add(SettingsText("Lyrics review", 22));
            var filter = new ComboBox { Name = "LyricsFilter", Margin = new Thickness(0, 8, 0, 8) }; leftHeader.Children.Add(filter);
            foreach (string name in LyricsFilters) filter.Items.Add(new ComboBoxItem { Content = name });
            var list = new ListBox { Name = "LyricsReviewList" }; left.Children.Add(list);

            var right = new DockPanel(); Grid.SetColumn(right, 1); root.Children.Add(right);
            var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); right.Children.Add(top);
            var heading = SettingsText("", 20); var attached = SettingsText("", 12, true); top.Children.Add(heading); top.Children.Add(attached);
            var resultRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; top.Children.Add(resultRow);
            var searchAgain = new Button { Name = "SearchLyricsAgain", Content = "Search again", Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(searchAgain, Dock.Right); resultRow.Children.Add(searchAgain);
            var results = new ComboBox { Name = "LyricsResultChoice" }; resultRow.Children.Add(results);
            var notes = SettingsText("", 11, true); top.Children.Add(notes);

            var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); right.Children.Add(bottom);
            var buttons = new WrapPanel(); bottom.Children.Add(buttons);
            var accept = new Button { Name = "AcceptLyricsReview", Content = "✓ Accept and save", Margin = new Thickness(0, 0, 8, 6) };
            var reject = new Button { Name = "RejectLyricsReview", Content = "None of these are right", Margin = new Thickness(0, 0, 8, 6) };
            var remove = new Button { Name = "RemoveLyricsReview", Content = "Remove lyrics", Margin = new Thickness(0, 0, 8, 6) };
            buttons.Children.Add(accept); buttons.Children.Add(reject); buttons.Children.Add(remove);
            var status = SettingsText("", 12, true); bottom.Children.Add(status);

            var modes = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) }; DockPanel.SetDock(modes, Dock.Top); right.Children.Add(modes);
            var textMode = new Button { Name = "LyricsTextMode", Content = "Lyrics text", Margin = new Thickness(0, 0, 8, 0) }; var timingMode = new Button { Name = "TimingMode", Content = "Timing (karaoke)" };
            modes.Children.Add(textMode); modes.Children.Add(timingMode);
            var pages = new Grid(); right.Children.Add(pages);
            var editor = new TextBox { Name = "ReviewLyricsText", AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 200000, FontSize = 15, Padding = new Thickness(12) };
            pages.Children.Add(editor);

            // Timing editor.
            var timing = new DockPanel { Visibility = Visibility.Collapsed }; pages.Children.Add(timing); bool timingShown = false;
            var timingBar = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) }; DockPanel.SetDock(timingBar, Dock.Top); timing.Children.Add(timingBar);
            var playStart = new Button { Content = "▶ Play from start", Margin = new Thickness(0, 0, 6, 6) }; var pause = new Button { Content = "Pause", Margin = new Thickness(0, 0, 6, 6) };
            var stamp = new Button { Name = "StampLine", Content = "Set selected line to now (Space)", Margin = new Thickness(0, 0, 6, 6) };
            var earlier = new Button { Content = "All lines −0.5 s", Margin = new Thickness(0, 0, 6, 6) }; var later = new Button { Content = "All lines +0.5 s", Margin = new Thickness(0, 0, 6, 6) };
            var clock = SettingsText("Not playing", 13); clock.VerticalAlignment = VerticalAlignment.Center; clock.Margin = new Thickness(8, 0, 0, 6);
            foreach (var element in new UIElement[] { playStart, pause, stamp, earlier, later, clock }) timingBar.Children.Add(element);
            var timingHelp = SettingsText("Space  stamp the line as it starts   ·   − / +  nudge 0.1 s   ·   ▶  hear it", 11, true); timingHelp.ToolTip = "Play the song and select a line. Press Space as each line starts; the next line is selected for you."; DockPanel.SetDock(timingHelp, Dock.Top); timing.Children.Add(timingHelp);
            var rowsList = new ListBox { Name = "TimingRows", Margin = new Thickness(0, 6, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch }; timing.Children.Add(rowsList);

            LyricsReviewItem selected = null; LyricsSearch search = null; var rows = new List<TimingRow>(); bool timingDirty = false, busy = false, refreshing = false; int lit = -1; Track focusTrack = focus;
            var cancel = new CancellationTokenSource();
            Func<Track, bool> visibleNow = t => LyricsFilter(t, filter.SelectedIndex);
            Action refreshList = delegate
            {
                var keep = focusTrack ?? (selected != null ? selected.Track : null); focusTrack = null;
                var items = Data.Tracks.Where(visibleNow).OrderBy(t => t.LyricsStatus == "Review" ? 0 : 1).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).Select(t => new LyricsReviewItem { Track = t }).ToList();
                if (keep != null && !items.Any(i => i.Track == keep)) items.Insert(0, new LyricsReviewItem { Track = keep });
                refreshing = true; ((ComboBoxItem)filter.Items[0]).Content = LyricsFilters[0] + " (" + LyricsReviewCount + ")"; ((ComboBoxItem)filter.Items[6]).Content = LyricsFilters[6] + " (" + LyricsCheckCount + ")"; refreshing = false;
                list.ItemsSource = items; list.SelectedItem = items.FirstOrDefault(i => i.Track == keep) ?? items.FirstOrDefault();
            };
            Action buildRows = delegate
            {
                rows.Clear(); rowsList.Items.Clear(); timingDirty = false; lit = -1;
                if (selected == null) return;
                var lines = LyricsFiles.IsSynced(editor.Text) ? LrcFormat.Parse(editor.Text) : editor.Text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => new LyricLine { Text = l }).ToList();
                foreach (var line in lines)
                {
                    var row = new TimingRow { Line = line }; var item = line;
                    var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) }); for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Time = new TextBox { Text = line.Time >= 0 ? LrcFormat.Stamp(line.Time) : "--:--.--", Margin = new Thickness(0, 1, 6, 1), Padding = new Thickness(4, 2, 4, 2), FontFamily = new FontFamily("Consolas") };
                    var minus = new Button { Content = "−", Padding = new Thickness(0), Margin = new Thickness(1), ToolTip = "0.1 s earlier" }; var plus = new Button { Content = "+", Padding = new Thickness(0), Margin = new Thickness(1), ToolTip = "0.1 s later" }; var hear = new Button { Content = "▶", Padding = new Thickness(0), Margin = new Thickness(1), ToolTip = "Play from just before this line" };
                    var text = SettingsText(String.IsNullOrWhiteSpace(line.Text) ? "♪ (instrumental gap)" : line.Text, 14); text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new Thickness(8, 0, 0, 0);
                    Grid.SetColumn(minus, 1); Grid.SetColumn(plus, 2); Grid.SetColumn(hear, 3); Grid.SetColumn(text, 4);
                    grid.Children.Add(row.Time); grid.Children.Add(minus); grid.Children.Add(plus); grid.Children.Add(hear); grid.Children.Add(text);
                    row.Frame = new Border { Child = grid, Padding = new Thickness(4, 2, 4, 2), Tag = row };
                    row.Time.LostFocus += delegate { double seconds; if (LrcFormat.TryStamp(row.Time.Text, out seconds)) { Shift(item, seconds - Math.Max(0, item.Time)); if (item.Time < 0) item.Time = seconds; timingDirty = true; } row.Time.Text = item.Time >= 0 ? LrcFormat.Stamp(item.Time) : "--:--.--"; };
                    minus.Click += delegate { if (item.Time >= 0) { Shift(item, -.1); row.Time.Text = LrcFormat.Stamp(item.Time); timingDirty = true; } };
                    plus.Click += delegate { if (item.Time >= 0) { Shift(item, .1); row.Time.Text = LrcFormat.Stamp(item.Time); timingDirty = true; } };
                    hear.Click += async delegate { if (selected != null) await PlayFrom(selected.Track, Math.Max(0, (item.Time >= 0 ? item.Time : 0) - 2)); };
                    rows.Add(row); rowsList.Items.Add(row.Frame);
                }
                if (rowsList.Items.Count > 0) rowsList.SelectedIndex = Math.Max(0, rows.FindIndex(r => r.Line.Time < 0));
            };
            Func<string> timedText = delegate
            {
                var lines = rows.Select(r => new LyricLine { Time = r.Line.Time, Text = r.Line.Text, Words = r.Line.Words.Select(w => new LyricWord { Time = w.Time, Text = w.Text }).ToList() }).ToList();
                LrcFormat.FillGaps(lines); return LrcFormat.Write(lines);
            };
            Action<bool> setBusy = delegate(bool value) { busy = value; foreach (var button in new[] { accept, reject, remove, searchAgain }) button.IsEnabled = !value; list.IsEnabled = !value; };
            Action showResults = delegate
            {
                results.ItemsSource = search != null ? search.Candidates : null;
                results.IsEnabled = search != null && search.Candidates.Count > 0;
                notes.Text = search == null ? "No saved search results for this song. Use Search again." : search.Candidates.Count == 0 ? "No results matched this song. " + String.Join(" ", search.Problems) : "";
            };
            Action load = delegate
            {
                selected = list.SelectedItem as LyricsReviewItem; if (selected == null) { heading.Text = "Nothing here"; attached.Text = ""; editor.Text = ""; buildRows(); return; }
                var track = selected.Track;
                heading.Text = track.Title + (String.IsNullOrWhiteSpace(track.Artist) ? "" : " — " + track.Artist);
                attached.Text = !String.IsNullOrWhiteSpace(track.Lyrics) ? "Attached: " + (track.LyricsSource ?? "lyrics") + " · " + (LyricsFiles.IsSynced(track.Lyrics) ? "time-synced" : "plain text") + (String.IsNullOrEmpty(track.LyricsFile) ? "" : " · userdata\\lyrics\\" + track.LyricsFile) : "No lyrics attached." + (String.IsNullOrWhiteSpace(track.LyricsSummary) ? "" : " " + track.LyricsSummary);
                if (track.LyricsStatus == "Review") attached.Text = track.LyricsSummary ?? "Waiting for your review.";
                search = LyricsResults.Get(track); showResults();
                status.Text = "";
                if (!String.IsNullOrWhiteSpace(track.Lyrics)) { editor.Text = track.Lyrics; results.SelectedIndex = -1; }
                else if (search != null && search.Candidates.Count > 0) results.SelectedIndex = 0;
                else editor.Text = "";
                buildRows();
            };
            results.SelectionChanged += delegate
            {
                var chosen = results.SelectedItem as LyricsCandidate; if (chosen == null) return;
                editor.Text = chosen.Instrumental ? "" : chosen.Text;
                notes.Text = (chosen.Instrumental ? "• Marked as instrumental (no lyrics).\n" : "") + String.Join("\n", chosen.Notes.Select(n => "• " + n));
                buildRows();
            };
            Action<bool> showTiming = delegate(bool show)
            {
                if (show == timingShown) return;
                if (show) buildRows(); else if (timingDirty) { editor.Text = timedText(); timingDirty = false; }
                timingShown = show; timing.Visibility = show ? Visibility.Visible : Visibility.Collapsed; editor.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
                textMode.SetResourceReference(Control.BackgroundProperty, show ? "B1C211D" : "B354233"); timingMode.SetResourceReference(Control.BackgroundProperty, show ? "B354233" : "B1C211D");
            };
            textMode.Click += delegate { showTiming(false); }; timingMode.Click += delegate { showTiming(true); };
            textMode.SetResourceReference(Control.BackgroundProperty, "B354233"); timingMode.SetResourceReference(Control.BackgroundProperty, "B1C211D");
            filter.SelectionChanged += delegate { if (filter.SelectedIndex >= 0 && !refreshing) { selected = null; refreshList(); } };
            list.SelectionChanged += delegate { load(); };
            searchAgain.Click += async delegate
            {
                if (selected == null || busy) return;
                setBusy(true); status.Text = "Searching LRCLIB and lyrics.ovh…";
                try
                {
                    search = await SearchLyrics(selected.Track, LyricsQueryFor(selected.Track), cancel.Token);
                    showResults(); if (search.Candidates.Count > 0) results.SelectedIndex = 0;
                    status.Text = search.Candidates.Count == 0 ? "Nothing matched this song's title, artist, and length." : search.Candidates.Count + " results. The best match is shown; pick another above if needed, then Accept.";
                }
                catch (Exception error) { status.Text = "Search failed: " + error.Message; }
                finally { setBusy(false); }
            };
            accept.Click += delegate
            {
                if (selected == null || busy) return;
                var track = selected.Track; if (timingShown && timingDirty) { editor.Text = timedText(); timingDirty = false; }
                var chosen = results.SelectedItem as LyricsCandidate;
                string before = track.Lyrics, beforeSource = track.LyricsSource, beforeStatus = track.LyricsStatus, beforeSummary = track.LyricsSummary; bool beforeSynced = track.LyricsSynced; int beforeConfidence = track.LyricsConfidence;
                if (chosen != null && (chosen.Instrumental ? editor.Text.Length == 0 : editor.Text == chosen.Text)) AcceptLyrics(track, chosen, true);
                else if (String.IsNullOrWhiteSpace(editor.Text)) { status.Text = "There are no lyrics to save. Use Remove lyrics to clear them."; return; }
                else
                {
                    bool retimed = LyricsFiles.IsSynced(editor.Text) && !LyricsFiles.IsSynced(before ?? (chosen != null ? chosen.Text : null));
                    track.Lyrics = editor.Text; track.LyricsSynced = LyricsFiles.IsSynced(editor.Text); track.LyricsStatus = "Accepted"; track.LyricsConfidence = 100;
                    track.LyricsSource = retimed ? "Timed by you" : editor.Text == before ? (beforeSource ?? "Edited by you") : "Edited by you";
                    track.LyricsSummary = track.LyricsSource + " · " + (track.LyricsSynced ? "time-synced" : "plain text");
                }
                if (Save()) { status.Text = "Saved in userdata\\lyrics\\" + track.LyricsFile + "."; displayedLyrics = null; UpdateLyricsDisplay(); refreshList(); }
                else { track.Lyrics = before; track.LyricsSource = beforeSource; track.LyricsStatus = beforeStatus; track.LyricsSummary = beforeSummary; track.LyricsSynced = beforeSynced; track.LyricsConfidence = beforeConfidence; status.Text = "Could not save; nothing was changed."; }
            };
            reject.Click += delegate
            {
                if (selected == null || busy) return;
                var track = selected.Track; track.LyricsStatus = "Rejected"; track.LyricsSummary = "You rejected the lyrics that were found. Digitone will not search automatically again.";
                if (Save()) { status.Text = "Rejected. Existing lyrics (if any) were kept."; refreshList(); }
            };
            remove.Click += delegate
            {
                if (selected == null || busy || String.IsNullOrWhiteSpace(selected.Track.Lyrics)) return;
                if (MessageBox.Show(dialog, "Remove the lyrics from “" + selected.Track.Title + "”? This deletes its lyrics file in userdata\\lyrics.", "Remove lyrics", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                var track = selected.Track; string before = track.Lyrics; track.Lyrics = null; track.LyricsStatus = "Rejected"; track.LyricsSummary = "You removed these lyrics.";
                if (Save()) { status.Text = "Lyrics removed."; displayedLyrics = null; UpdateLyricsDisplay(); refreshList(); } else track.Lyrics = before;
            };
            playStart.Click += async delegate { if (selected != null) await PlayFrom(selected.Track, 0); };
            pause.Click += delegate { if (selected != null) PauseFor(selected.Track); };
            Action stampNow = delegate
            {
                if (selected == null || rowsList.SelectedIndex < 0) return;
                double position = PositionOf(selected.Track);
                if (position < 0) { status.Text = "Play this song first (▶ Play from start), then press Space as each line begins."; return; }
                var row = rows[rowsList.SelectedIndex];
                if (row.Line.Time >= 0) Shift(row.Line, position - row.Line.Time); else row.Line.Time = position;
                row.Time.Text = LrcFormat.Stamp(row.Line.Time); timingDirty = true;
                if (rowsList.SelectedIndex < rows.Count - 1) { rowsList.SelectedIndex++; rowsList.ScrollIntoView(rowsList.SelectedItem); }
            };
            stamp.Click += delegate { stampNow(); };
            earlier.Click += delegate { foreach (var row in rows) { Shift(row.Line, -.5); if (row.Line.Time >= 0) row.Time.Text = LrcFormat.Stamp(row.Line.Time); } timingDirty = true; };
            later.Click += delegate { foreach (var row in rows) { Shift(row.Line, .5); if (row.Line.Time >= 0) row.Time.Text = LrcFormat.Stamp(row.Line.Time); } timingDirty = true; };
            dialog.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space && timingShown && !(Keyboard.FocusedElement is TextBox)) { stampNow(); e.Handled = true; } };
            var ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            ticker.Tick += delegate
            {
                if (selected == null) return;
                double position = PositionOf(selected.Track);
                clock.Text = position < 0 ? "Not playing" : (playing ? "Playing " : "Paused ") + LrcFormat.Stamp(position);
                int now = -1; if (position >= 0) for (int i = 0; i < rows.Count; i++) if (rows[i].Line.Time >= 0 && rows[i].Line.Time <= position + .05 && (now < 0 || rows[i].Line.Time >= rows[now].Line.Time)) now = i;
                if (now == lit) return;
                for (int i = 0; i < rows.Count; i++) { if (i == now) rows[i].Frame.SetResourceReference(Border.BackgroundProperty, "B354233"); else rows[i].Frame.Background = Brushes.Transparent; }
                lit = now;
            };
            ticker.Start();
            dialog.Closed += delegate { ticker.Stop(); cancel.Cancel(); if (lyricsReviewWindow == dialog) lyricsReviewWindow = null; };
            if (focus != null) { selected = new LyricsReviewItem { Track = focus }; filter.SelectedIndex = focus.LyricsStatus == "Review" ? 0 : focus.LyricsStatus == "Check" ? 6 : 1; }
            else filter.SelectedIndex = LyricsReviewCount > 0 ? 0 : LyricsCheckCount > 0 ? 6 : 1;
            NativeChrome.Apply(dialog);
            dialog.Show();
            return dialog;
        }
        private Window lyricsReviewWindow;
    }
}
