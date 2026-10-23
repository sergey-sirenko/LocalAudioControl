using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace LocalAudioControl
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly Control dispatcher = new Control();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Timer timer = new Timer { Interval = 300 };
        private readonly CommandServer server;
        private readonly AudioOverlay overlay = new AudioOverlay();
        internal Preferences Preferences;
        internal AudioState Current;
        internal List<AudioDevice> Devices = new List<AudioDevice>();
        private QuickPanel panel;
        private SettingsWindow settings;
        private string trayStamp;
        private bool stopping;
        private int ticks;

        internal TrayApplication(string firstCommand, bool preview = false)
        {
            bool firstRun = !System.IO.File.Exists(Storage.SettingsPath);
            Preferences = Storage.Load();
            if (!preview)
            {
                if (firstRun) Storage.Save(Preferences);
                Storage.Startup(Preferences.AutoStart);
            }
            var handle = dispatcher.Handle;
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Items.Add("Управление звуком", null, (s, e) => ShowPanel());
            tray.ContextMenuStrip.Items.Add("Настройки", null, (s, e) => ShowSettings());
            tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            tray.ContextMenuStrip.Items.Add("Выход", null, (s, e) => ExitThread());
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowPanel(); };
            RefreshState(true);
            if (preview) return;
            tray.Visible = true;
            server = new CommandServer(dispatcher, Execute, ExitThread);
            timer.Tick += (s, e) => RefreshState(++ticks % 5 == 0); timer.Start();
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (firstCommand != "--tray") Execute(firstCommand);
                if (firstRun && firstCommand != "--settings") ShowSettings();
            }));
        }
        internal string DisplayName(AudioState state)
        {
            if (state.DeviceId != null && state.DeviceId == Preferences.Favorite1) return Preferences.Name1;
            if (state.DeviceId != null && state.DeviceId == Preferences.Favorite2) return Preferences.Name2;
            return state.DeviceName;
        }
        private void RefreshState(bool devices)
        {
            if (stopping) return;
            try
            {
                Current = Audio.State();
                if (devices) Devices = Audio.Devices();
                string label = DisplayName(Current);
                string color = Preferences.ColorForDevice(Current.DeviceId);
                string stamp = label + "|" + Current.Percent + "|" + Current.Muted + "|" + color;
                if (stamp != trayStamp)
                {
                    Icon old = tray.Icon;
                    tray.Icon = Visuals.TrayIcon(Current.Percent, Current.Muted, Math.Max(32, SystemInformation.SmallIconSize.Width * 2), ColorTranslator.FromHtml(color));
                    if (old != null) old.Dispose();
                    string tip = label + (Current.Percent < 0 ? "" : " — " + Current.Percent + "%") + (Current.Muted ? " (звук выключен)" : "");
                    tray.Text = tip.Length > 63 ? tip.Substring(0, 60) + "…" : tip;
                    trayStamp = stamp;
                }
                if (panel != null && !panel.IsDisposed && panel.Visible) panel.RefreshState();
            }
            catch (Exception ex) { Storage.Log(ex); }
        }
        internal void Error(Exception ex)
        {
            Storage.Log(ex); tray.ShowBalloonTip(3500, "Local Audio Control", ex.Message, ToolTipIcon.Warning);
        }
        internal void Action(Action action)
        {
            try { action(); RefreshState(true); ShowOverlay(); }
            catch (Exception ex) { Error(ex); RefreshState(true); }
        }
        private void ShowOverlay()
        {
            if (Preferences.OsdEnabled && Current != null && Current.Percent >= 0)
                overlay.Present(DisplayName(Current), Current.Percent, Current.Muted, Preferences.OsdMilliseconds);
        }
        internal void SelectFavorite(int number)
        {
            string id = number == 1 ? Preferences.Favorite1 : Preferences.Favorite2;
            if (String.IsNullOrEmpty(id)) throw new InvalidOperationException("Сначала выберите избранное устройство №" + number + " в настройках.");
            Audio.Select(id);
        }
        internal string Execute(string command)
        {
            try
            {
                bool show = true;
                switch (command)
                {
                    case "--tray": show = false; break;
                    case "--settings": ShowSettings(); show = false; break;
                    case "--panel": ShowPanel(); show = false; break;
                    case "--favorite1": SelectFavorite(1); break;
                    case "--favorite2": SelectFavorite(2); break;
                    case "--volume-up": Adjust(Preferences.Step); break;
                    case "--volume-down": Adjust(-Preferences.Step); break;
                    case "--toggle-mute": Audio.SetMute(!Audio.State().Muted); break;
                    case "--quit": return Storage.Json.Serialize(new { ok = true });
                    case "--status": show = false; break;
                    default:
                        if (command.StartsWith("--volume=")) Audio.SetPercent(Int32.Parse(command.Substring(9)));
                        else if (command.StartsWith("--mute=")) Audio.SetMute(Boolean.Parse(command.Substring(7)));
                        else throw new ArgumentException("Неизвестная команда: " + command);
                        break;
                }
                RefreshState(true); if (show) ShowOverlay();
                return Storage.Json.Serialize(new { ok = true, state = Current, devices = Devices, settings = Preferences,
                    running = true, pid = System.Diagnostics.Process.GetCurrentProcess().Id, trayText = tray.Text,
                    trayColor = Preferences.ColorForDevice(Current.DeviceId), osdVisible = overlay.Visible });
            }
            catch (Exception ex)
            {
                Error(ex);
                return Storage.Json.Serialize(new { ok = false, error = ex.Message });
            }
        }
        private void Adjust(int delta)
        {
            AudioState state = Audio.State(); if (state.Percent < 0) throw new InvalidOperationException("Нет доступного устройства воспроизведения.");
            Audio.SetPercent(Math.Max(0, Math.Min(100, state.Percent + delta)));
        }
        internal void SavePreferences(Preferences value)
        {
            value.Validate();
            Storage.Save(value); Storage.Startup(value.AutoStart); Preferences = value;
            if (!value.OsdEnabled) overlay.Hide();
            trayStamp = null; RefreshState(true);
        }
        internal void ShowPanel()
        {
            RefreshState(true);
            if (panel == null || panel.IsDisposed) panel = new QuickPanel(this);
            panel.RefreshState(); panel.Show(); panel.Activate();
        }
        internal void ShowSettings()
        {
            if (settings == null || settings.IsDisposed) settings = new SettingsWindow(this);
            settings.Show(); settings.Activate();
        }
        protected override void ExitThreadCore()
        {
            if (stopping) return;
            stopping = true; timer.Stop(); if (server != null) server.Dispose();
            tray.Visible = false; if (tray.Icon != null) tray.Icon.Dispose(); tray.Dispose();
            overlay.Dispose(); if (panel != null) panel.Dispose(); if (settings != null) settings.Dispose();
            dispatcher.Dispose(); timer.Dispose(); base.ExitThreadCore();
        }
    }
    public static class Visuals
    {
        public static Icon TrayIcon(int percent, bool muted, int size)
        {
            return TrayIcon(percent, muted, size, Color.FromArgb(20, 94, 170));
        }
        public static Color ContrastText(Color background)
        {
            double luminance = .2126 * Linear(background.R) + .7152 * Linear(background.G) + .0722 * Linear(background.B);
            return luminance > .179 ? Color.Black : Color.White;
        }
        private static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        public static Icon TrayIcon(int percent, bool muted, int size, Color background)
        {
            using (var bitmap = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(background))
                    g.FillEllipse(brush, 0, 0, size - 1, size - 1);
                string text = percent < 0 ? "—" : percent.ToString();
                using (var font = new Font("Segoe UI", size * (text.Length == 3 ? 0.39f : 0.5f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(ContrastText(background)))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, font, brush, new RectangleF(0, 0, size, size), format);
                if (muted)
                {
                    using (var pen = new Pen(ContrastText(background), Math.Max(1.5f, size / 12f)))
                        g.DrawLine(pen, size * .16f, size * .84f, size * .84f, size * .16f);
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (var temporary = Icon.FromHandle(handle)) return (Icon)temporary.Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }
    }
    internal class AppWindow : Form
    {
        internal bool PreviewOnly;
        protected override bool ShowWithoutActivation { get { return PreviewOnly || base.ShowWithoutActivation; } }
        protected AppWindow()
        {
            Font = new Font("Segoe UI", 10f); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(246, 248, 251);
            Icon = Visuals.TrayIcon(50, false, 32);
        }
        protected static Label Label(string text) { return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 8, 6) }; }
        protected static Button Button(string text, EventHandler click)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(100, 34), Margin = new Padding(0, 6, 8, 6) };
            button.Click += click; return button;
        }
    }
    internal sealed class QuickPanel : AppWindow
    {
        private readonly TrayApplication app;
        private readonly Label current = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
        private readonly ComboBox devices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly TrackBar volume = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Dock = DockStyle.Fill, AutoSize = false, Height = 50 };
        private readonly CheckBox mute = new CheckBox { Text = "Выключить звук", AutoSize = true };
        private bool updating;
        private string deviceStamp;
        internal QuickPanel(TrayApplication app)
        {
            this.app = app; Text = "Local Audio Control"; ClientSize = new Size(550, 300); MinimumSize = new Size(450, 330);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 6 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(current); layout.Controls.Add(Label("Устройство воспроизведения")); layout.Controls.Add(devices); layout.Controls.Add(volume); layout.Controls.Add(mute);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            buttons.Controls.Add(Button("Тише", (s, e) => app.Execute("--volume-down")));
            buttons.Controls.Add(Button("Громче", (s, e) => app.Execute("--volume-up")));
            buttons.Controls.Add(Button("Настройки", (s, e) => app.ShowSettings()));
            buttons.Controls.Add(Button("Закрыть", (s, e) => Hide())); layout.Controls.Add(buttons); Controls.Add(layout);
            devices.SelectionChangeCommitted += (s, e) => { var d = devices.SelectedItem as AudioDevice; if (d != null) app.Action(() => Audio.Select(d.Id)); };
            volume.Scroll += (s, e) => { if (!updating) app.Action(() => Audio.SetPercent(volume.Value)); };
            mute.CheckedChanged += (s, e) => { if (!updating) app.Action(() => Audio.SetMute(mute.Checked)); };
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }
        internal void RefreshState()
        {
            updating = true;
            try
            {
                AudioState state = app.Current;
                current.Text = app.DisplayName(state) + (state.Percent < 0 ? "" : " · " + state.Percent + "%") + (state.Muted ? " · без звука" : "");
                string stamp = String.Join("|", app.Devices.Select(d => d.Id + d.Name));
                if (stamp != deviceStamp) { devices.Items.Clear(); foreach (var d in app.Devices) devices.Items.Add(d); deviceStamp = stamp; }
                for (int i = 0; i < devices.Items.Count; i++) if (((AudioDevice)devices.Items[i]).Id == state.DeviceId) devices.SelectedIndex = i;
                if (!volume.Capture) volume.Value = Math.Max(0, state.Percent);
                mute.Checked = state.Muted; volume.Enabled = mute.Enabled = state.Percent >= 0;
            }
            finally { updating = false; }
        }
    }
    internal sealed class SettingsWindow : AppWindow
    {
        private readonly TrayApplication app;
        private readonly ComboBox favorite1 = new ComboBox(), favorite2 = new ComboBox();
        private readonly TextBox name1 = new TextBox(), name2 = new TextBox();
        private readonly Button color1 = new Button(), color2 = new Button();
        private readonly NumericUpDown step = new NumericUpDown { Minimum = 1, Maximum = 20, Width = 100 };
        private readonly NumericUpDown duration = new NumericUpDown { Minimum = .5m, Maximum = 10m, DecimalPlaces = 1, Increment = .1m, Width = 100 };
        private readonly CheckBox osd = new CheckBox { Text = "Показывать индикатор поверх окон", AutoSize = true };
        private readonly CheckBox startup = new CheckBox { Text = "Запускать при входе в Windows", AutoSize = true };
        internal SettingsWindow(TrayApplication app)
        {
            this.app = app; Text = "Настройки · Local Audio Control"; ClientSize = new Size(650, 490); MinimumSize = new Size(580, 520);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 3, RowCount = 10, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            for (int row = 0; row < 10; row++) layout.RowStyles.Add(row == 1 || row == 3
                ? new RowStyle(SizeType.Absolute, 44) : new RowStyle(SizeType.AutoSize));
            AddRow(layout, 0, "Избранное устройство 1", favorite1); AddRow(layout, 1, "Название и цвет", name1);
            AddRow(layout, 2, "Избранное устройство 2", favorite2); AddRow(layout, 3, "Название и цвет", name2);
            layout.SetColumnSpan(name1, 1); layout.SetColumnSpan(name2, 1);
            ConfigureColorButton(color1); ConfigureColorButton(color2);
            layout.Controls.Add(color1, 2, 1); layout.Controls.Add(color2, 2, 3);
            AddRow(layout, 4, "Шаг громкости, %", step); AddRow(layout, 5, "Индикатор, секунд", duration);
            layout.Controls.Add(osd, 1, 6); layout.Controls.Add(startup, 1, 7);
            layout.SetColumnSpan(osd, 2); layout.SetColumnSpan(startup, 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.Add(Button("Сохранить", Save)); buttons.Controls.Add(Button("Обновить список", (s, e) => ReloadDevices()));
            buttons.Controls.Add(Button("Громкость…", (s, e) => app.ShowPanel()));
            buttons.Controls.Add(Button("Команды для мыши", (s, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Commands")) { UseShellExecute = true })));
            layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 3);
            var help = Label("Для Logitech: папка Commands рядом с программой.\nFavorite1 / Favorite2 — устройства; VolumeUp / VolumeDown — громкость.");
            layout.Controls.Add(help, 0, 9); layout.SetColumnSpan(help, 3); Controls.Add(layout);
            var p = app.Preferences;
            name1.Text = p.Name1; name2.Text = p.Name2; step.Value = p.Step; duration.Value = p.OsdMilliseconds / 1000m; osd.Checked = p.OsdEnabled; startup.Checked = p.AutoStart;
            SetColor(color1, ColorTranslator.FromHtml(p.Color1)); SetColor(color2, ColorTranslator.FromHtml(p.Color2));
            ReloadDevices();
        }
        private void ConfigureColorButton(Button color)
        {
            color.Text = "Цвет…"; color.Dock = DockStyle.Fill; color.Margin = new Padding(8, 7, 0, 7);
            color.UseVisualStyleBackColor = false;
            color.Click += (s, e) =>
            {
                using (var dialog = new ColorDialog { Color = color.BackColor, FullOpen = true, AnyColor = true, SolidColorOnly = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) SetColor(color, dialog.Color);
            };
        }
        private static void SetColor(Button button, Color color)
        {
            button.BackColor = color; button.ForeColor = Visuals.ContrastText(color);
            button.Tag = String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
        private static void AddRow(TableLayoutPanel layout, int row, string name, Control control)
        {
            layout.Controls.Add(Label(name), 0, row); control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 7, 0, 7); layout.Controls.Add(control, 1, row); layout.SetColumnSpan(control, 2);
        }
        private void ReloadDevices()
        {
            try
            {
                string id1 = favorite1.SelectedItem == null ? app.Preferences.Favorite1 : ((AudioDevice)favorite1.SelectedItem).Id;
                string id2 = favorite2.SelectedItem == null ? app.Preferences.Favorite2 : ((AudioDevice)favorite2.SelectedItem).Id;
                var list = Audio.Devices(); Fill(favorite1, list, id1); Fill(favorite2, list, id2);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        }
        private static void Fill(ComboBox combo, List<AudioDevice> list, string id)
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.Items.Clear(); combo.Items.Add(new AudioDevice { Name = "Не выбрано" });
            foreach (var d in list) combo.Items.Add(d);
            if (!String.IsNullOrEmpty(id) && !list.Exists(d => d.Id == id)) combo.Items.Add(new AudioDevice { Id = id, Name = "Недоступно: " + id });
            combo.SelectedIndex = 0; for (int i = 1; i < combo.Items.Count; i++) if (((AudioDevice)combo.Items[i]).Id == id) combo.SelectedIndex = i;
        }
        private void Save(object sender, EventArgs e)
        {
            try
            {
                var value = new Preferences { Favorite1 = ((AudioDevice)favorite1.SelectedItem).Id, Favorite2 = ((AudioDevice)favorite2.SelectedItem).Id,
                    Name1 = name1.Text, Name2 = name2.Text, Color1 = (string)color1.Tag, Color2 = (string)color2.Tag,
                    Step = (int)step.Value, OsdMilliseconds = (int)(duration.Value * 1000), OsdEnabled = osd.Checked, AutoStart = startup.Checked };
                app.SavePreferences(value); Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
    internal sealed class AudioOverlay : Form
    {
        private readonly Timer timer = new Timer { Interval = 60 };
        private DateTime hideAt;
        private string caption = "";
        private int percent;
        private bool muted;
        internal AudioOverlay()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 34, 39); DoubleBuffered = true;
            timer.Tick += (s, e) => { if (DateTime.UtcNow >= hideAt) { Hide(); timer.Stop(); } };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20; return p; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        internal void Present(string name, int level, bool isMuted, int milliseconds)
        {
            caption = name; percent = level; muted = isMuted; Text = name + " " + level + "%";
            float scale = DeviceDpi / 96f;
            Size = new Size((int)(360 * scale), (int)(92 * scale));
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - (int)(44 * scale));
            hideAt = DateTime.UtcNow.AddMilliseconds(milliseconds); Invalidate(); if (!Visible) Show(); timer.Start();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.ScaleTransform(ClientSize.Width / 360f, ClientSize.Height / 92f);
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var name = new Font("Segoe UI", 11f, GraphicsUnit.Point))
            using (var number = new Font("Segoe UI", 20f, FontStyle.Bold))
            using (var mutedFont = new Font("Segoe UI", 8f))
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.DrawString(caption, name, Brushes.White, new RectangleF(20, 17, 235, 27), format);
                g.DrawString(percent + "%", number, Brushes.White, new RectangleF(263, 10, 90, 37));
                if (muted) g.DrawString("Звук выключен", mutedFont, Brushes.LightGray, 21, 43);
            }
            using (var track = new SolidBrush(Color.FromArgb(67, 71, 80)))
            using (var fill = new SolidBrush(muted ? Color.Gray : Color.FromArgb(95, 184, 255)))
            { g.FillRectangle(track, 22, 67, 316, 6); if (percent > 0) g.FillRectangle(fill, 22, 67, 316 * percent / 100f, 6); }
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
