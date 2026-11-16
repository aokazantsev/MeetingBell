using System;
using System.Collections.Generic;
using System.Drawing;
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
        private static readonly Color Separator = Color.FromArgb(225, 225, 225);
        private static readonly Color Link = Color.FromArgb(0, 102, 204);
        private const string OpenText = "Открыть";
        private const string BackText = "<";
        private const string ForwardText = ">";

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
        private ListStatus status = new ListStatus("", null, false);
        private Rectangle statusBounds;
        private Rectangle statusActionBounds;
        private int hoverRow = -1;
        private string heldKey;
        private bool hoverStatus;
        private string mailText;
        private string mailAction;
        private Rectangle mailBounds;
        private Rectangle mailOpenBounds;
        private bool hoverMail;
        private string title = "";
        private string emptyText = "";
        private bool canBack;
        private bool canForward;
        private Rectangle backBounds;
        private Rectangle forwardBounds;
        private int hoverArrow;

        public event Action<Meeting, Point> MeetingClicked;
        public event Action StatusClicked;
        public event Action MailClicked;
        public event Action<Point> MenuRequested;
        public event Action<MouseButtons, string> Pressed;
        public event Action<int> DayStepRequested;

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

        public void Populate(List<Meeting> list, DateTime current, string dayTitle, bool back, bool forward, string empty, ListStatus listStatus, Func<Meeting, string> description, string mail, bool mailCanOpen, Point anchor)
        {
            title = dayTitle;
            canBack = back;
            canForward = forward;
            emptyText = empty;
            mailText = mail;
            mailAction = mailCanOpen ? OpenText : null;
            meetings = list;
            now = current;
            status = listStatus;
            describe = description;
            Arrange(Screen.FromPoint(anchor).WorkingArea.Height - ScreenMargin * 2);
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

        private void Arrange(int maxHeight)
        {
            rowBounds.Clear();
            int arrowWidth = TextRenderer.MeasureText(BackText, titleFont).Width + padding / 2;
            backBounds = new Rectangle(padding / 2, padding - padding / 4, arrowWidth, titleFont.Height + padding / 2);
            forwardBounds = new Rectangle(Width - padding / 2 - arrowWidth, padding - padding / 4, arrowWidth, titleFont.Height + padding / 2);
            int top = padding + titleFont.Height + padding / 3;
            int mailRow = rowFont.Height + padding;
            statusBounds = new Rectangle(0, top, Width, rowFont.Height);
            statusActionBounds = ActionBounds(status.Action, statusBounds);
            top = statusBounds.Bottom + padding / 2;
            int footer = padding / 2 + mailRow;
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
            mailBounds = new Rectangle(0, Height - mailRow, Width, mailRow);
            mailOpenBounds = ActionBounds(mailAction, mailBounds);
        }

        private Rectangle ActionBounds(string action, Rectangle line)
        {
            if (action == null) return Rectangle.Empty;
            int width = TextRenderer.MeasureText(action, smallFont).Width + padding / 2;
            return new Rectangle(Width - padding - width, line.Y, width + padding / 2, line.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var pen = new Pen(Border))
            {
                graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            PaintArrow(graphics, BackText, backBounds, canBack, hoverArrow == -1);
            PaintArrow(graphics, ForwardText, forwardBounds, canForward, hoverArrow == 1);
            var titleBox = new Rectangle(backBounds.Right, padding, forwardBounds.Left - backBounds.Right, titleFont.Height);
            TextRenderer.DrawText(graphics, title ?? "", titleFont, titleBox, ForeColor, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter);
            PaintLine(graphics, status.Text, status.Problem ? Warning : Muted, padding, statusBounds, status.Action, statusActionBounds, hoverStatus);
            if (meetings.Count == 0)
            {
                TextRenderer.DrawText(graphics, emptyText ?? "", rowFont, new Rectangle(padding, statusBounds.Bottom + padding / 2, Width - padding * 2, rowHeight), Muted, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            }
            for (int index = 0; index < meetings.Count; index++)
            {
                if (!rowBounds[index].IsEmpty) PaintRow(graphics, meetings[index], rowBounds[index], heldKey == null ? index == hoverRow : IsHeld(meetings[index]));
            }
            PaintMail(graphics);
        }

        private void PaintArrow(Graphics graphics, string arrow, Rectangle bounds, bool enabled, bool hovered)
        {
            TextRenderer.DrawText(graphics, arrow, titleFont, bounds, enabled ? (hovered ? Link : ForeColor) : Separator, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private int ArrowAt(Point point)
        {
            if (canBack && backBounds.Contains(point)) return -1;
            if (canForward && forwardBounds.Contains(point)) return 1;
            return 0;
        }

        private void PaintMail(Graphics graphics)
        {
            using (var pen = new Pen(Separator))
            {
                graphics.DrawLine(pen, padding, mailBounds.Y, Width - padding, mailBounds.Y);
            }
            int iconHeight = rowFont.Height * 2 / 3;
            int iconWidth = iconHeight * 4 / 3;
            var icon = new Rectangle(padding, mailBounds.Y + (mailBounds.Height - iconHeight) / 2, iconWidth, iconHeight);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(Muted, 1.2f))
            {
                graphics.DrawRectangle(pen, icon);
                graphics.DrawLines(pen, new[] { new Point(icon.Left, icon.Top), new Point(icon.Left + icon.Width / 2, icon.Top + icon.Height * 3 / 5), new Point(icon.Right, icon.Top) });
            }
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            PaintLine(graphics, mailText, Muted, icon.Right + padding / 2, mailBounds, mailAction, mailOpenBounds, hoverMail);
        }

        private void PaintLine(Graphics graphics, string text, Color color, int textLeft, Rectangle line, string action, Rectangle actionBounds, bool hovered)
        {
            int textRight = actionBounds.IsEmpty ? Width - padding : actionBounds.Left;
            TextRenderer.DrawText(graphics, text ?? "", rowFont, new Rectangle(textLeft, line.Y, textRight - textLeft, line.Height), color, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (actionBounds.IsEmpty) return;
            using (Font font = new Font(smallFont, hovered ? FontStyle.Underline : FontStyle.Regular))
            {
                TextRenderer.DrawText(graphics, action, font, actionBounds, Link, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }
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
            bool overStatus = !statusActionBounds.IsEmpty && statusActionBounds.Contains(e.Location);
            bool overMail = !mailOpenBounds.IsEmpty && mailOpenBounds.Contains(e.Location);
            int arrow = ArrowAt(e.Location);
            Cursor = row >= 0 || overStatus || overMail || arrow != 0 ? Cursors.Hand : Cursors.Default;
            if (row == hoverRow && overStatus == hoverStatus && overMail == hoverMail && arrow == hoverArrow) return;
            hoverArrow = arrow;
            hoverRow = row;
            hoverStatus = overStatus;
            hoverMail = overMail;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverRow < 0 && !hoverStatus && !hoverMail && hoverArrow == 0) return;
            hoverArrow = 0;
            hoverRow = -1;
            hoverStatus = false;
            hoverMail = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int row = e.Button == MouseButtons.Left ? RowAt(e.Location) : -1;
            if (Pressed != null) Pressed(e.Button, row >= 0 ? meetings[row].ReminderKey : null);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                if (MenuRequested != null) MenuRequested(PointToScreen(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int arrow = ArrowAt(e.Location);
            if (arrow != 0)
            {
                if (DayStepRequested != null) DayStepRequested(arrow);
                return;
            }
            if (!mailOpenBounds.IsEmpty && mailOpenBounds.Contains(e.Location))
            {
                if (MailClicked != null) MailClicked();
                return;
            }
            if (!statusActionBounds.IsEmpty && statusActionBounds.Contains(e.Location))
            {
                if (StatusClicked != null) StatusClicked();
                return;
            }
            int row = RowAt(e.Location);
            if (row < 0) return;
            heldKey = meetings[row].ReminderKey;
            Invalidate();
            if (MeetingClicked != null) MeetingClicked(meetings[row], PointToScreen(e.Location));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        public void ReleaseHeldRow()
        {
            if (heldKey == null) return;
            heldKey = null;
            Invalidate();
        }

        private bool IsHeld(Meeting meeting)
        {
            return heldKey != null && meeting.ReminderKey == heldKey;
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
