using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Digitone
{
    // Accepted lyrics are stored as files in userdata\lyrics (.lrc when time-synced, .txt otherwise).
    internal static class LyricsFiles
    {
        private static readonly Regex Timestamp = new Regex(@"^\s*\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\]", RegexOptions.Multiline);
        internal static bool IsSynced(string text) { return !String.IsNullOrEmpty(text) && Timestamp.Matches(text).Count >= 3; }
        private static string Safe(string value)
        {
            string text = new string((value ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c) && !Char.IsControl(c)).ToArray()).Trim(' ', '.');
            return text.Length > 70 ? text.Substring(0, 70).Trim(' ', '.') : text;
        }
        private static string NewName(Track track, string extension, IEnumerable<string> taken)
        {
            string label = Safe((String.IsNullOrWhiteSpace(track.Artist) ? "" : track.Artist + " - ") + track.Title);
            if (label.Length == 0) label = "Lyrics";
            string id = !String.IsNullOrEmpty(track.MusicBrainzRecordingId) && track.MusicBrainzRecordingId.Length >= 8 ? track.MusicBrainzRecordingId.Substring(0, 8) : Guid.NewGuid().ToString("N").Substring(0, 8);
            string name = label + " [" + id + "]" + extension;
            var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
            while (used.Contains(name)) name = label + " [" + Guid.NewGuid().ToString("N").Substring(0, 8) + "]" + extension;
            return name;
        }
        private static bool SafeName(string name) { return !String.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0; }
        internal static void LoadAll(Library library, PortableFolders folders, Dictionary<Track, string> written)
        {
            written.Clear();
            foreach (Track track in library.Tracks)
            {
                if (!String.IsNullOrEmpty(track.LyricsFile))
                {
                    string file = SafeName(track.LyricsFile) ? Path.Combine(folders.Lyrics, track.LyricsFile) : null;
                    if (file != null && File.Exists(file)) track.Lyrics = File.ReadAllText(file, Encoding.UTF8);
                    else { track.LyricsFile = null; track.Lyrics = null; }
                }
                // Older libraries kept lyrics inside library.json; they move to files on the next save.
                written[track] = String.IsNullOrEmpty(track.LyricsFile) ? null : track.Lyrics;
            }
        }
        internal static void SaveAll(Library library, PortableFolders folders, Dictionary<Track, string> written)
        {
            var taken = new HashSet<string>(library.Tracks.Where(t => !String.IsNullOrEmpty(t.LyricsFile)).Select(t => t.LyricsFile), StringComparer.OrdinalIgnoreCase);
            foreach (Track track in library.Tracks)
            {
                string text = String.IsNullOrWhiteSpace(track.Lyrics) ? null : track.Lyrics, previous;
                written.TryGetValue(track, out previous);
                if (String.Equals(text, previous, StringComparison.Ordinal) && (text == null || !String.IsNullOrEmpty(track.LyricsFile))) continue;
                if (text == null)
                {
                    if (!String.IsNullOrEmpty(track.LyricsFile) && SafeName(track.LyricsFile) && library.Tracks.Count(t => t.LyricsFile == track.LyricsFile) == 1) { string old = Path.Combine(folders.Lyrics, track.LyricsFile); if (File.Exists(old)) File.Delete(old); }
                    taken.Remove(track.LyricsFile ?? ""); track.LyricsFile = null; written[track] = null; continue;
                }
                string extension = IsSynced(text) ? ".lrc" : ".txt";
                if (String.IsNullOrEmpty(track.LyricsFile) || !SafeName(track.LyricsFile) || !track.LyricsFile.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || library.Tracks.Any(t => t != track && t.LyricsFile == track.LyricsFile))
                {
                    string old = track.LyricsFile;
                    track.LyricsFile = NewName(track, extension, taken); taken.Add(track.LyricsFile);
                    if (!String.IsNullOrEmpty(old) && SafeName(old) && !library.Tracks.Any(t => t != track && t.LyricsFile == old)) { string stale = Path.Combine(folders.Lyrics, old); if (File.Exists(stale)) File.Delete(stale); }
                }
                Directory.CreateDirectory(folders.Lyrics);
                string target = Path.Combine(folders.Lyrics, track.LyricsFile), temporary = target + ".tmp";
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                written[track] = text;
            }
        }
    }

    public sealed class LyricWord { public double Time { get; set; } public string Text { get; set; } }
    public sealed class LyricLine
    {
        public double Time { get; set; } // seconds; negative = not timed yet
        public string Text { get; set; }
        public List<LyricWord> Words { get; set; }
        public LyricLine() { Words = new List<LyricWord>(); Time = -1; }
    }
    // Reads and writes LRC: [mm:ss.xx] line times, <mm:ss.xx> word times (enhanced LRC), and [offset:±ms].
    internal static class LrcFormat
    {
        private static readonly Regex LineTag = new Regex(@"^\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]");
        private static readonly Regex WordTag = new Regex(@"<(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?>");
        private static readonly Regex Offset = new Regex(@"^\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase);
        private static double Seconds(Match match)
        {
            double fraction = 0; string part = match.Groups[3].Value;
            if (part.Length > 0) fraction = Int32.Parse(part, CultureInfo.InvariantCulture) / Math.Pow(10, part.Length);
            return Int32.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + Int32.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) + fraction;
        }
        internal static string Stamp(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int hundredths = (int)Math.Round(seconds * 100); int minutes = hundredths / 6000, rest = hundredths % 6000;
            return minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + (rest / 100).ToString("00", CultureInfo.InvariantCulture) + "." + (rest % 100).ToString("00", CultureInfo.InvariantCulture);
        }
        internal static bool TryStamp(string text, out double seconds)
        {
            seconds = 0; var match = Regex.Match((text ?? "").Trim(), @"^(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?$");
            if (!match.Success) return false;
            var tag = LineTag.Match("[" + text.Trim() + "]"); if (!tag.Success) return false;
            seconds = Seconds(tag); return true;
        }
        // Timed lines in play order. Lines with several timestamps (repeated choruses) appear once per time.
        internal static List<LyricLine> Parse(string text)
        {
            var lines = new List<LyricLine>(); double offset = 0;
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string rest = raw.Trim(); var offsetMatch = Offset.Match(rest);
                if (offsetMatch.Success) { offset = Int32.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0; continue; }
                var times = new List<double>(); Match tag;
                while ((tag = LineTag.Match(rest)).Success) { times.Add(Seconds(tag)); rest = rest.Substring(tag.Length); }
                if (times.Count == 0) continue;
                var words = new List<LyricWord>();
                var wordMatches = WordTag.Matches(rest);
                if (wordMatches.Count > 0)
                    for (int i = 0; i < wordMatches.Count; i++)
                    {
                        int start = wordMatches[i].Index + wordMatches[i].Length, end = i + 1 < wordMatches.Count ? wordMatches[i + 1].Index : rest.Length;
                        string word = rest.Substring(start, end - start); if (word.Length > 0) words.Add(new LyricWord { Time = Seconds(wordMatches[i]), Text = word });
                    }
                string plain = WordTag.Replace(rest, "").Trim();
                foreach (double time in times) lines.Add(new LyricLine { Time = time, Text = plain, Words = words.Select(w => new LyricWord { Time = w.Time, Text = w.Text }).ToList() });
            }
            if (offset != 0) foreach (var line in lines) { line.Time = Math.Max(0, line.Time - offset); foreach (var word in line.Words) word.Time = Math.Max(0, word.Time - offset); }
            return lines.OrderBy(l => l.Time).ToList();
        }
        internal static string Write(IEnumerable<LyricLine> lines)
        {
            var text = new StringBuilder();
            foreach (var line in lines.OrderBy(l => l.Time))
            {
                text.Append('[').Append(Stamp(line.Time)).Append(']');
                if (line.Words.Count > 0) foreach (var word in line.Words) text.Append('<').Append(Stamp(word.Time)).Append('>').Append(word.Text);
                else text.Append(line.Text ?? "");
                text.Append(Environment.NewLine);
            }
            return text.ToString().TrimEnd();
        }
        // Index of the line being sung at this position, or -1 before the first line.
        internal static int ActiveIndex(IList<LyricLine> lines, double position)
        {
            int found = -1;
            for (int i = 0; i < lines.Count; i++) { if (lines[i].Time <= position + .05) found = i; else break; }
            return found;
        }
        // Fills untimed lines between timed neighbours so a half-finished timing still plays in order.
        internal static void FillGaps(IList<LyricLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Time >= 0) continue;
                int before = i - 1; while (before >= 0 && lines[before].Time < 0) before--;
                int after = i + 1; while (after < lines.Count && lines[after].Time < 0) after++;
                double start = before >= 0 ? lines[before].Time : 0, end = after < lines.Count ? lines[after].Time : start + 2.5 * (after - before);
                lines[i].Time = start + (end - start) * (i - before) / (double)(after - before);
            }
        }
    }

    public sealed class LyricsCandidate
    {
        public string Source { get; set; }
        public bool Synced { get; set; }
        public bool Instrumental { get; set; }
        public string Text { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public double Duration { get; set; }
        public int Confidence { get; set; }
        public List<string> Notes { get; set; }
        public LyricsCandidate() { Notes = new List<string>(); }
        public override string ToString()
        {
            return Confidence + "% · " + Source + " · " + (Instrumental ? "instrumental" : Synced ? "time-synced" : "plain text") + (Duration > 0 ? " · " + SongRecognizer.Clock(Duration) : "") + (String.IsNullOrWhiteSpace(Album) ? "" : " · " + Album);
        }
    }
    public sealed class LyricsQuery
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public double Duration { get; set; }
        public bool Confirmed { get; set; }
        public string AudioPath { get; set; }
    }
    public sealed class LyricsSearch
    {
        public LyricsQuery Query { get; set; }
        public List<LyricsCandidate> Candidates { get; set; }
        public List<string> Problems { get; set; }
        public string CheckedUtc { get; set; }
        public LyricsSearch() { Candidates = new List<LyricsCandidate>(); Problems = new List<string>(); }
        internal LyricsCandidate Best { get { return Candidates.FirstOrDefault(); } }
    }

    // Finds lyrics from several sources, synced first, and only trusts ones that match the song's
    // title, artist, and length. Order: file beside the song → lyrics inside the song → LRCLIB → lyrics.ovh.
    internal static class LyricsEngine
    {
        internal const int HighConfidence = 85, CheckConfidence = 75;
        internal static Func<string, string> HttpOverride = null; // tests: url → body, or null for "not found"
        private static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private static string UserAgent { get { return "Digitone v" + AppInfo.Version + " (https://github.com/ItsHazardd/Digitone)"; } }

        private static readonly object pace = new object(); private static DateTime nextCall = DateTime.MinValue;
        // Spaces calls out and retries when a service is briefly busy (LRCLIB answers 503 under load).
        private static string Get(string url, CancellationToken token)
        {
            if (HttpOverride != null) return HttpOverride(url);
            for (int attempt = 0; ; attempt++)
            {
                lock (pace) { var wait = nextCall - DateTime.UtcNow; if (wait > TimeSpan.Zero) token.WaitHandle.WaitOne(wait); nextCall = DateTime.UtcNow.AddMilliseconds(400); }
                token.ThrowIfCancellationRequested();
                try { return GetOnce(url, token); }
                catch (WebException error)
                {
                    var response = error.Response as HttpWebResponse;
                    bool busy = error.Status == WebExceptionStatus.Timeout || (response != null && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500));
                    if (!busy || attempt >= 3 || token.IsCancellationRequested) throw;
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1.5 * (attempt + 1)));
                }
            }
        }
        private static string GetOnce(string url, CancellationToken token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent; request.Accept = "application/json"; request.Timeout = 20000; request.ReadWriteTimeout = 20000; request.AllowAutoRedirect = false; request.UseDefaultCredentials = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (token.Register(request.Abort))
            {
                try { using (var response = (HttpWebResponse)request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd(); }
                catch (WebException error) { var response = error.Response as HttpWebResponse; if (response != null && response.StatusCode == HttpStatusCode.NotFound) return null; throw; }
            }
        }
        private static string Str(Dictionary<string, object> item, string key) { object value; return item.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null; }
        private static double Num(Dictionary<string, object> item, string key) { double value; return Double.TryParse(Str(item, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0; }
        private static string Lines(string text) { return (text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine).Trim(); }

        internal static LyricsCandidate FromLrclib(Dictionary<string, object> item)
        {
            if (item == null || !item.ContainsKey("trackName")) return null;
            string synced = Str(item, "syncedLyrics"), plain = Str(item, "plainLyrics"); bool instrumental = Str(item, "instrumental") == "True";
            if (!instrumental && String.IsNullOrWhiteSpace(synced) && String.IsNullOrWhiteSpace(plain)) return null;
            return new LyricsCandidate
            {
                Source = "LRCLIB", Synced = !String.IsNullOrWhiteSpace(synced), Instrumental = instrumental, Text = instrumental ? "" : Lines(!String.IsNullOrWhiteSpace(synced) ? synced : plain),
                Title = Str(item, "trackName"), Artist = Str(item, "artistName"), Album = Str(item, "albumName"), Duration = Num(item, "duration")
            };
        }
        internal static LyricsCandidate ParseLrclibGet(string body) { return body == null ? null : FromLrclib(json.Deserialize<Dictionary<string, object>>(body)); }
        internal static List<LyricsCandidate> ParseLrclibSearch(string body)
        {
            if (body == null) return new List<LyricsCandidate>();
            var list = json.Deserialize<List<Dictionary<string, object>>>(body) ?? new List<Dictionary<string, object>>();
            return list.Select(FromLrclib).Where(c => c != null).ToList();
        }

        // Scores how surely a result belongs to this exact song. Returns false when it clearly does not.
        internal static bool Score(LyricsCandidate candidate, LyricsQuery query)
        {
            int confidence = 100;
            if (!String.IsNullOrWhiteSpace(candidate.Title))
            {
                double title = SongRecognizer.Similarity(SongRecognizer.CleanTitle(candidate.Title), query.Title);
                if (title < .6 && SongRecognizer.Compact(candidate.Title) != SongRecognizer.Compact(query.Title)) return false;
                if (title < .9 && SongRecognizer.Compact(candidate.Title) != SongRecognizer.Compact(query.Title)) { confidence -= 15; candidate.Notes.Add("The title is slightly different (\"" + candidate.Title + "\")."); }
            }
            if (!String.IsNullOrWhiteSpace(candidate.Artist) && !String.IsNullOrWhiteSpace(query.Artist))
            {
                double artist = SongRecognizer.Similarity(candidate.Artist, query.Artist);
                if (artist < .4) return false;
                if (artist < .8) { confidence -= 15; candidate.Notes.Add("The artist is credited differently (\"" + candidate.Artist + "\")."); }
            }
            if (candidate.Duration > 0 && query.Duration > 0)
            {
                double gap = Math.Abs(candidate.Duration - query.Duration);
                string lengths = "(" + SongRecognizer.Clock(candidate.Duration) + " vs this file's " + SongRecognizer.Clock(query.Duration) + ")";
                if (gap > 10) return false;
                if (gap <= 2) candidate.Notes.Add("Length matches " + lengths + ".");
                else if (gap <= 5) { confidence -= 5; candidate.Notes.Add("Length is close " + lengths + "."); }
                else { confidence -= 20; candidate.Notes.Add("Length differs " + lengths + "; the timing may be off."); }
            }
            else { confidence -= 20; candidate.Notes.Add("This source does not give a song length, so it could be a different version."); }
            if (!candidate.Synced && !candidate.Instrumental) { confidence -= 3; candidate.Notes.Add("Plain text only (no karaoke timing)."); }
            // An "instrumental" entry is usually a different version of a sung song; only trust it when the song says so.
            if (candidate.Instrumental && !Regex.IsMatch((query.Title ?? "") + " " + (query.AudioPath == null ? "" : Path.GetFileName(query.AudioPath)), @"\b(instrumental|karaoke|backing track|no vocals)\b", RegexOptions.IgnoreCase)) { confidence -= 45; candidate.Notes.Add("This result is marked instrumental (no lyrics), which is probably a different version of this song."); }
            if (!query.Confirmed) { confidence -= 10; candidate.Notes.Add("This song's title and artist are not confirmed yet (identify the song for better matches)."); }
            if (!String.IsNullOrWhiteSpace(candidate.Album) && !String.IsNullOrWhiteSpace(query.Album) && SongRecognizer.Similarity(candidate.Album, query.Album) >= .8) { confidence += 2; candidate.Notes.Add("Same album."); }
            candidate.Confidence = Math.Max(0, Math.Min(99, confidence));
            return true;
        }

        internal static LyricsSearch Find(LyricsQuery query, CancellationToken token)
        {
            var search = new LyricsSearch { Query = query, CheckedUtc = DateTime.UtcNow.ToString("o") };
            Action<LyricsCandidate> add = delegate(LyricsCandidate candidate) { if (candidate != null && Score(candidate, query)) search.Candidates.Add(candidate); };
            Func<bool> settled = delegate { return search.Candidates.Any(c => c.Synced && c.Confidence >= HighConfidence); };

            // 1. A .lrc/.txt file with the same name beside the song (the user's own).
            if (!String.IsNullOrEmpty(query.AudioPath))
                foreach (string extension in new[] { ".lrc", ".txt" })
                {
                    string sidecar = Path.ChangeExtension(query.AudioPath, extension);
                    try
                    {
                        if (LocalFiles.IsLocal(sidecar) && File.Exists(sidecar) && new FileInfo(sidecar).Length < 400000)
                        {
                            string text = Lines(File.ReadAllText(sidecar));
                            if (text.Length > 0) { var local = new LyricsCandidate { Source = "File beside the song", Text = text, Synced = LyricsFiles.IsSynced(text), Confidence = 95 }; local.Notes.Add("Found " + Path.GetFileName(sidecar) + " next to the song."); search.Candidates.Add(local); }
                        }
                    }
                    catch (Exception error) { search.Problems.Add("Could not read " + Path.GetFileName(sidecar) + ": " + error.Message); }
                }
            // 2. Lyrics already embedded in the song file's tags.
            if (!String.IsNullOrEmpty(query.AudioPath) && File.Exists(query.AudioPath) && AudioDownloader.ToolsReady)
                try
                {
                    object format, tags; var probe = SongTags.Probe(query.AudioPath);
                    if (probe.TryGetValue("format", out format) && ((Dictionary<string, object>)format).TryGetValue("tags", out tags))
                        foreach (var tag in (Dictionary<string, object>)tags)
                            if (tag.Key.StartsWith("lyrics", StringComparison.OrdinalIgnoreCase) || tag.Key.Equals("UNSYNCEDLYRICS", StringComparison.OrdinalIgnoreCase))
                            {
                                string text = Lines(Convert.ToString(tag.Value));
                                if (text.Length > 20) { var embedded = new LyricsCandidate { Source = "Inside the song file", Text = text, Synced = LyricsFiles.IsSynced(text), Confidence = 90 }; embedded.Notes.Add("Lyrics were already stored in this song's tags."); search.Candidates.Add(embedded); break; }
                            }
                }
                catch (Exception error) { search.Problems.Add("Could not read the song's own tags: " + error.Message); }

            if (!String.IsNullOrWhiteSpace(query.Title) && !String.IsNullOrWhiteSpace(query.Artist))
            {
                string artist = Uri.EscapeDataString(query.Artist.Trim()), title = Uri.EscapeDataString(query.Title.Trim());
                // 3. LRCLIB exact match (title + artist + length), then LRCLIB search.
                try
                {
                    if (query.Duration > 0) add(ParseLrclibGet(Get("https://lrclib.net/api/get?artist_name=" + artist + "&track_name=" + title + "&duration=" + ((int)Math.Round(query.Duration)).ToString(CultureInfo.InvariantCulture), token)));
                    if (!settled())
                        foreach (var found in ParseLrclibSearch(Get("https://lrclib.net/api/search?artist_name=" + artist + "&track_name=" + title, token)).Take(15)) add(found);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { search.Problems.Add("LRCLIB was not reachable: " + error.Message); }
                // 4. lyrics.ovh (plain text, no length to compare).
                if (!search.Candidates.Any(c => c.Confidence >= HighConfidence))
                    try
                    {
                        string body = Get("https://api.lyrics.ovh/v1/" + artist + "/" + title, token);
                        if (body != null)
                        {
                            string text = LyricsLookup.Parse(body);
                            add(new LyricsCandidate { Source = "lyrics.ovh", Text = text.Trim(), Title = query.Title, Artist = query.Artist });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (IOException) { }
                    catch (Exception error) { search.Problems.Add("lyrics.ovh was not reachable: " + error.Message); }
            }
            else search.Problems.Add("This song needs a title and an artist before lyrics can be searched.");

            // Same lyrics from several results: keep the best one of each kind.
            search.Candidates = search.Candidates
                .GroupBy(c => c.Source + "|" + c.Synced + "|" + (c.Text ?? "").GetHashCode())
                .Select(g => g.OrderByDescending(c => c.Confidence).First())
                .OrderByDescending(c => c.Confidence).ThenByDescending(c => c.Synced).Take(8).ToList();
            return search;
        }
    }

    // Lyrics search results per song, so review works offline and after moving the folder.
    internal sealed class LyricsCache
    {
        private readonly string folder;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        internal LyricsCache(string folder) { this.folder = folder; }
        internal static string Key(Track track)
        {
            string basis = !String.IsNullOrEmpty(track.MusicBrainzRecordingId) ? track.MusicBrainzRecordingId : !String.IsNullOrEmpty(track.AudioHash) ? track.AudioHash : (track.Artist + "|" + track.Title + "|" + Path.GetFileName(track.Path));
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(basis.ToLowerInvariant()))).Replace("-", "").Substring(0, 24);
        }
        internal LyricsSearch Get(Track track)
        {
            try { string file = Path.Combine(folder, Key(track) + ".json"); return File.Exists(file) ? json.Deserialize<LyricsSearch>(File.ReadAllText(file, Encoding.UTF8)) : null; }
            catch { return null; }
        }
        internal void Put(Track track, LyricsSearch search)
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, Key(track) + ".json"), temporary = file + ".tmp";
            File.WriteAllText(temporary, json.Serialize(search), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
        }
    }
}
