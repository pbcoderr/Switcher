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
        bool switching, refreshing, closing, settingsOpen, hotkeysRegistered, needsSetup;
        string loadError;
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
            icons["R"] = MakeIcon("R", Color.FromArgb(124, 58, 237));
            icons["T"] = MakeIcon("T", Color.FromArgb(37, 99, 235));
            icons["Z"] = MakeIcon("Z", Color.FromArgb(22, 163, 74));
            icons["!"] = MakeIcon("!", Color.FromArgb(220, 90, 20));
            icons["?"] = MakeIcon("?", Color.FromArgb(185, 28, 28));
            icons["-"] = MakeIcon("–", Color.FromArgb(100, 116, 139));
            icons["…"] = MakeIcon("…", Color.FromArgb(100, 116, 139));
            var menu = new ContextMenuStrip();
            status.Enabled = false;
            menu.Items.AddRange(new ToolStripItem[] { status, new ToolStripSeparator(), toggle, tail, zap, new ToolStripSeparator() });
            menu.Items.Add("Подробности", null, delegate { MessageBox.Show(details, "Tailscale ↔ zapret"); });
            menu.Items.Add(preferences);
            menu.Items.Add("Маршрутизация…", null, delegate { OpenRouting(); });
            menu.Items.Add("О программе", null, delegate {
                MessageBox.Show("Switcher " + Application.ProductVersion + "\r\nПереключатель Tailscale ↔ zapret\r\n\r\nАвтор Switcher: pb_coder\r\n© 2026 pb_coder. Все права защищены.\r\n\r\nСпасибо bol-van — автору zapret (MIT License).\r\nСпасибо Flowseal и участникам zapret-discord-youtube (MIT License).\r\nСпасибо nekohasekai и SagerNet за sing-box (GPLv3+).\r\n\r\nСсылки и лицензии сторонних компонентов приведены в README.\r\nTailscale и zapret — отдельные сторонние проекты.", "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            menu.Items.Add(quit);
            preferences.Click += delegate { OpenSettings(); };
            toggle.Click += delegate { Switch(null); };
            tail.Click += delegate { Switch(true); };
            zap.Click += delegate { Switch(false); };
            quit.Click += delegate { if (!switching) Close(); };
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
            using (var bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(color))
            using (var font = new Font("Segoe UI", 23, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillEllipse(brush, 0, 0, 31, 31);
                g.DrawString(letter, font, Brushes.White, new RectangleF(0, -1, 32, 32), format);
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
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
                if (!registered) tray.ShowBalloonTip(6000, "Горячие клавиши недоступны", hotkeyError + " Открой настройки в меню иконки.", ToolTipIcon.Warning);
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
            if (state.Tailscale && state.Zapret) details += "\r\n\r\nОба режима включены извне. Выбери нужный в меню.";
        }
        void Error(Exception ex, bool popup)
        {
            details = ex.Message;
            tray.Icon = icons["?"];
            tray.Text = "Tailscale ↔ zapret: ошибка — открой подробности";
            status.Text = "Ошибка проверки / переключения";
            tail.Checked = zap.Checked = false;
            if (popup) MessageBox.Show(details, "Ошибка переключения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        async void RefreshStatus()
        {
            if (switching || refreshing || closing || settingsOpen || needsSetup) return;
            if (routing.Active && !routing.Alive)
            {
                switching = true; generation++;
                try { await Task.Run(() => routing.Stop(backend)); tray.ShowBalloonTip(6000, "Маршрутизация остановилась", "Движок завершился. Предыдущие подключения восстановлены.", ToolTipIcon.Warning); }
                catch (Exception ex) { Error(ex, false); }
                finally { switching = false; }
            }
            if (routing.Active || routing.HasRecovery) { DisplayRouting(); return; }
            refreshing = true;
            int version = generation;
            WindowsBackend current = backend;
            try { State s = await Task.Run(() => current.Read()); if (!closing && !switching && !settingsOpen && version == generation) Display(s); }
            catch (Exception ex) { if (!closing && !switching && !settingsOpen && version == generation) Error(ex, false); }
            finally { refreshing = false; }
        }
        async void Switch(bool? target)
        {
            if (switching || closing || settingsOpen) return;
            if (needsSetup) { OpenSettings(); return; }
            if (routing.Active || routing.HasRecovery) { OpenRouting(); return; }
            switching = true;
            generation++;
            toggle.Enabled = tail.Enabled = zap.Enabled = quit.Enabled = preferences.Enabled = false;
            tray.Icon = icons["…"];
            tray.Text = "Tailscale ↔ zapret: переключение…";
            status.Text = "Переключение…";
            try
            {
                State s = await Task.Run(() => new SwitchEngine(backend).Switch(target));
                Display(s);
                tray.ShowBalloonTip(2500, "Режим: " + s.Label, "Переключение завершено.", ToolTipIcon.Info);
            }
            catch (Exception ex) { Error(ex, true); }
            finally { switching = false; toggle.Enabled = tail.Enabled = zap.Enabled = quit.Enabled = preferences.Enabled = true; }
        }
        void DisplayRouting()
        {
            bool running = routing.Tunnel && routing.Alive;
            tray.Icon = icons[running ? "R" : "?"];
            status.Text = running ? "Маршрутизация включена" : "Маршрутизация: требуется внимание";
            tray.Text = "Switcher: " + status.Text;
            tail.Checked = zap.Checked = false;
            details = status.Text + "\r\nОткрой «Маршрутизация…» для правил, остановки и восстановления подключений.\r\nПри выходе Switcher остановит маршрутизацию и восстановит предыдущий режим.\r\n\r\n" + routing.Log;
        }
        void OpenRouting()
        {
            if (switching || settingsOpen || closing) return;
            if (needsSetup) { OpenSettings(); return; }
            settingsOpen = true; generation++;
            try { using (var dialog = new RoutingForm(routing, backend)) dialog.ShowDialog(); }
            finally { settingsOpen = false; RefreshStatus(); }
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
        void OpenSettings()
        {
            if (!needsSetup && (routing.Active || routing.HasRecovery)) { OpenRouting(); return; }
            if (switching || settingsOpen || closing) return;
            settingsOpen = true; generation++;
            // Release our own combinations so the capture fields can receive them.
            ClearHotkeys();
            try
            {
                using (var dialog = new SettingsForm(settings.Copy(), async candidate => {
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
                        needsSetup = false; loadError = null;
                        toggle.Enabled = tail.Enabled = zap.Enabled = true;
                        UpdateHotkeyLabels(); generation++;
                    }
                    catch { ClearHotkeys(); throw; }
                }, needsSetup, loadError)) dialog.ShowDialog();
            }
            finally
            {
                settingsOpen = false;
                if (!needsSetup && !hotkeysRegistered)
                {
                    string error;
                    if (!TryRegisterHotkeys(settings, out error)) tray.ShowBalloonTip(6000, "Горячие клавиши недоступны", error, ToolTipIcon.Warning);
                }
                if (needsSetup) DisplaySetupPending();
                RefreshStatus();
            }
        }
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (switching && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
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
