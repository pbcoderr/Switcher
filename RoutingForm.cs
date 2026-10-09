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
        IBackend backend;
        public event Action StateChanged;
        public event Action ConnectionLost;
        public bool OperationBusy { get { return busy; } }
        bool externalBusy;
        public void SetExternalBusy(bool value) { externalBusy = value; UpdateState(); }
        public void SetBackend(IBackend value) { backend = value; }
        public void ToggleRouting() { if (busy || externalBusy) return; if (controller.Active || controller.HasRecovery) stop.PerformClick(); else start.PerformClick(); }
        readonly Label connection = new Label { Text = "Используется подключение и exit node обычного Tailscale.", Dock = DockStyle.Fill };
        readonly Label engine = new Label { Text = "Встроен · sing-box " + BundledEngine.Version, AutoSize = true };
        readonly DataGridView grid = new DataGridView();
        readonly ConsoleLogBox log = new ConsoleLogBox { Dock = DockStyle.Fill };
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
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(layout);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            var heading = UiLayout.Heading("Маршрутизация");
            state.AutoSize = false; state.Dock = DockStyle.Fill; state.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(heading, 0, 0); header.Controls.Add(state, 1, 0); layout.Controls.Add(header, 0, 0);
            var split = new SplitContainer { Name = "routingSplit", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(820, 350), SplitterWidth = 8, Margin = Padding.Empty };
            split.Panel1MinSize = 130; split.Panel2MinSize = 70; split.SplitterDistance = 238;
            split.AccessibleName = "Высота журнала: перетащи разделитель";
            split.Paint += delegate(object sender, PaintEventArgs e) { var area = split.SplitterRectangle; using (var fill = new SolidBrush(AppTheme.Surface)) e.Graphics.FillRectangle(fill, area); using (var pen = new Pen(AppTheme.Muted, 2)) e.Graphics.DrawLine(pen, area.Left + area.Width / 2 - 20, area.Top + area.Height / 2, area.Left + area.Width / 2 + 20, area.Top + area.Height / 2); };
            layout.Controls.Add(split, 0, 1);
            split.Panel1.Controls.Add(editor);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            editor.Controls.Add(fields);
            grid.Dock = DockStyle.Fill; grid.Margin = Padding.Empty; grid.AutoGenerateColumns = false; grid.AllowUserToAddRows = false;
            grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "Вкл.", FillWeight = 13 });
            grid.Columns.Add(Choices("kind", "Тип", new[] { "Сайт + поддомены", "IP / подсеть", "Программа (.exe)" }, 40));
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "Сайт, IP или программа", FillWeight = 70 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "pickProcess", HeaderText = "Выбор", FillWeight = 24, MinimumWidth = 104, FlatStyle = FlatStyle.Flat });
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.CellPainting += PaintEditableCell;
            grid.CellClick += PickProcess;
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty && grid.CurrentCell != null && grid.CurrentCell.ColumnIndex < 2) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex == 1) grid.InvalidateRow(e.RowIndex); };
            grid.CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e) { grid.Cursor = e.RowIndex >= 0 && e.ColumnIndex == 2 ? Cursors.IBeam : e.RowIndex >= 0 && e.ColumnIndex == 3 && Convert.ToString(grid.Rows[e.RowIndex].Cells[1].Value) == "Программа (.exe)" ? Cursors.Hand : Cursors.Default; };
            grid.MouseLeave += delegate { grid.Cursor = Cursors.Default; };
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            fields.Controls.Add(grid, 0, 0);
            var ruleButtons = UiLayout.ButtonRow(
                MakeButton("Добавить", delegate { Add(new RoutingRule()); }),
                MakeButton("Удалить", delegate { if (grid.CurrentRow != null) grid.Rows.Remove(grid.CurrentRow); }),
                MakeButton("Выше", delegate { MoveRule(-1); }),
                MakeButton("Ниже", delegate { MoveRule(1); }),
                MakeButton("Импорт…", delegate { ImportRules(); }),
                MakeButton(".ru / .рф / .su", delegate { foreach (string domain in new[] { "ru", "рф", "su" }) Add(new RoutingRule { Value = domain }); }));
            fields.Controls.Add(ruleButtons, 0, 1);
            var journal = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            journal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            journal.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); journal.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var logHeading = new Label { Text = "Журнал", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
            journal.Controls.Add(logHeading, 0, 0); log.Margin = Padding.Empty; journal.Controls.Add(log, 0, 1); split.Panel2.Controls.Add(journal);
            Setup(save, "Сохранить", async delegate { await Work(delegate { var s = Collect(); JsonStore.Save(controller.SettingsPath, s); notice = "Сохранено."; }); });
            Setup(check, "Проверить", async delegate { var s = TryCollect(); if (s != null) await WorkAsync(() => controller.Validate(s, backend), "Проверка пройдена."); });
            Setup(start, "Включить", async delegate { if (busy || externalBusy) return; var s = TryCollect(); if (s == null) return; if (ManualRoutingChange != null) ManualRoutingChange(true); await WorkAsync(delegate { JsonStore.Save(controller.SettingsPath, s); if (controller.Active || controller.HasRecovery) controller.Stop(backend); controller.StartRouting(s, backend); }, "Правила включены."); });
            Setup(stop, "Остановить", async delegate { if (ManualRoutingChange != null) ManualRoutingChange(false); await WorkAsync(() => controller.Stop(backend), "Исключения выключены."); });
            layout.Controls.Add(UiLayout.ButtonRow(MakeButton("Помощь", delegate { HelpForm.Open(this); }), check, save, stop, start), 0, 2);
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
                if (!busy && !externalBusy && controller.Active && !controller.Alive)
                    { if (ConnectionLost != null) ConnectionLost(); await WorkAsync(delegate { controller.Stop(backend); }, "Движок завершился. Временные исключения удалены."); }
                if (!busy && !externalBusy && controller.Active && controller.Alive && ++healthTicks % 5 == 0)
                    await CheckHealth();
                UpdateState();
            }; AppTheme.Apply(this); grid.Columns["value"].DefaultCellStyle.BackColor = Color.FromArgb(32, 47, 62); AppTheme.Primary(start); log.ForeColor = AppTheme.Muted; logHeading.ForeColor = AppTheme.Muted; heading.ForeColor = AppTheme.Blue; state.ForeColor = AppTheme.Blue; timer.Start(); UpdateState();
        }
        void PaintEditableCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string kind = Convert.ToString(grid.Rows[e.RowIndex].Cells[1].Value);
            if (e.ColumnIndex == 3)
            {
                e.PaintBackground(e.ClipBounds, true);
                if (kind == "Программа (.exe)")
                {
                    var rectangle = new Rectangle(e.CellBounds.X + 5, e.CellBounds.Y + 4, e.CellBounds.Width - 10, e.CellBounds.Height - 8);
                    using (var fill = new SolidBrush(AppTheme.Border)) e.Graphics.FillRectangle(fill, rectangle);
                    TextRenderer.DrawText(e.Graphics, "Выбрать…", grid.Font, rectangle, AppTheme.Blue, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                e.Handled = true; return;
            }
            if (e.ColumnIndex != 2) return;
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            using (var pen = new Pen((e.State & DataGridViewElementStates.Selected) != 0 ? AppTheme.Blue : Color.FromArgb(65, 91, 114)))
                e.Graphics.DrawRectangle(pen, e.CellBounds.X + 1, e.CellBounds.Y + 1, e.CellBounds.Width - 3, e.CellBounds.Height - 3);
            if (String.IsNullOrWhiteSpace(Convert.ToString(e.Value)) && !(grid.IsCurrentCellInEditMode && grid.CurrentCell == grid.Rows[e.RowIndex].Cells[2]))
            {
                string hint = kind == "Программа (.exe)" ? "Введи EXE или выбери →" : kind == "IP / подсеть" ? "Введи IP или подсеть…" : "Введи сайт, например example.ru";
                var area = e.CellBounds; area.Inflate(-9, -2);
                TextRenderer.DrawText(e.Graphics, hint, grid.Font, area, AppTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            e.Handled = true;
        }
        void PickProcess(object sender, DataGridViewCellEventArgs e)
        {
            if (busy || externalBusy || e.RowIndex < 0 || e.ColumnIndex != 3) return;
            var row = grid.Rows[e.RowIndex];
            if (Convert.ToString(row.Cells[1].Value) != "Программа (.exe)") return;
            grid.EndEdit();
            using (var picker = new ProcessPickerForm())
                if (picker.ShowDialog(this) == DialogResult.OK) { row.Cells[2].Value = picker.SelectedProcess; grid.CurrentCell = row.Cells[2]; }
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
            if (busy || externalBusy) return; busy = true; notice = "Подожди…"; UpdateState();
            try { await Task.Run(action); notice = success; }
            catch (Exception ex) { ShowError(ex); }
            finally { busy = false; UpdateState(); }
        }
        async Task CheckHealth()
        {
            busy = true; UpdateState();
            try { await Task.Run(() => controller.CheckConnection(backend)); }
            catch (Exception ex) { notice = "Ожидание восстановления сети. " + ex.Message; if (ConnectionLost != null) ConnectionLost(); }
            finally { busy = false; UpdateState(); }
        }
        void ShowError(Exception ex) { notice = ex.Message; log.ForeColor = AppTheme.Error; UpdateState(); }
        void UpdateState()
        {
            bool active = controller.Active, recovery = controller.HasRecovery;
            editor.Enabled = save.Enabled = check.Enabled = !busy && !externalBusy && !active && !recovery;
            start.Enabled = !busy && !externalBusy;
            start.Text = busy ? "Подожди…" : recovery ? "Восстановить и включить" : active ? "Перезапустить" : "Включить";
            stop.Enabled = !busy && !externalBusy && (active || recovery);
            state.Text = busy ? "Подожди…" : controller.Tunnel && controller.Alive ? "Правила работают" : recovery ? "Сеанс / восстановление" : active ? "Запуск правил" : "Выключено";
            string text = notice + "\r\n" + controller.Log;
            log.UpdateLog(text);
            if (StateChanged != null) StateChanged();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}



