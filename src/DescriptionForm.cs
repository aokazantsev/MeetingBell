using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MeetingBell
{
    internal sealed class DescriptionForm : Form
    {
        private static readonly Color LinkColor = Color.FromArgb(0, 102, 204);
        private static readonly Color Muted = Color.FromArgb(110, 110, 110);
        private static readonly CultureInfo Russian = new CultureInfo("ru-RU");
        private readonly Meeting meeting;
        private readonly RichTextBox text;
        private readonly Font titleFont;
        private List<TextLink> links = new List<TextLink>();

        public DescriptionForm(Meeting meeting)
        {
            this.meeting = meeting;
            Text = meeting.Start.ToString("HH:mm") + " " + meeting.DisplaySubject;
            Font = SystemFonts.MessageBoxFont;
            titleFont = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            KeyPreview = true;
            int unit = Font.Height;
            ClientSize = new Size(unit * 34, unit * 24);
            MinimumSize = new Size(unit * 16, unit * 8);
            text = new RichTextBox
            {
                ReadOnly = true,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window
            };
            Load += (sender, e) => Render();
            text.MouseMove += (sender, e) => text.Cursor = LinkAt(e.Location) != null ? Cursors.Hand : Cursors.IBeam;
            text.MouseUp += OnTextMouseUp;
            var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(unit), BackColor = SystemColors.Window };
            frame.Controls.Add(text);
            Controls.Add(frame);
            KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
            Shown += (sender, e) => text.Select(0, 0);
        }

        internal static string When(Meeting meeting)
        {
            string day = meeting.Start.ToString("dddd, d MMMM", Russian);
            return meeting.IsAllDay ? day + ", весь день" : day + ", " + meeting.Start.ToString("HH:mm") + "–" + meeting.End.ToString("HH:mm");
        }

        private void Render()
        {
            text.Clear();
            Append(meeting.DisplaySubject + "\n\n", titleFont, ForeColor);
            Field("От", meeting.Organizer);
            Field("Когда", When(meeting));
            Field("Где", string.IsNullOrWhiteSpace(meeting.Location) ? null : meeting.Location.Trim());
            Field("Кому", meeting.RequiredAttendees);
            Field("Копия", meeting.OptionalAttendees);
            Append("\n", Font, ForeColor);
            int bodyStart = text.TextLength;
            if (meeting.Description == null) Append("Текста приглашения нет.", Font, Muted);
            else Append(meeting.Description.Replace("\r\n", "\n"), Font, ForeColor);
            links = new List<TextLink>();
            foreach (TextLink link in MeetingLinks.WebLinks(text.Text.Substring(bodyStart)))
            {
                links.Add(new TextLink(bodyStart + link.Start, link.Length, link.Url));
            }
            using (var underline = new Font(Font, FontStyle.Underline))
            {
                foreach (TextLink link in links)
                {
                    text.Select(link.Start, link.Length);
                    text.SelectionColor = LinkColor;
                    text.SelectionFont = underline;
                }
            }
            text.Select(0, 0);
        }

        private void Field(string label, string value)
        {
            if (value == null) return;
            Append(label + ":  ", Font, Muted);
            Append(value + "\n", Font, ForeColor);
        }

        private void Append(string value, Font font, Color color)
        {
            text.Select(text.TextLength, 0);
            text.SelectionFont = font;
            text.SelectionColor = color;
            text.SelectedText = value;
        }

        private TextLink LinkAt(Point point)
        {
            if (links.Count == 0 || text.TextLength == 0) return null;
            int index = text.GetCharIndexFromPosition(point);
            if (index < 0 || index >= text.TextLength) return null;
            Point start = text.GetPositionFromCharIndex(index);
            if (point.Y < start.Y || point.Y >= start.Y + Font.Height || point.X < start.X) return null;
            Point next = index + 1 < text.TextLength ? text.GetPositionFromCharIndex(index + 1) : Point.Empty;
            int right = index + 1 < text.TextLength && next.Y == start.Y ? next.X : start.X + TextRenderer.MeasureText(text.Text.Substring(index, 1), Font).Width;
            if (point.X >= right) return null;
            return links.Find(link => link.Contains(index));
        }

        private void OnTextMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || text.SelectionLength > 0) return;
            TextLink link = LinkAt(e.Location);
            if (link == null) return;
            Usage.Hit("description.link");
            string problem = MeetingLinks.Open(link.Url);
            AppLog.Append(problem == null ? "description link opened" : "description link not opened: " + problem);
            if (problem != null) MessageBox.Show(this, "Не удалось открыть ссылку: " + problem, AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) titleFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
