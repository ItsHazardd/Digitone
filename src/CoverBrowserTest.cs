using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Digitone
{
    internal static class CoverBrowserTest
    {
        internal static int Run()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"TestResults","cover-browser-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
            int result = 1; var app = new Application(); var player = new PlayerApp(new LibraryStore(Path.Combine(folder,"library.json")),new Library());
            player.Window.Loaded += async delegate
            {
                CoverGallery gallery = null;
                try
                {
                    gallery = new CoverGallery(player.Window,"Daft Punk",Path.Combine(folder,"Browser")); gallery.Show();
                    for (int i = 0; i < 120 && gallery.Browser.CoreWebView2 == null; i++) await Task.Delay(500);
                    if (gallery.Browser.CoreWebView2 == null) throw new Exception("WebView2 did not initialize.");
                    bool found = false;
                    for (int i = 0; i < 90 && !found; i++) { await Task.Delay(1000); found = await gallery.Browser.ExecuteScriptAsync("!!document.querySelector('a[href*=\"mzstatic.com\"]')") == "true"; }
                    using (var stream = File.Create(Path.Combine(folder,"covers.png"))) await gallery.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
                    File.WriteAllText(Path.Combine(folder,"page.txt"),await gallery.Browser.ExecuteScriptAsync("document.body.innerText"));
                    if (!found) throw new Exception("COV did not show a cover result.");
                    if (await gallery.Browser.ExecuteScriptAsync("JSON.parse(localStorage.getItem('tours')).includes('integration') && document.body.getAttribute('data-tour') !== 'true'") != "true") throw new Exception("Integration intro was not disabled on a fresh browser profile.");
                    if (await gallery.Browser.ExecuteScriptAsync("document.querySelectorAll('#digitone-cov-link').length===1 && document.querySelector('.integration .wrapper #digitone-cov-link').href==='https://covers.musichoarders.xyz/' && document.getElementById('digitone-cov-link').target==='_blank'") != "true") throw new Exception("Website link is missing or misplaced.");
                    await gallery.Browser.ExecuteScriptAsync("document.querySelector('a[href*=\"mzstatic.com\"]').dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true}))");
                    for (int i = 0; i < 60 && gallery.Selected == null; i++) await Task.Delay(500);
                    if (gallery.Selected == null) throw new Exception("COV selection did not reach the editor bridge.");
                    File.WriteAllText(Path.Combine(folder,"results.txt"),"PASS: Real COV website loaded artist-only results and returned a selected cover through its supported browser integration."); result = 0;
                }
                catch(Exception e) { File.WriteAllText(Path.Combine(folder,"results.txt"),"FAIL: " + e); }
                finally { if (gallery != null) gallery.Close(); player.Window.Close(); }
            };
            app.Run(player.Window); return result;
        }
    }
}
