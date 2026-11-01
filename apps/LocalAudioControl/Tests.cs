using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LocalAudioControl
{
    internal static class Tests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string output = args[0]; Directory.CreateDirectory(output);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TestMouseCommands();
            TestFocusGuard();
            TestCommandSequence();
            var p = new Preferences(); p.Validate();
            if (p.Step != 3 || !p.OsdEnabled || !p.AutoStart || p.OsdMilliseconds != 1200) throw new Exception("Invalid defaults");
            var legacy = Storage.Json.Deserialize<Preferences>("{\"Favorite1\":\"a\",\"Favorite2\":\"b\",\"Name1\":\"One\",\"Name2\":\"Two\",\"Step\":3,\"OsdEnabled\":true,\"OsdMilliseconds\":1200,\"AutoStart\":true}");
            legacy.Validate();
            if (legacy.Color1 != "#145EAA" || legacy.Color2 != "#00836B" || legacy.Favorite1 != "a") throw new Exception("Legacy settings migration failed");
            legacy.Color1 = "#ffffff"; legacy.Color2 = "#101010"; legacy.Validate();
            var roundTrip = Storage.Json.Deserialize<Preferences>(Storage.Json.Serialize(legacy)); roundTrip.Validate();
            if (roundTrip.Color1 != "#FFFFFF" || roundTrip.ColorForDevice("b") != "#101010" || roundTrip.ColorForDevice("other") != "#145EAA") throw new Exception("Device colors did not round-trip");
            if (Visuals.ContrastText(Color.White) != Color.Black || Visuals.ContrastText(Color.Black) != Color.White) throw new Exception("Text contrast failed");
            foreach (string badColor in new [] { "red", "#123", "#GG0000", "#12345678" })
            {
                var invalid = new Preferences { Color1 = badColor }; bool rejected = false;
                try { invalid.Validate(); } catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new Exception("Invalid color accepted");
            }
            foreach (int bad in new [] { -1, 0, 21, 100 })
            {
                p.Step = bad; bool rejected = false;
                try { p.Validate(); } catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new Exception("Invalid step accepted");
            }
            using (var app = new TrayApplication("--preview", true))
            {
                foreach (float scale in new [] { 1f, 1.5f, 2f })
                {
                    using (var quick = new QuickPanel(app)) { quick.RefreshState(); Render(quick, scale, Path.Combine(output, "panel-" + scale + ".png")); }
                    using (var settings = new SettingsWindow(app)) Render(settings, scale, Path.Combine(output, "settings-" + scale + ".png"));
                }
                using (var settings = new SettingsWindow(app)) { settings.Size = settings.MinimumSize; Render(settings, 1f, Path.Combine(output, "settings-minimum.png")); }
                // Invisible preview contexts must not leave a tray icon or command server.
                app.ExitThread();
            }
            foreach (int percent in new [] { 0, 3, 50, 100 })
            foreach (int size in new [] { 16, 24, 32 })
            using (var icon = Visuals.TrayIcon(percent, false, size))
            using (var bitmap = icon.ToBitmap()) bitmap.Save(Path.Combine(output, "icon-" + percent + "-" + size + ".png"));
            foreach (Color color in new [] { ColorTranslator.FromHtml("#145EAA"), ColorTranslator.FromHtml("#00836B"), Color.White, Color.Black })
            foreach (bool muted in new [] { false, true })
            using (var icon = Visuals.TrayIcon(50, muted, 32, color))
            using (var bitmap = icon.ToBitmap())
            {
                int count = 0;
                for (int x = 0; x < bitmap.Width; x++) for (int y = 0; y < bitmap.Height; y++) if (bitmap.GetPixel(x, y).ToArgb() == color.ToArgb()) count++;
                if (count < 150) throw new Exception("Device color lost, muted=" + muted);
                bitmap.Save(Path.Combine(output, "color-" + color.ToArgb() + "-" + muted + ".png"));
            }
            // Real missing endpoints must be rejected without touching current roles.
            string original = Audio.DefaultId(1); bool missingRejected = false;
            try { Audio.Select("nonexistent-endpoint-for-test"); } catch (InvalidOperationException) { missingRejected = true; }
            if (!missingRejected || Audio.DefaultId(1) != original) throw new Exception("Missing endpoint changed output");
            Console.WriteLine("Defaults, legacy migration, colors, contrast, validation, missing device and UI render tests passed."); return 0;
        }
        private static void TestMouseCommands()
        {
            var preferences = new Preferences { Favorite1 = "a", Favorite2 = "b" };
            if (OutputToggle.Target(preferences, "a") != "b" || OutputToggle.Target(preferences, "b") != "a"
                || OutputToggle.Target(preferences, "other") != "a" || OutputToggle.Target(preferences, null) != "a"
                || OutputToggle.Target(preferences, "A") != "b") throw new Exception("Toggle target failed");
            foreach (var invalid in new [] {
                new Preferences(), new Preferences { Favorite1 = "a" }, new Preferences { Favorite2 = "b" },
                new Preferences { Favorite1 = "a", Favorite2 = "A" } })
            {
                bool rejected = false;
                try { OutputToggle.Target(invalid, "a"); } catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new Exception("Invalid toggle settings accepted");
            }
            int calls = 0;
            MediaControls.NextTrack(() => false, (count, inputs, size) =>
            {
                calls++;
                if (count != 2 || inputs.Length != 2 || size != (IntPtr.Size == 8 ? 40 : 28))
                    throw new Exception("Invalid native input layout");
                if (inputs.Any(i => i.Type != 1 || i.Data.Keyboard.Key != 0xB0 || i.Data.Keyboard.Scan != 0)
                    || inputs[0].Data.Keyboard.Flags != 0 || inputs[1].Data.Keyboard.Flags != 2)
                    throw new Exception("Expected one next-track press and release");
                return 2;
            });
            if (calls != 1) throw new Exception("Media command repeated");
            int localCalls = 0, sendCalls = 0;
            MediaControls.NextTrack(() => { localCalls++; return true; }, (count, inputs, size) => { sendCalls++; return 2; });
            if (localCalls != 1 || sendCalls != 0) throw new Exception("Local media command did not bypass the media key");
            foreach (uint result in new uint[] { 0, 1 })
            {
                calls = 0; bool rejected = false;
                try
                {
                    MediaControls.NextTrack(() => false, (count, inputs, size) =>
                    {
                        calls++;
                        if (calls == 1) return result;
                        if (count != 1 || inputs.Length != 1 || inputs[0].Data.Keyboard.Flags != 2)
                            throw new Exception("Partial send must only release the media key");
                        return 1;
                    });
                }
                catch (System.ComponentModel.Win32Exception) { rejected = true; }
                if (!rejected || calls != (result == 1 ? 2 : 1)) throw new Exception("Media send failure handling failed");
            }
            Console.WriteLine("Output toggle and media input tests passed (no real media input sent).");
        }
        private static void TestFocusGuard()
        {
            foreach (int id in new [] { 50009, 50010, 50011 })
                if (!FocusGuard.IsMenuId(id)) throw new Exception("Menu type not detected: " + id);
            foreach (int id in new [] { 0, 50004, 50020, 50030 })
                if (FocusGuard.IsMenuId(id)) throw new Exception("Non-menu type accepted: " + id);
            foreach (string command in new [] { "--favorite1", "--favorite2", "--toggle-output", "--next-track", "--volume-up", "--volume-down" })
                if (!FocusGuard.IsRepairCommand(command)) throw new Exception("Focus repair skipped for " + command);
            foreach (string command in new [] { "--panel", "--settings", "--status", "--quit", "--tray", "--toggle-mute", "--volume=50" })
                if (FocusGuard.IsRepairCommand(command)) throw new Exception("Focus repair started for " + command);
            Console.WriteLine("Focus guard classification tests passed.");
        }
        private static void TestCommandSequence()
        {
            string pipeName = "LocalAudioControl.Tests." + Guid.NewGuid().ToString("N");
            string current = "a";
            int executed = 0;
            var preferences = new Preferences { Favorite1 = "a", Favorite2 = "b" };
            using (var dispatcher = new Control())
            {
                var handle = dispatcher.Handle;
                int uiThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                using (var server = new CommandServer(dispatcher, command =>
                {
                    if (command != "--toggle-output" || System.Threading.Thread.CurrentThread.ManagedThreadId != uiThread)
                        throw new Exception("Wrong command dispatch");
                    current = OutputToggle.Target(preferences, current); executed++;
                    return current;
                }, () => { }, pipeName))
                {
                    var client = System.Threading.Tasks.Task.Factory.StartNew(() =>
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut))
                            {
                                pipe.Connect(3000);
                                using (var writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), 1024, true) { AutoFlush = true })
                                using (var reader = new StreamReader(pipe, System.Text.Encoding.UTF8, true, 1024, true))
                                {
                                    writer.WriteLine("--toggle-output");
                                    string reply = reader.ReadLine();
                                    if (reply != (i % 2 == 0 ? "b" : "a")) throw new Exception("Repeated toggle commands out of sequence");
                                }
                            }
                        }
                    });
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!client.IsCompleted && DateTime.UtcNow < deadline)
                    { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    if (!client.IsCompleted) throw new Exception("Command sequence timed out");
                    client.GetAwaiter().GetResult();
                    if (executed != 8 || current != "a") throw new Exception("Command sequence lost a toggle");
                }
            }
            Console.WriteLine("Repeated commands through isolated IPC passed.");
        }
        private static void Render(Form form, float scale, string path)
        {
            form.AutoScaleMode = AutoScaleMode.None;
            // Simulate scaled control geometry and fonts without changing the user's display settings.
            var controls = AllControls(form).ToArray();
            var fonts = controls.Select(c => new Font(c.Font.FontFamily, c.Font.Size * scale, c.Font.Style)).ToArray();
            for (int i = 0; i < controls.Length; i++) controls[i].Font = fonts[i];
            form.Scale(new SizeF(scale, scale));
            ((AppWindow)form).PreviewOnly = true; form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000);
            form.Show(); Application.DoEvents(); form.PerformLayout();
            if (form is SettingsWindow)
            {
                var colorButtons = AllControls(form).OfType<Button>().Where(b => b.Text == "Цвет…").ToArray();
                if (colorButtons.Length != 2) throw new Exception("Color buttons missing");
                foreach (var button in colorButtons)
                {
                    var bounds = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                    if (!button.Visible || bounds.Width < 30 || !form.ClientRectangle.Contains(bounds)) throw new Exception("Color button clipped");
                    foreach (var text in AllControls(form).OfType<TextBox>())
                        if (button.RectangleToScreen(button.ClientRectangle).IntersectsWith(text.RectangleToScreen(text.ClientRectangle)))
                            throw new Exception("Name textbox overlaps a color button");
                }
            }
            using (var image = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height)); image.Save(path); }
        }
        private static System.Collections.Generic.IEnumerable<Control> AllControls(Control control)
        {
            yield return control;
            foreach (Control child in control.Controls) foreach (Control item in AllControls(child)) yield return item;
        }
    }
}
