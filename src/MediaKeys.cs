using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Digitone
{
    public sealed partial class PlayerApp
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
        private HwndSource mediaSource;
        private readonly List<int> mediaRegistrations = new List<int>();
        private int lastMediaCommand;
        private long lastMediaTick;
        private void SetupMediaKeys()
        {
            Window.SourceInitialized += delegate { mediaSource = HwndSource.FromHwnd(new WindowInteropHelper(Window).Handle); mediaSource.AddHook(MediaMessage); ConfigureMediaKeys(); };
            Window.Closed += delegate { ReleaseMediaKeys(); if (mediaSource != null) mediaSource.RemoveHook(MediaMessage); mediaSource = null; };
        }
        private void ReleaseMediaKeys()
        {
            if (mediaSource != null) foreach (int id in mediaRegistrations) UnregisterHotKey(mediaSource.Handle,id);
            mediaRegistrations.Clear();
        }
        private void ConfigureMediaKeys()
        {
            ReleaseMediaKeys();
            if (mediaSource == null || Data.GlobalMediaKeys == false) return;
            for (int i=0;i<4;i++) if (RegisterHotKey(mediaSource.Handle,7100+i,0x4000,(uint)(0xB0+i))) mediaRegistrations.Add(7100+i);
            if (mediaRegistrations.Count < 4) Status("Some media keys are owned by another application. Focus Digitone or close that player and re-enable global media keys in Settings.");
        }
        private IntPtr MediaMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            int command = 0;
            if (message == 0x312 && mediaRegistrations.Contains(wParam.ToInt32())) command = wParam.ToInt32()-7100+11;
            if (message == 0x319) command = (int)((lParam.ToInt64() >> 16) & 0xFFF);
            if (command < 11 || (command > 14 && command != 46 && command != 47)) return IntPtr.Zero;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            handled = true;
            if (command != lastMediaCommand || (now-lastMediaTick)/(double)System.Diagnostics.Stopwatch.Frequency > .12) { DispatchMediaCommand(command); lastMediaCommand=command; lastMediaTick=now; }
            return new IntPtr(1);
        }
        internal void DispatchMediaCommand(int command)
        {
            if (command == 14) TogglePlay();
            else if (command == 46 && !playing) TogglePlay();
            else if (command == 47 && playing) TogglePlay();
            else if (current != null && !opening)
            {
                if (command == 11) Next(false);
                else if (command == 12) { if (ready && media.Position.TotalSeconds > 3) media.Position=TimeSpan.Zero; else if (queueIndex > 0) PlayAt(queueIndex-1); }
                else if (command == 13) { playing=false; media.Pause(); media.Position=TimeSpan.Zero; UpdatePlayButton(); }
            }
        }
    }
}
