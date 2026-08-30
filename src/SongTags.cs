using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Digitone
{
    internal static class SongTags
    {
        internal static readonly string[] Fields = { "SongTitle", "Artist", "Album", "AlbumArtist", "Genre", "Year", "TrackNumber", "DiscNumber", "Comment" };
        private static readonly string[] Keys = { "title", "artist", "album", "album_artist", "genre", "date", "track", "disc", "comment" };
        internal static bool CanEdit(string path) { return LocalFiles.IsLocal(path) && new[] { ".mp3", ".flac", ".m4a" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
        internal static byte[] Run(string tool, IEnumerable<string> args)
        {
            var info = new ProcessStartInfo(Path.Combine(AudioDownloader.FfmpegFolder, tool), String.Join(" ", args.Select(AudioDownloader.Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var job = new ProcessJob())
            using (var process = Process.Start(info))
            {
                job.Attach(process);
                var error = Task.Run(delegate { return process.StandardError.ReadToEnd(); });
                var output = Task.Run(delegate { using (var memory = new MemoryStream()) { var buffer = new byte[8192]; int read; while ((read = process.StandardOutput.BaseStream.Read(buffer, 0, buffer.Length)) > 0) { if (memory.Length + read > 8 * 1024 * 1024) throw new IOException("Media tool output exceeded its safety limit."); memory.Write(buffer, 0, read); } return memory.ToArray(); } });
                if (!process.WaitForExit(90000)) throw new IOException("The media tool took too long. Your file was not replaced.");
                if (process.ExitCode != 0) throw new IOException(error.Result.Length > 800 ? error.Result.Substring(0, 800) : error.Result);
                return output.Result;
            }
        }
        internal static Dictionary<string, object> Probe(string path)
        {
            if (!LocalFiles.IsLocal(path) || !File.Exists(path)) throw new IOException("The local audio file is unavailable.");
            string json = Encoding.UTF8.GetString(Run("ffprobe.exe", new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-show_format", "-show_streams", "-of", "json", path }));
            return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
        }
        internal static void ReadInto(Track track)
        {
            if (!File.Exists(Path.Combine(AudioDownloader.FfmpegFolder, "ffprobe.exe"))) return;
            var probe = Probe(track.Path);
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            object formatObject, tagsObject;
            if (probe.TryGetValue("format", out formatObject) && ((Dictionary<string, object>)formatObject).TryGetValue("tags", out tagsObject)) foreach (var tag in (Dictionary<string, object>)tagsObject) tags[tag.Key] = Convert.ToString(tag.Value);
            for (int i = 0; i < Fields.Length; i++) { string value; typeof(Track).GetProperty(Fields[i]).SetValue(track, tags.TryGetValue(Keys[i], out value) ? value : "", null); }
            if (String.IsNullOrEmpty(track.Year) && tags.ContainsKey("year")) track.Year = tags["year"];
            track.SongTitle = Track.CleanTitle(track.SongTitle);
            track.CoverData = null;
            object streams;
            if (probe.TryGetValue("streams", out streams))
            {
                foreach (Dictionary<string, object> stream in (System.Collections.IEnumerable)streams)
                {
                    object disposition, attached;
                    if (stream.TryGetValue("disposition", out disposition) && ((Dictionary<string, object>)disposition).TryGetValue("attached_pic", out attached) && Convert.ToInt32(attached) == 1)
                    {
                        try { track.CoverData = Convert.ToBase64String(Run("ffmpeg.exe", new[] { "-v", "error", "-nostdin", "-protocol_whitelist", "file,pipe", "-i", track.Path, "-map", "0:" + stream["index"], "-frames:v", "1", "-vf", "scale=600:-1", "-c:v", "mjpeg", "-f", "image2pipe", "pipe:1" })); } catch { }
                        break;
                    }
                }
            }
        }
        internal static BitmapImage ImageFromData(string data)
        {
            if (String.IsNullOrEmpty(data) || data.Length > 12 * 1024 * 1024) return null;
            try { using (var stream = new MemoryStream(Convert.FromBase64String(data))) { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 600; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; } } catch { return null; }
        }
        internal static byte[] LoadCover(string path)
        {
            if (!LocalFiles.IsLocal(path) || !File.Exists(path) || new FileInfo(path).Length > 20 * 1024 * 1024) throw new IOException("Choose a local image smaller than 20 MB.");
            using (var stream = File.OpenRead(path))
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 1000; image.StreamSource = stream; image.EndInit(); image.Freeze();
                var encoder = new JpegBitmapEncoder { QualityLevel = 90 }; encoder.Frames.Add(BitmapFrame.Create(image));
                using (var memory = new MemoryStream()) { encoder.Save(memory); return memory.ToArray(); }
            }
        }
        internal static double ArtworkDistance(byte[] first, byte[] second)
        {
            Func<byte[],BitmapSource> decode=delegate(byte[] bytes){using(var stream=new MemoryStream(bytes)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=64;image.StreamSource=stream;image.EndInit();image.Freeze();return new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);}};
            BitmapSource a=decode(first),b=decode(second);int aw=a.PixelWidth,ah=a.PixelHeight,bw=b.PixelWidth,bh=b.PixelHeight;var ap=new byte[aw*ah*4];var bp=new byte[bw*bh*4];a.CopyPixels(ap,aw*4,0);b.CopyPixels(bp,bw*4,0);double difference=0;int samples=0;
            for(int y=0;y<16;y++)for(int x=0;x<16;x++){int ai=((y*(ah-1)/15)*aw+x*(aw-1)/15)*4,bi=((y*(bh-1)/15)*bw+x*(bw-1)/15)*4;for(int c=0;c<3;c++){difference+=Math.Abs(ap[ai+c]-bp[bi+c]);samples++;}}return difference/samples;
        }
        internal static string AudioHash(string path) { return Encoding.UTF8.GetString(Run("ffmpeg.exe", new[] { "-v", "error", "-nostdin", "-protocol_whitelist", "file,pipe", "-i", path, "-map", "0:a:0", "-c:a", "copy", "-f", "hash", "-hash", "sha256", "pipe:1" })).Trim(); }
        internal static void SaveArtwork(string path,byte[] cover)
        {
            if(!CanEdit(path)||!File.Exists(path))throw new IOException("Artwork editing supports available MP3, FLAC, and M4A files.");if(cover==null||cover.Length==0)throw new IOException("Choose a valid artwork image.");if(!AudioDownloader.ToolsReady)throw new IOException("FFmpeg tools are missing.");
            string extension=Path.GetExtension(path),token=Guid.NewGuid().ToString("N"),temp=Path.Combine(Path.GetDirectoryName(path),".digitone-art-"+token+extension),picture=Path.Combine(Path.GetDirectoryName(path),".digitone-cover-"+token+".jpg");long originalLength=new FileInfo(path).Length;DateTime originalWrite=File.GetLastWriteTimeUtc(path);
            try
            {
                File.WriteAllBytes(picture,cover);var args=new List<string>{"-v","error","-nostdin","-y","-protocol_whitelist","file,pipe","-i",path,"-i",picture,"-map","0:a","-map_metadata","0","-map","1:v:0","-c","copy","-disposition:v:0","attached_pic","-metadata:s:v:0","title=Album cover","-metadata:s:v:0","comment=Cover (front)"};if(extension.Equals(".mp3",StringComparison.OrdinalIgnoreCase))args.AddRange(new[]{"-id3v2_version","3"});args.Add(temp);Run("ffmpeg.exe",args);
                string originalHash=AudioHash(path),editedHash=AudioHash(temp);if(String.IsNullOrEmpty(originalHash)||originalHash!=editedHash)throw new IOException("Audio verification failed. Your original file was not replaced.");var verified=new Track{Path=temp};ReadInto(verified);if(String.IsNullOrEmpty(verified.CoverData)||ArtworkDistance(cover,Convert.FromBase64String(verified.CoverData))>18)throw new IOException("The embedded artwork did not match the selected picture. Your original file was not replaced.");if(new FileInfo(path).Length!=originalLength||File.GetLastWriteTimeUtc(path)!=originalWrite)throw new IOException("The file changed while artwork was being saved. Your original file was not replaced.");File.Replace(temp,path,null);
            }
            finally{if(File.Exists(temp))File.Delete(temp);if(File.Exists(picture))File.Delete(picture);}
        }
        internal static string Save(Track edited, byte[] cover, bool changeCover)
        {
            string path = edited.Path;
            if (!CanEdit(path) || !File.Exists(path)) throw new IOException("File editing supports available MP3, FLAC, and M4A files.");
            if (!AudioDownloader.ToolsReady) throw new IOException("FFmpeg tools are missing.");
            string extension = Path.GetExtension(path), token = Guid.NewGuid().ToString("N");
            string temp = Path.Combine(Path.GetDirectoryName(path), ".digitone-edit-" + token + extension);
            string picture = Path.Combine(Path.GetDirectoryName(path), ".digitone-cover-" + token + ".jpg");
            string backup = null;
            long originalLength = new FileInfo(path).Length; DateTime originalWrite = File.GetLastWriteTimeUtc(path);
            try
            {
                var args = new List<string> { "-v", "error", "-nostdin", "-y", "-protocol_whitelist", "file,pipe", "-i", path };
                if (changeCover && cover != null) { File.WriteAllBytes(picture, cover); args.AddRange(new[] { "-i", picture }); }
                args.AddRange(new[] { "-map", changeCover ? "0:a" : "0", "-map_metadata", "0", "-c", "copy" });
                if (changeCover && cover != null) args.AddRange(new[] { "-map", "1:v:0", "-disposition:v:0", "attached_pic", "-metadata:s:v:0", "title=Album cover", "-metadata:s:v:0", "comment=Cover (front)" });
                for (int i = 0; i < Fields.Length; i++) args.AddRange(new[] { "-metadata", Keys[i] + "=" + (string)typeof(Track).GetProperty(Fields[i]).GetValue(edited, null) });
                if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) args.AddRange(new[] { "-id3v2_version", "3" });
                args.Add(temp); Run("ffmpeg.exe", args);
                string originalHash = AudioHash(path), editedHash = AudioHash(temp);
                if (String.IsNullOrEmpty(originalHash) || originalHash != editedHash) throw new IOException("Audio verification failed. Your original file was not replaced.");
                var verified = new Track { Path = temp }; ReadInto(verified);
                for (int i = 0; i < Fields.Length; i++)
                {
                    string expected = (string)typeof(Track).GetProperty(Fields[i]).GetValue(edited, null) ?? "", actual = (string)typeof(Track).GetProperty(Fields[i]).GetValue(verified, null) ?? "";
                    if (expected != actual) throw new IOException("This file format did not preserve " + Fields[i] + ". Your original file was not replaced.");
                }
                if (changeCover && ((cover != null) != !String.IsNullOrEmpty(verified.CoverData))) throw new IOException("Artwork verification failed. Your original file was not replaced.");
                if(changeCover&&cover!=null&&ArtworkDistance(cover,Convert.FromBase64String(verified.CoverData))>18)throw new IOException("The embedded artwork did not match the selected cover. Your original file was not replaced.");
                if (new FileInfo(path).Length != originalLength || File.GetLastWriteTimeUtc(path) != originalWrite) throw new IOException("The file changed while editing. Save was stopped to protect it.");
                File.Replace(temp, path, backup); return backup;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); if (File.Exists(picture)) File.Delete(picture); }
        }
    }
    internal sealed class SongEditor : Window
    {
        private readonly Track track;
        private readonly Action releasePlayback;
        private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        private readonly Image preview = new Image { Width = 160, Height = 160, Stretch = Stretch.UniformToFill };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12), Foreground = new SolidColorBrush(Color.FromRgb(190, 204, 176)) };
        private readonly Button save = new Button { Content = "Save to file", IsDefault = true };
        private bool busy, changeCover;
        private byte[] cover;
        internal Button FolderArtworkButton;
        internal Button AutoArtworkButton;
        internal bool HasPendingArtwork { get { return changeCover && cover != null; } }
        internal bool ReleasedPlayback;
        internal TextBox Input(string field) { return fields[field]; }
        internal Button SaveButton { get { return save; } }
        internal SongEditor(Window owner, Track track, Action releasePlayback)
        {
            this.track = track; this.releasePlayback = releasePlayback; status.Foreground = Themes.Brush(owner, "C1D3A8");
            Owner = owner; Title = "Edit song"; Width = 720; Height = 730; MinWidth = 600; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = owner.Background; Foreground = owner.Foreground; Resources = owner.Resources; FontFamily = owner.FontFamily;
            var root = new StackPanel { Margin = new Thickness(28) }; Content = new ScrollViewer { Background = owner.Background, Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(new TextBlock { Text = "Make it yours.", FontSize = 28, Margin = new Thickness(0, 0, 0, 6) });
            root.Children.Add(new TextBlock { Text = Path.GetFileName(track.Path), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Themes.Brush(owner, "919A92"), Margin = new Thickness(0, 0, 0, 20) });
            var top = new DockPanel(); root.Children.Add(top); var art = new StackPanel { Width = 176, Margin = new Thickness(0, 0, 22, 0) }; DockPanel.SetDock(art, Dock.Left); top.Children.Add(art);
            var artSurface = new Grid { Width = 160, Height = 160 }; artSurface.Children.Add(new TextBlock { Text = "♫", FontSize = 48, Foreground = Themes.Brush(owner, "C1D3A8"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }); artSurface.Children.Add(preview); art.Children.Add(new Border { Background = Themes.Brush(owner, "252E26"), CornerRadius = new CornerRadius(10), Child = artSurface });
            var choose = new Button { Content = "Choose picture", Margin = new Thickness(0, 12, 0, 8) }; art.Children.Add(choose);
            AutoArtworkButton = new Button { Content = "Browse covers", Margin = new Thickness(0,0,0,8), ToolTip = "Browse COV artwork by artist" }; art.Children.Add(AutoArtworkButton);
            FolderArtworkButton = new Button { Content = "Use folder cover", Margin = new Thickness(0,0,0,8) }; art.Children.Add(FolderArtworkButton);
            var remove = new Button { Content = "Remove picture", Margin = new Thickness(0) }; art.Children.Add(remove);
            var form = new Grid(); form.ColumnDefinitions.Add(new ColumnDefinition()); form.ColumnDefinitions.Add(new ColumnDefinition()); for (int row = 0; row < 5; row++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); top.Children.Add(form);
            string[] labels = { "Title", "Artist", "Album", "Album artist", "Genre", "Date Released", "Track number", "Disc number", "Comment" };
            for (int i = 0; i < SongTags.Fields.Length; i++) { var cell = new StackPanel { Margin = new Thickness(0, 0, i % 2 == 0 && i != 8 ? 12 : 0, 0) }; Grid.SetColumn(cell, i % 2); Grid.SetRow(cell, i / 2); if (i == 8) Grid.SetColumnSpan(cell, 2); form.Children.Add(cell); cell.Children.Add(new TextBlock { Text = labels[i], FontSize = 11, Foreground = Themes.Brush(owner, "919A92"), Margin = new Thickness(0, 0, 0, 4) }); var input = new TextBox { MaxLength = 500, Margin = new Thickness(0, 0, 0, 10) }; fields[SongTags.Fields[i]] = input; cell.Children.Add(input); }
            root.Children.Add(new TextBlock { Text = "Saves tags without re-encoding audio. Audio is verified before replacement. No song backups are kept.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Themes.Brush(owner, "919A92"), Margin = new Thickness(0, 16, 0, 0) });
            root.Children.Add(status); root.Children.Add(save);
            save.IsEnabled = false;
            fields["Year"].ToolTip = "YYYY/MM/DD · a year or year/month is also accepted";
            bool formattingDate = false;
            fields["Year"].TextChanged += delegate { if (formattingDate) return; var input = fields["Year"]; string formatted = ReleaseDate.Format(input.Text); if (formatted == input.Text) return; int digits = input.Text.Take(input.CaretIndex).Count(Char.IsDigit); formattingDate = true; input.Text = formatted; int caret = 0, seen = 0; while (caret < formatted.Length && seen < digits) { if (Char.IsDigit(formatted[caret])) seen++; caret++; } input.CaretIndex = caret; formattingDate = false; };
            Loaded += async delegate
            {
                try { var loaded = await Task.Run(delegate { var t = new Track { Path = track.Path }; SongTags.ReadInto(t); return t; }); foreach (string field in SongTags.Fields) fields[field].Text = (string)typeof(Track).GetProperty(field).GetValue(loaded, null) ?? ""; if (String.IsNullOrWhiteSpace(fields["SongTitle"].Text)) fields["SongTitle"].Text = track.Title; preview.Source = SongTags.ImageFromData(loaded.CoverData); save.IsEnabled = true; }
                catch (Exception e) { status.Text = "Unable to read this file: " + e.Message; }
            };
            choose.Click += delegate { if (busy) return; var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Cover images|*.jpg;*.jpeg;*.png;*.bmp", Title = "Choose cover art" }; if (dialog.ShowDialog(this) == true) { try { cover = SongTags.LoadCover(dialog.FileName); preview.Source = SongTags.ImageFromData(Convert.ToBase64String(cover)); changeCover = true; } catch (Exception e) { status.Text = e.Message; } } };
            remove.Click += delegate { if (!busy) { cover = null; changeCover = true; preview.Source = null; } };
            AutoArtworkButton.Click += delegate
            {
                if (busy || !save.IsEnabled) return;
                var gallery = new CoverGallery(this, fields["Artist"].Text);
                if (gallery.ShowDialog() == true && gallery.Selected != null) { cover = gallery.Selected.Image; preview.Source = SongTags.ImageFromData(Convert.ToBase64String(cover)); changeCover = true; status.Text = gallery.Selected.Description + ". Save to file to apply."; }
            };
            FolderArtworkButton.Click += async delegate
            {
                if (busy || !save.IsEnabled) return;
                save.IsEnabled = FolderArtworkButton.IsEnabled = AutoArtworkButton.IsEnabled = false;
                try { var match = await Task.Run(delegate { return ArtworkFinder.Local(track.Path); }); if (match == null) { status.Text = "No cover, folder, front or album image found beside this song."; return; } cover = match.Image; preview.Source = SongTags.ImageFromData(Convert.ToBase64String(cover)); changeCover = true; status.Text = match.Description + ". Save to file to apply."; }
                catch (Exception e) { status.Text = "Could not load folder artwork: " + e.Message; }
                finally { save.IsEnabled = FolderArtworkButton.IsEnabled = AutoArtworkButton.IsEnabled = true; }
            };
            save.Click += async delegate
            {
                if (busy) return;
                if (String.IsNullOrWhiteSpace(fields["SongTitle"].Text)) { status.Text = "Enter a title."; return; }
                if (!ReleaseDate.Valid(fields["Year"].Text)) { status.Text = "Use YYYY, YYYY/MM, or YYYY/MM/DD with a valid calendar date."; return; }
                foreach (string field in new[] { "TrackNumber", "DiscNumber" }) if (!System.Text.RegularExpressions.Regex.IsMatch(fields[field].Text.Trim(), @"^(\d{1,4}(/\d{1,4})?)?$")) { status.Text = "Use a number or a value such as 3/12 for track and disc numbers."; return; }
                var edited = new Track { Path = track.Path }; foreach (string field in SongTags.Fields) typeof(Track).GetProperty(field).SetValue(edited, fields[field].Text.Trim(), null);
                edited.Year = ReleaseDate.Storage(fields["Year"].Text);
                busy = true; save.IsEnabled = false; foreach (var input in fields.Values) input.IsEnabled = false; status.Text = "Saving and verifying the audio…";
                releasePlayback(); ReleasedPlayback = true;
                try { await Task.Run(delegate { SongTags.Save(edited, cover, changeCover); SongTags.ReadInto(edited); }); foreach (string field in SongTags.Fields) typeof(Track).GetProperty(field).SetValue(track, typeof(Track).GetProperty(field).GetValue(edited, null), null); track.CoverData = edited.CoverData; busy = false; DialogResult = true; }
                catch (Exception e) { status.Text = "Save failed: " + e.Message; }
                finally { busy = false; save.IsEnabled = true; foreach (var input in fields.Values) input.IsEnabled = true; }
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (busy) { e.Cancel = true; status.Text = "Please wait while the file is being verified."; } };

        }
    }
}


