using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;

namespace Digitone
{
    internal sealed class ArtworkMatch { internal byte[] Image; internal string Description; }
    internal static class ArtworkFinder
    {
        private static readonly object networkGate = new object();
        private static DateTime lastSearch = DateTime.MinValue;
        internal static ArtworkMatch Local(string song)
        {
            if (!LocalFiles.IsLocal(song)) return null;
            foreach (string name in new[] { "cover", "folder", "front", "album" })
                foreach (string extension in new[] { ".jpg", ".jpeg", ".png" })
                {
                    string path = Path.Combine(Path.GetDirectoryName(song), name + extension);
                    if (!LocalFiles.IsLocal(path) || !File.Exists(path)) continue;
                    try { return new ArtworkMatch { Image = SongTags.LoadCover(path), Description = "Local picture: " + Path.GetFileName(path) }; } catch { }
                }
            return null;
        }
        internal static bool Allowed(Uri uri)
        {
            return uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo) && (uri.Host == "musicbrainz.org" || uri.Host == "coverartarchive.org" || uri.Host == "archive.org" || uri.Host.EndsWith(".archive.org", StringComparison.OrdinalIgnoreCase) || new[] { "mzstatic.com", "scdn.co", "tidal.com", "qobuz.com", "bcbits.com", "vgm.io", "discogs.com", "metal-archives.com", "bugsm.co.kr", "gaanacdn.com", "melon.co.kr", "recochoku.jp", "ototoy.jp", "lastfm.freetls.fastly.net", "sndcdn.com", "music.126.net", "kugou.com" }.Any(host => uri.Host == host || uri.Host.EndsWith("." + host,StringComparison.OrdinalIgnoreCase)));
        }
        internal static byte[] Request(Uri uri, CancellationToken token)
        {
            for (int redirect = 0; redirect < 6; redirect++)
            {
                token.ThrowIfCancellationRequested(); if (!Allowed(uri)) throw new IOException("Artwork service redirected to an unsupported address.");
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(uri); request.AllowAutoRedirect = false; request.UserAgent = "DigitoneMusicPlayer/1.0"; request.Timeout = 15000; request.ReadWriteTimeout = 15000; request.UseDefaultCredentials = false;
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    if (status >= 300 && status < 400) { var next = new Uri(uri,response.Headers["Location"]); if (next.Scheme == "http") next = new UriBuilder(next) { Scheme = "https", Port = -1 }.Uri; uri = next; continue; }
                    if (response.ContentLength > 6 * 1024 * 1024) throw new IOException("Artwork response is too large.");
                    using (var input = response.GetResponseStream()) using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192]; int count;
                        while ((count = input.Read(buffer,0,buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); if (output.Length + count > 6 * 1024 * 1024) throw new IOException("Artwork response is too large."); output.Write(buffer,0,count); }
                        return output.ToArray();
                    }
                }
            }
            throw new IOException("Too many artwork redirects.");
        }
        private static string Quote(string text) { return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
        internal static string Query(string artist, string album, string title)
        {
            if (String.IsNullOrWhiteSpace(artist) || (String.IsNullOrWhiteSpace(album) && String.IsNullOrWhiteSpace(title))) throw new IOException("Enter an artist and an album or song title first.");
            bool release = !String.IsNullOrWhiteSpace(album);
            return "https://musicbrainz.org/ws/2/" + (release ? "release" : "recording") + "?fmt=json&limit=5&query=" + Uri.EscapeDataString("artist:" + Quote(artist.Trim()) + " AND " + (release ? "release:" + Quote(album.Trim()) : "recording:" + Quote(title.Trim())));
        }
        internal static byte[] Normalize(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 6 * 1024 * 1024) throw new IOException("Invalid artwork size.");
            using (var source = new MemoryStream(bytes))
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 600; image.StreamSource = source; image.EndInit(); image.Freeze();
                if (image.PixelHeight > 3000) throw new IOException("Unsupported artwork dimensions.");
                var encoder = new JpegBitmapEncoder { QualityLevel = 90 }; encoder.Frames.Add(BitmapFrame.Create(image)); using (var result = new MemoryStream()) { encoder.Save(result); return result.ToArray(); }
            }
        }
        internal static ArtworkMatch Online(string artist, string album, string title, CancellationToken token, Func<Uri,CancellationToken,byte[]> fetch = null)
        {
            var uri = new Uri(Query(artist,album,title));
            if (fetch == null) fetch = Request;
            lock (networkGate)
            {
                int delay = Math.Max(0, (int)(1100 - (DateTime.UtcNow - lastSearch).TotalMilliseconds)); if (token.WaitHandle.WaitOne(delay)) token.ThrowIfCancellationRequested(); lastSearch = DateTime.UtcNow;
            }
            var json = new JavaScriptSerializer { MaxJsonLength = 6 * 1024 * 1024 };
            var root = json.DeserializeObject(Encoding.UTF8.GetString(fetch(uri,token))) as Dictionary<string,object>;
            string key = String.IsNullOrWhiteSpace(album) ? "recordings" : "releases";
            if (root == null || !root.ContainsKey(key)) throw new IOException("Unexpected artwork search response.");
            var candidates = new List<Dictionary<string,object>>();
            foreach (var entry in (object[])root[key])
            {
                var item = entry as Dictionary<string,object>; if (item == null) continue;
                int score; if (item.ContainsKey("score") && Int32.TryParse(Convert.ToString(item["score"]),out score) && score < 90) continue;
                if (key == "releases") candidates.Add(item);
                else if (item.ContainsKey("releases")) candidates.AddRange(((object[])item["releases"]).OfType<Dictionary<string,object>>());
            }
            var seen = new HashSet<Guid>();
            foreach (var candidate in candidates.Take(8))
            {
                token.ThrowIfCancellationRequested(); Guid id; if (!candidate.ContainsKey("id") || !Guid.TryParse(Convert.ToString(candidate["id"]),out id) || !seen.Add(id)) continue;
                try { byte[] picture = fetch(new Uri("https://coverartarchive.org/release/" + id + "/front-500"),token); return new ArtworkMatch { Image = Normalize(picture), Description = "Cover Art Archive · " + artist + " — " + (candidate.ContainsKey("title") ? Convert.ToString(candidate["title"]) : album) }; }
                catch (WebException e) { var response = e.Response as HttpWebResponse; if (response == null || response.StatusCode != HttpStatusCode.NotFound) throw; response.Close(); }
            }
            return null;
        }
    }
}
