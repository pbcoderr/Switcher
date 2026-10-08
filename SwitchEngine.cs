using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using TimeoutException = System.TimeoutException;

namespace Switcher
{
    public sealed class State
    {
        public bool Tailscale, Zapret;
        public State(bool t, bool z) { Tailscale = t; Zapret = z; }
        public string Label { get { return Tailscale ? (Zapret ? "Оба включены" : "Tailscale") : (Zapret ? "zapret" : "Оба выключены"); } }
    }

    public interface IBackend
    {
        State Read();
        void Validate();
        void Tail(bool on);
        void Zap(bool on);
    }

    public sealed class SwitchEngine
    {
        readonly IBackend backend;
        public SwitchEngine(IBackend b) { backend = b; }
        public State Switch(bool? toTailscale)
        {
            // Validate before disconnecting anything. Unknown states never mean "off".
            backend.Validate();
            State old = backend.Read();
            bool target = toTailscale ?? !old.Tailscale;
            if (old.Tailscale == target && old.Zapret == !target) return old;
            try
            {
                if (target) { backend.Zap(false); backend.Tail(true); }
                else { backend.Tail(false); backend.Zap(true); }
                State current = backend.Read();
                if (current.Tailscale != target || current.Zapret == target)
                    throw new InvalidOperationException("Не удалось подтвердить выбранный режим.");
                return current;
            }
            catch (Exception failure)
            {
                string recovery;
                try
                {
                    // Stop the attempted target first; do not restore the old mode if that fails.
                    if (target) { backend.Tail(false); backend.Zap(old.Zapret); if (old.Tailscale) backend.Tail(true); }
                    else { backend.Zap(false); backend.Tail(old.Tailscale); if (old.Zapret) backend.Zap(true); }
                    State restored = backend.Read();
                    if (restored.Tailscale != old.Tailscale || restored.Zapret != old.Zapret)
                        throw new InvalidOperationException("Исходное состояние не подтверждено.");
                    recovery = "Исходное состояние восстановлено: " + restored.Label + ".";
                }
                catch (Exception rollback) { recovery = "Не удалось восстановить исходное состояние: " + rollback.Message; }
                throw new InvalidOperationException(failure.Message + "\r\n\r\n" + recovery, failure);
            }
        }
    }

    public sealed class WindowsBackend : IBackend, IExitNodeSource
    {
        readonly AppSettings settings;
        string TailExe { get { return settings.TailExe; } }
        string ZapExe { get { return settings.ZapExe; } }
        public WindowsBackend(AppSettings settings) { this.settings = settings.Copy(); }
        public List<ExitNodeInfo> GetExitNodes() { return ExitNodes.Parse(Run(TailExe, "status --json", 8000)); }
        public static string Run(string file, string args, int timeout)
        {
            using (Process p = new Process())
            {
                p.StartInfo = new ProcessStartInfo(file, args) {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(file)
                };
                p.Start();
                Task<string> stdout = p.StandardOutput.ReadToEndAsync();
                Task<string> stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeout))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch (InvalidOperationException) { }
                    throw new TimeoutException("Истекло время ожидания: " + Path.GetFileName(file) + " " + args);
                }
                Task.WaitAll(stdout, stderr);
                if (p.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(file) + ": " + stderr.Result.Trim() + " " + stdout.Result.Trim());
                return stdout.Result;
            }
        }

        public static bool ServiceOn(string name)
        {
            using (ServiceController s = new ServiceController(name))
            {
                if (s.Status == ServiceControllerStatus.Running) return true;
                if (s.Status == ServiceControllerStatus.Stopped) return false;
                throw new InvalidOperationException("Служба " + name + " меняет состояние: " + s.Status + ". Подожди и повтори.");
            }
        }

        static void SetService(string name, bool on)
        {
            using (ServiceController s = new ServiceController(name))
            {
                if (s.Status == ServiceControllerStatus.StartPending) s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
                if (s.Status == ServiceControllerStatus.StopPending) s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25));
                s.Refresh();
                ServiceControllerStatus target = on ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped;
                if (s.Status == target) return;
                if (on) s.Start(); else s.Stop();
                s.WaitForStatus(target, TimeSpan.FromSeconds(25));
            }
        }

        public State Read()
        {
            bool z = ServiceOn("zapret");
            CheckStandalone(z);
            if (!ServiceOn("Tailscale")) return new State(false, z);
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Run(TailExe, "status --json", 8000));
            object value;
            if (!data.TryGetValue("BackendState", out value)) throw new InvalidOperationException("Tailscale не сообщил состояние подключения.");
            string state = value as string;
            if (state == "Running") return new State(true, z);
            if (state == "Stopped") return new State(false, z);
            throw new InvalidOperationException("Tailscale: " + state + ". Проверь вход в аккаунт и подключение через приложение Tailscale.");
        }

        public static void CheckStandalone(bool serviceRunning)
        {
            Process[] processes = Process.GetProcessesByName("winws");
            try
            {
                if (processes.Length > (serviceRunning ? 1 : 0))
                    throw new InvalidOperationException("Обнаружен отдельно запущенный winws.exe. Закрой окно zapret, запущенное через .bat, и повтори. Переключатель управляет службой zapret.");
            }
            finally { foreach (Process p in processes) p.Dispose(); }
        }

        public void Validate()
        {
            if (!File.Exists(TailExe)) throw new FileNotFoundException("Не найден Tailscale.", TailExe);
            if (!File.Exists(ZapExe)) throw new FileNotFoundException("Не найден zapret.", ZapExe);
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret"))
            {
                string command = key == null ? "" : Convert.ToString(key.GetValue("ImagePath"));
                if (!String.Equals(AppSettings.ExecutableFromCommand(command), ZapExe, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Служба zapret отсутствует или относится к другой папке. Открой «Настройки», выбери папку и сохрани стратегию.");
                string actual = Convert.ToString(key.GetValue("zapret-discord-youtube"));
                if (!String.Equals(actual, Path.GetFileNameWithoutExtension(settings.Strategy), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Стратегия службы изменена вне переключателя. Открой «Настройки» и сохрани нужную стратегию.");
            }
            // This also verifies access and stable service states before disconnecting.
            ServiceOn("Tailscale");
            CheckStandalone(ServiceOn("zapret"));
        }

        public void Tail(bool on)
        {
            if (on) SetService("Tailscale", true);
            else if (!ServiceOn("Tailscale")) return;
            // No flags: preserve saved routes, exit node and all other preferences.
            Run(TailExe, on ? "up" : "down", 35000);
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 15000)
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Run(TailExe, "status --json", 8000));
                string value = Convert.ToString(d["BackendState"]);
                if (value == (on ? "Running" : "Stopped")) return;
                if (value == "NeedsLogin" || value == "NeedsMachineAuth")
                    throw new InvalidOperationException("Tailscale требует входа или подтверждения устройства: " + value);
                Thread.Sleep(500);
            }
            throw new TimeoutException("Tailscale не подтвердил " + (on ? "подключение." : "отключение."));
        }

        public void Zap(bool on)
        {
            SetService("zapret", on);
            if (on) { Thread.Sleep(1200); if (!ServiceOn("zapret")) throw new InvalidOperationException("Служба zapret завершилась сразу после запуска. Проверь выбранную стратегию в настройках."); }
            else
            {
                // SCM can report Stopped slightly before the process fully exits.
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 5000)
                {
                    Process[] ps = Process.GetProcessesByName("winws");
                    int count = ps.Length;
                    foreach (Process p in ps) p.Dispose();
                    if (count == 0) return;
                    Thread.Sleep(200);
                }
                throw new InvalidOperationException("winws.exe всё ещё работает после остановки службы. Tailscale не будет включён.");
            }
        }
    }

}
