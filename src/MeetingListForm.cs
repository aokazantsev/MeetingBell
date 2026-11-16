using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MeetingBell
{
    internal sealed class MeetingListForm : Form
    {
        private const int ToolWindow = 0x80;
        private const int TopMostStyle = 0x8;
        private const int NoActivate = 0x08000000;
        private const int ScreenMargin = 8;
        private static readonly Color Background = SystemColors.Window;
        private static readonly Color Border = Color.FromArgb(160, 160, 160);
        private static readonly Color Muted = Color.FromArgb(120, 120, 120);
        private static readonly Color Past = Color.FromArgb(150, 150, 150);
        private static readonly Color Accent = Color.FromArgb(255, 183, 0);
        private static readonly Color Hover = Color.FromArgb(232, 240, 254);
        private static readonly Color Warning = Color.FromArgb(176, 90, 0);
        private static readonly CultureInfo Russian = new CultureInfo("ru-RU");

        private readonly Font titleFont;
        private readonly Font rowFont;
        private readonly Font timeFont;
        private readonly Font smallFont;
        private readonly int padding;
        private readonly int rowHeight;
        private readonly int timeWidth;
        private List<Meeting> meetings = new List<Meeting>();
        private readonly List<Rectangle> rowBounds = new List<Rectangle>();
        private Func<Meeting, string> describe;
        private DateTime now;
        private string status;
        private bool statusIsProblem;
        private bool statusClickable;
        private Rectangle statusBounds;
        private int hoverRow = -1;
        private bool hoverStatus;

        public event Action<Meeting, Point> MeetingClicked;
        public event Action StatusClicked;

        public MeetingListForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Background;
            DoubleBuffered = true;
            Font = SystemFonts.MessageBoxFont;
            titleFont = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold);
            rowFont = new Font(Font.FontFamily, Font.Size);
            timeFont = new Font(Font.FontFamily, Font.Size, FontStyle.Bold);
            smallFont = new Font(Font.FontFamily, Font.Size - 0.5f);
            padding = Font.Height * 3 / 4;
            rowHeight = rowFont.Height + smallFont.Height + padding * 2 / 3;
            timeWidth = TextRenderer.MeasureText("00:00–00:00", timeFont).Width + padding / 2;
            Width = Font.Height * 26;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= ToolWindow | TopMostStyle | NoActivate;
                return parameters;
            }
        }

        public void Populate(List<Meeting> list, DateTime current, string statusText, bool problem, bool clickable, Func<Meeting, string> description)
        {
            meetings = list;
            now = current;
            status = statusText;
            statusIsProblem = problem;
            statusClickable = clickable;
            describe = description;
            Arrange(Screen.FromPoint(Cursor.Position).WorkingArea.Height - ScreenMargin * 2);
            Invalidate();
        }

        public void ShowNear(Point anchor)
        {
            Rectangle area = Screen.FromPoint(anchor).WorkingArea;
            int x = Math.Max(area.Left + ScreenMargin, Math.Min(anchor.X - Width / 2, area.Right - Width - ScreenMargin));
            int y = anchor.Y > area.Bottom - ScreenMargin ? area.Bottom - Height - ScreenMargin : anchor.Y - Height - ScreenMargin;
            if (anchor.Y < area.Top + ScreenMargin) y = area.Top + ScreenMargin;
            y = Math.Max(area.Top + ScreenMargin, Math.Min(y, area.Bottom - Height - ScreenMargin));
            Location = new Point(x, y);
            if (!Visible) Show();
        }

        public bool Contains(Point screenPoint, int slack)
        {
            Rectangle bounds = Bounds;
            bounds.Inflate(slack, slack);
            return Visible && bounds.Contains(screenPoint);
        }

        private void Arrange(int maxHeight)
        {
            rowBounds.Clear();
            int top = padding + titleFont.Height + padding / 3;
            statusBounds = new Rectangle(padding, top, Width - padding * 2, smallFont.Height);
            top = statusBounds.Bottom + padding / 2;
            int footer = smallFont.Height + padding;
            int visible = meetings.Count == 0 ? 0 : Math.Max(1, Math.Min(meetings.Count, (maxHeight - top - footer - padding) / rowHeight));
            int skip = meetings.Count - visible;
            for (int index = 0; index < meetings.Count; index++)
            {
                if (index < skip && meetings[index].IsOver(now))
                {
                    rowBounds.Add(Rectangle.Empty);
                    continue;
                }
                if (rowBounds.FindAll(bounds => !bounds.IsEmpty).Count >= visible)
                {
                    rowBounds.Add(Rectangle.Empty);
                    continue;
                }
                rowBounds.Add(new Rectangle(0, top, Width, rowHeight));
                top += rowHeight;
            }
            if (meetings.Count == 0) top += rowHeight;
            Height = top + footer;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var pen = new Pen(Border))
            {
                graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            string title = "Сегодня, " + DateTime.Today.ToString("d MMMM, dddd", Russian);
            TextRenderer.DrawText(graphics, title, titleFont, new Point(padding, padding), ForeColor, TextFormatFlags.NoPrefix);
            Color statusColor = statusIsProblem ? Warning : Muted;
            Font statusFont = statusClickable && hoverStatus ? new Font(smallFont, FontStyle.Underline) : smallFont;
            TextRenderer.DrawText(graphics, status ?? "", statusFont, statusBounds, statusColor, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (statusFont != smallFont) statusFont.Dispose();
            if (meetings.Count == 0)
            {
                TextRenderer.DrawText(graphics, "Созвонов на сегодня нет", rowFont, new Rectangle(padding, statusBounds.Bottom + padding / 2, Width - padding * 2, rowHeight), Muted, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            }
            for (int index = 0; index < meetings.Count; index++)
            {
                if (!rowBounds[index].IsEmpty) PaintRow(graphics, meetings[index], rowBounds[index], index == hoverRow);
            }
            TextRenderer.DrawText(graphics, "Клик по встрече — напоминание и ссылка", smallFont, new Point(padding, Height - smallFont.Height - padding / 2), Muted, TextFormatFlags.NoPrefix);
        }

        private void PaintRow(Graphics graphics, Meeting meeting, Rectangle bounds, bool hovered)
        {
            if (hovered)
            {
                using (var brush = new SolidBrush(Hover))
                {
                    graphics.FillRectangle(brush, bounds.X + 1, bounds.Y, bounds.Width - 2, bounds.Height);
                }
            }
            if (meeting.IsGoing(now))
            {
                using (var brush = new SolidBrush(Accent))
                {
                    graphics.FillRectangle(brush, bounds.X + 1, bounds.Y + 3, padding / 3, bounds.Height - 6);
                }
            }
            bool over = meeting.IsOver(now);
            Color main = over ? Past : ForeColor;
            Color detail = over ? Past : Muted;
            int top = bounds.Y + padding / 3;
            string time = meeting.IsAllDay ? "весь день" : meeting.Start.ToString("HH:mm") + "–" + meeting.End.ToString("HH:mm");
            TextRenderer.DrawText(graphics, time, timeFont, new Point(padding, top), main, TextFormatFlags.NoPrefix);
            var subjectBox = new Rectangle(padding + timeWidth, top, bounds.Width - padding * 2 - timeWidth, rowFont.Height);
            TextRenderer.DrawText(graphics, meeting.DisplaySubject, rowFont, subjectBox, main, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            var detailBox = new Rectangle(subjectBox.X, top + rowFont.Height, subjectBox.Width, smallFont.Height);
            TextRenderer.DrawText(graphics, describe == null ? "" : describe(meeting), smallFont, detailBox, detail, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Location);
            bool overStatus = statusClickable && statusBounds.Contains(e.Location);
            Cursor = row >= 0 || overStatus ? Cursors.Hand : Cursors.Default;
            if (row == hoverRow && overStatus == hoverStatus) return;
            hoverRow = row;
            hoverStatus = overStatus;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverRow < 0 && !hoverStatus) return;
            hoverRow = -1;
            hoverStatus = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (statusClickable && statusBounds.Contains(e.Location))
            {
                if (StatusClicked != null) StatusClicked();
                return;
            }
            int row = RowAt(e.Location);
            if (row >= 0 && MeetingClicked != null) MeetingClicked(meetings[row], PointToScreen(e.Location));
        }

        private int RowAt(Point point)
        {
            for (int index = 0; index < rowBounds.Count; index++)
            {
                if (!rowBounds[index].IsEmpty && rowBounds[index].Contains(point)) return index;
            }
            return -1;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont.Dispose();
                rowFont.Dispose();
                timeFont.Dispose();
                smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
