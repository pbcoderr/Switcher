using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Switcher
{
    public sealed class HotkeyBox : TextBox
    {
        public HotkeySpec Value { get; private set; }
        public HotkeyBox(HotkeySpec initial)
        {
            ReadOnly = true; ShortcutsEnabled = false; BackColor = Color.White;
            BorderStyle = BorderStyle.FixedSingle;
            SetValue(initial);
            GotFocus += delegate { SelectAll(); };
        }
        public void SetValue(HotkeySpec value) { Value = new HotkeySpec(value.Modifiers, (Keys)value.Key); Text = Value.ToString(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Tab || key == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            if (key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey || key == Keys.LWin || key == Keys.RWin) return true;
            uint modifiers = ((keyData & Keys.Control) != 0 ? 2u : 0) | ((keyData & Keys.Alt) != 0 ? 1u : 0) | ((keyData & Keys.Shift) != 0 ? 4u : 0);
            var candidate = new HotkeySpec(modifiers, key);
            try { candidate.Validate(); SetValue(candidate); }
            catch (InvalidOperationException) { System.Media.SystemSounds.Beep.Play(); }
            return true;
        }
    }

    public sealed class SettingsForm : Form
    {
        readonly TextBox tailscale = new TextBox(), zapret = new TextBox();
        readonly ComboBox strategy = new ComboBox();
        readonly HotkeyBox switchKey, exitKey;
        readonly Label error = new Label();
        readonly Button save = new Button(), cancel = new Button();
        readonly TableLayoutPanel root;
        readonly Func<AppSettings, Task> apply;
        bool saving;
        public SettingsForm(AppSettings current, Func<AppSettings, Task> apply, bool firstRun = false, string startupError = null)
        {
            this.apply = apply;
            Text = firstRun ? "Первый запуск • Switcher" : "Настройки • Tailscale ↔ zapret";
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(246, 248, 251);
            ForeColor = Color.FromArgb(30, 41, 59);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(680, 450);
            MinimumSize = new Size(696, 489);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            switchKey = new HotkeyBox(current.SwitchHotkey);
            exitKey = new HotkeyBox(current.ExitHotkey);
            root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 44, 170, 118, 0, 30, 44 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(root);
            var heading = new Label { Text = firstRun ? "Добро пожаловать в Switcher" : "Настрой под себя", Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Margin = new Padding(0) };
            root.Controls.Add(heading, 0, 0);

            var apps = new GroupBox { Text = "Программы и стратегия", Dock = DockStyle.Fill, Padding = new Padding(14, 22, 14, 12), BackColor = Color.White };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            for (int i = 0; i < 3; i++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
            apps.Controls.Add(table); root.Controls.Add(apps, 0, 1);
            tailscale.Text = current.TailscaleFolder; zapret.Text = current.ZapretFolder;
            AddFolderRow(table, 0, "Папка Tailscale", tailscale, false);
            AddFolderRow(table, 1, "Папка zapret", zapret, true);
            table.Controls.Add(LabelFor("Стратегия zapret"), 0, 2);
            strategy.DropDownStyle = ComboBoxStyle.DropDownList; strategy.Dock = DockStyle.Top; strategy.Margin = new Padding(4, 10, 4, 0);
            strategy.AccessibleName = "Стратегия zapret";
            table.Controls.Add(strategy, 1, 2);
            var refresh = new Button { Text = "Обновить", Dock = DockStyle.Top, Height = 30, Margin = new Padding(4, 8, 0, 0) };
            refresh.Click += delegate { LoadStrategies(Convert.ToString(strategy.SelectedItem)); };
            table.Controls.Add(refresh, 2, 2);
            zapret.Leave += delegate { LoadStrategies(Convert.ToString(strategy.SelectedItem)); };
            LoadStrategies(current.Strategy);
            if (!String.IsNullOrEmpty(startupError)) error.Text = "Проверь сохранённые настройки: " + startupError;

            var keys = new GroupBox { Text = "Горячие клавиши", Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 22, 14, 10), Margin = new Padding(3, 12, 3, 0) };
            var keyTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            keyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); keyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            keyTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); keyTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            keyTable.Controls.Add(LabelFor("Переключить T ↔ Z"), 0, 0); keyTable.Controls.Add(LabelFor("Закрыть переключатель"), 1, 0);
            switchKey.Dock = exitKey.Dock = DockStyle.Top;
            switchKey.Margin = new Padding(4, 0, 16, 0); exitKey.Margin = new Padding(4, 0, 4, 0);
            switchKey.AccessibleName = "Горячая клавиша переключения"; exitKey.AccessibleName = "Горячая клавиша выхода";
            keyTable.Controls.Add(switchKey, 0, 1); keyTable.Controls.Add(exitKey, 1, 1);
            keys.Controls.Add(keyTable); root.Controls.Add(keys, 0, 2);
            var note = new Label { Text = "Нажми на поле и введи сочетание с Ctrl или Alt (можно с Shift).\r\nПри смене стратегии работающий zapret кратко перезапустится.", Dock = DockStyle.Fill, ForeColor = AppTheme.Muted, Margin = new Padding(4, 12, 0, 0) };
            note.Visible = false; root.Controls.Add(note, 0, 3);
            error.Dock = DockStyle.Fill; error.ForeColor = AppTheme.Error; error.AutoEllipsis = true;
            root.Controls.Add(error, 0, 4);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            save.Text = "Сохранить"; save.Size = new Size(128, 36); save.BackColor = Color.FromArgb(37, 99, 235); save.ForeColor = Color.White; save.FlatStyle = FlatStyle.Flat; save.FlatAppearance.BorderSize = 0;
            cancel.Text = firstRun ? "Позже" : "Отмена"; cancel.Size = new Size(108, 36); cancel.DialogResult = DialogResult.Cancel;
            var defaults = new Button { Text = "Клавиши по умолчанию", Size = new Size(196, 36) };
            defaults.Click += delegate { switchKey.SetValue(new HotkeySpec(3, Keys.F8)); exitKey.SetValue(new HotkeySpec(3, Keys.F9)); };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); buttons.Controls.Add(defaults); var help = new Button { Text = "Помощь", Size = new Size(90, 36) }; help.Click += delegate { HelpForm.Open(this); }; buttons.Controls.Add(help);
            root.Controls.Add(buttons, 0, 5);
            AcceptButton = save; CancelButton = cancel;
            save.Click += Save;
            AppTheme.Apply(this); AppTheme.Primary(save); note.ForeColor = AppTheme.Muted; error.ForeColor = AppTheme.Error; heading.ForeColor = AppTheme.Blue;
            if (firstRun && String.IsNullOrEmpty(startupError)) error.ForeColor = AppTheme.Muted;
        }
        static Label LabelFor(string text) { return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 0, 4, 0) }; }
        void AddFolderRow(TableLayoutPanel table, int row, string label, TextBox input, bool isZapret)
        {
            table.Controls.Add(LabelFor(label), 0, row);
            input.Dock = DockStyle.Top; input.Margin = new Padding(4, 10, 4, 0); input.AccessibleName = label;
            table.Controls.Add(input, 1, row);
            var browse = new Button { Text = "Обзор…", Dock = DockStyle.Top, Height = 30, Margin = new Padding(4, 8, 0, 0) };
            browse.Click += delegate {
                using (var dialog = new FolderBrowserDialog { Description = label, ShowNewFolderButton = false, SelectedPath = Directory.Exists(input.Text) ? input.Text : "" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    input.Text = dialog.SelectedPath;
                    if (isZapret) LoadStrategies(Convert.ToString(strategy.SelectedItem));
                }
            };
            table.Controls.Add(browse, 2, row);
        }
        void LoadStrategies(string preferred)
        {
            try
            {
                string[] names = ZapretStrategy.List(zapret.Text.Trim().Trim('"'));
                strategy.Items.Clear(); strategy.Items.AddRange(names);
                foreach (string name in names) if (String.Equals(name, preferred, StringComparison.OrdinalIgnoreCase)) { strategy.SelectedItem = name; break; }
                error.ForeColor = AppTheme.Muted;
                error.Text = String.IsNullOrWhiteSpace(zapret.Text) ? "Выбери обе папки и стратегию, затем нажми «Сохранить»." :
                    names.Length == 0 ? "В выбранной папке не найдены .bat-файлы стратегий zapret." :
                    strategy.SelectedIndex < 0 ? "Выбери нужную стратегию zapret из списка." : "";
            }
            catch (Exception ex) { strategy.Items.Clear(); error.ForeColor = AppTheme.Error; error.Text = ex.Message; }
        }
        async void Save(object sender, EventArgs e)
        {
            if (saving) return;
            var candidate = new AppSettings { TailscaleFolder = tailscale.Text, ZapretFolder = zapret.Text, Strategy = Convert.ToString(strategy.SelectedItem), SwitchHotkey = switchKey.Value, ExitHotkey = exitKey.Value };
            saving = true; root.Enabled = false; error.Text = "Применяю настройки…";
            try { await apply(candidate); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { error.ForeColor = AppTheme.Error; error.Text = ex.Message;  }
            finally { saving = false; root.Enabled = true; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (saving && DialogResult != DialogResult.OK && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
