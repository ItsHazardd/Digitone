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
    // What YouTube (or Spotify, or the file's own tags) said about a song before recognition.
    public sealed class SongHints
    {
        public string Title { get; set; }
        public string Uploader { get; set; }
        public string Channel { get; set; }
        public string Artist { get; set; }
        public string Track { get; set; }
        public string Album { get; set; }
        public string ReleaseYear { get; set; }
        public string Description { get; set; }
        public double Duration { get; set; }
        public string VideoId { get; set; }
        public string SpotifyTitle { get; set; }
        public string SpotifyArtist { get; set; }
        public string FileName { get; set; }
        public string TagTitle { get; set; }
        public string TagArtist { get; set; }
        public string TagAlbum { get; set; }
    }
    public sealed class RecognitionCandidate
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string AlbumArtist { get; set; }
        public string Year { get; set; }
        public string TrackNumber { get; set; }
        public double DurationSeconds { get; set; }
        public string RecordingId { get; set; }
        public string ReleaseId { get; set; }
        public string ReleaseGroupId { get; set; }
        public string ArtistId { get; set; }
        public string AcoustId { get; set; }
        public string Source { get; set; }
        public int Confidence { get; set; }
        public List<string> Notes { get; set; }
        internal double Tiebreak; // agreement with the source info and how "original" the release is; orders equal confidences
        public RecognitionCandidate() { Notes = new List<string>(); }
        public override string ToString()
        {
            string length = DurationSeconds > 0 ? " · " + TimeSpan.FromSeconds(Math.Round(DurationSeconds)).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "";
            return Confidence + "% · " + (Title ?? "?") + " — " + (Artist ?? "?") + (String.IsNullOrEmpty(Album) ? "" : " · " + Album) + (String.IsNullOrEmpty(Year) ? "" : " (" + Year + ")") + length + " · " + Source;
        }
    }
    public sealed class RecognitionResult
    {
        public string AudioHash { get; set; }
        public double FileDuration { get; set; }
        public string Fingerprint { get; set; }
        public SongHints Hints { get; set; }
        public List<RecognitionCandidate> Candidates { get; set; }
        public string Error { get; set; }
        public string CheckedUtc { get; set; }
        public RecognitionResult() { Candidates = new List<RecognitionCandidate>(); }
        internal RecognitionCandidate Best { get { return Candidates.OrderByDescending(c => c.Confidence).ThenByDescending(c => c.Tiebreak).FirstOrDefault(); } }
    }

    // Identifies songs from their audio (Chromaprint fingerprint → AcoustID → MusicBrainz),
    // falling back to YouTube/Spotify/file information searched on MusicBrainz.
    internal static class SongRecognizer
    {
        internal const int HighConfidence = 85, MediumConfidence = 60;
        internal static string UserAgent { get { return "Digitone/" + AppInfo.Version + " ( https://github.com/ItsHazardd/Digitone )"; } }
        private static readonly object rateGate = new object();
        private static readonly Dictionary<string, DateTime> nextCall = new Dictionary<string, DateTime>();
        private static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        // ---------- Audio ----------
        internal static string Fingerprint(string path, out double duration)
        {
            duration = 0;
            var probe = SongTags.Probe(path); object format, value;
            if (probe.TryGetValue("format", out format) && ((Dictionary<string, object>)format).TryGetValue("duration", out value)) Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
            string fingerprint = Encoding.ASCII.GetString(SongTags.Run("ffmpeg.exe", new[] { "-v", "error", "-nostdin", "-protocol_whitelist", "file,pipe", "-i", path, "-t", "120", "-map", "0:a:0", "-f", "chromaprint", "-fp_format", "base64", "pipe:1" })).Trim();
            if (fingerprint.Length < 20) throw new IOException("This file is too short or silent to fingerprint.");
            return fingerprint;
        }

        // ---------- Web ----------
        private static void Wait(string host, int milliseconds)
        {
            lock (rateGate)
            {
                DateTime allowed; if (nextCall.TryGetValue(host, out allowed) && allowed > DateTime.UtcNow) Thread.Sleep(allowed - DateTime.UtcNow);
                nextCall[host] = DateTime.UtcNow.AddMilliseconds(milliseconds);
            }
        }
        internal static Func<string, string, string> HttpOverride = null; // tests: (url, postBody) → response
        private static string Http(string url, string post, CancellationToken token)
        {
            if (HttpOverride != null) return HttpOverride(url, post);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var uri = new Uri(url); Wait(uri.Host, uri.Host.EndsWith("musicbrainz.org") ? 1100 : 350);
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent = UserAgent; request.Accept = "application/json"; request.Timeout = 20000; request.ReadWriteTimeout = 20000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; request.UseDefaultCredentials = false; request.AllowAutoRedirect = false;
            if (post != null)
            {
                request.Method = "POST"; request.ContentType = "application/x-www-form-urlencoded";
                byte[] body = Encoding.UTF8.GetBytes(post); request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            }
            using (token.Register(request.Abort))
            {
                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
                }
                catch (WebException error)
                {
                    var response = error.Response as HttpWebResponse;
                    if (response != null) using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) { string text = reader.ReadToEnd(); if (text.TrimStart().StartsWith("{")) return text; }
                    throw;
                }
            }
        }
        private static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        private static IEnumerable<Dictionary<string, object>> List(Dictionary<string, object> parent, string key) { object value; return parent.TryGetValue(key, out value) && value is System.Collections.IEnumerable && !(value is string) ? ((System.Collections.IEnumerable)value).OfType<Dictionary<string, object>>() : Enumerable.Empty<Dictionary<string, object>>(); }
        private static string Str(Dictionary<string, object> parent, string key) { object value; return parent.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null; }
        private static double Num(Dictionary<string, object> parent, string key) { double result; return Double.TryParse(Str(parent, key), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : 0; }
        private static string Credit(Dictionary<string, object> parent, out string firstArtistId)
        {
            firstArtistId = null; var text = new StringBuilder();
            foreach (var credit in List(parent, "artist-credit")) { text.Append(Str(credit, "name") ?? Str(Obj(credit.ContainsKey("artist") ? credit["artist"] : null), "name")).Append(Str(credit, "joinphrase") ?? ""); if (firstArtistId == null) firstArtistId = Str(Obj(credit.ContainsKey("artist") ? credit["artist"] : null), "id"); }
            if (text.Length == 0) foreach (var artist in List(parent, "artists")) { if (text.Length > 0) text.Append(", "); text.Append(Str(artist, "name")); if (firstArtistId == null) firstArtistId = Str(artist, "id"); }
            return text.ToString().Trim();
        }

        // ---------- AcoustID ----------
        internal sealed class FingerprintMatch { internal string AcoustId, RecordingId, Title, Artist, ArtistId; internal double Score, Duration; internal List<string> ReleaseGroupTitles = new List<string>(); }
        internal static List<FingerprintMatch> LookupAcoustId(string key, string fingerprint, double duration, CancellationToken token)
        {
            string body = "client=" + Uri.EscapeDataString(key) + "&format=json&meta=recordings+releasegroups+compress&duration=" + ((int)Math.Round(duration)).ToString(CultureInfo.InvariantCulture) + "&fingerprint=" + Uri.EscapeDataString(fingerprint);
            return ParseAcoustId(Http("https://api.acoustid.org/v2/lookup", body, token));
        }
        internal static List<FingerprintMatch> ParseAcoustId(string text)
        {
            var root = json.Deserialize<Dictionary<string, object>>(text);
            if (Str(root, "status") != "ok")
            {
                var error = Obj(root.ContainsKey("error") ? root["error"] : null); string code = Str(error, "code");
                if (code == "4") throw new InvalidOperationException("The AcoustID application key is not valid. Check it in Settings > Song recognition.");
                throw new IOException("AcoustID could not look this song up: " + (Str(error, "message") ?? "unknown error") + ".");
            }
            var matches = new List<FingerprintMatch>();
            foreach (var result in List(root, "results"))
                foreach (var recording in List(result, "recordings"))
                {
                    string artistId; var match = new FingerprintMatch { AcoustId = Str(result, "id"), Score = Num(result, "score"), RecordingId = Str(recording, "id"), Title = Str(recording, "title"), Duration = Num(recording, "duration") };
                    match.Artist = Credit(recording, out artistId); match.ArtistId = artistId;
                    match.ReleaseGroupTitles.AddRange(List(recording, "releasegroups").Select(g => Str(g, "title")).Where(t => !String.IsNullOrEmpty(t)));
                    if (!String.IsNullOrEmpty(match.RecordingId) && !String.IsNullOrEmpty(match.Title)) matches.Add(match);
                }
            return matches;
        }

        // ---------- MusicBrainz ----------
        internal sealed class Recording { internal string Id, Title, Artist, ArtistId, FirstReleaseDate; internal double Length; internal int SearchScore; internal List<Release> Releases = new List<Release>(); }
        internal sealed class Release { internal string Id, Title, Status, Date, GroupId, PrimaryType, Artist, TrackNumber, Format;internal List<string> SecondaryTypes = new List<string>(); }
        private static Recording ParseRecording(Dictionary<string, object> item)
        {
            string artistId; var recording = new Recording { Id = Str(item, "id"), Title = Str(item, "title"), Length = Num(item, "length") / 1000.0, FirstReleaseDate = Str(item, "first-release-date"), SearchScore = (int)Num(item, "score") };
            recording.Artist = Credit(item, out artistId); recording.ArtistId = artistId;
            foreach (var release in List(item, "releases"))
            {
                var group = Obj(release.ContainsKey("release-group") ? release["release-group"] : null); string releaseArtistId;
                var parsed = new Release { Id = Str(release, "id"), Title = Str(release, "title"), Status = Str(release, "status"), Date = Str(release, "date"), GroupId = Str(group, "id"), PrimaryType = Str(group, "primary-type") };
                parsed.Artist = Credit(release, out releaseArtistId);
                object secondary; if (group.TryGetValue("secondary-types", out secondary) && secondary is System.Collections.IEnumerable) parsed.SecondaryTypes.AddRange(((System.Collections.IEnumerable)secondary).OfType<object>().Select(Convert.ToString));
                foreach (var medium in List(release, "media"))
                {
                    var tracks = List(medium, "tracks").Concat(List(medium, "track")).ToList();
                    if (tracks.Count > 0)
                    {
                        string number = Str(tracks[0], "number"); int numeric;
                        parsed.TrackNumber = number != null && Int32.TryParse(number, out numeric) ? number : Str(tracks[0], "position");
                        parsed.Format = Str(medium, "format"); break;
                    }
                }
                recording.Releases.Add(parsed);
            }
            return recording;
        }
        // MusicBrainz answers errors (like "slow down") as JSON too; never treat those as an empty song.
        private static Dictionary<string, object> MusicBrainzJson(string text, string requiredKey)
        {
            var root = json.Deserialize<Dictionary<string, object>>(text) ?? new Dictionary<string, object>();
            if (!root.ContainsKey(requiredKey)) throw new IOException("MusicBrainz: " + (Str(root, "error") ?? "unexpected reply"));
            return root;
        }
        internal static Recording ParseRecordingJson(string text) { return ParseRecording(MusicBrainzJson(text, "id")); }
        internal static List<Recording> ParseSearchJson(string text) { return List(MusicBrainzJson(text, "recordings"), "recordings").Select(ParseRecording).ToList(); }
        private static T MusicBrainz<T>(string url, Func<string, T> parse, CancellationToken token)
        {
            try { return parse(Http(url, null, token)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { token.WaitHandle.WaitOne(2500); token.ThrowIfCancellationRequested(); return parse(Http(url, null, token)); }
        }
        internal static Recording LookupRecording(string id, CancellationToken token)
        {
            return MusicBrainz("https://musicbrainz.org/ws/2/recording/" + Uri.EscapeDataString(id) + "?inc=artist-credits+releases+release-groups+media&fmt=json", ParseRecordingJson, token);
        }
        private static string Lucene(string value) { return "\"" + Regex.Replace(value ?? "", @"([+\-!(){}\[\]^""~*?:\\/]|&&|\|\|)", @"\$1") + "\""; }
        internal static List<Recording> SearchRecordings(string artist, string title, CancellationToken token)
        {
            string query = "recording:" + Lucene(title) + (String.IsNullOrWhiteSpace(artist) ? "" : " AND artist:" + Lucene(artist));
            return MusicBrainz("https://musicbrainz.org/ws/2/recording?limit=25&fmt=json&query=" + Uri.EscapeDataString(query), ParseSearchJson, token);
        }
        // Prefers the original official album over singles, compilations, and live releases.
        internal static Release PickRelease(IEnumerable<Release> releases, string albumHint)
        {
            return releases.OrderByDescending(r =>
            {
                double score = 0;
                if (r.Status == "Official") score += 3;
                if (r.PrimaryType == "Album") score += 3; else if (r.PrimaryType == "EP") score += 1.5; else if (r.PrimaryType == "Single") score += 1;
                score -= 3 * r.SecondaryTypes.Count(t => new[] { "Compilation", "Live", "Soundtrack", "Remix", "DJ-mix", "Mixtape/Street" }.Contains(t));
                if (!String.IsNullOrWhiteSpace(albumHint) && Similarity(albumHint, r.Title) >= .8) score += 5;
                if (r.Format == "CD" || r.Format == "Digital Media") score += .5;
                int year; if (r.Date != null && r.Date.Length >= 4 && Int32.TryParse(r.Date.Substring(0, 4), out year)) score += (3000 - year) / 1000.0;
                return score;
            }).FirstOrDefault();
        }

        // How "original" a recording looks: official albums and singles without compilation/live tags.
        internal static int StudioScore(Recording recording)
        {
            return recording.Releases.Count(r => r.Status == "Official" && (r.PrimaryType == "Album" || r.PrimaryType == "Single" || r.PrimaryType == "EP") && r.SecondaryTypes.Count == 0);
        }

        // ---------- Text ----------
        private static readonly Regex Noise = new Regex(@"\s*[\(\[\{][^\)\]\}]*\b(official|video|audio|lyrics?|lyric video|visuali[sz]er|hd|hq|4k|mv|m/v|music video|explicit|clean|remaster(ed)?(\s+\d{4})?|\d{4}\s+remaster(ed)?|full song|color coded)\b[^\)\]\}]*[\)\]\}]", RegexOptions.IgnoreCase);
        private static readonly Regex Featuring = new Regex(@"\s*[\(\[]?\s*\b(feat\.?|ft\.?|featuring)\s+[^\)\]]*[\)\]]?", RegexOptions.IgnoreCase);
        internal static string CleanTitle(string title)
        {
            string value = Track.CleanTitle(title ?? "");
            for (int i = 0; i < 3; i++) value = Noise.Replace(value, "");
            value = Regex.Replace(value, @"\s*[-|–—]\s*(official\s+)?(music\s+)?(video|audio|lyrics?|visuali[sz]er)\s*$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(value, @"\s{2,}", " ").Trim(' ', '-', '|', '–', '—');
        }
        internal static string CleanArtist(string artist)
        {
            string value = Regex.Replace(artist ?? "", @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"VEVO$", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\s*(official)(\s+(channel|music))?$", "", RegexOptions.IgnoreCase);
            // "Nova Reed, Nova Reed, Nova Reed" → "Nova Reed"
            var names = value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
            if (names.Count > 1) value = String.Join(", ", names.Distinct(StringComparer.OrdinalIgnoreCase));
            return value.Trim();
        }
        private static HashSet<string> Tokens(string value)
        {
            string text = Normalize(value);
            return new HashSet<string>(Regex.Split(text,@"[^\p{L}\p{N}]+").Where(t => t.Length > 0 && t != "the" && t != "and" && t != "x"));
        }
        // Like Compact but keeps bracketed version labels, so "Let's Groove (instrumental)" stays a separate song from "Let's Groove".
        internal static string VersionKey(string title) { return Regex.Replace((title ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", ""); }
        private static readonly Regex VersionWords = new Regex(@"\b(instrumental|karaoke|a\s?cappella|acapella|remix|live|acoustic|demo|slowed|sped\s?up|nightcore|reverb|8d|cover|edit|mix|version|rehearsal|session)\b", RegexOptions.IgnoreCase);
        // A special version (instrumental, live, remix…) is only likely if the download's own name says so.
        internal static void PenalizeVersion(RecognitionCandidate candidate, SongHints hints)
        {
            var found = VersionWords.Matches(candidate.Title ?? "").Cast<Match>().Select(m => m.Value.ToLowerInvariant()).Where(w => w != "version" && w != "mix" && w != "edit").ToList();
            if (found.Count == 0) return;
            string said = hints == null ? "" : String.Join(" ", new[] { hints.Title, hints.Track, hints.TagTitle, hints.FileName, hints.SpotifyTitle, hints.Description == null ? null : hints.Description.Split('\n').FirstOrDefault() }.Where(s => s != null)).ToLowerInvariant();
            foreach (string word in found.Distinct())
                if (!said.Contains(word)) { candidate.Confidence -= 15; candidate.Notes.Add("This match is a " + word + " version, but nothing about your file says " + word + ". Check it is the right one."); break; }
        }
        // Letters and digits only, so "Free Bird" and "Freebird" compare equal.
        internal static string Compact(string value) { return string.Concat(Regex.Split(Normalize(value), @"[^\p{L}\p{N}]+")); }
        private static string Normalize(string value)
        {
            string text = (value ?? "").ToLowerInvariant().Replace("&", " and ").Replace("+", " and ");
            text = Featuring.Replace(text, " "); text = Regex.Replace(text, @"[\(\[][^\)\]]*[\)\]]", " ");
            return new string(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        }
        internal static double Similarity(string first, string second)
        {
            var a = Tokens(first); var b = Tokens(second);
            if (a.Count == 0 || b.Count == 0) return 0;
            return a.Intersect(b).Count() / (double)Math.Max(a.Count, b.Count);
        }
        // "Lynyrd Skynyrd - Free Bird" with artist "Lynyrd Skynyrd" → "Free Bird".
        internal static string WithoutArtist(string title, string artist)
        {
            var split = Regex.Match(title ?? "", @"^(.+?)\s+[-–—|]\s+(.+)$");
            return split.Success && Similarity(split.Groups[1].Value, artist) >= .6 ? split.Groups[2].Value.Trim() : title;
        }
        internal sealed class ParsedHint { internal string Artist, Title, Album, Year, Kind; internal int Quality; }
        // Best guesses from what the download source said, strongest first.
        internal static List<ParsedHint> ParseHints(SongHints hints)
        {
            var parsed = new List<ParsedHint>();
            if (hints == null) return parsed;
            if (!String.IsNullOrWhiteSpace(hints.SpotifyTitle) && !String.IsNullOrWhiteSpace(hints.SpotifyArtist)) parsed.Add(new ParsedHint { Title = hints.SpotifyTitle, Artist = hints.SpotifyArtist, Kind = "Spotify info", Quality = 80 });
            if (!String.IsNullOrWhiteSpace(hints.Track) && !String.IsNullOrWhiteSpace(hints.Artist)) parsed.Add(new ParsedHint { Title = WithoutArtist(hints.Track, hints.Artist), Artist = hints.Artist.Split(',')[0].Trim(), Album = hints.Album, Year = hints.ReleaseYear, Kind = "YouTube Music info", Quality = 80 });
            if (!String.IsNullOrWhiteSpace(hints.Description))
            {
                var lines = hints.Description.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                if (lines.Count >= 2 && lines[0].StartsWith("Provided to YouTube by", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = lines[1].Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var hint = new ParsedHint { Title = parts[0].Trim(), Artist = parts[1].Trim(), Kind = "YouTube Music description", Quality = 78 };
                        if (lines.Count >= 3 && !lines[2].StartsWith("℗") && !lines[2].StartsWith("©") && !lines[2].StartsWith("Released on", StringComparison.OrdinalIgnoreCase)) hint.Album = lines[2];
                        var released = lines.Select(l => Regex.Match(l, @"^Released on:\s*(\d{4})")).FirstOrDefault(m => m.Success); if (released != null) hint.Year = released.Groups[1].Value;
                        parsed.Add(hint);
                    }
                }
            }
            string title = hints.Title;
            if (!String.IsNullOrWhiteSpace(title))
            {
                string channel = CleanArtist(hints.Channel ?? hints.Uploader);
                var split = Regex.Match(Track.CleanTitle(title), @"^(.+?)\s+[-–—|]\s+(.+)$");
                if (split.Success) parsed.Add(new ParsedHint { Artist = CleanArtist(split.Groups[1].Value), Title = CleanTitle(split.Groups[2].Value), Kind = "YouTube title", Quality = 60 });
                if (!String.IsNullOrWhiteSpace(channel)) parsed.Add(new ParsedHint { Artist = channel, Title = CleanTitle(split.Success ? split.Groups[2].Value : title), Kind = "YouTube title and channel", Quality = (hints.Channel ?? hints.Uploader ?? "").EndsWith("- Topic", StringComparison.OrdinalIgnoreCase) ? 72 : 45 });
            }
            if (!String.IsNullOrWhiteSpace(hints.TagTitle) && !String.IsNullOrWhiteSpace(hints.TagArtist))
            {
                string tagTitle = CleanTitle(hints.TagTitle), withoutArtist = WithoutArtist(tagTitle, hints.TagArtist);
                parsed.Add(new ParsedHint { Title = withoutArtist, Artist = CleanArtist(hints.TagArtist), Album = hints.TagAlbum, Kind = "current song details", Quality = 55 });
                // Title "Ivy Lane & The Static - Paper Moons" with the uploader as artist: the real artist is inside the title.
                var inside = Regex.Match(tagTitle, @"^(.+?)\s+[-–—|]\s+(.+)$");
                if (withoutArtist == tagTitle && inside.Success) parsed.Add(new ParsedHint { Artist = CleanArtist(inside.Groups[1].Value), Title = inside.Groups[2].Value.Trim(), Kind = "current song title", Quality = 58 });
            }
            if (!String.IsNullOrWhiteSpace(hints.FileName))
            {
                var split = Regex.Match(CleanTitle(Path.GetFileNameWithoutExtension(hints.FileName)), @"^(?:\d+\s*[-.]\s*)?(.+?)\s+-\s+(.+)$");
                if (split.Success) parsed.Add(new ParsedHint { Artist = split.Groups[1].Value.Trim(), Title = split.Groups[2].Value.Trim(), Kind = "file name", Quality = 50 });
            }
            return parsed.Where(p => !String.IsNullOrWhiteSpace(p.Title)).GroupBy(p => (p.Artist ?? "").ToLowerInvariant() + "|" + p.Title.ToLowerInvariant()).Select(g => g.OrderByDescending(p => p.Quality).First()).OrderByDescending(p => p.Quality).ToList();
        }

        // ---------- Scoring ----------
        private static void ScoreLength(RecognitionCandidate candidate, double fileDuration, double candidateDuration, int unknownPenalty = 10)
        {
            if (fileDuration <= 0 || candidateDuration <= 0) { candidate.Confidence -= unknownPenalty; candidate.Notes.Add("Song length could not be compared."); return; }
            double difference = Math.Abs(fileDuration - candidateDuration);
            string lengths = "(" + Clock(candidateDuration) + " vs this file's " + Clock(fileDuration) + ")";
            if (difference <= 3) candidate.Notes.Add("Length matches " + lengths + ".");
            else if (difference <= 7) { candidate.Confidence -= 10; candidate.Notes.Add("Length is close " + lengths + "."); }
            else if (difference <= 15) { candidate.Confidence -= 25; candidate.Notes.Add("Length differs " + lengths + ". This may be a different version."); }
            else { candidate.Confidence -= 45; candidate.Notes.Add("Length is very different " + lengths + ". Probably a different version or song."); }
        }
        internal static string Clock(double seconds) { return TimeSpan.FromSeconds(Math.Round(seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss"); }
        private static void CompareWithHints(RecognitionCandidate candidate, List<ParsedHint> hints)
        {
            var hint = hints.FirstOrDefault(); if (hint == null) return;
            double title = Similarity(hint.Title, candidate.Title), artist = String.IsNullOrWhiteSpace(hint.Artist) ? .5 : Math.Max(Similarity(hint.Artist, candidate.Artist), hints.Max(h => Similarity(h.Artist, candidate.Artist)));
            candidate.Tiebreak += title + artist;
            if (title >= .6 && artist >= .5) { candidate.Confidence += 5; candidate.Notes.Add("Agrees with the " + hint.Kind + "."); }
            else if (title < .3 && hints.All(h => Similarity(h.Title, candidate.Title) < .3)) { candidate.Confidence -= 12; candidate.Notes.Add("Differs from the " + hint.Kind + " (\"" + hint.Title + "\"). Check this one."); }
        }
        private static RecognitionCandidate FromRecording(Recording recording, string source, string albumHint)
        {
            var release = PickRelease(recording.Releases, albumHint);
            string year = recording.FirstReleaseDate != null && recording.FirstReleaseDate.Length >= 4 ? recording.FirstReleaseDate.Substring(0, 4) : release != null && release.Date != null && release.Date.Length >= 4 ? release.Date.Substring(0, 4) : null;
            return new RecognitionCandidate
            {
                Title = recording.Title, Artist = recording.Artist, ArtistId = recording.ArtistId, RecordingId = recording.Id, DurationSeconds = recording.Length,
                Album = release != null ? release.Title : null, AlbumArtist = release != null && !String.IsNullOrEmpty(release.Artist) && release.Artist != recording.Artist ? release.Artist : null,
                ReleaseId = release != null ? release.Id : null, ReleaseGroupId = release != null ? release.GroupId : null, TrackNumber = release != null ? release.TrackNumber : null, Year = year, Source = source
            };
        }

        // Manual search from the review window.
        internal static List<RecognitionCandidate> Search(string artist, string title, double fileDuration, CancellationToken token)
        {
            var list = new List<RecognitionCandidate>();
            foreach (var recording in SearchRecordings(artist, title, token).Take(8))
            {
                var candidate = FromRecording(recording, "Your search + MusicBrainz", null);
                candidate.Confidence = (int)Math.Round(70 * recording.SearchScore / 100.0);
                candidate.Notes.Add("Found by searching MusicBrainz for \"" + (artist ?? "") + " - " + title + "\".");
                ScoreLength(candidate, fileDuration, recording.Length, 15);
                candidate.Confidence = Math.Max(0, Math.Min(HighConfidence - 1, candidate.Confidence));
                list.Add(candidate);
            }
            return list.OrderByDescending(c => c.Confidence).ToList();
        }

        // Full recognition for one file. Never throws for "no match"; Error explains network or key problems.
        internal static RecognitionResult Recognize(string path, SongHints hints, string acoustIdKey, CancellationToken token, RecognitionResult cached = null)
        {
            var result = new RecognitionResult { Hints = hints, CheckedUtc = DateTime.UtcNow.ToString("o") };
            var parsedHints = ParseHints(hints);
            try
            {
                if (cached != null && !String.IsNullOrEmpty(cached.Fingerprint) && cached.FileDuration > 0) { result.Fingerprint = cached.Fingerprint; result.FileDuration = cached.FileDuration; }
                else { double duration; result.Fingerprint = Fingerprint(path, out duration); result.FileDuration = duration; }
            }
            catch (Exception error) { result.Error = "Could not read the audio: " + error.Message; }

            // 1. Fingerprint.
            if (!String.IsNullOrEmpty(result.Fingerprint) && !String.IsNullOrWhiteSpace(acoustIdKey))
            {
                try
                {
                    var matches = LookupAcoustId(acoustIdKey, result.Fingerprint, result.FileDuration, token);
                    // One group per song ("Free Bird" and "Freebird" are the same song); each group holds that song's recordings.
                    var groups = matches.GroupBy(m => VersionKey(m.Title) + "|" + Compact(m.Artist))
                        .Select(g => new { Members = g.OrderByDescending(m => m.Score).ThenBy(m => m.Duration > 0 ? Math.Abs(m.Duration - result.FileDuration) : 999).ToList(), Score = g.Max(m => m.Score) })
                        .OrderByDescending(g => g.Score).Take(3).ToList();
                    string albumHint = parsedHints.Select(h => h.Album).FirstOrDefault(a => a != null);
                    var scores = new Dictionary<RecognitionCandidate, double>();
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var match = groups[i].Members[0]; RecognitionCandidate candidate = null;
                        // For the best song, compare a few of its recordings and prefer a studio release of the same length.
                        var options = groups[i].Members.Where(m => m.Duration <= 0 || result.FileDuration <= 0 || Math.Abs(m.Duration - result.FileDuration) <= 7).GroupBy(m => m.RecordingId).Select(r => r.First()).Take(i == 0 ? 3 : 1).ToList();
                        if (options.Count == 0) options.Add(match);
                        Recording chosen = null;
                        foreach (var option in options)
                        {
                            try
                            {
                                var recording = LookupRecording(option.RecordingId, token);
                                if (chosen == null || StudioScore(recording) > StudioScore(chosen)) { chosen = recording; match = option; }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch { }
                        }
                        if (chosen != null) { candidate = FromRecording(chosen, "Audio fingerprint", albumHint ?? match.ReleaseGroupTitles.FirstOrDefault()); candidate.Tiebreak = .1 * Math.Min(5, StudioScore(chosen)); }
                        else { candidate = new RecognitionCandidate { Title = match.Title, Artist = match.Artist, ArtistId = match.ArtistId, RecordingId = match.RecordingId, DurationSeconds = match.Duration, Album = match.ReleaseGroupTitles.FirstOrDefault(), Source = "Audio fingerprint" }; candidate.Notes.Add("MusicBrainz details were not available, so album and track number may be missing."); }
                        if (String.IsNullOrWhiteSpace(candidate.Title)) continue;
                        candidate.AcoustId = match.AcoustId;
                        candidate.Confidence = (int)Math.Round(groups[i].Score * 100);
                        candidate.Notes.Insert(0, "Audio fingerprint match: " + candidate.Confidence + "%.");
                        ScoreLength(candidate, result.FileDuration, candidate.DurationSeconds > 0 ? candidate.DurationSeconds : match.Duration);
                        scores[candidate] = groups[i].Score;
                        result.Candidates.Add(candidate);
                    }
                    foreach (var candidate in scores.Keys)
                    {
                        // Only truly different songs make a fingerprint ambiguous; other credits for the same title barely do.
                        var rivals = scores.Where(o => o.Key != candidate && o.Value >= scores[candidate] - .1).Select(o => o.Key).ToList();
                        Func<RecognitionCandidate, bool> otherTitle = o => Similarity(o.Title, candidate.Title) < .6 && Compact(o.Title) != Compact(candidate.Title);
                        if (rivals.Any(o => otherTitle(o) && Similarity(o.Artist, candidate.Artist) < .5)) { candidate.Confidence -= 20; candidate.Notes.Add("Several different songs match this audio closely."); }
                        else if (rivals.Any(otherTitle)) { candidate.Confidence -= 3; candidate.Notes.Add("MusicBrainz also lists this recording under another title (\"" + rivals.First(otherTitle).Title + "\")."); }
                        else if (rivals.Count > 0) { candidate.Confidence -= 3; candidate.Notes.Add("MusicBrainz also lists this song under another credit (" + rivals[0].Artist + ")."); }
                        PenalizeVersion(candidate, hints);
                        CompareWithHints(candidate, parsedHints);
                        candidate.Confidence = Math.Max(0, Math.Min(99, candidate.Confidence));
                    }
                    if (groups.Count == 0) result.Error = "The audio fingerprint is not in the AcoustID database yet.";
                }
                catch (OperationCanceledException) { throw; }
                catch (InvalidOperationException error) { result.Error = error.Message; }
                catch (Exception error) { result.Error = "AcoustID was not reachable: " + error.Message; }
            }
            else if (String.IsNullOrWhiteSpace(acoustIdKey)) result.Error = "No AcoustID key is set, so the audio could not be fingerprinted online.";

            // 2. Fallback: what YouTube/Spotify/the file said, checked against MusicBrainz.
            if (result.Best == null || result.Best.Confidence < HighConfidence)
            {
                foreach (var hint in parsedHints.Take(3))
                {
                    List<Recording> found;
                    try { found = SearchRecordings(hint.Artist, hint.Title, token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { if (result.Error == null) result.Error = "MusicBrainz was not reachable: " + error.Message; break; }
                    // Closest length first, so extended and edited versions find their own recording.
                    Func<Recording, int> lengthBucket = delegate(Recording r) { double gap = r.Length > 0 && result.FileDuration > 0 ? Math.Abs(r.Length - result.FileDuration) : 9999; return gap <= 3 ? 0 : gap <= 7 ? 1 : gap <= 15 ? 2 : 3; };
                    var close = found.Where(r => r.SearchScore >= 70).OrderBy(lengthBucket).ThenByDescending(StudioScore).ThenByDescending(r => r.SearchScore).Take(4);
                    foreach (var recording in close)
                    {
                        double titleMatch = Similarity(hint.Title, recording.Title), artistMatch = String.IsNullOrWhiteSpace(hint.Artist) ? .5 : Similarity(hint.Artist, recording.Artist);
                        if (titleMatch < .5) continue;
                        var candidate = FromRecording(recording, hint.Kind + " + MusicBrainz", hint.Album);
                        candidate.Confidence = (int)Math.Round(hint.Quality * recording.SearchScore / 100.0);
                        candidate.Notes.Add("Found on MusicBrainz from the " + hint.Kind + " (\"" + (hint.Artist ?? "") + " - " + hint.Title + "\").");
                        if (artistMatch < .5) { candidate.Confidence -= 30; candidate.Notes.Add("The artist differs from the " + hint.Kind + "."); }
                        if (recording.Releases.Count > 0 && recording.Releases.All(r => r.SecondaryTypes.Contains("Live")) && !Regex.IsMatch(hint.Title + " " + (hints != null ? hints.Title : ""), @"\blive\b", RegexOptions.IgnoreCase)) { candidate.Confidence -= 25; candidate.Notes.Add("This is a live recording; the download may be the studio version."); }
                        else if (recording.Releases.Count > 0 && StudioScore(recording) <= 0) { candidate.Confidence -= 8; candidate.Notes.Add("This recording only appears on compilations, so the album may not be the original."); }
                        double before = candidate.Confidence; ScoreLength(candidate, result.FileDuration, recording.Length, 15);
                        bool exact = recording.Length > 0 && Math.Abs(recording.Length - result.FileDuration) <= 2 && titleMatch >= .9 && artistMatch >= .9;
                        if (exact) candidate.Confidence += 8;
                        candidate.Confidence = Math.Max(0, Math.Min(exact && hint.Quality >= 78 ? 88 : HighConfidence - 1, candidate.Confidence));
                        if (!result.Candidates.Any(c => c.RecordingId == candidate.RecordingId)) result.Candidates.Add(candidate);
                    }
                    if (result.Candidates.Any(c => c.Confidence >= MediumConfidence)) break;
                }
                // Search results only list some releases; fetch the full list for the best guess to find its original album.
                var top = result.Candidates.Where(c => c.Source.EndsWith("+ MusicBrainz") && c.RecordingId != null && c.Confidence >= 40).OrderByDescending(c => c.Confidence).FirstOrDefault();
                if (top != null)
                    try
                    {
                        var full = FromRecording(LookupRecording(top.RecordingId, token), top.Source, null);
                        top.Album = full.Album ?? top.Album; top.AlbumArtist = full.AlbumArtist; top.ReleaseId = full.ReleaseId ?? top.ReleaseId; top.ReleaseGroupId = full.ReleaseGroupId ?? top.ReleaseGroupId; top.TrackNumber = full.TrackNumber ?? top.TrackNumber; top.Year = full.Year ?? top.Year;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
            }
            // 3. Nothing online: offer the cleaned-up source info itself, always for review.
            foreach (var hint in parsedHints.Take(2))
                if (!result.Candidates.Any(c => Similarity(c.Title, hint.Title) >= .9 && Similarity(c.Artist, hint.Artist) >= .9))
                {
                    var candidate = new RecognitionCandidate { Title = hint.Title, Artist = hint.Artist, Album = hint.Album, Year = hint.Year, Source = hint.Kind, Confidence = Math.Min(45, hint.Quality / 2 + 5) };
                    candidate.Notes.Add("Taken from the " + hint.Kind + " only; not confirmed by the audio or MusicBrainz.");
                    result.Candidates.Add(candidate);
                }
            result.Candidates = result.Candidates.OrderByDescending(c => c.Confidence).ThenByDescending(c => c.Tiebreak).ToList();
            return result;
        }
    }
}
