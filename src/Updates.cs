using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Digitone
{
    internal sealed class DigitoneRelease { internal string Version,Notes,Page,ZipUrl,ChecksumUrl;internal bool Prerelease; }
    internal sealed class UpdatePlan
    {
        public string InstallRoot { get; set; } public string PackageRoot { get; set; } public string StagingRoot { get; set; }
        public string BackupRoot { get; set; } public string HealthMarker { get; set; } public int ParentProcessId { get; set; }
    }
    internal sealed class UpdateTransaction
    {
        private readonly string installRoot,backupRoot;private readonly List<string> replaced=new List<string>(),created=new List<string>();
        private static readonly string[] Protected={"Data","DigiMusic","Backups","Source","TestResults"};
        internal UpdateTransaction(string install,string backup){installRoot=Path.GetFullPath(install);backupRoot=Path.GetFullPath(backup);}
        private static bool ProtectedPath(string relative){string first=relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)[0];return Protected.Contains(first,StringComparer.OrdinalIgnoreCase);}
        internal void Apply(string packageRoot)
        {
            packageRoot=Path.GetFullPath(packageRoot);Directory.CreateDirectory(backupRoot);
            foreach(string source in Directory.GetFiles(packageRoot,"*",SearchOption.AllDirectories))
            {
                string relative=source.Substring(packageRoot.Length).TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);if(String.IsNullOrWhiteSpace(relative)||ProtectedPath(relative))throw new InvalidDataException("The update package contains a protected personal-data path: "+relative);
                string target=Path.GetFullPath(Path.Combine(installRoot,relative));if(!target.StartsWith(installRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("An update file escaped the Digitone folder.");Directory.CreateDirectory(Path.GetDirectoryName(target));
                if(File.Exists(target)){string backup=Path.Combine(backupRoot,relative);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(target,backup,true);replaced.Add(relative);}else created.Add(relative);
                string pending=target+".digitone-new";File.Copy(source,pending,true);if(File.Exists(target))File.Replace(pending,target,null,true);else File.Move(pending,target);
            }
        }
        internal void Rollback(){foreach(string relative in created.AsEnumerable().Reverse()){string target=Path.Combine(installRoot,relative);try{if(File.Exists(target))File.Delete(target);}catch{}}foreach(string relative in replaced.AsEnumerable().Reverse()){string backup=Path.Combine(backupRoot,relative),target=Path.Combine(installRoot,relative);try{Directory.CreateDirectory(Path.GetDirectoryName(target));if(File.Exists(backup))File.Copy(backup,target,true);}catch{}}}
        internal void Commit(){try{if(Directory.Exists(backupRoot))Directory.Delete(backupRoot,true);}catch{}}
    }
    internal static class DigitoneUpdates
    {
        internal const string LatestAddress="https://api.github.com/repos/ItsHazardd/Digitone/releases/latest";
        internal static Version ParseVersion(string value){if(String.IsNullOrWhiteSpace(value))return new Version(0,0,0);value=value.Trim().TrimStart('v','V');Version parsed;return Version.TryParse(value,out parsed)?parsed:new Version(0,0,0);}
        internal static DigitoneRelease Parse(string json)
        {
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);var release=new DigitoneRelease{Version=Convert.ToString(data.ContainsKey("tag_name")?data["tag_name"]:""),Notes=Convert.ToString(data.ContainsKey("body")?data["body"]:""),Page=Convert.ToString(data.ContainsKey("html_url")?data["html_url"]:""),Prerelease=data.ContainsKey("prerelease")&&Convert.ToBoolean(data["prerelease"])};
            string plain=release.Version.TrimStart('v','V'),zip="Digitone-"+plain+"-Windows-x64.zip",checksum="Digitone-"+plain+"-SHA256.txt";var assets=data.ContainsKey("assets")?data["assets"] as IEnumerable:null;if(assets!=null)foreach(object item in assets){var asset=item as Dictionary<string,object>;if(asset==null)continue;string name=Convert.ToString(asset.ContainsKey("name")?asset["name"]:""),url=Convert.ToString(asset.ContainsKey("browser_download_url")?asset["browser_download_url"]:"");if(name==zip)release.ZipUrl=url;if(name==checksum)release.ChecksumUrl=url;}return release;
        }
        internal static bool IsNewer(string available,string installed){return ParseVersion(available)>ParseVersion(installed);}
        internal static void EnsureTls(){ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;}
        private static bool OfficialAsset(string url,string version){Uri uri;return Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)&&uri.AbsolutePath.StartsWith("/ItsHazardd/Digitone/releases/download/v"+version+"/",StringComparison.OrdinalIgnoreCase);}
        internal static bool Installable(DigitoneRelease release){if(release==null)return false;string version=release.Version.TrimStart('v','V');return OfficialAsset(release.ZipUrl,version)&&OfficialAsset(release.ChecksumUrl,version);}
        internal static DigitoneRelease Latest()
        {
            EnsureTls();var request=(HttpWebRequest)WebRequest.Create(LatestAddress);request.Method="GET";request.UserAgent="Digitone/"+AppInfo.Version;request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2022-11-28";request.AllowAutoRedirect=false;request.Timeout=8000;request.ReadWriteTimeout=8000;request.UseDefaultCredentials=false;
            try{using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return Parse(reader.ReadToEnd());}catch(WebException error){var response=error.Response as HttpWebResponse;if(response!=null&&response.StatusCode==HttpStatusCode.NotFound)return null;throw;}
        }
        private static void Download(string url,string path,long limit,Action<long,long> progress)
        {
            EnsureTls();var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="Digitone/"+AppInfo.Version;request.AllowAutoRedirect=true;request.Timeout=20000;request.ReadWriteTimeout=20000;using(var response=(HttpWebResponse)request.GetResponse()){if(response.ResponseUri.Scheme!="https")throw new InvalidDataException("GitHub redirected the update to an insecure address.");if(response.ContentLength>limit)throw new InvalidDataException("The update asset is larger than Digitone's safety limit.");using(var input=response.GetResponseStream())using(var output=File.Create(path)){byte[] buffer=new byte[131072];long total=0;int read;while((read=input.Read(buffer,0,buffer.Length))>0){total+=read;if(total>limit)throw new InvalidDataException("The update asset exceeded Digitone's safety limit.");output.Write(buffer,0,read);if(progress!=null)progress(total,response.ContentLength);}}}
        }
        internal static string HashFile(string path){using(var sha=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");}
        internal static string ExpectedHash(string checksum,string zipName){foreach(string line in File.ReadAllLines(checksum)){Match match=Regex.Match(line,"^([A-Fa-f0-9]{64})\\s+\\*?(.+)$");if(match.Success&&String.Equals(match.Groups[2].Value.Trim(),zipName,StringComparison.OrdinalIgnoreCase))return match.Groups[1].Value.ToUpperInvariant();}throw new InvalidDataException("The release checksum does not name the expected Digitone ZIP.");}
        internal static string ExtractPackage(string zipPath,string destination)
        {
            Directory.CreateDirectory(destination);long total=0;int count=0;using(var archive=ZipFile.OpenRead(zipPath))foreach(var entry in archive.Entries){if(++count>512)throw new InvalidDataException("The update ZIP contains too many entries.");string name=entry.FullName.Replace('/',Path.DirectorySeparatorChar);if(name.IndexOf(':')>=0||Path.IsPathRooted(name)||name.Split(Path.DirectorySeparatorChar).Any(p=>p==".."))throw new InvalidDataException("The update ZIP contains an unsafe path.");string target=Path.GetFullPath(Path.Combine(destination,name));if(!target.StartsWith(Path.GetFullPath(destination)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The update ZIP escaped its staging folder.");if(String.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(target);continue;}total+=entry.Length;if(total>1073741824L)throw new InvalidDataException("The expanded update is larger than Digitone's safety limit.");Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=entry.Open())using(var output=File.Create(target))input.CopyTo(output);}string[] executables=Directory.GetFiles(destination,"Digitone.exe",SearchOption.AllDirectories);if(executables.Length!=1)throw new InvalidDataException("The update ZIP must contain exactly one Digitone.exe.");string root=Path.GetDirectoryName(executables[0]);foreach(string protectedName in new[]{"Data","DigiMusic"})if(Directory.Exists(Path.Combine(root,protectedName)))throw new InvalidDataException("The update package tried to include personal data.");return root;
        }
        internal static UpdatePlan Prepare(DigitoneRelease release,Action<string> status)
        {
            if(!Installable(release))throw new InvalidDataException("This GitHub Release is missing Digitone's ZIP or checksum asset.");string version=release.Version.TrimStart('v','V'),root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Digitone","Updates",version+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string zipName="Digitone-"+version+"-Windows-x64.zip",zip=Path.Combine(root,zipName),checksum=Path.Combine(root,"checksum.txt");status("Downloading update checksum…");Download(release.ChecksumUrl,checksum,65536,null);status("Downloading Digitone "+version+"…");Download(release.ZipUrl,zip,786432000,delegate(long current,long length){if(length>0)status("Downloading Digitone "+version+" · "+(current*100/length)+"%");});status("Verifying update integrity…");string expected=ExpectedHash(checksum,zipName),actual=HashFile(zip);if(!String.Equals(expected,actual,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The downloaded update failed SHA-256 verification. Nothing was changed.");string package=ExtractPackage(zip,Path.Combine(root,"package"));return new UpdatePlan{InstallRoot=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),PackageRoot=package,StagingRoot=root,BackupRoot=Path.Combine(root,"rollback"),HealthMarker=Path.Combine(root,"healthy.txt"),ParentProcessId=Process.GetCurrentProcess().Id};
        }
        internal static int ApplyUpdate(string planPath)
        {
            UpdateTransaction transaction=null;UpdatePlan plan=null;try{plan=new JavaScriptSerializer().Deserialize<UpdatePlan>(File.ReadAllText(planPath));if(plan==null)throw new InvalidDataException("The update plan is missing.");try{Process.GetProcessById(plan.ParentProcessId).WaitForExit(30000);}catch(ArgumentException){}transaction=new UpdateTransaction(plan.InstallRoot,plan.BackupRoot);transaction.Apply(plan.PackageRoot);string installed=Path.Combine(plan.InstallRoot,"Digitone.exe");if(!File.Exists(installed))throw new FileNotFoundException("The updated Digitone.exe is missing.");var child=Process.Start(new ProcessStartInfo(installed,"--update-health \""+plan.HealthMarker+"\" \""+plan.StagingRoot+"\""){UseShellExecute=true,WorkingDirectory=plan.InstallRoot});DateTime deadline=DateTime.UtcNow.AddSeconds(35);while(DateTime.UtcNow<deadline&&!File.Exists(plan.HealthMarker)){if(child.HasExited)break;Thread.Sleep(250);}if(!File.Exists(plan.HealthMarker)){try{if(!child.HasExited)child.Kill();}catch{}transaction.Rollback();Process.Start(new ProcessStartInfo(installed){UseShellExecute=true,WorkingDirectory=plan.InstallRoot});MessageBox.Show("The new Digitone build did not finish starting, so the previous files were restored.","Digitone update restored",MessageBoxButton.OK,MessageBoxImage.Warning);return 2;}transaction.Commit();return 0;}catch(Exception error){if(transaction!=null)transaction.Rollback();try{if(plan!=null){string installed=Path.Combine(plan.InstallRoot,"Digitone.exe");if(File.Exists(installed))Process.Start(new ProcessStartInfo(installed){UseShellExecute=true,WorkingDirectory=plan.InstallRoot});}}catch{}MessageBox.Show("Digitone could not install the update. Your personal data was untouched.\n\n"+error.Message,"Update stopped",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
        }
    }
    public sealed partial class PlayerApp
    {
        private bool checkingUpdates,installingUpdate;
        private void SetupUpdates(){Window.Loaded+=async delegate{if(!Data.AutoCheckUpdates||Environment.GetCommandLineArgs().Any(a=>a=="--self-test"))return;await Task.Delay(1400);await CheckForUpdates(false);};}
        internal async Task<DigitoneRelease> CheckForUpdates(bool manual)
        {
            if(checkingUpdates||installingUpdate)return null;checkingUpdates=true;if(manual)Status("Checking GitHub for a Digitone update…");try{var release=await Task.Run(delegate{return DigitoneUpdates.Latest();});Data.LastUpdateCheckUtc=DateTime.UtcNow.ToString("o");Save();if(release==null||release.Prerelease||!DigitoneUpdates.IsNewer(release.Version,AppInfo.Version)){if(manual)MessageBox.Show(Window,"Digitone "+AppInfo.Version+" is the newest stable release.","Digitone updates",MessageBoxButton.OK,MessageBoxImage.Information);return null;}await OfferUpdate(release);return release;}catch(Exception error){if(manual)MessageBox.Show(Window,"Digitone could not check GitHub right now. Nothing was changed.\n\n"+error.Message,"Update check unavailable",MessageBoxButton.OK,MessageBoxImage.Warning);return null;}finally{checkingUpdates=false;}
        }
        private async Task OfferUpdate(DigitoneRelease release)
        {
            var result=MessageBox.Show(Window,"Digitone "+release.Version.TrimStart('v','V')+" is available.\n\nDownload, verify, and install it now? Your Data and DigiMusic folders will be preserved.\n\nRelease details remain available in Digitone’s Changelog menu.","Digitone update available",MessageBoxButton.YesNo,MessageBoxImage.Information);if(result!=MessageBoxResult.Yes)return;installingUpdate=true;try{var plan=await Task.Run(delegate{return DigitoneUpdates.Prepare(release,delegate(string message){Window.Dispatcher.BeginInvoke(new Action(delegate{Status(message);}));});});string planPath=Path.Combine(plan.StagingRoot,"update-plan.json");File.WriteAllText(planPath,new JavaScriptSerializer().Serialize(plan));string helper=Path.Combine(plan.PackageRoot,"Digitone.exe");Process.Start(new ProcessStartInfo(helper,"--apply-update \""+planPath+"\""){UseShellExecute=true,WorkingDirectory=plan.PackageRoot});Application.Current.Shutdown();}catch(Exception error){installingUpdate=false;MessageBox.Show(Window,"Digitone could not prepare this update. Nothing was changed.\n\n"+error.Message,"Update stopped",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        private StackPanel UpdateSettingsPanel()
        {
            var panel=new StackPanel{Name="UpdateSettingsSection"};panel.Children.Add(SettingsText("Digitone updates",16));panel.Children.Add(SettingsText("Checks official stable GitHub releases. If you approve an update, Digitone downloads it here, verifies its SHA-256 checksum, preserves personal data, installs it with rollback protection, and restarts itself.",12,true));var toggle=new CheckBox{Name="AutoUpdateCheck",Content="Check for stable updates when Digitone opens",IsChecked=Data.AutoCheckUpdates,Margin=new Thickness(0,10,0,8)};panel.Children.Add(toggle);var check=new Button{Name="CheckForUpdatesButton",Content="Check now",HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(check);var last=SettingsText(String.IsNullOrWhiteSpace(Data.LastUpdateCheckUtc)?"Never checked on this device.":"Last successful check: "+Data.LastUpdateCheckUtc,11,true);last.Margin=new Thickness(0,8,0,0);panel.Children.Add(last);toggle.Click+=delegate{Data.AutoCheckUpdates=toggle.IsChecked==true;Save();};check.Click+=async delegate{check.IsEnabled=false;await CheckForUpdates(true);if(!installingUpdate)check.IsEnabled=true;};return panel;
        }
    }
}
