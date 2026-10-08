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
    public partial class Form1 : Form
    {
        WindowsBackend backend;
        readonly RoutingController routing = new RoutingController(AppDomain.CurrentDomain.BaseDirectory);
        readonly AutoRoutingPolicy autoRouting = new AutoRoutingPolicy();
        readonly ToolStripMenuItem stopRouting = new ToolStripMenuItem("Выключить маршрутизацию");
        AppSettings settings;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>();
        readonly ToolStripMenuItem status = new ToolStripMenuItem("Проверка…");
        readonly ToolStripMenuItem toggle = new ToolStripMenuItem("Переключить   Ctrl+Alt+F8");
        readonly ToolStripMenuItem tail = new ToolStripMenuItem("Включить Tailscale");
        readonly ToolStripMenuItem zap = new ToolStripMenuItem("Включить zapret");
        readonly ToolStripMenuItem quit = new ToolStripMenuItem("Выход   Ctrl+Alt+F9");
        readonly ToolStripMenuItem preferences = new ToolStripMenuItem("Настройки…");
        RoutingForm routingWindow;
        SettingsForm settingsWindow;
        readonly Dictionary<string, HelpForm> informationWindows = new Dictionary<string, HelpForm>();
        bool switchingValue, settingsOpenValue;
        bool RoutingBusy { get { return routingWindow != null && routingWindow.OperationBusy; } }
        bool switching { get { return switchingValue; } set { switchingValue = value; if (routingWindow != null) routingWindow.SetExternalBusy(value || settingsOpen); } }
        bool settingsOpen { get { return settingsOpenValue; } set { settingsOpenValue = value; if (routingWindow != null) routingWindow.SetExternalBusy(value || switching); } }
        bool refreshing, closing, hotkeysRegistered, needsSetup;
        string loadError, autoFailure;
        string details = "Проверка состояния…";
        int generation;

        public Form1()
        {
            InitializeComponent();
            if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime) return;
            ShowInTaskbar = false;
            StartupSettings startup = SettingsStore.ReadStartup(SettingsStore.FilePath);
            settings = startup.Value; needsSetup = startup.NeedsSetup; loadError = startup.Error;
            if (!needsSetup) backend = new WindowsBackend(settings);
            UpdateHotkeyLabels();
            icons["R"] = MakeIcon("R", Color.FromArgb(186, 210, 250));
            icons["T"] = MakeIcon("T", AppTheme.Blue);
            icons["Z"] = MakeIcon("Z", Color.FromArgb(157, 220, 223));
            icons["!"] = MakeIcon("!", Color.FromArgb(246, 203, 145));
            icons["?"] = MakeIcon("?", AppTheme.Error);
            icons["-"] = MakeIcon("–", AppTheme.Muted);
            icons["…"] = MakeIcon("…", AppTheme.Muted);
            var menu = new ContextMenuStrip();
            status.Enabled = false;
            menu.Items.AddRange(new ToolStripItem[] { status, new ToolStripSeparator(), toggle, tail, zap, new ToolStripSeparator() });
            menu.Items.Add("Подробности", null, delegate { OpenInformation("Подробности · Switcher", details); });
            menu.Items.Add(preferences);
            menu.Items.Add("Маршрутизация…", null, delegate { OpenRouting(); });
            menu.Items.Add(stopRouting); stopRouting.Click += delegate { ToggleRoutingFromTray(); };
            menu.Opening += delegate { UpdateRoutingMenu(); };
            menu.Items.Add("Помощь", null, delegate { HelpForm.Open(this); });
            menu.Items.Add("О программе", null, delegate { OpenInformation("О программе · Switcher", "Switcher " + Application.ProductVersion + "\r\nАвтор: pb_coder\r\n© 2026 pb_coder. Все права защищены.\r\n\r\nTailscale ↔ zapret и временные исключения маршрутизации.\r\n\r\nСпасибо bol-van — zapret (MIT), Flowseal и участникам zapret-discord-youtube (MIT), nekohasekai и SagerNet — sing-box (GPLv3+).\r\n\r\nСсылки и лицензии — в README."); });
            menu.Items.Add(quit);
            preferences.Click += delegate { OpenSettings(); };
            toggle.Click += delegate { Switch(null); };
            tail.Click += delegate { Switch(true); };
            zap.Click += delegate { Switch(false); };
            quit.Click += delegate { if (!switching) Close(); };
            AppTheme.Menu(menu);
            tray.ContextMenuStrip = menu;
            tray.Icon = icons["…"];
            tray.Text = "Tailscale ↔ zapret: проверка";
            tray.Visible = true;
            if (needsSetup) DisplaySetupPending();
            tray.DoubleClick += delegate { Switch(null); };
            timer.Interval = 5000;
            timer.Tick += delegate { RefreshStatus(); };
            timer.Start();
        }

        public static Icon MakeIcon(string letter, Color color)
        {
            return BrandIcons.Tray(letter, color);
        }

        protected override void SetVisibleCore(bool value) { if (DesignMode || System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime) { base.SetVisibleCore(value); return; } if (!IsHandleCreated) CreateHandle(); base.SetVisibleCore(false); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (DesignMode || System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime) return;
            BeginInvoke(new Action(delegate {
                if (needsSetup) { OpenSettings(); return; }
                string hotkeyError;
                bool registered = TryRegisterHotkeys(settings, out hotkeyError);
                RefreshStatus();
                if (!registered) Error(new InvalidOperationException(hotkeyError), false);
            }));
        }
        protected override void OnHandleDestroyed(EventArgs e) { ClearHotkeys(); base.OnHandleDestroyed(e); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && !settingsOpen && !needsSetup) { if (m.WParam.ToInt32() == 1) Switch(null); else if (m.WParam.ToInt32() == 2 && !switching) Close(); }
            base.WndProc(ref m);
        }

        void Display(State state)
        {
            string key = state.Tailscale ? (state.Zapret ? "!" : "T") : (state.Zapret ? "Z" : "-");
            tray.Icon = icons[key];
            tray.Text = "Tailscale ↔ zapret: " + state.Label;
            status.Text = state.Label;
            tail.Checked = state.Tailscale;
            zap.Checked = state.Zapret;
            details = "Состояние: " + state.Label + ".\r\nСтратегия: " + settings.Strategy + "\r\n\r\n" + settings.SwitchHotkey + " или двойной щелчок — переключить.\r\n" + settings.ExitHotkey + " — закрыть переключатель.\r\n\r\nИконка показывает состояние подключения Tailscale и службы zapret, а не доступность сайтов.\r\nПри выходе выбранный режим продолжит работать.";
            if (!state.Tailscale) autoFailure = null;
            if (state.Tailscale && !state.Zapret && !String.IsNullOrEmpty(autoFailure)) { tray.Icon = icons["?"]; status.Text = "Tailscale · исключения не запущены"; details += "\r\n\r\n" + autoFailure; }
            if (state.Tailscale && state.Zapret) details += "\r\n\r\nОба режима включены извне. Выбери нужный в меню.";
        }
        void Error(Exception ex, bool popup)
        {
            details = ex.Message;
            tray.Icon = icons["?"];
            tray.Text = "Tailscale ↔ zapret: ошибка — открой подробности";
            status.Text = "Ошибка проверки / переключения";
            tail.Checked = zap.Checked = false;
            // Errors remain visible in the tray status and on-demand details.
        }
        async void RefreshStatus()
        {
            if (switching || refreshing || closing || settingsOpen || needsSetup || RoutingBusy) return;
            if (routingWindow != null && routing.Active) { DisplayRouting(); return; }
            if (routing.Active && !routing.Alive)
            {
                switching = true; generation++;
                try { await Task.Run(() => routing.Stop(backend)); details = "Маршрутизация остановилась. Временные исключения удалены."; }
                catch (Exception ex) { Error(ex, false); }
                finally { switching = false; }
            }
            if (routing.Active && routing.Alive)
            {
                switching = true;
                try { await Task.Run(() => routing.CheckConnection(backend)); }
                catch (Exception ex) { Error(ex, false);  return; }
                finally { switching = false; }
            }
            if (routing.Active || routing.HasRecovery) { DisplayRouting(); return; }
            refreshing = true;
            int version = generation;
            WindowsBackend current = backend;
            try { State s = await Task.Run(() => current.Read()); if (!closing && !switching && !settingsOpen && !RoutingBusy && version == generation) { Display(s); if (routingWindow == null && settingsWindow == null) await StartAutomaticRouting(s); } }
            catch (Exception ex) { if (!closing && !switching && !settingsOpen && !RoutingBusy && version == generation) Error(ex, false); }
            finally { refreshing = false; }
        }
        async void Switch(bool? target)
        {
            if (switching || closing || settingsOpen || RoutingBusy) return;
            if (needsSetup) { OpenSettings(); return; }
            if (routing.HasRecovery) { OpenRouting(); return; }
            switching = true;
            generation++;
            toggle.Enabled = tail.Enabled = zap.Enabled = quit.Enabled = preferences.Enabled = false;
            tray.Icon = icons["…"];
            tray.Text = "Tailscale ↔ zapret: переключение…";
            status.Text = "Переключение…";
            try
            {
                if (routing.Active) await Task.Run(() => routing.Stop(backend));
                State s = await Task.Run(() => new SwitchEngine(backend).Switch(target));
                autoRouting.Resume();
                Display(s);
                
            }
            catch (Exception ex) { Error(ex, true); }
            finally { switching = false; toggle.Enabled = tail.Enabled = zap.Enabled = quit.Enabled = preferences.Enabled = true; RefreshStatus(); }
        }
        async Task StartAutomaticRouting(State state)
        {
            bool hasRules = false;
            RoutingSettings saved = null;
            try
            {
                if (!autoRouting.ShouldStart(state, routing.Active, routing.HasRecovery, true, DateTime.UtcNow)) return;
                saved = JsonStore.Read<RoutingSettings>(routing.SettingsPath);
                hasRules = saved != null && saved.Rules != null && saved.Rules.Exists(rule => rule != null && rule.Enabled && rule.Target == "direct");
                if (!autoRouting.ShouldStart(state, routing.Active, routing.HasRecovery, hasRules, DateTime.UtcNow)) return;
                // Only the built-in engine and the current exceptions schema are used automatically.
                saved.EnginePath = BundledEngine.FilePath;
                if (saved.DefaultTarget != "tailscale" || saved.Rules.Exists(rule => rule == null || rule.Target != "direct"))
                    throw new InvalidDataException("Открой маршрутизацию и сохрани список исключений для автоматического запуска.");
                switching = true; generation++;
                toggle.Enabled = tail.Enabled = zap.Enabled = quit.Enabled = preferences.Enabled = false;
                tray.Icon = icons["…"]; status.Text = "Включаю исключения…";
                await Task.Run(() => routing.StartRouting(saved, backend));

                DisplayRouting();
            }
            catch (Exception ex)
            {
                autoRouting.Failed(DateTime.UtcNow); autoFailure = ex.Message;
                Display(state);
                details += "\r\n\r\nАвтозапуск исключений: " + ex.Message;
                status.Text = "Tailscale · исключения не запущены";
                tray.Icon = icons["?"];
            }
            finally
            {
                switching = false;
                toggle.Enabled = tail.Enabled = zap.Enabled = quit.Enabled = preferences.Enabled = true;
            }
        }

        async void StopRoutingFromTray()
        {
            if (switching || settingsOpen || closing || RoutingBusy || (!routing.Active && !routing.HasRecovery)) return;
            autoRouting.Pause();
            switching = true; generation++;
            toggle.Enabled = tail.Enabled = zap.Enabled = quit.Enabled = preferences.Enabled = stopRouting.Enabled = false;
            try
            {
                await Task.Run(() => routing.Stop(backend));
                State current = await Task.Run(() => backend.Read());
                Display(current);
            }
            catch (Exception ex) { Error(ex, false); }
            finally
            {
                switching = false;
                toggle.Enabled = tail.Enabled = zap.Enabled = quit.Enabled = preferences.Enabled = true;
            }
        }

        void DisplayRouting()
        {
            bool running = routing.Tunnel && routing.Alive; if (running) autoFailure = null;
            tray.Icon = icons[running ? "R" : "?"];
            status.Text = running ? "Маршрутизация включена" : "Маршрутизация: требуется внимание";
            tray.Text = "Switcher: " + status.Text;
            tail.Checked = running; zap.Checked = false;
            details = status.Text + "\r\nОткрой «Маршрутизация…» для временных исключений. Обычный Tailscale остаётся подключённым.\r\nПри выходе Switcher удалит временные исключения и адаптер.\r\n\r\n" + routing.Log;
        }
        void UpdateRoutingMenu()
        {
            stopRouting.Text = routing.Active || routing.HasRecovery ? "Выключить маршрутизацию" : "Включить маршрутизацию";
            stopRouting.Enabled = !switching && !settingsOpen && !closing && !needsSetup && !RoutingBusy;
        }
        void SyncRoutingWindow()
        {
            UpdateRoutingMenu();
            if (switching || settingsOpen || closing) return;
            if (RoutingBusy) { tray.Icon = icons["…"]; tray.Text = "Switcher: обновление маршрутизации"; return; }
            if (routing.Active || routing.HasRecovery) DisplayRouting();
            else RefreshStatus();
        }
        async void ToggleRoutingFromTray()
        {
            if (switching || settingsOpen || closing || RoutingBusy) return;
            if (needsSetup) { OpenSettings(); return; }
            if (routingWindow != null) { routingWindow.ToggleRouting(); return; }
            if (routing.Active || routing.HasRecovery) { StopRoutingFromTray(); return; }
            autoRouting.Resume(); autoFailure = null;
            switching = true; generation++;
            try
            {
                var saved = JsonStore.Read<RoutingSettings>(routing.SettingsPath);
                saved.EnginePath = BundledEngine.FilePath;
                if (saved.DefaultTarget != "tailscale" || saved.Rules == null || saved.Rules.Exists(rule => rule == null || rule.Target != "direct"))
                    throw new InvalidDataException("Открой маршрутизацию и сохрани список исключений.");
                await Task.Run(() => routing.StartRouting(saved, backend));
                DisplayRouting();
            }
            catch (Exception ex) { autoRouting.Failed(DateTime.UtcNow); Error(ex, false); }
            finally { switching = false; UpdateRoutingMenu(); }
        }
        void OpenRouting()
        {
            if (routingWindow != null) { if (routingWindow.WindowState == FormWindowState.Minimized) routingWindow.WindowState = FormWindowState.Normal; routingWindow.Activate(); return; }
            if (switching || settingsOpen || closing) return;
            if (needsSetup) { OpenSettings(); return; }
            generation++;
            routingWindow = new RoutingForm(routing, backend);
            routingWindow.ManualRoutingChange += enabled => { generation++; autoFailure = null; if (enabled) autoRouting.Resume(); else autoRouting.Pause(); };
            routingWindow.StateChanged += SyncRoutingWindow;
            routingWindow.FormClosed += delegate { routingWindow = null; RefreshStatus(); };
            routingWindow.Show();
            SyncRoutingWindow();
        }
        void UpdateHotkeyLabels()
        {
            toggle.Text = "Переключить   " + settings.SwitchHotkey;
            quit.Text = "Выход   " + settings.ExitHotkey;
        }
        void DisplaySetupPending()
        {
            toggle.Enabled = tail.Enabled = zap.Enabled = false;
            tray.Icon = icons["-"];
            tray.Text = "Switcher: требуется первоначальная настройка";
            status.Text = "Требуется настройка";
            details = "Выбери папки Tailscale и zapret, стратегию и горячие клавиши через меню «Настройки…». До сохранения переключение отключено.";
        }
        void ClearHotkeys()
        {
            if (IsHandleCreated) { UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2); }
            hotkeysRegistered = false;
        }
        bool TryRegisterHotkeys(AppSettings value, out string error)
        {
            ClearHotkeys();
            if (!RegisterHotKey(Handle, 1, value.SwitchHotkey.Modifiers | 0x4000, (uint)value.SwitchHotkey.Key))
            { error = "Не удалось назначить " + value.SwitchHotkey + ": сочетание занято или зарезервировано."; return false; }
            if (!RegisterHotKey(Handle, 2, value.ExitHotkey.Modifiers | 0x4000, (uint)value.ExitHotkey.Key))
            { ClearHotkeys(); error = "Не удалось назначить " + value.ExitHotkey + ": сочетание занято или зарезервировано."; return false; }
            hotkeysRegistered = true; error = null; return true;
        }
        static void BringForward(Form window)
        {
            if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
            window.BringToFront(); window.Activate();
        }
        void OpenInformation(string title, string text)
        {
            HelpForm window;
            if (informationWindows.TryGetValue(title, out window)) { BringForward(window); return; }
            window = new HelpForm(title, text);
            informationWindows.Add(title, window);
            window.FormClosed += delegate { informationWindows.Remove(title); };
            window.Show();
        }
        void OpenSettings()
        {
            if (settingsWindow != null) { BringForward(settingsWindow); return; }
            if (closing) return;
            generation++; ClearHotkeys();
            settingsWindow = new SettingsForm(settings.Copy(), async candidate => {
                if (switching || RoutingBusy) throw new InvalidOperationException("Дождись завершения переключения.");
                settingsOpen = true; generation++;
                try
                {
                    candidate.Validate();
                    string command = await Task.Run(() => ZapretStrategy.Build(candidate));
                    string keyError;
                    if (!TryRegisterHotkeys(candidate, out keyError)) throw new InvalidOperationException(keyError);
                    try
                    {
                        await Task.Run(() => {
                            var service = new ZapretService(backend ?? new WindowsBackend(candidate));
                            var actual = service.Snapshot();
                            bool samePath = String.Equals(AppSettings.ExecutableFromCommand(actual.Command), candidate.ZapExe, StringComparison.OrdinalIgnoreCase);
                            bool sameName = String.Equals(actual.Name, Path.GetFileNameWithoutExtension(candidate.Strategy), StringComparison.OrdinalIgnoreCase);
                            if (!settings.SameStrategy(candidate) || !samePath || !sameName)
                                StrategyUpdate.Apply(service, command, Path.GetFileNameWithoutExtension(candidate.Strategy), () => SettingsStore.Save(candidate));
                            else SettingsStore.Save(candidate);
                        });
                        settings = candidate.Copy(); backend = new WindowsBackend(settings);
                        if (routingWindow != null) routingWindow.SetBackend(backend);
                        needsSetup = false; loadError = null;
                        toggle.Enabled = tail.Enabled = zap.Enabled = true;
                        UpdateHotkeyLabels(); generation++;
                    }
                    catch { ClearHotkeys(); throw; }
                }
                finally { settingsOpen = false; }
            }, needsSetup, loadError);
            settingsWindow.FormClosed += delegate {
                settingsWindow = null;
                if (!needsSetup && !hotkeysRegistered && !closing) {
                    string error;
                    if (!TryRegisterHotkeys(settings, out error)) Error(new InvalidOperationException(error), false);
                }
                if (needsSetup) DisplaySetupPending();
                RefreshStatus();
            };
            settingsWindow.Show();
        }
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if ((switching || RoutingBusy || settingsOpen) && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
            if (!closing && (routing.Active || routing.HasRecovery))
            {
                e.Cancel = true; switching = true;
                try { await Task.Run(() => routing.Stop(backend)); closing = true; switching = false; Close(); }
                catch (Exception ex) { switching = false; Error(ex, true); }
                return;
            }
            closing = true;
            timer.Stop();
            tray.Visible = false;
            base.OnFormClosing(e);
        }
        private void DisposeTray(bool disposing)
        {
            if (disposing) { routing.Dispose(); timer.Dispose(); tray.Dispose(); foreach (Icon icon in icons.Values) icon.Dispose(); }

        }
        private void Form1_Load(object sender, EventArgs e) { }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    }

}



