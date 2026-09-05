using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Digitone
{
    internal sealed class SpotifyTrackCandidate
    {
        internal string SpotifyUrl,YouTubeUrl;internal int DurationMs;internal bool Confident;
        public string Title { get; set; } public string Artist { get; set; } public bool Selected { get; set; }
        public string Match { get { return String.IsNullOrWhiteSpace(YouTubeUrl)?"Not searched":Confident?"Strong YouTube match":"Review YouTube match"; } }
        public string Display { get { return Artist+" · "+Title; } }
    }
    internal sealed class SpotifyLink
    {
        internal string Kind,Id;
        internal static SpotifyLink Parse(string value)
        {
            if(String.IsNullOrWhiteSpace(value)||value.Any(Char.IsControl))return null;value=value.Trim();
            if(value.StartsWith("spotify:",StringComparison.OrdinalIgnoreCase)){var p=value.Split(':');return p.Length==3&&ValidKind(p[1])&&ValidId(p[2])?new SpotifyLink{Kind=p[1].ToLowerInvariant(),Id=p[2]}:null;}
            Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https")return null;string host=uri.DnsSafeHost.TrimEnd('.');if(!host.Equals("open.spotify.com",StringComparison.OrdinalIgnoreCase))return null;var parts=uri.AbsolutePath.Split(new[]{'/'},StringSplitOptions.RemoveEmptyEntries);int index=parts.Length>2&&parts[0].StartsWith("intl-",StringComparison.OrdinalIgnoreCase)?1:0;return parts.Length>=index+2&&ValidKind(parts[index])&&ValidId(parts[index+1])?new SpotifyLink{Kind=parts[index].ToLowerInvariant(),Id=parts[index+1]}:null;
        }
        private static bool ValidKind(string value){return value=="track"||value=="album"||value=="playlist";}
        private static bool ValidId(string value){return value!=null&&value.Length>=16&&value.Length<=32&&value.All(Char.IsLetterOrDigit);}
    }
    internal static class SpotifySharedLinks
    {
        private static Dictionary<string,object> Object(object value){return value as Dictionary<string,object>;}
        private static IEnumerable<object> Array(object value){var items=value as IEnumerable;return items==null?Enumerable.Empty<object>():items.Cast<object>();}
        private static string Text(Dictionary<string,object> value,string key){object found;return value!=null&&value.TryGetValue(key,out found)?Convert.ToString(found):null;}
        private static Dictionary<string,object> Child(Dictionary<string,object> value,string key){object found;return value!=null&&value.TryGetValue(key,out found)?Object(found):null;}
        private static SpotifyTrackCandidate Track(Dictionary<string,object> value)
        {
            if(value==null)return null;string uri=Text(value,"uri"),title=Text(value,"title")??Text(value,"name"),artist=Text(value,"subtitle");if(String.IsNullOrWhiteSpace(artist)){object raw;if(value.TryGetValue("artists",out raw))artist=String.Join(", ",Array(raw).Select(Object).Where(x=>x!=null).Select(x=>Text(x,"name")).Where(x=>!String.IsNullOrWhiteSpace(x)));}object duration;int ms=value.TryGetValue("duration",out duration)&&duration!=null?Convert.ToInt32(duration):0;return String.IsNullOrWhiteSpace(title)?null:new SpotifyTrackCandidate{Title=title,Artist=String.IsNullOrWhiteSpace(artist)?"Unknown artist":artist,DurationMs=ms,SpotifyUrl=uri==null?null:"https://open.spotify.com/track/"+uri.Split(':').Last(),Selected=true};
        }
        internal static List<SpotifyTrackCandidate> ParseEmbed(string html)
        {
            if(String.IsNullOrWhiteSpace(html))throw new InvalidDataException("Spotify returned an empty shared page.");const string marker="<script id=\"__NEXT_DATA__\" type=\"application/json\">";int start=html.IndexOf(marker,StringComparison.OrdinalIgnoreCase);if(start<0)throw new InvalidDataException("Spotify did not expose track details for this shared link.");start+=marker.Length;int end=html.IndexOf("</script>",start,StringComparison.OrdinalIgnoreCase);if(end<=start)throw new InvalidDataException("Spotify's shared link data was incomplete.");var root=new JavaScriptSerializer{MaxJsonLength=8388608}.Deserialize<Dictionary<string,object>>(html.Substring(start,end-start));var entity=Child(Child(Child(Child(Child(root,"props"),"pageProps"),"state"),"data"),"entity");if(entity==null)throw new InvalidDataException("Spotify did not expose track details for this shared link.");object raw;var tracks=new List<SpotifyTrackCandidate>();if(entity.TryGetValue("trackList",out raw))foreach(var item in Array(raw)){var track=Track(Object(item));if(track!=null)tracks.Add(track);}if(tracks.Count==0){var track=Track(entity);if(track!=null)tracks.Add(track);}return tracks;
        }
        internal static bool IsShortLink(string value){Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.DnsSafeHost.TrimEnd('.').Equals("spotify.link",StringComparison.OrdinalIgnoreCase)&&String.IsNullOrEmpty(uri.UserInfo);}
        internal static async Task<SpotifyLink> Resolve(string value)
        {
            var direct=SpotifyLink.Parse(value);if(direct!=null)return direct;if(!IsShortLink(value))return null;var request=(HttpWebRequest)WebRequest.Create(value);request.Method="GET";request.AllowAutoRedirect=true;request.MaximumAutomaticRedirections=5;request.Timeout=20000;request.UserAgent="Digitone/SpotiDev";using(var response=(HttpWebResponse)await request.GetResponseAsync())return SpotifyLink.Parse(response.ResponseUri.AbsoluteUri);
        }
        internal static async Task<List<SpotifyTrackCandidate>> Load(SpotifyLink link)
        {
            string url="https://open.spotify.com/embed/"+link.Kind+"/"+link.Id;var request=(HttpWebRequest)WebRequest.Create(url);request.Method="GET";request.Timeout=25000;request.ReadWriteTimeout=25000;request.UserAgent="Digitone/SpotiDev";request.Accept="text/html";try{using(var response=(HttpWebResponse)await request.GetResponseAsync())using(var reader=new StreamReader(response.GetResponseStream()))return ParseEmbed(await reader.ReadToEndAsync());}catch(WebException e){var response=e.Response as HttpWebResponse;throw new InvalidOperationException("Spotify could not open this public shared link"+(response==null?"":" ("+(int)response.StatusCode+")")+". Check that the link is public and try again.");}
        }
    }    internal static class YouTubeMatcher
    {
        private static string[] Words(string value){return new string((value??"").ToLowerInvariant().Select(c=>Char.IsLetterOrDigit(c)?c:' ').ToArray()).Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).Where(x=>x.Length>1&&!new[]{"official","audio","video","lyrics","music"}.Contains(x)).Distinct().ToArray();}
        internal static double Score(SpotifyTrackCandidate track,string title,int seconds){var wanted=Words(track.Artist+" "+track.Title);var found=Words(title);double words=wanted.Length==0?0:(double)wanted.Count(found.Contains)/wanted.Length;double duration=track.DurationMs<=0||seconds<=0?0.5:Math.Max(0,1-Math.Abs(track.DurationMs/1000.0-seconds)/30.0);return words*.78+duration*.22;}
        internal static async Task<string> Find(SpotifyTrackCandidate track)
        {
            string tool=Path.Combine(AudioDownloader.ToolRoot,"yt-dlp.exe");if(!File.Exists(tool))throw new InvalidOperationException("yt-dlp is missing.");string query=track.Artist+" "+track.Title+" official audio";var args=new[]{"--ignore-config","--no-plugin-dirs","--no-cache-dir","--flat-playlist","--playlist-end","5","--dump-single-json","--","ytsearch5:"+query};var info=new ProcessStartInfo(tool,String.Join(" ",args.Select(AudioDownloader.Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AudioDownloader.ToolRoot};using(var process=Process.Start(info)){string output=await process.StandardOutput.ReadToEndAsync(),error=await process.StandardError.ReadToEndAsync();process.WaitForExit();if(process.ExitCode!=0||String.IsNullOrWhiteSpace(output))throw new InvalidOperationException(String.IsNullOrWhiteSpace(error)?"No YouTube match was found.":error.Trim());var root=new JavaScriptSerializer{MaxJsonLength=4194304}.Deserialize<Dictionary<string,object>>(output);object raw;if(!root.TryGetValue("entries",out raw))throw new InvalidOperationException("No YouTube match was found.");var list=raw as IEnumerable;string best=null;double bestScore=-1;foreach(var item in list==null?Enumerable.Empty<object>():list.Cast<object>()){var entry=item as Dictionary<string,object>;if(entry==null)continue;object value;string url=entry.TryGetValue("url",out value)?Convert.ToString(value):null;if(!AudioDownloader.ValidUrl(url)){string id=entry.TryGetValue("id",out value)?Convert.ToString(value):null;if(!String.IsNullOrWhiteSpace(id))url="https://www.youtube.com/watch?v="+id;}string title=entry.TryGetValue("title",out value)?Convert.ToString(value):"";int seconds=entry.TryGetValue("duration",out value)&&value!=null?Convert.ToInt32(value):0;double score=Score(track,title,seconds);if(AudioDownloader.ValidUrl(url)&&score>bestScore){best=url;bestScore=score;}}if(best==null)throw new InvalidOperationException("No YouTube match was found.");track.Confident=bestScore>=0.72;track.Selected=track.Confident;return best;}
        }
    }
    internal sealed class SpotifyImportWindow
    {
        private readonly PlayerApp player;private readonly DownloadController downloads;private Window window;private TextBox link;private TextBlock status;private ListBox results;private Button load,queue;private List<SpotifyTrackCandidate> tracks=new List<SpotifyTrackCandidate>();
        internal SpotifyImportWindow(PlayerApp player,DownloadController downloads,Action save){this.player=player;this.downloads=downloads;}
        internal void Show(){if(window!=null){window.Show();window.Activate();return;}Build();window.Show();}
        private Button Button(string text){return new Button{Content=text,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0)};}
        private void Build()
        {
            window=new Window{Owner=player.Window,Title="Spotify Import Assistant · Digitone",Width=900,Height=680,MinWidth=740,MinHeight=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,Resources=player.Window.Resources,FontFamily=player.Window.FontFamily,Icon=player.Window.Icon,UseLayoutRounding=true};window.SetResourceReference(Control.BackgroundProperty,"B161917");window.SetResourceReference(Control.ForegroundProperty,"BEEEFE8");var root=new Grid{Margin=new Thickness(24)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});window.Content=root;
            var heading=new StackPanel();heading.Children.Add(new TextBlock{Text="SPOTIFY IMPORT ASSISTANT",FontSize=27,FontWeight=FontWeights.Bold});var explanation=new TextBlock{Text="Paste a public Spotify share link. Digitone reads its visible track details, finds separate YouTube results, and lets you review every match before downloading anything. No Spotify login is used.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,14)};explanation.SetResourceReference(TextBlock.ForegroundProperty,"B919A92");heading.Children.Add(explanation);root.Children.Add(heading);
            var input=new Grid{Margin=new Thickness(0,0,0,12)};input.ColumnDefinitions.Add(new ColumnDefinition());input.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});link=new TextBox{MinWidth=360,MaxLength=4096,ToolTip="Public Spotify track, album, or playlist share link"};input.Children.Add(link);load=Button("Load shared link");Grid.SetColumn(load,1);input.Children.Add(load);Grid.SetRow(input,1);root.Children.Add(input);
            results=new ListBox{SelectionMode=SelectionMode.Extended,MinHeight=240,ToolTip="Double click a matched row to open its proposed YouTube result"};results.ItemTemplate=(DataTemplate)System.Windows.Markup.XamlReader.Parse(@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Margin='5'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/><ColumnDefinition Width='190'/></Grid.ColumnDefinitions><CheckBox IsChecked='{Binding Selected,Mode=TwoWay}' Margin='0,0,10,0'/><StackPanel Grid.Column='1'><TextBlock Text='{Binding Title}' TextWrapping='Wrap'/><TextBlock Text='{Binding Artist}' Opacity='.7' FontSize='11'/></StackPanel><TextBlock Grid.Column='2' Text='{Binding Match}' Margin='12,0,0,0' VerticalAlignment='Center'/></Grid></DataTemplate>");results.MouseDoubleClick+=delegate{var selected=results.SelectedItem as SpotifyTrackCandidate;if(selected!=null&&AudioDownloader.ValidUrl(selected.YouTubeUrl))Process.Start(new ProcessStartInfo(selected.YouTubeUrl){UseShellExecute=true});};Grid.SetRow(results,2);root.Children.Add(results);
            var footer=new Grid{Margin=new Thickness(0,12,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});status=new TextBlock{Text="Paste a public Spotify share link to begin. No account is needed.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,12,0)};status.SetResourceReference(TextBlock.ForegroundProperty,"B919A92");footer.Children.Add(status);queue=Button("Queue selected downloads");queue.SetResourceReference(Control.BackgroundProperty,"BC1D3A8");queue.SetResourceReference(Control.ForegroundProperty,"B20291E");queue.VerticalAlignment=VerticalAlignment.Bottom;Grid.SetColumn(queue,1);footer.Children.Add(queue);Grid.SetRow(footer,3);root.Children.Add(footer);
            load.Click+=async delegate{await Load();};queue.Click+=delegate{Queue();};window.Closed+=delegate{window=null;};NativeChrome.Apply(window);
        }
        private async Task Load(){try{Toggle(false);status.Text="Opening the public Spotify share link...";var parsed=await SpotifySharedLinks.Resolve(link.Text.Trim());if(parsed==null){status.Text="Paste a valid public Spotify track, album, or playlist share link.";return;}status.Text="Reading the public Spotify share page...";tracks=await SpotifySharedLinks.Load(parsed);results.ItemsSource=null;results.ItemsSource=tracks;if(tracks.Count==0){status.Text="Spotify did not expose any tracks for this link.";return;}await Match();}catch(Exception e){status.Text="Spotify could not open this shared link. "+e.Message;}finally{Toggle(true);}}
        private async Task Match(){if(tracks.Count==0){status.Text="Load a Spotify link first.";return;}try{Toggle(false);int done=0;foreach(var track in tracks.Where(t=>t.Selected).ToList()){status.Text="Finding YouTube matches... "+done+" / "+tracks.Count;try{track.YouTubeUrl=await YouTubeMatcher.Find(track);}catch{track.YouTubeUrl=null;track.Confident=false;track.Selected=false;}done++;results.Items.Refresh();}status.Text=tracks.Count(t=>t.Confident)+" strong matches selected. "+tracks.Count(t=>!String.IsNullOrWhiteSpace(t.YouTubeUrl)&&!t.Confident)+" uncertain matches need your approval. Double click any matched row to inspect it.";}finally{Toggle(true);}}
        internal static string ImportName(string root,DateTime when){string basis="Spotify Import ("+when.ToString("M-d-yyyy 'at' h-mm",CultureInfo.InvariantCulture)+")",name=basis;int suffix=2;while(Directory.Exists(Path.Combine(root,name)))name=basis+" ("+(suffix++)+")";return name;}
        private void Queue(){var chosen=tracks.Where(t=>t.Selected&&!String.IsNullOrWhiteSpace(t.YouTubeUrl)).ToList();if(chosen.Count==0){status.Text="Select at least one track with a YouTube match.";return;}string name=ImportName(player.Data.DownloadFolder,DateTime.Now);foreach(var track in chosen)downloads.Enqueue(track.YouTubeUrl,false,name,null,track.Title,track.Artist);status.Text=chosen.Count+" approved YouTube downloads added to "+name+".";player.ShowDownloads(true);window.Close();}
        private void Toggle(bool enabled){load.IsEnabled=queue.IsEnabled=enabled;}
    }
}
