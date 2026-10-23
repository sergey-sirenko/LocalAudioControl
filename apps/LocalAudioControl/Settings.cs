using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace LocalAudioControl
{
    public sealed class Preferences
    {
        public string Favorite1 { get; set; }
        public string Favorite2 { get; set; }
        public string Name1 { get; set; }
        public string Name2 { get; set; }
        public string Color1 { get; set; }
        public string Color2 { get; set; }
        public int Step { get; set; }
        public bool OsdEnabled { get; set; }
        public int OsdMilliseconds { get; set; }
        public bool AutoStart { get; set; }
        public Preferences() { Step = 3; OsdEnabled = true; OsdMilliseconds = 1200; AutoStart = true; Name1 = "Первое избранное"; Name2 = "Второе избранное"; Color1 = "#145EAA"; Color2 = "#00836B"; }
        public void Validate()
        {
            if (Step < 1 || Step > 20) throw new InvalidDataException("Шаг должен быть от 1 до 20%.");
            if (OsdMilliseconds < 500 || OsdMilliseconds > 10000) throw new InvalidDataException("Время индикатора должно быть от 0,5 до 10 секунд.");
            Name1 = String.IsNullOrWhiteSpace(Name1) ? "Первое избранное" : Name1.Trim();
            Name2 = String.IsNullOrWhiteSpace(Name2) ? "Второе избранное" : Name2.Trim();
            if (Name1.Length > 80 || Name2.Length > 80) throw new InvalidDataException("Название не должно превышать 80 символов.");
            // Old settings do not contain colors; preserve all other values and use defaults.
            Color1 = NormalizeColor(Color1, "#145EAA"); Color2 = NormalizeColor(Color2, "#00836B");
        }
        private static string NormalizeColor(string value, string fallback)
        {
            if (String.IsNullOrEmpty(value)) return fallback;
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$"))
                throw new InvalidDataException("Цвет должен быть записан в формате #RRGGBB.");
            return value.ToUpperInvariant();
        }
        public string ColorForDevice(string id)
        {
            if (!String.IsNullOrEmpty(id) && id == Favorite1) return Color1;
            if (!String.IsNullOrEmpty(id) && id == Favorite2) return Color2;
            return "#145EAA";
        }
    }
    public static class Storage
    {
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalAudioControl");
        public static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public static Preferences Load()
        {
            if (!File.Exists(SettingsPath)) return new Preferences();
            try { var p = Json.Deserialize<Preferences>(File.ReadAllText(SettingsPath)); p.Validate(); return p; }
            catch (Exception ex) { Log(ex); throw new InvalidDataException("Не удалось прочитать настройки. Исходный файл сохранён: " + SettingsPath, ex); }
        }
        public static void Save(Preferences p)
        {
            p.Validate(); Directory.CreateDirectory(DirectoryPath);
            string temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Serialize(p)); file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (File.Exists(SettingsPath)) File.Replace(temp, SettingsPath, SettingsPath + ".bak"); else File.Move(temp, SettingsPath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void Startup(bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enable) key.SetValue("LocalAudioControl", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --tray");
                else key.DeleteValue("LocalAudioControl", false);
            }
        }
        public static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath); string path = Path.Combine(DirectoryPath, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 262144) File.Move(path, path + "." + DateTime.UtcNow.Ticks);
                File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + ex + Environment.NewLine);
            }
            catch { }
        }
    }
}
