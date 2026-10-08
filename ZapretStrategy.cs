using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Switcher
{
    public static class ZapretStrategy
    {
        public static string[] List(string folder)
        {
            if (!Directory.Exists(folder)) return new string[0];
            return Directory.GetFiles(folder, "*.bat", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).Where(n => !n.StartsWith("service", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => Regex.Replace(n, @"\d+", m => m.Value.PadLeft(8, '0')), StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static string Build(AppSettings settings)
        {
            settings.Validate();
            string flag = Path.Combine(settings.ZapretFolder, "utils", "game_filter.enabled");
            string mode = File.Exists(flag) ? (File.ReadLines(flag).FirstOrDefault(l => !String.IsNullOrWhiteSpace(l)) ?? "").Trim().ToLowerInvariant() : "disabled";
            return BuildFromText(File.ReadAllText(Path.Combine(settings.ZapretFolder, settings.Strategy)), settings.ZapretFolder, mode, true);
        }
        public static string BuildFromText(string text, string folder, string gameMode, bool verifyFiles)
        {
            folder = AppSettings.CleanFolder(folder);
            string tcp = gameMode == "all" || gameMode == "tcp" ? "1024-65535" : "12";
            string udp = gameMode != "disabled" && gameMode != "tcp" ? "1024-65535" : "12";
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var command = new StringBuilder();
            bool found = false, continued = false, ended = false;
            foreach (string source in lines)
            {
                string line = source.Trim();
                if (line.Length == 0 || line.StartsWith("::") || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase)) continue;
                if (!found)
                {
                    Match start = Regex.Match(line, "^start\\s+.*?\"%BIN%winws\\.exe\"\\s+(.+)$", RegexOptions.IgnoreCase);
                    if (!start.Success) continue;
                    found = true;
                    line = start.Groups[1].Value;
                }
                else if (ended) throw new InvalidDataException("После команды winws есть дополнительные команды. Этот формат стратегии пока не поддерживается.");
                continued = line.EndsWith("^");
                if (continued) line = line.Substring(0, line.Length - 1).TrimEnd();
                command.Append(line).Append(' ');
                if (!continued) ended = true;
            }
            if (!found || continued) throw new InvalidDataException("Не удалось прочитать команду winws.exe из стратегии. Поддерживаются стандартные стратегии zapret 1.10.2.");
            string args = command.ToString().Trim().Replace("^!", "!");
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                {"BIN", Path.Combine(folder, "bin") + "\\"}, {"LISTS", Path.Combine(folder, "lists") + "\\"},
                {"GameFilterTCP", tcp}, {"GameFilterUDP", udp}, {"GameFilter", gameMode == "disabled" ? "12" : "1024-65535"}
            };
            args = Regex.Replace(args, "%([^%]+)%", m => {
                string value;
                if (!variables.TryGetValue(m.Groups[1].Value, out value)) throw new InvalidDataException("Неизвестная переменная стратегии: " + m.Value);
                return value;
            });
            bool quoted = false;
            var tokens = new List<string>();
            var token = new StringBuilder();
            foreach (char c in args)
            {
                if (c == '"') quoted = !quoted;
                if (!quoted && "&|<>^\r\n".IndexOf(c) >= 0) throw new InvalidDataException("Стратегия содержит неподдерживаемые команды оболочки.");
                if (!quoted && Char.IsWhiteSpace(c)) { if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); } }
                else token.Append(c);
            }
            if (quoted) throw new InvalidDataException("Незакрытая кавычка в стратегии.");
            if (token.Length > 0) tokens.Add(token.ToString());
            if (tokens.Count < 2 || tokens.Any(t => !t.StartsWith("--")) || !tokens.Any(t => t.StartsWith("--wf-")))
                throw new InvalidDataException("Неподдерживаемый формат аргументов стратегии.");
            if (verifyFiles)
                foreach (Match match in Regex.Matches(args, "\"([^\"]+)\""))
                {
                    string path = match.Groups[1].Value;
                    if (Path.IsPathRooted(path) && !File.Exists(path)) throw new FileNotFoundException("Стратегии нужен отсутствующий файл: " + path);
                }
            string result = "\"" + Path.Combine(folder, "bin", "winws.exe") + "\" " + String.Join(" ", tokens);
            if (result.Length >= 32767) throw new InvalidDataException("Команда стратегии слишком длинная.");
            return result;
        }
    }

    public sealed class StrategySnapshot
    {
        public string Command, Name;
        public bool Running;
    }
    public interface IStrategyService
    {
        StrategySnapshot Snapshot();
        void SetRunning(bool on);
        void Configure(string command, string name);
    }
    public static class StrategyUpdate
    {
        public static void Apply(IStrategyService service, string command, string name, Action saveSettings)
        {
            StrategySnapshot old = service.Snapshot();
            bool change = old.Command != command || old.Name != name;
            if (!change) { saveSettings(); return; }
            try
            {
                if (old.Running) service.SetRunning(false);
                service.Configure(command, name);
                if (old.Running) service.SetRunning(true);
                saveSettings();
            }
            catch (Exception ex)
            {
                try
                {
                    service.SetRunning(false);
                    service.Configure(old.Command, old.Name);
                    if (old.Running) service.SetRunning(true);
                }
                catch (Exception rollback) { throw new InvalidOperationException(ex.Message + "\r\nНе удалось восстановить прежнюю стратегию: " + rollback.Message, ex); }
                throw new InvalidOperationException(ex.Message + "\r\nПрежняя стратегия и состояние службы восстановлены.", ex);
            }
        }
    }
    public sealed class ZapretService : IStrategyService
    {
        readonly WindowsBackend backend;
        const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\zapret";
        public ZapretService(WindowsBackend backend) { this.backend = backend; }
        public StrategySnapshot Snapshot()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKey))
            {
                if (key == null) throw new InvalidOperationException("Служба zapret не установлена. Сначала установи её через service.bat → 1.");
                bool running = WindowsBackend.ServiceOn("zapret");
                WindowsBackend.CheckStandalone(running);
                return new StrategySnapshot { Command = Convert.ToString(key.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames)), Name = key.GetValue("zapret-discord-youtube") as string, Running = running };
            }
        }
        public void SetRunning(bool on) { backend.Zap(on); }
        public void Configure(string command, string name)
        {
            IntPtr scm = OpenSCManager(null, null, 1);
            if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                IntPtr service = OpenService(scm, "zapret", 2);
                if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    if (!ChangeServiceConfig(service, UInt32.MaxValue, UInt32.MaxValue, UInt32.MaxValue, command, null, IntPtr.Zero, null, null, null, null)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKey, true))
                    {
                        if (key == null) throw new InvalidOperationException("Служба zapret исчезла во время настройки.");
                        if (name == null) key.DeleteValue("zapret-discord-youtube", false); else key.SetValue("zapret-discord-youtube", name);
                    }
                }
                finally { CloseServiceHandle(service); }
            }
            finally { CloseServiceHandle(scm); }
        }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr OpenSCManager(string machine, string database, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr OpenService(IntPtr manager, string name, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ChangeServiceConfig(IntPtr service, uint type, uint start, uint error, string binary, string group, IntPtr tag, string dependencies, string user, string password, string display);
        [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CloseServiceHandle(IntPtr handle);
    }
}
