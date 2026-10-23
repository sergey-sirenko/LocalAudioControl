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
