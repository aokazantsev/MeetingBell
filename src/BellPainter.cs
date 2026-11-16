using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MeetingBell
{
    internal static class BellPainter
    {
        private static readonly Color ReadyColor = Color.FromArgb(46, 160, 67);
        private static readonly Color OfflineColor = Color.FromArgb(150, 150, 150);
        private static readonly Color ProblemColor = Color.FromArgb(218, 54, 51);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon TrayIcon(TrayState state)
        {
            Size size = SystemInformation.SmallIconSize;
            using (var bitmap = new Bitmap(size.Width, size.Height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);
                    DrawBell(graphics, new RectangleF(0, 0, size.Width, size.Height), ColorOf(state));
                }
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        public static Color ColorOf(TrayState state)
        {
            if (state == TrayState.Ready) return ReadyColor;
            return state == TrayState.Offline ? OfflineColor : ProblemColor;
        }

        public static void DrawBell(Graphics graphics, RectangleF box, Color color)
        {
            float w = box.Width;
            float h = box.Height;
            float x = box.X;
            float y = box.Y;
            using (var path = new GraphicsPath())
            {
                path.AddArc(x + w * 0.22f, y + h * 0.12f, w * 0.56f, h * 0.56f, 180, 180);
                path.AddLine(x + w * 0.78f, y + h * 0.40f, x + w * 0.84f, y + h * 0.66f);
                path.AddLine(x + w * 0.84f, y + h * 0.66f, x + w * 0.96f, y + h * 0.78f);
                path.AddLine(x + w * 0.96f, y + h * 0.78f, x + w * 0.04f, y + h * 0.78f);
                path.AddLine(x + w * 0.04f, y + h * 0.78f, x + w * 0.16f, y + h * 0.66f);
                path.AddLine(x + w * 0.16f, y + h * 0.66f, x + w * 0.22f, y + h * 0.40f);
                path.CloseFigure();
                using (var brush = new SolidBrush(color))
                {
                    graphics.FillPath(brush, path);
                    graphics.FillEllipse(brush, x + w * 0.38f, y + h * 0.78f, w * 0.24f, h * 0.2f);
                    graphics.FillEllipse(brush, x + w * 0.43f, y + h * 0.02f, w * 0.14f, h * 0.14f);
                }
            }
        }
    }
}
