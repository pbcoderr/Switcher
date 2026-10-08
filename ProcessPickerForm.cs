using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Switcher
{
    public sealed class ProcessPickerForm : Form
    {
        readonly TextBox search = new TextBox();
        readonly DataGridView list = new DataGridView();
        readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        readonly Button choose = new Button { Text = "Выбрать" };
        string[] names = new string[0];
        public string SelectedProcess { get; private set; }
        public ProcessPickerForm()
        {
            Text = "Выбор программы · Switcher"; Font = new Font("Segoe UI", 10);
            Size = new Size(560, 440); MinimumSize = new Size(460, 340); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var heading = UiLayout.Heading("Запущенные программы"); layout.Controls.Add(heading,0,0);
            var filter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60)); filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            filter.Controls.Add(new Label { Text = "Поиск", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft },0,0);
            search.Dock = DockStyle.Top; search.Margin = new Padding(0,4,0,0); search.AccessibleName = "Поиск программы по имени EXE"; filter.Controls.Add(search,1,0); layout.Controls.Add(filter,0,1);
            list.Dock = DockStyle.Fill; list.Margin = Padding.Empty; list.ReadOnly = true; list.AllowUserToAddRows = false; list.AllowUserToDeleteRows = false;
            list.RowHeadersVisible = false; list.MultiSelect = false; list.SelectionMode = DataGridViewSelectionMode.FullRowSelect; list.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            list.Columns.Add("process", "Имя программы (.exe)"); layout.Controls.Add(list,0,2); layout.Controls.Add(status,0,3);
            var refresh = new Button { Text = "Обновить" }; var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel };
            layout.Controls.Add(UiLayout.ButtonRow(refresh,cancel,choose),0,4); Controls.Add(layout);
            refresh.Click += delegate { Reload(); }; search.TextChanged += delegate { Filter(); };
            list.SelectionChanged += delegate { choose.Enabled = list.CurrentRow != null; };
            choose.Click += delegate { SelectCurrent(); }; list.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if(e.RowIndex>=0) SelectCurrent(); };
            AcceptButton = choose; CancelButton = cancel; AppTheme.Apply(this); AppTheme.Primary(choose); heading.ForeColor = AppTheme.Blue; status.ForeColor = AppTheme.Muted;
            Shown += delegate { Reload(); search.Focus(); };
        }
        void Reload()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try {
                foreach(var process in Process.GetProcesses()) using(process) {
                    try { if(process.Id==0 || process.Id==4) continue; string name=process.ProcessName;
                        if(!String.IsNullOrWhiteSpace(name)) found.Add(name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?name:name+".exe");
                    } catch(InvalidOperationException) { } catch(System.ComponentModel.Win32Exception) { }
                }
                names=found.OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).ToArray(); Filter();
            } catch(Exception ex) { status.Text="Не удалось получить список: "+ex.Message; }
        }
        void Filter()
        {
            list.Rows.Clear(); string query=search.Text.Trim();
            foreach(string name in names) if(name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0) list.Rows.Add(name);
            choose.Enabled=list.CurrentRow!=null; status.Text=list.Rows.Count==0 ? "Ничего не найдено. Запусти программу и обнови список." : "Найдено: "+list.Rows.Count;
        }
        void SelectCurrent()
        {
            if(list.CurrentRow==null) return;
            SelectedProcess=Convert.ToString(list.CurrentRow.Cells[0].Value); DialogResult=DialogResult.OK; Close();
        }
    }
}
