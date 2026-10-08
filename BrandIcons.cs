using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Switcher
{
    public static class BrandIcons
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static Bitmap Draw(int size, string letter, Color accent)
        {
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent); g.ScaleTransform(size / 256f, size / 256f);
                using (var tile = new GraphicsPath())
                {
                    tile.AddArc(4, 4, 64, 64, 180, 90); tile.AddArc(188, 4, 64, 64, 270, 90);
                    tile.AddArc(188, 188, 64, 64, 0, 90); tile.AddArc(4, 188, 64, 64, 90, 90); tile.CloseFigure();
                    using (var fill = new LinearGradientBrush(new Point(0, 0), new Point(256, 256), Color.FromArgb(51, 62, 77), Color.FromArgb(20, 26, 35))) g.FillPath(fill, tile);
                    using (var outline = new Pen(Color.FromArgb(79, 100, 122), 3)) g.DrawPath(outline, tile);
                }
                using (var fill = new LinearGradientBrush(new Point(48, 200), new Point(210, 35), Color.FromArgb(100, 174, 226), accent))
                {
                    if (letter == "S")
                    {
                        // Two tapered strokes form a continuous, needle-sharp S silhouette.
                        g.FillPolygon(fill, new[] { new Point(215, 35), new Point(99, 52), new Point(47, 118), new Point(159, 153), new Point(41, 222), new Point(160, 204), new Point(210, 137), new Point(98, 102) });
                        using (var eye = new Pen(Color.FromArgb(32, 41, 53), 4)) g.DrawLine(eye, 151, 66, 180, 51);
                    }
                    else
                    {
                        using (var font = new Font("Segoe UI", 167, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(letter, font, fill, new RectangleF(0, -5, 256, 256), format);
                        using (var dot = new SolidBrush(accent)) g.FillEllipse(dot, 203, 207, 26, 26);
                    }
                }
            }
            return bitmap;
        }
        public static Icon Tray(string letter, Color accent)
        {
            using (var bitmap = Draw(32, letter, accent))
            {
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }
}
