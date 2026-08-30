using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Digitone
{
    internal static class NativeChrome
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        internal static bool IsDark(Window window) { int value; return DwmGetWindowAttribute(new WindowInteropHelper(window).Handle,20,out value,4) == 0 && value == 1; }
        private static bool registered;
        internal static void Register()
        {
            if (registered) return; registered = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(delegate(object sender,RoutedEventArgs e) { var window = sender as Window; if (window != null) Apply(window); }));
        }
        internal static void Apply(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
            try
            {
                int dark = 1; if (DwmSetWindowAttribute(handle,20,ref dark,4) != 0) DwmSetWindowAttribute(handle,19,ref dark,4);
                var brush = window.Background as SolidColorBrush; Color color = brush == null ? Color.FromRgb(16,19,21) : brush.Color;
                int caption = color.R | color.G << 8 | color.B << 16, text = 0x00F2F2F2;
                DwmSetWindowAttribute(handle,35,ref caption,4); DwmSetWindowAttribute(handle,36,ref text,4);
            }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
    }
}
