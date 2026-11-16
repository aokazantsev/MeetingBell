using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MeetingBell
{
    internal sealed class SettingsForm : Form
    {
        private static readonly Font SectionFont = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
        private readonly AppSettings original;
        private readonly ExchangeCredential saved;
        private readonly TextBox urlBox = new TextBox();
        private readonly TextBox userBox = new TextBox();
        private readonly TextBox passwordBox = new TextBox { UseSystemPasswordChar = true };
        private readonly Button testButton = new Button { Text = "Проверить подключение", AutoSize = true };
        private readonly Label testResult = new Label { AutoSize = true, UseMnemonic = false };
        private readonly ComboBox vpnBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly NumericUpDown leadBox = new NumericUpDown { Minimum = 0, Maximum = AppSettings.MaxLeadMinutes, Width = 60 };
        private readonly NumericUpDown calendarBox = new NumericUpDown { Minimum = 1, Maximum = AppSettings.MaxRefreshMinutes, Width = 60 };
        private readonly NumericUpDown mailBox = new NumericUpDown { Minimum = 1, Maximum = AppSettings.MaxRefreshMinutes, Width = 60 };
        private readonly TextBox mailUrlBox = new TextBox();
        private readonly CheckBox autoUpdateBox = new CheckBox { Text = "Проверять обновление раз в сутки", AutoSize = true };
        private readonly CheckBox usageBox = new CheckBox { Text = "Отправлять автору анонимную статистику использования", AutoSize = true };
        private int testGeneration;

        public AppSettings Result { get; private set; }
        public ExchangeCredential NewCredential { get; private set; }
        public bool AutoUpdateChosen { get; private set; }
        public bool UsageStatisticsChosen { get; private set; }
        public bool RemoveCredential { get; private set; }

        public SettingsForm(AppSettings settings, ExchangeCredential credential, string intro)
        {
            original = settings;
            saved = credential;
            Text = AppIdentity.Name + " — настройки";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int unit = Font.Height;
            int fieldWidth = unit * 24;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(unit),
                Dock = DockStyle.Fill
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            if (!string.IsNullOrEmpty(intro)) AddWide(layout, Wrapped(intro, fieldWidth + unit * 8, SystemColors.ControlText));
            AddSection(layout, "Exchange");
            urlBox.Width = fieldWidth;
            urlBox.Text = settings.EwsUrl;
            AddRow(layout, "Адрес EWS:", urlBox, "Обычно https://<почтовый сервер>/EWS/Exchange.asmx");
            userBox.Width = fieldWidth;
            userBox.Text = credential == null ? "" : credential.UserName;
            AddRow(layout, "Логин:", userBox, "DOMAIN\\user или user@company.com");
            passwordBox.Width = fieldWidth;
            AddRow(layout, "Пароль:", passwordBox, credential != null && credential.IsComplete
                ? "Сохранён в диспетчере учётных данных Windows. Пусто — оставить прежний."
                : "Хранится в диспетчере учётных данных Windows.");
            var testRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            testResult.MaximumSize = new Size(fieldWidth - unit * 8, 0);
            testResult.Margin = new Padding(unit / 2, unit / 3, 0, 0);
            testButton.Click += OnTestClick;
            testRow.Controls.Add(testButton);
            testRow.Controls.Add(testResult);
            layout.Controls.Add(new Label());
            layout.Controls.Add(testRow);

            AddSection(layout, "VPN");
            vpnBox.Width = fieldWidth;
            foreach (string adapter in VpnDetector.AdapterDescriptions()) vpnBox.Items.Add(adapter);
            vpnBox.Text = AppSettings.JoinList(settings.VpnAdapters);
            AddRow(layout, "Адаптер VPN:", vpnBox, "Часть названия адаптера, несколько — через |. Без VPN Exchange не опрашивается. Пусто — VPN не проверяется.");

            AddSection(layout, "Напоминания");
            leadBox.Value = settings.LeadMinutes;
            var leadRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            leadRow.Controls.Add(leadBox);
            leadRow.Controls.Add(new Label { Text = "мин до начала (0 — в момент начала)", AutoSize = true, Margin = new Padding(unit / 3, unit / 3, 0, 0) });
            AddRow(layout, "Напоминать за:", leadRow, "У отдельной встречи время меняется в списке: наведи на значок и нажми на встречу.");

            AddSection(layout, "Обновление и почта");
            calendarBox.Value = settings.CalendarMinutes;
            AddRow(layout, "Календарь:", MinutesRow(calendarBox, "раз в столько минут"), null);
            mailBox.Value = settings.MailMinutes;
            AddRow(layout, "Почта:", MinutesRow(mailBox, "раз в столько минут"), "Число непрочитанных писем во «Входящих» (без подпапок) — внизу списка.");
            mailUrlBox.Width = fieldWidth;
            mailUrlBox.Text = settings.MailUrl;
            AddRow(layout, "Веб-почта:", mailUrlBox, "Адрес для кнопки «Открыть». Пусто — https://<сервер EWS>/owa/#path=/mail");

            AddSection(layout, "Программа");
            autoUpdateBox.Checked = AutoUpdateCheck.IsEnabled;
            usageBox.Checked = UsageConsent.IsGiven;
            AddWide(layout, autoUpdateBox);
            AddWide(layout, usageBox);
            AddWide(layout, Wrapped("Без имён, адресов, тем встреч и логина: только число запусков, нажатий и настройки.", fieldWidth + unit * 8, Color.FromArgb(110, 110, 110)));

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, unit, 0, 0) };
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
            var save = new Button { Text = "Сохранить", AutoSize = true };
            save.Click += OnSaveClick;
            cancel.Click += (sender, e) => Close();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            AddWide(layout, buttons);
            Controls.Add(layout);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static FlowLayoutPanel MinutesRow(NumericUpDown box, string caption)
        {
            int unit = SystemFonts.MessageBoxFont.Height;
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            row.Controls.Add(box);
            row.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(unit / 3, unit / 3, 0, 0) });
            return row;
        }

        private static void AddSection(TableLayoutPanel layout, string title)
        {
            var label = new Label { Text = title, AutoSize = true, Font = SectionFont, Margin = new Padding(0, SystemFonts.MessageBoxFont.Height * 2 / 3, 0, 2) };
            AddWide(layout, label);
        }

        private static void AddWide(TableLayoutPanel layout, Control control)
        {
            layout.Controls.Add(control);
            layout.SetColumnSpan(control, 2);
        }

        private static void AddRow(TableLayoutPanel layout, string caption, Control field, string hint)
        {
            int unit = SystemFonts.MessageBoxFont.Height;
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, unit / 3 + 2, unit / 2, 0) });
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            stack.Controls.Add(field);
            if (hint != null) stack.Controls.Add(Wrapped(hint, unit * 24, Color.FromArgb(110, 110, 110)));
            layout.Controls.Add(stack);
        }

        private static Label Wrapped(string text, int width, Color color)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = color, UseMnemonic = false, Margin = new Padding(0, 0, 0, 2) };
        }

        private CredentialChoice EnteredCredential()
        {
            return CredentialChoice.Decide(saved, userBox.Text, passwordBox.Text);
        }

        private void OnTestClick(object sender, EventArgs e)
        {
            Usage.Hit("settings.test");
            string url = urlBox.Text.Trim();
            ExchangeCredential credential = EnteredCredential().Credential;
            string problem = EwsClient.CheckUrl(url);
            if (problem == null && !credential.IsComplete) problem = "введи логин и пароль";
            if (problem != null)
            {
                ShowTestResult(problem, false);
                return;
            }
            int generation = ++testGeneration;
            testButton.Enabled = false;
            ShowTestResult("Проверяю…", true);
            List<string> vpn = AppSettings.ParseList(vpnBox.Text);
            ThreadPool.QueueUserWorkItem(state =>
            {
                string text;
                bool ok = false;
                try
                {
                    List<Meeting> meetings = new EwsClient(url, credential).Day(DateTime.Today);
                    text = "Подключено, встреч сегодня: " + meetings.Count;
                    ok = true;
                }
                catch (CalendarException error)
                {
                    text = Capitalize(error.Message) + (VpnDetector.IsUp(vpn) ? "" : ". VPN сейчас не поднят");
                }
                catch (Exception error)
                {
                    AppLog.Append("connection test failed: " + error);
                    text = "Внутренняя ошибка: " + error.Message;
                }
                Finish(generation, text, ok);
            });
        }

        private void Finish(int generation, string text, bool ok)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (generation != testGeneration || IsDisposed) return;
                    testButton.Enabled = true;
                    ShowTestResult(text, ok);
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ShowTestResult(string text, bool ok)
        {
            testResult.ForeColor = ok ? Color.FromArgb(0, 120, 60) : Color.FromArgb(176, 40, 40);
            testResult.Text = text;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            string url = urlBox.Text.Trim();
            string problem = EwsClient.CheckUrl(url);
            if (problem != null)
            {
                MessageBox.Show(this, Capitalize(problem) + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                urlBox.Focus();
                return;
            }
            string mailUrl = mailUrlBox.Text.Trim();
            if (mailUrl.Length > 0 && !MeetingLinks.IsWebLink(mailUrl))
            {
                MessageBox.Show(this, "Адрес веб-почты должен начинаться с https:// или http://.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                mailUrlBox.Focus();
                return;
            }
            CredentialChoice choice = EnteredCredential();
            if (choice.Problem != null)
            {
                MessageBox.Show(this, choice.Problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                passwordBox.Focus();
                return;
            }
            NewCredential = choice.Changed ? choice.Credential : null;
            RemoveCredential = choice.Remove;
            AppSettings result = original.Clone();
            result.EwsUrl = url;
            result.VpnAdapters = AppSettings.ParseList(vpnBox.Text);
            result.LeadMinutes = (int)leadBox.Value;
            result.CalendarMinutes = (int)calendarBox.Value;
            result.MailMinutes = (int)mailBox.Value;
            result.MailUrl = mailUrlBox.Text.Trim();
            Result = result;
            AutoUpdateChosen = autoUpdateBox.Checked;
            UsageStatisticsChosen = usageBox.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string Capitalize(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0]) + text.Substring(1);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            testGeneration++;
            base.OnFormClosed(e);
        }
    }
}
