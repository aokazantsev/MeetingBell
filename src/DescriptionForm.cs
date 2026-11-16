using System.Drawing;
using System.Windows.Forms;

namespace MeetingBell
{
    internal sealed class DescriptionForm : Form
    {
        public DescriptionForm(Meeting meeting)
        {
            Text = meeting.Start.ToString("HH:mm") + " " + meeting.DisplaySubject;
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            KeyPreview = true;
            int unit = Font.Height;
            ClientSize = new Size(unit * 32, unit * 20);
            MinimumSize = new Size(unit * 16, unit * 8);
            var text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                Text = meeting.Description ?? ""
            };
            var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(unit / 2), BackColor = SystemColors.Window };
            frame.Controls.Add(text);
            Controls.Add(frame);
            KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
            Shown += (sender, e) => text.Select(0, 0);
        }
    }
}
