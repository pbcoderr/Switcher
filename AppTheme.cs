using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Switcher
{
    internal static class AppTheme
    {
        internal static readonly Color Background = Color.FromArgb(27, 31, 38);
        internal static readonly Color Surface = Color.FromArgb(37, 43, 52);
        internal static readonly Color Input = Color.FromArgb(22, 27, 34);
        internal static readonly Color Border = Color.FromArgb(61, 72, 87);
        internal static readonly Color Text = Color.FromArgb(233, 240, 248);
        internal static readonly Color Muted = Color.FromArgb(161, 177, 195);
        internal static readonly Color Blue = Color.FromArgb(163, 214, 246);
        internal static readonly Color Error = Color.FromArgb(255, 163, 156);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal static void Apply(Form form)
        {
            Paint(form);
            using (var stream = typeof(AppTheme).Assembly.GetManifestResourceStream("Switcher.App.ico"))
            using (var icon = new Icon(stream)) form.Icon = (Icon)icon.Clone();
            form.HandleCreated += delegate {
                int enabled = 1;
                try { DwmSetWindowAttribute(form.Handle, 20, ref enabled, 4); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            };
        }

        static void Paint(Control control)
        {
            control.BackColor = Background; control.ForeColor = Text;
            var group = control as GroupBox;
            if (group != null) { group.BackColor = Surface; group.ForeColor = Blue; }
            var input = control as TextBoxBase;
            if (input != null) { input.BackColor = Input; input.ForeColor = Text; input.BorderStyle = BorderStyle.FixedSingle; }
            var combo = control as ComboBox;
            if (combo != null) { combo.BackColor = Input; combo.ForeColor = Text; combo.FlatStyle = FlatStyle.Flat; combo.DrawMode = DrawMode.OwnerDrawFixed; combo.DrawItem += delegate(object sender, DrawItemEventArgs e) { using (var fill = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Border : Input)) e.Graphics.FillRectangle(fill, e.Bounds); string text = e.Index >= 0 ? Convert.ToString(combo.Items[e.Index]) : combo.Text; TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter); }; }
            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat; button.BackColor = Surface;
                button.FlatAppearance.BorderColor = Border; button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 66, 83);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(66, 85, 105);
                button.Paint += delegate(object sender, PaintEventArgs e) { if (!button.Enabled) { using (var fill = new SolidBrush(Surface)) e.Graphics.FillRectangle(fill, button.ClientRectangle); using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, button.Width - 1, button.Height - 1); TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle, Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); } };
                button.Cursor = Cursors.Hand; button.UseVisualStyleBackColor = false;
            }
            var grid = control as DataGridView;
            if (grid != null)
            {
                grid.BackgroundColor = Input; grid.BorderStyle = BorderStyle.None;
                grid.GridColor = Border; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                grid.ColumnHeadersHeight = 32; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Surface; grid.ColumnHeadersDefaultCellStyle.ForeColor = Blue;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
                grid.DefaultCellStyle.BackColor = Input; grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(49, 76, 98);
                grid.DefaultCellStyle.SelectionForeColor = Text; grid.DefaultCellStyle.Padding = new Padding(7, 3, 7, 3);
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(29, 35, 43);
                grid.RowTemplate.Height = 30;
                foreach (DataGridViewRow row in grid.Rows) row.Height = 30;
                return;
            }
            foreach (Control child in control.Controls) Paint(child);
            if (group != null) foreach (Control child in group.Controls) SetSurface(child);
        }
        static void SetSurface(Control control)
        {
            if (control is Panel || control is Label) control.BackColor = Surface;
            foreach (Control child in control.Controls) SetSurface(child);
        }
        internal static void Primary(Button button)
        {
            button.BackColor = Blue; button.ForeColor = Input; button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(193, 229, 252);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(128, 191, 232);
        }
        internal static void Menu(ContextMenuStrip menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            menu.BackColor = Surface; menu.ForeColor = Text; menu.Font = new Font("Segoe UI", 10);
            menu.ShowImageMargin = false; menu.Padding = new Padding(5);
            foreach (ToolStripItem item in menu.Items) item.Padding = new Padding(9, 5, 9, 5);
        }
        sealed class MenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Border; } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Surface; } }
            public override Color CheckBackground { get { return Blue; } }
            public override Color CheckSelectedBackground { get { return Blue; } }
        }
    }
}
