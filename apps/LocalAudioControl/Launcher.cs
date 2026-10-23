using System;
using System.Diagnostics;
using System.IO;

internal static class Launcher
{
    [STAThread]
    private static int Main()
    {
        string name = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
        string command;
        switch (name)
        {
            case "Favorite1": command = "--favorite1"; break;
            case "Favorite2": command = "--favorite2"; break;
            case "ToggleOutput": command = "--toggle-output"; break;
            case "NextTrack": command = "--next-track"; break;
            case "VolumeUp": command = "--volume-up"; break;
            case "VolumeDown": command = "--volume-down"; break;
            default: return 2;
        }
        string app = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "LocalAudioControl.exe"));
        try
        {
            using (var process = Process.Start(new ProcessStartInfo(app, command) { UseShellExecute = false, CreateNoWindow = true }))
            {
                // A first command can start the long-lived tray process; do not wait for its lifetime.
                if (process.WaitForExit(1500)) return process.ExitCode;
                return 0;
            }
        }
        catch { return 1; }
    }
}
