using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeetingBell
{
    internal sealed class ReminderForm : Form
    {
        private const int ToolWindow = 0x80;
        private const int TopMostStyle = 0x8;
        private const int RefreshMs = 15 * 1000;
        private static readonly Color Muted = Color.FromArgb(110, 110, 110);

        private readonly Meeting meeting;
        private readonly Label timing;
        private readonly Timer refreshTimer = new Timer();
        private readonly Font subjectFont;

        public event Action<ReminderForm> JoinClicked;
        public event Action<ReminderForm> SnoozeClicked;

        private const string NewMeetingHeadline = "На сегодня назначена новая встреча";

        public ReminderForm(Meeting meeting, bool newMeetingNotice)
        {
            this.meeting = meeting;
            IsNewMeetingNotice = newMeetingNotice;
            Text = AppIdentity.Name;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = SystemFonts.MessageBoxFont;
            subjectFont = new Font(Font.FontFamily, Font.Size + 2, FontStyle.Bold);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Window;
            int unit = Font.Height;
            int width = unit * 22;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(unit * 3 / 4, unit / 2, unit * 3 / 4, unit / 2),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            var subject = new Label
            {
                Text = meeting.DisplaySubject,
                Font = subjectFont,
                AutoSize = true,
                MaximumSize = new Size(width, unit * 4),
                UseMnemonic = false,
                Margin = new Padding(0, 0, 0, unit / 4)
            };
            timing = new Label { AutoSize = true, MaximumSize = new Size(width, 0), UseMnemonic = false };
            if (newMeetingNotice)
            {
                layout.Controls.Add(new Label
                {
                    Text = NewMeetingHeadline,
                    AutoSize = true,
                    MaximumSize = new Size(width, 0),
                    UseMnemonic = false,
                    ForeColor = Muted,
                    Margin = new Padding(0, 0, 0, unit / 4)
                });
            }
            layout.Controls.Add(subject);
            layout.Controls.Add(timing);
            if (!string.IsNullOrWhiteSpace(meeting.Location) && meeting.Location.Trim() != meeting.Link)
            {
                layout.Controls.Add(new Label
                {
                    Text = meeting.Location.Trim(),
                    AutoSize = true,
                    MaximumSize = new Size(width, unit * 3),
                    UseMnemonic = false,
                    ForeColor = Muted,
                    Margin = new Padding(0, unit / 4, 0, 0)
                });
            }
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, unit / 2, 0, 0),
                WrapContents = false
            };
            var close = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.Cancel };
            close.Click += (sender, e) => Close();
            buttons.Controls.Add(close);
            if (!newMeetingNotice)
            {
                var snooze = new Button { Text = "Через 1 мин", AutoSize = true };
                snooze.Click += (sender, e) =>
                {
                    if (SnoozeClicked != null) SnoozeClicked(this);
                    Close();
                };
                buttons.Controls.Add(snooze);
            }
            if (meeting.Link != null)
            {
                var join = new Button { Text = MeetingLinks.JoinText(meeting.Link), AutoSize = true };
                join.Click += (sender, e) =>
                {
                    if (JoinClicked != null) JoinClicked(this);
                    Close();
                };
                buttons.Controls.Add(join);
            }
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            CancelButton = close;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(width + unit * 2, 0);
            UpdateTiming();
            refreshTimer.Interval = RefreshMs;
            refreshTimer.Tick += (sender, e) => UpdateTiming();
            refreshTimer.Start();
        }

        public bool IsNewMeetingNotice { get; private set; }

        public Meeting Meeting
        {
            get { return meeting; }
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
                parameters.ExStyle |= ToolWindow | TopMostStyle;
                return parameters;
            }
        }

        public static string Timing(Meeting meeting, DateTime now)
        {
            string range = meeting.Start.ToString("HH:mm") + "–" + meeting.End.ToString("HH:mm");
            double minutes = (meeting.Start - now).TotalMinutes;
            if (minutes >= 0.5) return range + " · через " + Minutes((int)Math.Round(minutes, MidpointRounding.AwayFromZero));
            if (minutes > -0.5) return range + " · начинается сейчас";
            return range + " · идёт " + Minutes((int)Math.Round(-minutes, MidpointRounding.AwayFromZero));
        }

        public static string Minutes(int count)
        {
            int lastTwo = count % 100;
            int last = count % 10;
            string word = lastTwo >= 11 && lastTwo <= 14 ? "минут" : last == 1 ? "минуту" : last >= 2 && last <= 4 ? "минуты" : "минут";
            return count + " " + word;
        }

        private void UpdateTiming()
        {
            timing.Text = Timing(meeting, DateTime.Now);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                subjectFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
