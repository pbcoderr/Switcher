using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;


namespace Switcher
{
    public sealed class HotkeySpec
    {
        public uint Modifiers { get; set; }
        public int Key { get; set; }
        public HotkeySpec() { }
        public HotkeySpec(uint modifiers, Keys key) { Modifiers = modifiers; Key = (int)key; }
        public void Validate()
        {
            bool keyAllowed = (Key >= (int)Keys.A && Key <= (int)Keys.Z) ||
                (Key >= (int)Keys.D0 && Key <= (int)Keys.D9) ||
                (Key >= (int)Keys.F1 && Key <= (int)Keys.F24 && Key != (int)Keys.F12) ||
                Key == (int)Keys.Space || Key == (int)Keys.Pause || Key == (int)Keys.Insert ||
                Key == (int)Keys.Home || Key == (int)Keys.End || Key == (int)Keys.PageUp || Key == (int)Keys.PageDown;
            if (!keyAllowed || (Modifiers & ~7u) != 0 || (Modifiers & 3) == 0)
                throw new InvalidOperationException("Выбери Ctrl или Alt и букву, цифру либо F-клавишу. F12 зарезервирована Windows.");
        }
        public bool Same(HotkeySpec other) { return other != null && Modifiers == other.Modifiers && Key == other.Key; }
        public override string ToString()
        {
            string key = ((Keys)Key).ToString();
            if (Key >= (int)Keys.D0 && Key <= (int)Keys.D9) key = (Key - (int)Keys.D0).ToString();
            return ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "") + ((Modifiers & 4) != 0 ? "Shift+" : "") + key;
        }
    }

    public sealed class AppSettings
    {
        public string TailscaleFolder { get; set; }
        public string ZapretFolder { get; set; }
        public string Strategy { get; set; }
        public HotkeySpec SwitchHotkey { get; set; }
        public HotkeySpec ExitHotkey { get; set; }
        [ScriptIgnore] public string TailExe { get { return Path.Combine(TailscaleFolder, "tailscale.exe"); } }
        [ScriptIgnore] public string ZapExe { get { return Path.Combine(ZapretFolder, "bin", "winws.exe"); } }
        public static AppSettings Defaults()
        {
            return new AppSettings {
                TailscaleFolder = "", ZapretFolder = "", Strategy = "",
                SwitchHotkey = new HotkeySpec(3, Keys.F8), ExitHotkey = new HotkeySpec(3, Keys.F9)
            };
        }
        public static string ExecutableFromCommand(string command)
        {
            Match match = Regex.Match(command ?? "", "^\\s*\"([^\"]+\\.exe)\"", RegexOptions.IgnoreCase);
            if (match.Success) return Environment.ExpandEnvironmentVariables(match.Groups[1].Value);
            match = Regex.Match(command ?? "", "^\\s*(.+?\\.exe)(?:\\s|$)", RegexOptions.IgnoreCase);
            return match.Success ? Environment.ExpandEnvironmentVariables(match.Groups[1].Value) : "";
        }
        public AppSettings Copy() { return new JavaScriptSerializer().Deserialize<AppSettings>(new JavaScriptSerializer().Serialize(this)); }
        public void Validate()
        {
            if (SwitchHotkey == null || ExitHotkey == null) throw new InvalidOperationException("Укажи обе горячие клавиши.");
            SwitchHotkey.Validate(); ExitHotkey.Validate();
            if (SwitchHotkey.Same(ExitHotkey)) throw new InvalidOperationException("Для переключения и выхода нужны разные сочетания.");
            TailscaleFolder = CleanFolder(TailscaleFolder);
            ZapretFolder = CleanFolder(ZapretFolder);
            if (String.IsNullOrWhiteSpace(Strategy) || Path.GetFileName(Strategy) != Strategy || !Strategy.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || Strategy.StartsWith("service", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Выбери стратегию zapret из списка.");
            if (!File.Exists(TailExe)) throw new FileNotFoundException("В папке Tailscale не найден tailscale.exe.");
            if (!File.Exists(ZapExe)) throw new FileNotFoundException("В папке zapret не найден bin\\winws.exe.");
            if (!File.Exists(Path.Combine(ZapretFolder, Strategy))) throw new FileNotFoundException("Файл выбранной стратегии не найден.");
        }
        public static string CleanFolder(string path)
        {
            path = (path ?? "").Trim().Trim('"');
            if (!Path.IsPathRooted(path)) throw new InvalidOperationException("Укажи полный путь к папке.");
            string full = Path.GetFullPath(path);
            return full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd(Path.DirectorySeparatorChar);
        }
        public bool SameStrategy(AppSettings other)
        {
            return String.Equals(ZapretFolder, other.ZapretFolder, StringComparison.OrdinalIgnoreCase) && String.Equals(Strategy, other.Strategy, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class StartupSettings
    {
        public AppSettings Value;
        public bool NeedsSetup;
        public string Error;
    }

    public static class SettingsStore
    {
        // Installed under Program Files: only an elevated process can change executable paths.
        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"); } }
        public static StartupSettings ReadStartup(string path)
        {
            var result = new StartupSettings { Value = AppSettings.Defaults(), NeedsSetup = true };
            if (!File.Exists(path)) return result;
            try
            {
                result.Value = LoadFrom(path);
                // A moved/deleted installation must reopen setup with the previous values.
                result.Value.Validate();
                result.NeedsSetup = false;
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }
        public static AppSettings Load() { return LoadFrom(FilePath); }
        public static AppSettings LoadFrom(string path)
        {
            if (!File.Exists(path)) return AppSettings.Defaults();
            var settings = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(path));
            if (settings == null || settings.SwitchHotkey == null || settings.ExitHotkey == null) throw new InvalidDataException("Файл настроек повреждён.");
            settings.SwitchHotkey.Validate(); settings.ExitHotkey.Validate();
            if (settings.SwitchHotkey.Same(settings.ExitHotkey)) throw new InvalidDataException("В файле настроек совпадают горячие клавиши.");
            if (String.IsNullOrWhiteSpace(settings.TailscaleFolder) || String.IsNullOrWhiteSpace(settings.ZapretFolder) || String.IsNullOrWhiteSpace(settings.Strategy)) throw new InvalidDataException("В файле настроек отсутствуют папки или стратегия.");
            return settings;
        }
        public static void Save(AppSettings settings) { SaveTo(FilePath, settings); }
        public static void SaveTo(string path, AppSettings settings)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(settings), System.Text.Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, null, true); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
