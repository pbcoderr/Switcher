using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Switcher
{
    public sealed class RoutingForm : Form
    {
        public event Action<bool> ManualRoutingChange;
        readonly RoutingController controller;
        readonly IBackend backend;
        readonly Label connection = new Label { Text = "Используется подключение и exit node обычного Tailscale.", Dock = DockStyle.Fill };
        readonly Label engine = new Label { Text = "Встроен · sing-box " + BundledEngine.Version, AutoSize = true };
        readonly DataGridView grid = new DataGridView();
        readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly Label state = new Label { AutoSize = true };
        readonly Button start = new Button(), stop = new Button(), save = new Button(), check = new Button();
        readonly Panel editor = new Panel { Dock = DockStyle.Fill };
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        bool busy; int healthTicks;
        string notice = "Маршрутизация выключена.";
        public RoutingForm(RoutingController controller, IBackend backend)
        {
            this.controller = controller; this.backend = backend;
            Text = "Switcher — маршрутизация"; Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(850, 470); Size = new Size(880, 510);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            Controls.Add(layout);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "Исключения маршрутизации", Font = new Font("Segoe UI", 18, FontStyle.Bold), Dock = DockStyle.Top, Height = 36 });
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(editor, 0, 1);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            for (int i = 0; i < 3; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            editor.Controls.Add(fields);
            fields.Controls.Add(new Label { Text = "Маршрутизация", AutoSize = true }, 0, 0); fields.Controls.Add(engine, 1, 0); fields.SetColumnSpan(engine, 2);
            fields.Controls.Add(new Label { Text = "Tailscale", AutoSize = true }, 0, 1); fields.Controls.Add(connection, 1, 1); fields.SetColumnSpan(connection, 2);

            fields.Controls.Add(new Label { Text = "Как работает", AutoSize = true }, 0, 2);
            var routingHint = new Label { Text = "Из списка — напрямую. Всё остальное — через Tailscale.", Dock = DockStyle.Fill };
            fields.Controls.Add(routingHint, 1, 2); fields.SetColumnSpan(routingHint, 2);
            var hint = new Label { Text = "Примеры: example.ru · 203.0.113.0/24 · Discord.exe. Для сервиса добавь все нужные домены.", Dock = DockStyle.Fill };
            fields.Controls.Add(hint, 0, 3); fields.SetColumnSpan(hint, 3);
            foreach (Control explanation in fields.Controls) explanation.Visible = false;
            grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.AutoGenerateColumns = false; grid.AllowUserToAddRows = false;
            grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "Вкл.", FillWeight = 13 });
            grid.Columns.Add(Choices("kind", "Тип", new[] { "Сайт + поддомены", "IP / подсеть", "Программа (.exe)" }, 40));
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "Сайт, IP или программа", FillWeight = 70 });
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            fields.Controls.Add(grid, 0, 4); fields.SetColumnSpan(grid, 3);
            var ruleButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            ruleButtons.Controls.Add(MakeButton("Добавить", delegate { Add(new RoutingRule()); }));
            ruleButtons.Controls.Add(MakeButton("Удалить", delegate { if (grid.CurrentRow != null) grid.Rows.Remove(grid.CurrentRow); }));
            ruleButtons.Controls.Add(MakeButton("Выше", delegate { MoveRule(-1); }));
            ruleButtons.Controls.Add(MakeButton("Ниже", delegate { MoveRule(1); }));
            ruleButtons.Controls.Add(MakeButton("Импорт исключений…", delegate { ImportRules(); }));
            ruleButtons.Controls.Add(MakeButton(".ru / .рф / .su напрямую", delegate {
                foreach (string domain in new[] { "ru", "рф", "su" }) Add(new RoutingRule { Value = domain });
            }));
            fields.Controls.Add(ruleButtons, 0, 5); fields.SetColumnSpan(ruleButtons, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Setup(save, "Сохранить", async delegate { await Work(delegate { var s = Collect(); JsonStore.Save(controller.SettingsPath, s); notice = "Сохранено."; }); });
            Setup(check, "Проверить", async delegate { var s = TryCollect(); if (s != null) await WorkAsync(() => controller.Validate(s, backend), "Проверка пройдена."); });
            actions.Controls.AddRange(new Control[] { save, check, MakeButton("Помощь", delegate { HelpForm.Open(this); }) }); layout.Controls.Add(actions, 0, 2);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Setup(start, "Включить", async delegate { if (ManualRoutingChange != null) ManualRoutingChange(true); var s = TryCollect(); if (s != null) await WorkAsync(delegate { JsonStore.Save(controller.SettingsPath, s); controller.StartRouting(s, backend); }, "Правила включены."); });
            Setup(stop, "Остановить", async delegate { if (ManualRoutingChange != null) ManualRoutingChange(false); await WorkAsync(() => controller.Stop(backend), "Исключения выключены."); });
            controls.Controls.AddRange(new Control[] { start, stop, state }); layout.Controls.Add(controls, 0, 3); layout.Controls.Add(log, 0, 4);
            try
            {
                var value = JsonStore.Read<RoutingSettings>(controller.SettingsPath);
                if (value == null || value.Rules == null) throw new InvalidDataException("Пустой файл правил.");

                foreach (var rule in value.Rules) if (rule.Target == "direct") Add(rule);
                if (value.DefaultTarget != "tailscale" || value.Rules.Exists(rule => rule.Target != "direct"))
                    notice = "Применена схема исключений: из списка — напрямую, остальное — через Tailscale. Старые правила «через Tailscale» не добавлены в исключения. Сохрани настройки для перехода на эту схему.";
            }
            catch (Exception ex) { notice = "Ошибка чтения правил: " + ex.Message; }
            timer.Tick += async delegate {
                if (!busy && controller.Active && !controller.Alive)
                    await WorkAsync(() => controller.Stop(backend), "Движок завершился. Временные исключения удалены.");
                if (!busy && controller.Active && controller.Alive && ++healthTicks % 5 == 0)
                    await WorkAsync(() => controller.CheckConnection(backend), notice);
                UpdateState();
            }; AppTheme.Apply(this); AppTheme.Primary(start); engine.ForeColor = AppTheme.Muted; hint.ForeColor = AppTheme.Muted; log.ForeColor = AppTheme.Muted; header.Controls[0].ForeColor = AppTheme.Blue; timer.Start(); UpdateState();
        }
        static DataGridViewComboBoxColumn Choices(string name, string title, string[] values, int width)
        {
            var col = new DataGridViewComboBoxColumn { Name = name, HeaderText = title, FillWeight = width, FlatStyle = FlatStyle.Flat };
            col.Items.AddRange(values); return col;
        }
        static Button MakeButton(string text, EventHandler click) { var b = new Button(); Setup(b, text, click); return b; }
        static void Setup(Button b, string text, EventHandler click) { b.Text = text; b.AutoSize = true; b.Height = 32; b.Padding = new Padding(6, 2, 6, 2); b.Click += click; }
        void Add(RoutingRule rule) { grid.Rows.Add(rule.Enabled, rule.Kind == "ip" ? "IP / подсеть" : rule.Kind == "process" ? "Программа (.exe)" : "Сайт + поддомены", rule.Value); }
        void MoveRule(int delta)
        {
            grid.EndEdit(); if (grid.CurrentRow == null) return;
            int at = grid.CurrentRow.Index, next = at + delta; if (next < 0 || next >= grid.Rows.Count) return;
            var row = grid.Rows[at]; grid.Rows.RemoveAt(at); grid.Rows.Insert(next, row); grid.CurrentCell = row.Cells[2];
        }
        RoutingSettings Collect()
        {
            var value = new RoutingSettings { EnginePath = BundledEngine.FilePath, DefaultTarget = "tailscale" };
            value.Rules = CollectRules();
            value.Validate(false); return value;
        }
        List<RoutingRule> CollectRules()
        {
            grid.EndEdit();
            var rules = new List<RoutingRule>();
            foreach (DataGridViewRow row in grid.Rows)
                rules.Add(new RoutingRule { Enabled = Convert.ToBoolean(row.Cells[0].Value), Kind = Convert.ToString(row.Cells[1].Value) == "IP / подсеть" ? "ip" : Convert.ToString(row.Cells[1].Value) == "Программа (.exe)" ? "process" : "domain", Value = Convert.ToString(row.Cells[2].Value), Target = "direct" });
            foreach (var rule in rules) rule.Validate();
            return rules;
        }
        void ImportRules()
        {
            if (busy || controller.Active || controller.HasRecovery) return;
            try
            {
                var existing = CollectRules();
                using (var picker = new OpenFileDialog { Title = "Импорт списка сайтов и правил", Filter = "Списки правил|*.txt;*.list;*.json;*.csv|Текстовый список|*.txt;*.list|JSON|*.json|CSV|*.csv", CheckFileExists = true })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    using (var dialog = new RuleImportForm(picker.FileName, existing))
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        ApplyImport(dialog.Result);
                    }
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }
        void ApplyImport(RuleImportResult result)
        {
            if (result == null || result.ErrorCount != 0 || grid.Rows.Count + result.Rules.Count > 2000)
                throw new InvalidDataException("Исправь ошибки списка перед импортом.");
            foreach (var rule in result.Rules) { rule.Validate(); if (rule.Target != "direct") throw new InvalidDataException("Импортируются только исключения напрямую."); }
            foreach (var rule in result.Rules) Add(rule);
            notice = "Добавлено исключений: " + result.Rules.Count + ". Повторов пропущено: " + result.Duplicates + ". Всё без правила — через Tailscale. Нажми «Сохранить».";
            UpdateState();
        }
        RoutingSettings TryCollect() { try { return Collect(); } catch (Exception ex) { ShowError(ex); return null; } }
        Task Work(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } UpdateState(); return Task.FromResult(0); }
        async Task WorkAsync(Action action, string success)
        {
            if (busy) return; busy = true; notice = "Подожди…"; UpdateState();
            try { await Task.Run(action); notice = success; }
            catch (Exception ex) { ShowError(ex); }
            finally { busy = false; UpdateState(); }
        }
        void ShowError(Exception ex) { notice = ex.Message; log.ForeColor = AppTheme.Error; UpdateState(); }
        void UpdateState()
        {
            bool active = controller.Active, recovery = controller.HasRecovery;
            editor.Enabled = save.Enabled = check.Enabled = start.Enabled = !busy && !active && !recovery;
            stop.Enabled = !busy && (active || recovery);
            state.Text = busy ? "Подожди…" : controller.Tunnel && controller.Alive ? "Правила работают" : recovery ? "Сеанс / восстановление" : active ? "Запуск правил" : "Выключено";
            string text = notice + "\r\n" + controller.Log;
            if (log.Text != text) { log.Text = text; log.SelectionStart = log.TextLength; log.ScrollToCaret(); }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}



