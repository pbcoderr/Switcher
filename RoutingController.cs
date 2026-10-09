using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Switcher
{
    // A job owns only the child started here. Closing Switcher also closes its tunnel.
    public sealed class ChildJob : IDisposable
    {
        IntPtr handle;
        [StructLayout(LayoutKind.Sequential)] struct Basic { public long User, Job; public uint Flags; public UIntPtr Min, Max; public uint Limit; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] struct IO { public ulong A, B, C, D, E, F; }
        [StructLayout(LayoutKind.Sequential)] struct Limits { public Basic Basic; public IO IO; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attr, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int type, ref Limits info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public ChildJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) throw new Win32Exception();
            var limits = new Limits(); limits.Basic.Flags = 0x2000;
            if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf(limits))) { Dispose(); throw new Win32Exception(); }
        }
        public void Assign(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) throw new Win32Exception(); }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }

    public sealed class RoutingRecovery
    {
        public bool Tailscale { get; set; }
        public bool Zapret { get; set; }
    }

    public sealed class RoutingController : IDisposable
    {
        readonly string root;
        readonly object logLock = new object();
        readonly Queue<string> lines = new Queue<string>();
        Process process;
        ChildJob job;
        int port, connectivityFailures;
        NativeRoutingPlan plan;
        IDisposable firewall;
        readonly Func<string, uint, IDisposable> firewallFactory;
        volatile bool started;

        public bool Tunnel { get; private set; }
        public bool Active { get { return process != null; } }
        public bool Alive { get { return process != null && !process.HasExited; } }
        public bool HasRecovery { get { return File.Exists(RecoveryPath); } }

        public string Log { get { lock (logLock) return String.Join(Environment.NewLine, lines.ToArray()); } }
        public string SettingsPath { get { return Path.Combine(root, "routing.json"); } }
        string RecoveryPath { get { return Path.Combine(root, "routing-recovery.json"); } }
        string RuntimePath { get { return Path.Combine(root, "routing-runtime.json"); } }
        public RoutingController(string root) : this(root, (path, index) => new TemporaryFirewall(path, index)) { }
        public RoutingController(string root, Func<string, uint, IDisposable> firewallFactory) { this.root = root; this.firewallFactory = firewallFactory; }
        public void Validate(RoutingSettings settings, IBackend backend)
        {
            if (String.Equals(settings.EnginePath, BundledEngine.FilePath, StringComparison.OrdinalIgnoreCase))
                BundledEngine.EnsureAvailable();
            var native = backend as INativeRoutingBackend;
            if (native == null) throw new InvalidOperationException("Недоступно установленное подключение Tailscale.");
            plan = native.ReadRoutingPlan();
            settings.Validate(true);
            string version = WindowsBackend.Run(settings.EnginePath, "version", 8000);
            if (!Regex.IsMatch(version, @"sing-box version 1\.14\.\d+\s"))
                throw new InvalidOperationException("Нужен комплектный sing-box 1.14.2.");
            WriteConfig(settings, false);
            WindowsBackend.Run(settings.EnginePath, "check -c \"" + RuntimePath + "\"", 15000);
            WriteConfig(settings, true);
            WindowsBackend.Run(settings.EnginePath, "check -c \"" + RuntimePath + "\"", 15000);
        }
        void WriteConfig(RoutingSettings settings, bool tunnel)
        {
            if (port == 0)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            }
            JsonStore.Save(RuntimePath, RoutingConfig.Build(settings, plan, port, tunnel));
        }
        public void StartRouting(RoutingSettings settings, IBackend backend)
        {
            if (Active || HasRecovery) throw new InvalidOperationException("Сначала останови текущий сеанс или восстанови старое подключение.");
            Validate(settings, backend);
            State old = backend.Read();
            if (!old.Tailscale || old.Zapret) throw new InvalidOperationException("Сначала включи обычный Tailscale и отключи zapret.");
            try
            {
                firewall = firewallFactory(settings.EnginePath, plan.PhysicalIndex);
                StartChild(settings, false);
                if (!Probe(15000)) throw new InvalidOperationException("Обычный Tailscale не передаёт трафик через выбранный exit node.");
                StopProcess();
                StartChild(settings, true);
                var windowsFirewall = firewall as TemporaryFirewall;
                if (windowsFirewall != null) windowsFirewall.AllowTunnel("Switcher-Routing");
                if (!Probe(15000)) throw new InvalidOperationException("Exit node не ответил после включения правил.");
                Tunnel = true; connectivityFailures = 0;
            }
            catch (Exception failure) { StopChild(); SaveDiagnostics(failure.Message); throw; }
        }
        public void CheckConnection(IBackend backend)
        {
            if (!Active) return;
            try
            {
                var native = backend as INativeRoutingBackend;
                if (native == null || !plan.SameConnection(native.ReadRoutingPlan()))
                    throw new InvalidOperationException("Подключение изменилось. Ожидание восстановления сети.");
                if (Probe(2500)) connectivityFailures = 0;
                else if (++connectivityFailures >= 2) throw new InvalidOperationException("Нет связи через Tailscale. Ожидание восстановления сети.");
            }
            catch (Exception ex) { StopChild(); SaveDiagnostics(ex.Message); throw; }
        }
        void StartChild(RoutingSettings settings, bool tunnel)
        {
            WriteConfig(settings, tunnel);
            lock (logLock) { lines.Clear(); }
            started = false;
            job = new ChildJob();
            process = new Process { StartInfo = new ProcessStartInfo(settings.EnginePath, "run -c \"" + RuntimePath + "\"") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = root } };
            process.OutputDataReceived += Receive; process.ErrorDataReceived += Receive;
            try
            {
                process.Start(); job.Assign(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                var clock = Stopwatch.StartNew();
                while (!started && clock.ElapsedMilliseconds < 10000 && !process.HasExited) Thread.Sleep(100);
                if (!started || process.HasExited) throw new InvalidOperationException("Движок не запустился.\r\n" + Log);
            }
            catch { StopChild(); throw; }
        }
        void Receive(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            string line = Regex.Replace(e.Data, @"\x1b\[[0-9;]*m", "");
            if (line.Contains("sing-box started")) started = true;
            lock (logLock)
            {
                lines.Enqueue(line); while (lines.Count > 160) lines.Dequeue();
            }
        }
        public bool Probe(int timeout)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeout && Alive)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        client.ReceiveTimeout = 2500; client.SendTimeout = 2500;
                        client.Connect(IPAddress.Loopback, port);
                        using (var stream = client.GetStream())
                        {
                            stream.Write(new byte[] { 5, 1, 0 }, 0, 3);
                            if (stream.ReadByte() != 5 || stream.ReadByte() != 0) throw new IOException();
                            // TCP reachability of Cloudflare through the selected exit node, no user data.
                            byte[] request = { 5, 1, 0, 1, 1, 1, 1, 1, 1, 187 };
                            stream.Write(request, 0, request.Length);
                            if (stream.ReadByte() == 5 && stream.ReadByte() == 0) return true;
                        }
                    }
                }
                catch (IOException) { }
                catch (SocketException) { }
                Thread.Sleep(250);
            }
            return false;
        }
        public void Stop(IBackend backend)
        {
            SaveDiagnostics("Завершение сеанса");
            StopChild();
            if (!HasRecovery) return;
            var old = JsonStore.Read<RoutingRecovery>(RecoveryPath);
            if (old == null) throw new InvalidDataException("Файл восстановления повреждён.");
            backend.Zap(false); backend.Tail(false);
            if (old.Tailscale) backend.Tail(true);
            if (old.Zapret) backend.Zap(true);
            State restored = backend.Read();
            if (restored.Tailscale != old.Tailscale || restored.Zapret != old.Zapret) throw new InvalidOperationException("Не удалось подтвердить восстановление подключений.");
            File.Delete(RecoveryPath);
        }
        public void StopChild() { try { StopProcess(); } finally { if (firewall != null) { firewall.Dispose(); firewall = null; } } }
        void StopProcess()
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited) { process.Kill(); if (!process.WaitForExit(5000)) throw new TimeoutException("Движок ещё завершает работу."); }
                    process.WaitForExit();
                }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); process = null; if (job != null) { job.Dispose(); job = null; } Tunnel = false; }
            }
            else if (job != null) { job.Dispose(); job = null; }
        }
        public void Dispose() { StopChild(); }
        void SaveDiagnostics(string reason)
        {
            try
            {
                string safe = Regex.Replace(reason + Environment.NewLine + Log, @"https://login\.tailscale\.com/[^\s]+", "[ссылка входа скрыта]");
                safe = Regex.Replace(safe, @"tskey-[^\s]+", "[ключ скрыт]");
                File.WriteAllText(Path.Combine(root, "routing-last.log"), DateTimeOffset.Now.ToString("u") + " " + safe, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}



