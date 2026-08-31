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
        private Track lyricsTrack;
        private string displayedLyrics;
        private CheckBox lyricsFollow;
        private bool lyricsUsesCurrent;
        private double lyricsLastPosition;
        private readonly System.Collections.Generic.HashSet<string> lyricsAttempted = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource autoLyricsCancellation;
        internal Func<string,string,CancellationToken,string> FetchLyrics = LyricsLookup.Fetch;
        internal async void AutoFindLyrics(Track track)
        {
            if(autoLyricsCancellation!=null) autoLyricsCancellation.Cancel();
            if(Data.AutoLyrics==false || !String.IsNullOrWhiteSpace(track.Lyrics) || String.IsNullOrWhiteSpace(track.Artist) || !lyricsAttempted.Add(track.Path)) return;
            var cancellation=new CancellationTokenSource(); autoLyricsCancellation=cancellation;
            EventHandler closing=delegate { cancellation.Cancel(); }; Window.Closed+=closing;
            try
            {
                await Task.Delay(1200,cancellation.Token);
                string artist=track.Artist,title=track.Title;
                string found=await Task.Run(()=>FetchLyrics(artist,title,cancellation.Token));
                if(!closed && !cancellation.IsCancellationRequested && Data.AutoLyrics!=false && Data.Tracks.Contains(track) && String.IsNullOrWhiteSpace(track.Lyrics) && found.Length<=200000)
                {
                    track.Lyrics=found; if(!Save()) track.Lyrics=null; UpdateLyricsDisplay();
                }
            }
            catch { if(cancellation.IsCancellationRequested) lyricsAttempted.Remove(track.Path); }
            finally { Window.Closed-=closing; if(autoLyricsCancellation==cancellation) autoLyricsCancellation=null; cancellation.Dispose(); }
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
                header.Children.Add(SettingsText("Gentle progress-based scrolling · pause Follow song to browse freely",11,true));
                var empty=new WrapPanel(); lyricsEmpty=empty; header.Children.Add(empty);
                empty.Children.Add(SettingsText("No lyrics found",17));
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
            if(value!=displayedLyrics) { displayedLyrics=value; lyricsBody.Text=DisplayLyrics(value); lyricsEmpty.Visibility=String.IsNullOrWhiteSpace(lyricsBody.Text) ? Visibility.Visible : Visibility.Collapsed; lyricsScroll.ScrollToTop(); lyricsLastPosition=-1; }
            if(lyricsFollow.IsChecked != true || current!=lyricsTrack || !ready) return;
            double position=media.Position.TotalSeconds;
            double target=LyricsProgress(position,Find<Slider>("Seek").Maximum)*lyricsScroll.ScrollableHeight;
            bool seek=lyricsLastPosition<0 || Math.Abs(position-lyricsLastPosition)>1;
            if(playing || seek) lyricsScroll.ScrollToVerticalOffset(seek ? target : lyricsScroll.VerticalOffset+(target-lyricsScroll.VerticalOffset)*.08);
            lyricsLastPosition=position;
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
            var status=SettingsText("Search via lyrics.ovh, or paste lyrics below. Save keeps them in your Digitone library.",12,true); header.Children.Add(status);
            var footer=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0) }; DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
            var save=new Button { Content="Save lyrics",Name="SaveLyrics" }; var close=new Button { Content="Close",Margin=new Thickness(10,0,0,0) }; footer.Children.Add(save); footer.Children.Add(close);
            var editor=new TextBox { Name="LyricsText",Text=track.Lyrics ?? "",AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=200000,FontSize=16,Padding=new Thickness(16) }; root.Children.Add(editor);
            var cancel=new CancellationTokenSource(); bool ended=false;
            dialog.Closed+=delegate { ended=true; cancel.Cancel(); cancel.Dispose(); };
            close.Click+=delegate { dialog.Close(); };
            save.Click+=delegate { string previous=track.Lyrics; track.Lyrics=editor.Text; if(Save()) status.Text="Lyrics saved. Your audio file is unchanged."; else track.Lyrics=previous; };
            import.Click+=delegate {
                var picker=new Microsoft.Win32.OpenFileDialog { Filter="Lyrics|*.txt;*.lrc",Title="Import lyrics" };
                if(picker.ShowDialog(dialog)!=true) return;
                try { if(!LocalFiles.IsLocal(picker.FileName) || new FileInfo(picker.FileName).Length>400000) throw new IOException("Choose a local lyrics file smaller than 400 KB."); string content=File.ReadAllText(picker.FileName); if(content.Length>editor.MaxLength) throw new IOException("Lyrics file is too long."); editor.Text=content; status.Text="Imported preview. Choose Save lyrics to keep it."; }
                catch(Exception e) { status.Text=e.Message; }
            };
            if(uploadImmediately) dialog.Loaded+=delegate { import.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            search.Click+=async delegate {
                if(!String.IsNullOrWhiteSpace(editor.Text) && MessageBox.Show(dialog,"Replace this preview with an online result? Saved lyrics stay unchanged until you save.","Find lyrics",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                string a=artist.Text,t=title.Text; var token=cancel.Token; search.IsEnabled=false; import.IsEnabled=false; editor.IsReadOnly=true; save.IsEnabled=false; status.Text="Looking for lyrics…";
                try { string lyrics=await Task.Run(()=>LyricsLookup.Fetch(a,t,token)); if(!ended) { if(lyrics.Length>editor.MaxLength) throw new IOException("Lyrics result is too long."); editor.Text=lyrics; status.Text="Preview from lyrics.ovh. Review or edit it, then save."; } }
                catch(Exception e) { if(!ended) status.Text=e is WebException ? "Lyrics could not be found or the service is unavailable. Try again, or paste/import lyrics." : e.Message; }
                finally { if(!ended) { search.IsEnabled=true; import.IsEnabled=true; editor.IsReadOnly=false; save.IsEnabled=true; } }
            };
            return dialog;
        }
    }
}
