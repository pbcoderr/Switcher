using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Switcher
{
    public sealed class ConsoleLogBox : TextBox
    {
        [StructLayout(LayoutKind.Sequential)] struct ScrollInfo { public uint Size, Mask; public int Min, Max; public uint Page; public int Position, Track; }
        [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        public ConsoleLogBox() { Multiline = true; ReadOnly = true; WordWrap = false; ScrollBars = ScrollBars.Both; }
        public void UpdateLog(string value)
        {
            if (Text == value) return;
            if (!IsHandleCreated) { Text = value; Select(TextLength, 0); return; }
            var scroll = new ScrollInfo { Size = (uint)Marshal.SizeOf(typeof(ScrollInfo)), Mask = 7 };
            bool follow = TextLength == 0 || !GetScrollInfo(Handle, 1, ref scroll) || scroll.Position + scroll.Page >= scroll.Max;
            follow = follow && SelectionLength == 0;
            int top = (int)SendMessage(Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
            int oldStart = SelectionStart, oldLength = SelectionLength;
            string[] oldLines = Lines;
            string anchor = top >= 0 && top < oldLines.Length ? oldLines[top] : null;
            string selection = SelectedText;
            SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            try
            {
                Text = value;
                if (follow) { Select(TextLength, 0); SendMessage(Handle, 0x00B6, IntPtr.Zero, new IntPtr(Lines.Length)); }
                else
                {
                    int selectionStart = selection.Length > 0 ? value.IndexOf(selection, StringComparison.Ordinal) : -1;
                    Select(selectionStart >= 0 ? selectionStart : Math.Min(oldStart, TextLength), Math.Min(oldLength, Math.Max(0, TextLength - (selectionStart >= 0 ? selectionStart : Math.Min(oldStart, TextLength)))));
                    int target = Math.Min(top, Math.Max(0, Lines.Length - 1));
                    if (!String.IsNullOrEmpty(anchor)) { int index = Array.IndexOf(Lines, anchor); if (index >= 0) target = index; }
                    int current = (int)SendMessage(Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
                    SendMessage(Handle, 0x00B6, IntPtr.Zero, new IntPtr(target - current));
                }
            }
            finally { SendMessage(Handle, 0x000B, new IntPtr(1), IntPtr.Zero); Invalidate(); }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Select(TextLength, 0); ScrollToCaret(); }
    }
}
