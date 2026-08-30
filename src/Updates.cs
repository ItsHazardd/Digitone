using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Digitone
{
    internal sealed class DigitoneRelease
    {
        internal string Version,Notes,Page;internal bool Prerelease;
    }
    internal static class DigitoneUpdates
    {
        internal const string LatestAddress="https://api.github.com/repos/ItsHazardd/Digitone/releases/latest";
        internal static Version ParseVersion(string value){if(String.IsNullOrWhiteSpace(value))return new Version(0,0,0);value=value.Trim().TrimStart('v','V');Version parsed;return Version.TryParse(value,out parsed)?parsed:new Version(0,0,0);}
        internal static DigitoneRelease Parse(string json)
        {
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);return new DigitoneRelease{Version=Convert.ToString(data.ContainsKey("tag_name")?data["tag_name"]:""),Notes=Convert.ToString(data.ContainsKey("body")?data["body"]:""),Page=Convert.ToString(data.ContainsKey("html_url")?data["html_url"]:""),Prerelease=data.ContainsKey("prerelease")&&Convert.ToBoolean(data["prerelease"])};
        }
        internal static bool IsNewer(string available,string installed){return ParseVersion(available)>ParseVersion(installed);}
        internal static void EnsureTls(){ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;}
        internal static DigitoneRelease Latest()
        {
            EnsureTls();
            var request=(HttpWebRequest)WebRequest.Create(LatestAddress);request.Method="GET";request.UserAgent="Digitone/"+AppInfo.Version;request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2022-11-28";request.AllowAutoRedirect=false;request.Timeout=8000;request.ReadWriteTimeout=8000;request.UseDefaultCredentials=false;
            try{using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return Parse(reader.ReadToEnd());}
            catch(WebException error){var response=error.Response as HttpWebResponse;if(response!=null&&response.StatusCode==HttpStatusCode.NotFound)return null;throw;}
        }
    }
    public sealed partial class PlayerApp
    {
        private bool checkingUpdates;
        private void SetupUpdates()
        {
            Window.Loaded+=async delegate{if(!Data.AutoCheckUpdates||Environment.GetCommandLineArgs().Any(a=>a=="--self-test"))return;await Task.Delay(1400);await CheckForUpdates(false);};
        }
        internal async Task<DigitoneRelease> CheckForUpdates(bool manual)
        {
            if(checkingUpdates)return null;checkingUpdates=true;if(manual)Status("Checking GitHub for a Digitone update…");
            try
            {
                var release=await Task.Run(delegate{return DigitoneUpdates.Latest();});Data.LastUpdateCheckUtc=DateTime.UtcNow.ToString("o");Save();
                if(release==null||release.Prerelease||!DigitoneUpdates.IsNewer(release.Version,AppInfo.Version)){if(manual)MessageBox.Show(Window,"Digitone "+AppInfo.Version+" is the newest stable release.","Digitone updates",MessageBoxButton.OK,MessageBoxImage.Information);return null;}
                ShowUpdateAvailable(release);return release;
            }
            catch(Exception error){if(manual)MessageBox.Show(Window,"Digitone could not check GitHub right now. Nothing was changed.\n\n"+error.Message,"Update check unavailable",MessageBoxButton.OK,MessageBoxImage.Warning);return null;}
            finally{checkingUpdates=false;}
        }
        private void ShowUpdateAvailable(DigitoneRelease release)
        {
            string notes=String.IsNullOrWhiteSpace(release.Notes)?"Open the release page to see what changed.":release.Notes;var result=MessageBox.Show(Window,"Digitone "+release.Version.TrimStart('v','V')+" is available.\n\n"+notes+"\n\nOpen the verified GitHub release page?","Digitone update available",MessageBoxButton.YesNo,MessageBoxImage.Information);if(result==MessageBoxResult.Yes&&Uri.IsWellFormedUriString(release.Page,UriKind.Absolute)&&release.Page.StartsWith("https://github.com/ItsHazardd/Digitone/releases/",StringComparison.OrdinalIgnoreCase))try{Process.Start(new ProcessStartInfo(release.Page){UseShellExecute=true});}catch(Exception error){Status("Could not open the release page: "+error.Message);}
        }
        private StackPanel UpdateSettingsPanel()
        {
            var panel=new StackPanel{Name="UpdateSettingsSection"};panel.Children.Add(SettingsText("Digitone updates",16));panel.Children.Add(SettingsText("Checks the official ItsHazardd/Digitone releases. Digitone never downloads or installs an update without asking you first.",12,true));var toggle=new CheckBox{Name="AutoUpdateCheck",Content="Check for stable updates when Digitone opens",IsChecked=Data.AutoCheckUpdates,Margin=new Thickness(0,10,0,8)};panel.Children.Add(toggle);var check=new Button{Name="CheckForUpdatesButton",Content="Check now",HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(check);var last=SettingsText(String.IsNullOrWhiteSpace(Data.LastUpdateCheckUtc)?"Never checked on this device.":"Last successful check: "+Data.LastUpdateCheckUtc,11,true);last.Margin=new Thickness(0,8,0,0);panel.Children.Add(last);toggle.Click+=delegate{Data.AutoCheckUpdates=toggle.IsChecked==true;Save();};check.Click+=async delegate{check.IsEnabled=false;await CheckForUpdates(true);check.IsEnabled=true;};return panel;
        }
    }
}
