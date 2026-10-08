using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Switcher
{
    public sealed class RuleImportForm : Form
    {
        readonly string path;
        readonly IList<RoutingRule> existing;
        readonly Label summary = new Label { Dock = DockStyle.Fill };
        readonly DataGridView preview = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White };
        readonly TextBox errors = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
        readonly Button import = new Button { Text = "Добавить правила", AutoSize = true, Enabled = false };
        readonly Button cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        bool loading;
        public RuleImportResult Result { get; private set; }
        public RuleImportForm(string path, IList<RoutingRule> existing)
        {
            this.path = path; this.existing = JsonStore.Clone(new List<RoutingRule>(existing));
            Text = "Импорт исключений — " + Path.GetFileName(path); Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent; Size = new Size(840, 660); MinimumSize = new Size(780, 600);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); Controls.Add(layout);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "TXT / LIST: один сайт или IP на строку. JSON / CSV: сайты или подробные правила.\r\nАдреса https:// преобразуются в домены: путь и порт не учитываются. Поддомены тоже включаются.\r\nНовые правила добавятся в конец. При ошибках или конфликте направлений импорт не применяется." }, 0, 0);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Из этого списка — напрямую. Всё без правила — через Tailscale.", Font = new Font(Font, FontStyle.Bold) }, 0, 1);
            layout.Controls.Add(summary, 0, 2);
            preview.Columns.Add("kind", "Тип"); preview.Columns.Add("value", "Сайт, IP или программа"); preview.Columns.Add("target", "Маршрут"); preview.Columns.Add("enabled", "Включено");
            preview.Columns[1].FillWeight = 180; layout.Controls.Add(preview, 0, 3); layout.Controls.Add(errors, 0, 4);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; buttons.Controls.Add(cancel); buttons.Controls.Add(import); layout.Controls.Add(buttons, 0, 5);
            import.Click += delegate { if (!loading && Result != null && Result.ErrorCount == 0 && Result.Rules.Count > 0) { DialogResult = DialogResult.OK; Close(); } };
            AcceptButton = import; CancelButton = cancel;
            Shown += async delegate { await LoadPreview(); };
        }
        async Task LoadPreview()
        {
            if (loading) return;
            loading = true; import.Enabled = cancel.Enabled = false; Result = null;
            preview.Rows.Clear(); errors.Text = ""; summary.Text = "Чтение и проверка списка…";
            try
            {
                Result = await Task.Run(() => RuleImport.ReadExceptions(path, existing));
                foreach (var rule in Result.Rules) preview.Rows.Add(rule.Kind == "domain" ? "Сайт + поддомены" : rule.Kind == "ip" ? "IP / подсеть" : "Программа", rule.Value, rule.Target == "direct" ? "Напрямую" : "Через Tailscale", rule.Enabled ? "Да" : "Нет");
                summary.Text = "К добавлению: " + Result.Rules.Count + " · Повторов пропущено: " + Result.Duplicates + " · Ошибок: " + Result.ErrorCount + " · " + Result.EncodingName;
                errors.Text = Result.ErrorCount == 0 ? "Ошибок нет. Приоритет существующих правил сохранится. После добавления нажми «Сохранить» в окне маршрутизации." : String.Join(Environment.NewLine, Result.Errors) + (Result.ErrorCount > Result.Errors.Count ? "\r\nПоказаны первые 50 ошибок." : "");
                import.Enabled = Result.ErrorCount == 0 && Result.Rules.Count > 0;
            }
            catch (Exception ex) { summary.Text = "Импорт не выполнен. Существующие правила не изменены."; errors.Text = ex.Message; }
            finally { loading = false; cancel.Enabled = true; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (loading) { e.Cancel = true; return; } base.OnFormClosing(e); }
    }
}
