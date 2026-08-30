using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Digitone
{
    public sealed partial class PlayerApp
    {
        internal Window CreateFirstRun()
        {
            var dialog=new Window { Owner=Window,Title="Welcome to Digitone",Width=650,Height=650,MinWidth=540,MinHeight=550,Resources=Window.Resources,Background=Window.Background,Foreground=Window.Foreground,FontFamily=Window.FontFamily,Icon=Window.Icon,WindowStartupLocation=WindowStartupLocation.CenterOwner };
            var dock=new DockPanel { Margin=new Thickness(30),Background=Window.Background }; dialog.Content=dock;
            var finish=new Button { Name="FinishSetup",Content="Start listening",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,6,8),Padding=new Thickness(18,10,18,10) }; DockPanel.SetDock(finish,Dock.Bottom); dock.Children.Add(finish);
            var body=new StackPanel(); dock.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
            body.Children.Add(new Image { Source=Window.Icon,Width=86,Height=86,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,16) });
            body.Children.Add(SettingsText("Your music. Your space.",30));
            body.Children.Add(SettingsText("Digitone creates DigiMusic beside the app as your default library and download folder. You can choose a different local folder if you prefer.",14,true));
            string selected=DigiMusicFolder;
            var path=SettingsText(selected,13); body.Children.Add(path);
            var browse=new Button { Content="Choose music folder…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(10,6,0,18) }; body.Children.Add(browse);
            browse.Click+=delegate { using(var picker=new System.Windows.Forms.FolderBrowserDialog { Description="Choose your music folder" }) if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK) { if(!LocalFiles.IsLocal(picker.SelectedPath)) { MessageBox.Show(dialog,"Choose a local folder, not a network location or link."); return; } selected=picker.SelectedPath; path.Text=selected; } };
            var automatic=new CheckBox { Content="Automatically find missing lyrics",IsChecked=false,Margin=new Thickness(0,8,0,6) }; body.Children.Add(automatic);
            body.Children.Add(SettingsText("Optional: sends the playing song’s artist and title to lyrics.ovh. Downloads and artwork searches also use the internet when requested.",12,true));
            var keys=new CheckBox { Name="SetupMediaKeys",Content="Use media keys while Digitone is in the background",IsChecked=true,Margin=new Thickness(0,8,0,14) }; body.Children.Add(keys);
            body.Children.Add(SettingsText("Your library and preferences are saved in this copy’s Data folder. Keep Digitone in a writable folder and extract the whole ZIP before opening it.",12,true));
            var status=SettingsText("",12,true); body.Children.Add(status);
            finish.Click+=delegate {
                try {
                    if(!AudioDownloader.ToolsReady) throw new IOException("Required audio tools are missing. Extract the entire package, including Tools.");
                    Directory.CreateDirectory(selected);RemoveMainFolderPlaylist();Data.MainMusicFolder=selected; Data.DownloadFolder=selected; Data.MusicFolders.Clear();RemoveMainFolderPlaylist();
                    if(selected!=null) RememberMusicFolders(new[]{selected});
                    Data.AutoLyrics=automatic.IsChecked==true; Data.GlobalMediaKeys=keys.IsChecked==true; Data.SetupCompleted=true;
                    if(!Save()) { Data.SetupCompleted=false; status.Text="Could not save setup. Move Digitone to a writable folder and try again."; return; }
                    ConfigureMediaKeys(); if(selected!=null) Find<TextBlock>("DownloadFolder").Text=selected;
                    dialog.DialogResult=true;
                } catch(Exception e) { Data.SetupCompleted=false; status.Text=e.Message; }
            };
            return dialog;
        }
    }
}
