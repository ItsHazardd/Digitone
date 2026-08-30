using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Digitone
{
    internal sealed class CoverGallery : Window
    {
        internal ArtworkMatch Selected;
        internal readonly WebView2 Browser = new WebView2();
        internal readonly Button UseButton = new Button { Content = "Use cover", IsEnabled = false };
        private readonly Image preview = new Image { Width = 52, Height = 52, Margin = new Thickness(0,0,12,0) };
        private readonly TextBlock status = new TextBlock { Text = "Loading COV…", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool closed, downloading;
        private readonly string artist;
        private readonly string browserData;
        internal CoverGallery(Window owner, string artist, string browserData = null)
        {
            this.artist = artist;
            this.browserData = browserData;
            Owner = owner; Title = "Cover search · Digitone"; Width = 1080; Height = 780; MinWidth = 740; MinHeight = 540; Resources = owner.Resources; Background = owner.Background; Foreground = owner.Foreground; FontFamily = owner.FontFamily; Icon = owner.Icon; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel { Background = owner.Background }; Content = root;
            var footer = new DockPanel { Margin = new Thickness(18,12,18,12) }; DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
            DockPanel.SetDock(UseButton,Dock.Right); footer.Children.Add(UseButton); var close = new Button { Content = "Close", IsCancel = true }; DockPanel.SetDock(close,Dock.Right); footer.Children.Add(close); footer.Children.Add(preview); footer.Children.Add(status); root.Children.Add(Browser);
            UseButton.Click += delegate { if (Selected != null && !downloading) DialogResult = true; };
            Loaded += async delegate { await Initialize(); };
            Closed += delegate { closed = true; lifetime.Cancel(); Browser.Dispose(); };
        }
        private async Task Initialize()
        {
            try
            {
                string data = browserData ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data","CoverBrowser");
                string bundled=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Tools","WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(Directory.Exists(bundled) ? bundled : null,data);
                if (closed) return;
                await Browser.EnsureCoreWebView2Async(environment); if (closed) return;
                Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                Browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                Browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                Browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                Browser.CoreWebView2.PermissionRequested += delegate(object sender,CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                Browser.CoreWebView2.DownloadStarting += delegate(object sender,CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                Browser.CoreWebView2.NewWindowRequested += delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; if (e.IsUserInitiated && e.Uri == "https://covers.musichoarders.xyz/") { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://covers.musichoarders.xyz/") { UseShellExecute = true }); } catch (Exception error) { status.Text = "Could not open your browser: " + error.Message; } } };
                Browser.CoreWebView2.NavigationStarting += delegate(object sender,CoreWebView2NavigationStartingEventArgs e) { if (!CoverSearch.TrustedPage(e.Uri)) e.Cancel = true; };
                Browser.CoreWebView2.WebMessageReceived += async delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e) { if (CoverSearch.TrustedPage(e.Source)) await Receive(e.WebMessageAsJson); };
                // COV's supported browser integration sends its selected cover with window.postMessage.
                using (var script = new StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CoverIntegration.js"))) await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script.ReadToEnd());
                Browser.CoreWebView2.NavigationCompleted += delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) { if (!closed) status.Text = e.IsSuccess ? "COV · Choose a cover above, then Use cover." : "COV could not load. Close this window and try again when connected."; };
                Browser.CoreWebView2.Navigate(CoverSearch.Address(artist));
            }
            catch (Exception e) { if (!closed) status.Text = "Cover browser unavailable: " + e.Message; }
        }
        private async Task Receive(string message)
        {
            if (closed || downloading) return;
            try
            {
                string address = CoverSearch.Picture(message); if (address == null) { status.Text = "This image host is not supported. Choose another source."; return; }
                downloading = true; UseButton.IsEnabled = false; status.Text = "Loading selected cover…";
                byte[] bytes = await Task.Run(delegate { return ArtworkFinder.Normalize(ArtworkFinder.Request(new Uri(address),lifetime.Token)); });
                if (closed) return;
                Selected = new ArtworkMatch { Image = bytes, Description = "COV artwork" }; preview.Source = SongTags.ImageFromData(Convert.ToBase64String(bytes)); status.Text = "Cover ready. Use cover to return to the editor.";
            }
            catch (Exception e) { if (!closed) status.Text = "Could not load this cover: " + e.Message; }
            finally { downloading = false; if (!closed) UseButton.IsEnabled = Selected != null; }
        }
    }
}
