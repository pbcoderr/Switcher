using System.Drawing;
using System.Windows.Forms;

namespace Switcher
{
    internal static class UiLayout
    {
        internal static TableLayoutPanel ButtonRow(params Control[] controls)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = controls.Length, RowCount = 1, Margin = Padding.Empty };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < controls.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
                var control = controls[i]; control.AutoSize = false; control.Dock = DockStyle.Fill;
                control.Margin = new Padding(i == 0 ? 0 : 4, 4, i == controls.Length - 1 ? 0 : 4, 4);
                control.Padding = new Padding(4, 0, 4, 0); row.Controls.Add(control, i, 0);
            }
            return row;
        }
        internal static Label Heading(string text)
        {
            return new Label { Text = text, Font = new Font("Segoe UI", 17, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        }
    }
}
