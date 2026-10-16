using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Digitone
{
    internal static class LyricsLookup
    {
        internal static string Address(string artist,string title)
        {
            if (String.IsNullOrWhiteSpace(artist) || String.IsNullOrWhiteSpace(title)) throw new IOException("Enter the artist and song title first.");
            return "https://api.lyrics.ovh/v1/"+Uri.EscapeDataString(artist.Trim())+"/"+Uri.EscapeDataString(title.Trim());
        }
        internal static string Parse(string json)
        {
            var values = new JavaScriptSerializer { MaxJsonLength=524288 }.Deserialize<Dictionary<string,object>>(json);
            object value;
            if (values == null || !values.TryGetValue("lyrics",out value) || !(value is string) || String.IsNullOrWhiteSpace((string)value)) throw new IOException("No lyrics found. Try adjusting the artist or title, or paste your own.");
            return ((string)value).Replace("\r\n","\n").Replace("\n",Environment.NewLine);
        }
        internal static string Fetch(string artist,string title,CancellationToken token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create(Address(artist,title));
            request.AllowAutoRedirect=false; request.Timeout=20000; request.ReadWriteTimeout=20000; request.UserAgent="Digitone/1.0"; request.UseDefaultCredentials=false;
            using(token.Register(request.Abort))
            using(var response=(HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK) throw new IOException("The lyrics service did not return a result.");
                using(var input=response.GetResponseStream()) using(var output=new MemoryStream())
                {
                    var buffer=new byte[8192]; int count;
                    while((count=input.Read(buffer,0,buffer.Length))>0) { token.ThrowIfCancellationRequested(); if(output.Length+count>524288) throw new IOException("Lyrics response is too large."); output.Write(buffer,0,count); }
                    return Parse(Encoding.UTF8.GetString(output.ToArray()));
                }
            }
        }
    }
    public sealed partial class PlayerApp
    {
        private Border lyricsPane;
        private ScrollViewer lyricsScroll;
        private TextBlock lyricsBody, lyricsHeading;
        private FrameworkElement lyricsEmpty;
        private TextBlock lyricsEmptyText, lyricsModeText;
        private StackPanel karaokePanel;
        internal List<LyricLine> KaraokeLines { get; private set; }
        internal int KaraokeActive { get; private set; }
        private int karaokeWordsLit=-1;
        private Button lyricsReviewButton;
        private Track lyricsTrack;
        private string displayedLyrics;
        private CheckBox lyricsFollow;
        private bool lyricsUsesCurrent;
        private double lyricsLastPosition;
        private readonly System.Collections.Generic.HashSet<string> lyricsAttempted = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource autoLyricsCancellation;
        internal Func<LyricsQuery,CancellationToken,LyricsSearch> FindLyrics = LyricsEngine.Find;
        private LyricsCache lyricsCache;
        internal LyricsCache LyricsResults { get { return lyricsCache ?? (lyricsCache = new LyricsCache(Path.Combine(PortableFolders.Current.Cache, "lyrics"))); } }
        internal int LyricsReviewCount { get { return Data.Tracks.Count(t => t.LyricsStatus == "Review"); } }
        private static bool IdentityConfirmed(Track track) { return track.RecognitionStatus == "Applied" || track.RecognitionStatus == "Confirmed"; }
        internal LyricsQuery LyricsQueryFor(Track track, string artist = null, string title = null)
        {
            bool typed = (artist != null && artist != track.Artist) || (title != null && title != track.Title);
            string cleanArtist = SongRecognizer.CleanArtist(artist ?? track.Artist);
            // "worry - Slowed" → "worry": lyrics sites list edits under the song's own name; the length check still tells versions apart.
            string cleanTitle = System.Text.RegularExpressions.Regex.Replace(SongRecognizer.WithoutArtist(SongRecognizer.CleanTitle(title ?? track.Title), cleanArtist), @"\s+[-–—]\s+(slowed|sped\s?up|reverb|nightcore|8d|remix|remastered|radio edit|extended)\b.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return new LyricsQuery { Title = cleanTitle, Artist = cleanArtist, Album = track.Album, Confirmed = IdentityConfirmed(track) && !typed, AudioPath = track.Path };
        }
        private static double LengthOf(string path, RecognitionResult cached)
        {
            if (cached != null && cached.FileDuration > 0) return cached.FileDuration;
            try { object format, value; double seconds; var probe = SongTags.Probe(path); if (probe.TryGetValue("format", out format) && ((Dictionary<string, object>)format).TryGetValue("duration", out value) && Double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds)) return seconds; } catch { }
            return 0;
        }
        // Searches all lyrics sources for one song; the result is cached for review and offline use.
        internal async Task<LyricsSearch> SearchLyrics(Track track, LyricsQuery query, CancellationToken token)
        {
            var recognized = RecognitionResults.Get(track.AudioHash);
            var search = await Task.Run(delegate { if (query.Duration <= 0) query.Duration = LengthOf(query.AudioPath, recognized); return FindLyrics(query, token); });
            try { LyricsResults.Put(track, search); } catch { }
            return search;
        }
        // Automatic lookup when a song plays or has just been identified. Never replaces lyrics you already have.
        internal async void AutoFindLyrics(Track track, bool fromPlayback = true)
        {
            if(fromPlayback && autoLyricsCancellation!=null) autoLyricsCancellation.Cancel();
            if(Data.AutoLyrics==false || !String.IsNullOrWhiteSpace(track.Lyrics) || String.IsNullOrWhiteSpace(track.Artist) || track.LyricsStatus=="Rejected" || track.LyricsStatus=="Review" || track.LyricsStatus=="Accepted" || track.LyricsStatus=="Check" || !lyricsAttempted.Add(track.Path)) return;
            var cancellation=new CancellationTokenSource(); if(fromPlayback) autoLyricsCancellation=cancellation;
            EventHandler closing=delegate { cancellation.Cancel(); }; Window.Closed+=closing;
            try
            {
                await Task.Delay(fromPlayback ? 1200 : 200,cancellation.Token);
                var search=await SearchLyrics(track,LyricsQueryFor(track),cancellation.Token);
                if(!closed && !cancellation.IsCancellationRequested && Data.AutoLyrics!=false && Data.Tracks.Contains(track) && String.IsNullOrWhiteSpace(track.Lyrics)) DecideLyrics(track,search);
            }
            catch { if(cancellation.IsCancellationRequested) lyricsAttempted.Remove(track.Path); }
            finally { Window.Closed-=closing; if(autoLyricsCancellation==cancellation) autoLyricsCancellation=null; cancellation.Dispose(); }
        }
        private void DecideLyrics(Track track, LyricsSearch search)
        {
            var best = search.Best;
            if (best == null) { track.LyricsStatus = "NotFound"; track.LyricsConfidence = 0; track.LyricsSummary = search.Problems.FirstOrDefault() ?? "No lyrics were found for this exact song."; }
            else if (best.Confidence >= LyricsEngine.HighConfidence && best.Text != null && best.Text.Length <= 200000) AcceptLyrics(track, best, false);
            else if (best.Confidence >= LyricsEngine.CheckConfidence && best.Text != null && best.Text.Length <= 200000)
            {
                // Probably right: add it so lyrics just work, but list it in Lyrics review under "please check".
                AcceptLyrics(track, best, false); track.LyricsStatus = "Check";
                track.LyricsSummary = "Added automatically at " + best.Confidence + "% from " + best.Source + ". Please check it in Lyrics review.";
            }
            else { track.LyricsStatus = "Review"; track.LyricsConfidence = best.Confidence; track.LyricsSummary = "Possible lyrics from " + best.Source + " (" + best.Confidence + "%) are waiting for your review."; }
            if (!Save()) { track.Lyrics = null; track.LyricsStatus = null; }
            UpdateLyricsDisplay();
        }
        // Background lookup: fills in missing lyrics for the whole library, one song at a time.
        private readonly Queue<Track> lyricsQueue = new Queue<Track>();
        private bool lyricsSweeping; private int lyricsSweepFound, artSweepFound;
        private bool WantLyrics(Track track, bool retry) { return Data.AutoLyrics != false && NeedsLyrics(track, retry); }
        private CancellationTokenSource lyricsSweepCancel = new CancellationTokenSource();
        internal Task LyricsSweepIdle { get { return lyricsSweepIdle.Task; } }
        private TaskCompletionSource<bool> lyricsSweepIdle = DoneSource();
        private static TaskCompletionSource<bool> DoneSource() { var done = new TaskCompletionSource<bool>(); done.SetResult(true); return done; }
        internal bool NeedsLyrics(Track track, bool retryNotFound)
        {
            if (!String.IsNullOrWhiteSpace(track.Lyrics) || String.IsNullOrWhiteSpace(track.Artist) || !File.Exists(track.Path)) return false;
            if (track.LyricsStatus == null) return true;
            if (track.LyricsStatus != "NotFound") return false;
            DateTime checkedAt; return retryNotFound || !DateTime.TryParse(track.LyricsCheckedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out checkedAt) || DateTime.UtcNow - checkedAt > TimeSpan.FromDays(7);
        }
        internal void QueueLyricsLookup(IEnumerable<Track> tracks, bool retryNotFound = false)
        {
            // One gentle background pass fills in both missing lyrics and missing album art.
            foreach (var track in tracks) if ((WantLyrics(track, retryNotFound) || NeedsArt(track, retryNotFound)) && !lyricsQueue.Contains(track)) lyricsQueue.Enqueue(track);
            if (!lyricsSweeping && lyricsQueue.Count > 0) SweepLyrics(retryNotFound);
        }
        private async void SweepLyrics(bool retryNotFound)
        {
            lyricsSweeping = true; lyricsSweepFound = 0; artSweepFound = 0; int checkedCount = 0; if (lyricsSweepIdle.Task.IsCompleted) lyricsSweepIdle = new TaskCompletionSource<bool>();
            var token = lyricsSweepCancel.Token;
            try
            {
                await Task.Delay(3000, token); // let downloads finish identifying first so lyrics use the corrected names
                while (lyricsQueue.Count > 0 && !closed)
                {
                    var track = lyricsQueue.Dequeue();
                    bool wantLyrics = WantLyrics(track, retryNotFound), wantArt = NeedsArt(track, retryNotFound);
                    if (!Data.Tracks.Contains(track) || !(wantLyrics || wantArt) || recognitionJobs.Any(j => j.Track == track)) continue;
                    checkedCount++;
                    if (wantLyrics)
                        try
                        {
                            var search = await SearchLyrics(track, LyricsQueryFor(track), token);
                            if (!closed && Data.AutoLyrics != false && String.IsNullOrWhiteSpace(track.Lyrics))
                            {
                                track.LyricsCheckedUtc = DateTime.UtcNow.ToString("o");
                                DecideLyrics(track, search);
                                if (!String.IsNullOrWhiteSpace(track.Lyrics)) lyricsSweepFound++;
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch { }
                    if (wantArt && !closed && NeedsArt(track, retryNotFound))
                        try { if (await FillArt(track, token) != null) artSweepFound++; }
                        catch (OperationCanceledException) { break; }
                        catch { track.ArtworkCheckedUtc = DateTime.UtcNow.ToString("o"); }
                    requestedArt.Remove(track);
                    await Task.Delay(800, token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lyricsSweeping = false; lyricsSweepIdle.TrySetResult(true);
                // Quiet background work: only a short result, and only when something was actually added.
                if (!closed && lyricsSweepFound + artSweepFound > 0) Status("Added " + (lyricsSweepFound > 0 ? lyricsSweepFound + " lyrics" : "") + (lyricsSweepFound > 0 && artSweepFound > 0 ? " and " : "") + (artSweepFound > 0 ? artSweepFound + (artSweepFound == 1 ? " cover" : " covers") : "") + ".");
            }
        }
        internal void AcceptLyrics(Track track, LyricsCandidate candidate, bool byUser)
        {
            track.Lyrics = candidate.Instrumental ? null : candidate.Text;
            track.LyricsSource = candidate.Source; track.LyricsSynced = candidate.Synced && !candidate.Instrumental; track.LyricsStatus = "Accepted"; track.LyricsConfidence = candidate.Confidence;
            track.LyricsSummary = (byUser ? "Chosen by you" : "Matched automatically") + " · " + candidate.Source + " · " + (candidate.Instrumental ? "instrumental (no lyrics)" : candidate.Synced ? "time-synced" : "plain text") + " · " + candidate.Confidence + "%";
        }
        internal bool LyricsVisible { get { return lyricsPane != null && lyricsPane.Visibility == Visibility.Visible; } }
        private void ShowLyrics(bool nowPlaying)
        {
            if (LyricsVisible) { lyricsPane.Visibility=Visibility.Collapsed; return; }
            lyricsUsesCurrent=current != null;
            lyricsTrack=current ?? Find<ListBox>("Tracks").SelectedItem as Track;
            if (lyricsPane == null)
            {
                lyricsPane=new Border { Margin=new Thickness(24,8,24,12),Padding=new Thickness(22),BorderThickness=new Thickness(1) };
                lyricsPane.SetResourceReference(Border.BackgroundProperty,"B161917"); lyricsPane.SetResourceReference(Border.BorderBrushProperty,"BC1D3A8");
                Grid.SetRow(lyricsPane,2); Panel.SetZIndex(lyricsPane,40); ((Grid)Find<Grid>("ExpandedView").Parent).Children.Add(lyricsPane);
                var dock=new DockPanel(); lyricsPane.Child=dock;
                var header=new StackPanel(); DockPanel.SetDock(header,Dock.Top); dock.Children.Add(header);
                var actions=new WrapPanel(); header.Children.Add(actions);
                var close=new Button { Content="Hide lyrics" }; actions.Children.Add(close); close.Click+=delegate { lyricsPane.Visibility=Visibility.Collapsed; };
                var edit=new Button { Content="Search / edit / save" }; actions.Children.Add(edit); edit.Click+=delegate { if(lyricsTrack!=null) { CreateLyrics(lyricsTrack).ShowDialog(); UpdateLyricsDisplay(); } };
                lyricsFollow=new CheckBox { Content="Follow song",IsChecked=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0) }; actions.Children.Add(lyricsFollow);
                lyricsHeading=SettingsText("Lyrics",20); header.Children.Add(lyricsHeading);
                var review=new Button { Name="OpenLyricsReview",Content="Lyrics review & timing" }; actions.Children.Insert(2,review); review.Click+=delegate { ShowLyricsReview(lyricsTrack); };
                lyricsModeText=SettingsText("",11,true); header.Children.Add(lyricsModeText);
                karaokePanel=new StackPanel { Name="KaraokeLines",Margin=new Thickness(16,30,16,260) };
                var empty=new WrapPanel(); lyricsEmpty=empty; header.Children.Add(empty);
                lyricsEmptyText=SettingsText("No lyrics found",17); empty.Children.Add(lyricsEmptyText);
                lyricsReviewButton=new Button { Name="ReviewFoundLyrics",Content="Review found lyrics",Margin=new Thickness(14,0,0,8),Visibility=Visibility.Collapsed }; empty.Children.Add(lyricsReviewButton);
                lyricsReviewButton.Click+=delegate { if(lyricsTrack!=null) ShowLyricsReview(lyricsTrack); };
                var upload=new Button { Content="Upload lyrics",Margin=new Thickness(14,0,0,8) }; empty.Children.Add(upload);
                upload.Click+=delegate { if(lyricsTrack!=null) { CreateLyrics(lyricsTrack,true).ShowDialog(); UpdateLyricsDisplay(); } else Status("Select a song before uploading lyrics."); };
                lyricsBody=SettingsText("",23); lyricsBody.LineHeight=42; lyricsBody.Margin=new Thickness(16,30,16,60);
                lyricsScroll=new ScrollViewer { Content=lyricsBody,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; dock.Children.Add(lyricsScroll);
                lyricsScroll.PreviewMouseWheel+=delegate { lyricsFollow.IsChecked=false; };
            }
            displayedLyrics=null; lyricsPane.Visibility=Visibility.Visible; UpdateLyricsDisplay();
        }
        internal static string DisplayLyrics(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text ?? "",@"\[[^\]\r\n]*\]", "").Trim();
        }
        internal static double LyricsProgress(double position,double duration)
        {
            return duration <= 0 ? 0 : Math.Max(0,Math.Min(1,(position-5)/Math.Max(1,duration-10)));
        }
        private void UpdateLyricsDisplay()
        {
            if(!LyricsVisible) return;
            if(current!=null && (lyricsUsesCurrent || lyricsTrack==null)) { lyricsUsesCurrent=true; lyricsTrack=current; }
            string value=lyricsTrack == null ? "" : lyricsTrack.Lyrics ?? "";
            lyricsHeading.Text=lyricsTrack == null ? "Select a song" : lyricsTrack.Title;
            if(value!=displayedLyrics)
            {
                displayedLyrics=value; lyricsBody.Text=DisplayLyrics(value);
                var parsed=LyricsFiles.IsSynced(value) ? LrcFormat.Parse(value) : null;
                KaraokeLines=parsed!=null && parsed.Count>=3 ? parsed : null; KaraokeActive=-2; karaokeWordsLit=-1;
                if(KaraokeLines!=null) { BuildKaraoke(); lyricsScroll.Content=karaokePanel; lyricsModeText.Text="Click a line to jump there"; }
                else { lyricsScroll.Content=lyricsBody; lyricsModeText.Text="No timing yet · add it in Lyrics review"; }
                lyricsEmpty.Visibility=String.IsNullOrWhiteSpace(lyricsBody.Text) ? Visibility.Visible : Visibility.Collapsed; lyricsScroll.ScrollToTop(); lyricsLastPosition=-1;
            }
            bool waiting=lyricsTrack!=null && lyricsTrack.LyricsStatus=="Review" && String.IsNullOrWhiteSpace(value);
            lyricsReviewButton.Visibility=waiting ? Visibility.Visible : Visibility.Collapsed;
            lyricsEmptyText.Text=waiting ? "Lyrics found, but not certain (" + lyricsTrack.LyricsConfidence + "%)" : lyricsTrack!=null && (lyricsTrack.LyricsStatus=="Accepted"||lyricsTrack.LyricsStatus=="Check") && String.IsNullOrWhiteSpace(value) ? "Instrumental" : "No lyrics found";
            if(KaraokeLines!=null) { UpdateKaraoke(); return; }
            if(lyricsFollow.IsChecked != true || current!=lyricsTrack || !ready) return;
            double position=media.Position.TotalSeconds;
            double target=LyricsProgress(position,Find<Slider>("Seek").Maximum)*lyricsScroll.ScrollableHeight;
            bool seek=lyricsLastPosition<0 || Math.Abs(position-lyricsLastPosition)>1;
            if(playing || seek) lyricsScroll.ScrollToVerticalOffset(seek ? target : lyricsScroll.VerticalOffset+(target-lyricsScroll.VerticalOffset)*.08);
            lyricsLastPosition=position;
        }
        private void BuildKaraoke()
        {
            karaokePanel.Children.Clear();
            foreach(var line in KaraokeLines)
            {
                var row=line; var block=new TextBlock { Text=String.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text,FontSize=24,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,7),Opacity=.6,Cursor=System.Windows.Input.Cursors.Hand,ToolTip=LrcFormat.Stamp(line.Time) };
                block.SetResourceReference(TextBlock.ForegroundProperty,"BEEEFE8");
                block.MouseLeftButtonUp+=delegate { if(current==lyricsTrack && ready) { media.Position=TimeSpan.FromSeconds(row.Time); lyricsFollow.IsChecked=true; KaraokeActive=-2; } };
                karaokePanel.Children.Add(block);
            }
        }
        // Highlights the line being sung (and its words, when the lyrics have word timing) and keeps it centred.
        private void UpdateKaraoke()
        {
            double position=current==lyricsTrack && ready ? media.Position.TotalSeconds : -1;
            int active=position<0 ? -1 : LrcFormat.ActiveIndex(KaraokeLines,position);
            if(active!=KaraokeActive)
            {
                for(int i=0;i<karaokePanel.Children.Count;i++)
                {
                    var block=(TextBlock)karaokePanel.Children[i]; bool now=i==active;
                    block.SetResourceReference(TextBlock.ForegroundProperty,now ? "BC1D3A8" : "BEEEFE8");
                    block.FontSize=now ? 30 : 24; block.FontWeight=now ? FontWeights.SemiBold : FontWeights.Normal; block.Opacity=now ? 1 : i<active ? .35 : .65;
                    if(!now && block.Inlines.Count>1) { block.Inlines.Clear(); block.Text=String.IsNullOrWhiteSpace(KaraokeLines[i].Text) ? "♪" : KaraokeLines[i].Text; }
                }
                if(active>=0 && KaraokeLines[active].Words.Count>0) { var block=(TextBlock)karaokePanel.Children[active]; block.Inlines.Clear(); foreach(var word in KaraokeLines[active].Words) { var run=new System.Windows.Documents.Run(word.Text); run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"BEEEFE8"); block.Inlines.Add(run); } }
                KaraokeActive=active; karaokeWordsLit=-1;
            }
            if(active>=0 && KaraokeLines[active].Words.Count>0)
            {
                var words=KaraokeLines[active].Words; int lit=words.Count(w=>w.Time<=position+.03);
                if(lit!=karaokeWordsLit)
                {
                    var runs=((TextBlock)karaokePanel.Children[active]).Inlines.ToList();
                    for(int i=0;i<runs.Count;i++) runs[i].SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,i<lit ? "BC1D3A8" : "BEEEFE8");
                    karaokeWordsLit=lit;
                }
            }
            if(lyricsFollow.IsChecked!=true || active<0) return;
            try
            {
                var target=(FrameworkElement)karaokePanel.Children[active];
                double y=target.TransformToAncestor(karaokePanel).Transform(new Point(0,0)).Y+karaokePanel.Margin.Top+target.ActualHeight/2-lyricsScroll.ViewportHeight/2;
                y=Math.Max(0,Math.Min(lyricsScroll.ScrollableHeight,y));
                double gap=y-lyricsScroll.VerticalOffset; if(Math.Abs(gap)>.5) lyricsScroll.ScrollToVerticalOffset(lyricsScroll.VerticalOffset+(Math.Abs(gap)>lyricsScroll.ViewportHeight ? gap : gap*.18));
            }
            catch(InvalidOperationException) { }
        }
        internal Window CreateLyrics(Track track,bool uploadImmediately=false)
        {
            var dialog=new Window { Owner=Window, Title="Lyrics · Digitone", Width=680,Height=730,MinWidth=460,MinHeight=480,ResizeMode=ResizeMode.NoResize,Resources=Window.Resources,FontFamily=Window.FontFamily,Icon=Window.Icon,WindowStartupLocation=WindowStartupLocation.CenterOwner };
            dialog.SetResourceReference(Control.BackgroundProperty,"B161917"); dialog.SetResourceReference(Control.ForegroundProperty,"BEEEFE8");
            var root=new DockPanel { Margin=new Thickness(26) }; dialog.Content=root;
            var header=new StackPanel(); DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
            header.Children.Add(SettingsText("Lyrics",32)); header.Children.Add(SettingsText(track.Title,17));
            var artist=new TextBox { Text=track.Artist ?? "",MaxLength=250,Margin=new Thickness(0,0,0,8) };
            var title=new TextBox { Text=track.Title,MaxLength=250,Margin=new Thickness(0,0,0,8) };
            header.Children.Add(SettingsText("Artist",11)); header.Children.Add(artist); header.Children.Add(SettingsText("Song title",11)); header.Children.Add(title);
            var actions=new WrapPanel(); header.Children.Add(actions);
            var search=new Button { Content="Find lyrics online",Margin=new Thickness(0,0,8,8) };
            var import=new Button { Content="Import .txt / .lrc",Margin=new Thickness(0,0,8,8) }; actions.Children.Add(search); actions.Children.Add(import);
            var choices=new ComboBox { Name="LyricsChoice",Margin=new Thickness(0,0,0,4),Visibility=Visibility.Collapsed }; header.Children.Add(choices);
            var notes=SettingsText("",11,true); header.Children.Add(notes);
            var reject=new Button { Name="RejectLyrics",Content="None of these are right",Margin=new Thickness(0,0,8,8),Visibility=Visibility.Collapsed }; actions.Children.Add(reject);
            var status=SettingsText("Search online, or paste lyrics below.",12,true); status.ToolTip="Searches LRCLIB (time-synced) first, then lyrics.ovh, and checks the title, artist, and length. Saved lyrics go in userdata\\lyrics."; header.Children.Add(status);
            if(!String.IsNullOrWhiteSpace(track.LyricsSummary)) status.Text=track.LyricsSummary;
            var footer=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0) }; DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
            var save=new Button { Content="Save lyrics",Name="SaveLyrics" }; var close=new Button { Content="Close",Margin=new Thickness(10,0,0,0) }; footer.Children.Add(save); footer.Children.Add(close);
            var editor=new TextBox { Name="LyricsText",Text=track.Lyrics ?? "",AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=200000,FontSize=16,Padding=new Thickness(16) }; root.Children.Add(editor);
            Action<LyricsSearch> showResults=delegate(LyricsSearch found)
            {
                choices.ItemsSource=found==null ? null : found.Candidates; bool any=found!=null && found.Candidates.Count>0;
                choices.Visibility=any ? Visibility.Visible : Visibility.Collapsed; reject.Visibility=any ? Visibility.Visible : Visibility.Collapsed;
                if(any) choices.SelectedIndex=0; else notes.Text=found==null ? "" : String.Join("\n", found.Problems);
            };
            choices.SelectionChanged+=delegate { var chosen=choices.SelectedItem as LyricsCandidate; if(chosen==null) return; editor.Text=chosen.Instrumental ? "" : chosen.Text; notes.Text=(chosen.Instrumental ? "• Marked as instrumental (no lyrics).\n" : "")+String.Join("\n", chosen.Notes.Select(n=>"• "+n)); };
            reject.Click+=delegate { track.LyricsStatus="Rejected"; track.LyricsSummary="You rejected the lyrics that were found. Digitone will not search automatically again."; if(Save()) { status.Text=track.LyricsSummary; showResults(null); if(String.IsNullOrWhiteSpace(track.Lyrics)) editor.Text=""; } };
            var cancel=new CancellationTokenSource(); bool ended=false;
            dialog.Closed+=delegate { ended=true; cancel.Cancel(); cancel.Dispose(); };
            close.Click+=delegate { dialog.Close(); };
            save.Click+=delegate {
                string previous=track.Lyrics,previousSource=track.LyricsSource,previousStatus=track.LyricsStatus,previousSummary=track.LyricsSummary; bool previousSynced=track.LyricsSynced; int previousConfidence=track.LyricsConfidence;
                var chosen=choices.Visibility==Visibility.Visible ? choices.SelectedItem as LyricsCandidate : null;
                if(chosen!=null && (chosen.Instrumental ? editor.Text.Length==0 : editor.Text==chosen.Text)) AcceptLyrics(track,chosen,true);
                else { track.Lyrics=editor.Text; track.LyricsSource=chosen!=null ? "Edited by you" : "Typed or imported by you"; track.LyricsSynced=LyricsFiles.IsSynced(editor.Text); track.LyricsStatus=String.IsNullOrWhiteSpace(editor.Text) ? null : "Accepted"; track.LyricsConfidence=100; track.LyricsSummary=String.IsNullOrWhiteSpace(editor.Text) ? null : track.LyricsSource+" · "+(track.LyricsSynced ? "time-synced" : "plain text"); }
                if(Save()) status.Text="Lyrics saved in userdata\\lyrics. Your audio file is unchanged.";
                else { track.Lyrics=previous; track.LyricsSource=previousSource; track.LyricsStatus=previousStatus; track.LyricsSummary=previousSummary; track.LyricsSynced=previousSynced; track.LyricsConfidence=previousConfidence; }
            };
            if(track.LyricsStatus=="Review") { var waiting=LyricsResults.Get(track); if(waiting!=null) { showResults(waiting); if(String.IsNullOrWhiteSpace(track.Lyrics)) status.Text="These lyrics were not certain enough to save automatically. Check the preview, pick another result, or edit it, then Save."; else editor.Text=track.Lyrics; } }
            import.Click+=delegate {
                var picker=new Microsoft.Win32.OpenFileDialog { Filter="Lyrics|*.txt;*.lrc",Title="Import lyrics" };
                if(picker.ShowDialog(dialog)!=true) return;
                try { if(!LocalFiles.IsLocal(picker.FileName) || new FileInfo(picker.FileName).Length>400000) throw new IOException("Choose a local lyrics file smaller than 400 KB."); string content=File.ReadAllText(picker.FileName); if(content.Length>editor.MaxLength) throw new IOException("Lyrics file is too long."); editor.Text=content; status.Text="Imported preview. Choose Save lyrics to keep it."; }
                catch(Exception e) { status.Text=e.Message; }
            };
            if(uploadImmediately) dialog.Loaded+=delegate { import.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            search.Click+=async delegate {
                if(!String.IsNullOrWhiteSpace(editor.Text) && MessageBox.Show(dialog,"Replace this preview with an online result? Saved lyrics stay unchanged until you save.","Find lyrics",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                var query=LyricsQueryFor(track,artist.Text,title.Text); var token=cancel.Token; search.IsEnabled=false; import.IsEnabled=false; editor.IsReadOnly=true; save.IsEnabled=false; status.Text="Looking for lyrics (LRCLIB, then lyrics.ovh)…";
                try
                {
                    var found=await SearchLyrics(track,query,token);
                    if(!ended)
                    {
                        found.Candidates.RemoveAll(c=>c.Text!=null && c.Text.Length>editor.MaxLength);
                        showResults(found);
                        status.Text=found.Candidates.Count==0 ? "No lyrics matched this exact song. Check the artist and title above, or paste/import lyrics." : found.Candidates.Count+" result"+(found.Candidates.Count==1?"":"s")+". Showing the best match; pick another above if it is wrong, then Save.";
                    }
                }
                catch(Exception e) { if(!ended) status.Text=e is WebException ? "The lyrics services are unavailable right now. Try again, or paste/import lyrics." : e.Message; }
                finally { if(!ended) { search.IsEnabled=true; import.IsEnabled=true; editor.IsReadOnly=false; save.IsEnabled=true; } }
            };
            return dialog;
        }
    }
}
