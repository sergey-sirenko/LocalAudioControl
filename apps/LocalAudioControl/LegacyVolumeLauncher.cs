using System;
using System.Diagnostics;
using System.IO;

// Compatibility for existing Logitech wheel assignments at C:\Tools\LocalAudio.
internal static class LegacyVolumeLauncher
{
    [STAThread]
    private static int Main()
    {
        string name = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
        string command;
        if (name == "Local-Volume-Up") command = "--volume-up";
        else if (name == "Local-Volume-Down") command = "--volume-down";
        else return 2;
        string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "LocalAudioControl", "LocalAudioControl.exe");
        try
        {
            using (var process = Process.Start(new ProcessStartInfo(app, command) { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (process.WaitForExit(1500)) return process.ExitCode;
                return 0;
            }
        }
        catch { return 1; }
    }
}
