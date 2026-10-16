using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DigitoneLauncher
{
    // Sits at the top of the portable Digitone folder and starts app\<same name>.exe.
    internal static class Launcher
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string self = Application.ExecutablePath, root = Path.GetDirectoryName(self), target = Path.Combine(root, "app", Path.GetFileName(self));
            if (!File.Exists(target)) target = Path.Combine(root, "app", "DigitoneApp.exe");
            if (!File.Exists(target))
            {
                MessageBox.Show("Digitone's program files are missing from:\n" + Path.Combine(root, "app") + "\n\nYour music, playlists, and settings in the userdata folder are safe. Double-click \"Repair Digitone\" in this folder to reinstall the program files.", "Digitone", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            try
            {
                Process.Start(new ProcessStartInfo(target, String.Join(" ", args.Select(Quote))) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) });
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("Digitone could not start.\n\n" + error.Message, "Digitone", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        private static string Quote(string input)
        {
            var output = new StringBuilder("\""); int slashes = 0;
            foreach (char ch in input) { if (ch == '\\') { slashes++; continue; } if (ch == '"') { output.Append('\\', slashes * 2 + 1); output.Append(ch); } else { output.Append('\\', slashes); output.Append(ch); } slashes = 0; }
            output.Append('\\', slashes * 2); output.Append('"'); return output.ToString();
        }
    }
}
