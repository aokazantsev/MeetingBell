using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MeetingBell
{
    internal sealed class AppSettings
    {
        public const string DefaultEwsUrl = "https://mail.example.com/EWS/Exchange.asmx";
        public const string DefaultVpnAdapter = "Fortinet";
        public const int DefaultLeadMinutes = 1;
        public const int MaxLeadMinutes = 60;

        private const string EwsUrlKey = "ewsUrl";
        private const string VpnAdaptersKey = "vpnAdapters";
        private const string LeadMinutesKey = "leadMinutes";
        private const string CalendarMinutesKey = "calendarMinutes";
        private const string MailMinutesKey = "mailMinutes";
        private const string MailUrlKey = "mailUrl";
        public const int DefaultRefreshMinutes = 5;
        public const int MaxRefreshMinutes = 1440;
        private const char KeyValueSeparator = '=';
        private const char ListSeparator = '|';
        private const int ReadAttempts = 5;
        private const int ReadRetryMs = 300;
        private const int PatientReadRetries = 10;
        private const int PatientReadRetryMs = 1500;

        public static readonly string FilePath = UserDataPaths.File("settings.txt");

        public string EwsUrl = DefaultEwsUrl;
        public List<string> VpnAdapters = new List<string> { DefaultVpnAdapter };
        public int LeadMinutes = DefaultLeadMinutes;
        public int CalendarMinutes = DefaultRefreshMinutes;
        public int MailMinutes = DefaultRefreshMinutes;
        public string MailUrl = "";
        public string ReadError;
        public bool ReadDenied;

        public static bool FileExists
        {
            get { return File.Exists(FilePath); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(FilePath)) return settings;
            string[] lines = null;
            for (int attempt = 1; lines == null; attempt++)
            {
                try
                {
                    lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                }
                catch (FileNotFoundException)
                {
                    return settings;
                }
                catch (IOException error)
                {
                    if (attempt >= ReadAttempts) settings.ReadError = error.Message;
                }
                catch (UnauthorizedAccessException error)
                {
                    settings.ReadError = error.Message;
                    settings.ReadDenied = true;
                }
                if (settings.ReadError != null) return settings;
                if (lines == null) Thread.Sleep(ReadRetryMs);
            }
            foreach (string line in lines)
            {
                int separator = line.IndexOf(KeyValueSeparator);
                if (separator <= 0) continue;
                settings.Apply(line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim());
            }
            return settings;
        }

        public static AppSettings LoadPatiently(Action<string> log)
        {
            AppSettings settings = Load();
            for (int attempt = 1; settings.ReadError != null && !settings.ReadDenied && attempt <= PatientReadRetries; attempt++)
            {
                log("settings not read: " + settings.ReadError + ", retry " + attempt);
                Thread.Sleep(PatientReadRetryMs);
                settings = Load();
            }
            return settings;
        }

        public string ReadAdvice
        {
            get { return ReadDenied ? "Нет доступа к файлу: верни права на него или удали его." : "Файл занят другой программой: подожди, пока он освободится."; }
        }

        public bool ChecksVpn
        {
            get { return VpnAdapters.Count > 0; }
        }

        public AppSettings Clone()
        {
            return new AppSettings
            {
                EwsUrl = EwsUrl,
                VpnAdapters = new List<string>(VpnAdapters),
                LeadMinutes = LeadMinutes,
                CalendarMinutes = CalendarMinutes,
                MailMinutes = MailMinutes,
                MailUrl = MailUrl
            };
        }

        public string Save()
        {
            var content = new StringBuilder();
            AppendLine(content, EwsUrlKey, EwsUrl);
            AppendLine(content, VpnAdaptersKey, JoinList(VpnAdapters));
            AppendLine(content, LeadMinutesKey, LeadMinutes.ToString(CultureInfo.InvariantCulture));
            AppendLine(content, CalendarMinutesKey, CalendarMinutes.ToString(CultureInfo.InvariantCulture));
            AppendLine(content, MailMinutesKey, MailMinutes.ToString(CultureInfo.InvariantCulture));
            AppendLine(content, MailUrlKey, MailUrl);
            string temporary = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(temporary, content.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
                return null;
            }
            catch (IOException error)
            {
                return error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                return error.Message;
            }
        }

        public string ApplyInstallFields(string url, string vpn, bool fieldsUnread)
        {
            if (!fieldsUnread || vpn.Length > 0) VpnAdapters = ParseList(vpn);
            if (fieldsUnread && url.Length == 0) return null;
            string problem = EwsClient.CheckUrl(url);
            if (problem == null) EwsUrl = url.Trim();
            return problem;
        }

        public string WebMailUrl
        {
            get
            {
                if (MeetingLinks.IsWebLink(MailUrl)) return MailUrl;
                Uri ews;
                if (!Uri.TryCreate(EwsUrl, UriKind.Absolute, out ews)) return null;
                return ews.GetLeftPart(UriPartial.Authority) + "/owa/#path=/mail";
            }
        }

        public static List<string> ParseList(string value)
        {
            var items = new List<string>();
            foreach (string item in (value ?? "").Split(ListSeparator))
            {
                string trimmed = item.Trim();
                if (trimmed.Length > 0 && !items.Exists(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))) items.Add(trimmed);
            }
            return items;
        }

        public static string JoinList(List<string> items)
        {
            return string.Join(ListSeparator.ToString(), items.ToArray());
        }

        private void Apply(string key, string value)
        {
            if (Is(key, EwsUrlKey)) EwsUrl = value.Length == 0 ? DefaultEwsUrl : value;
            else if (Is(key, VpnAdaptersKey)) VpnAdapters = ParseList(value);
            else if (Is(key, LeadMinutesKey)) LeadMinutes = ParseInt(value, DefaultLeadMinutes, 0, MaxLeadMinutes);
            else if (Is(key, CalendarMinutesKey)) CalendarMinutes = ParseInt(value, DefaultRefreshMinutes, 1, MaxRefreshMinutes);
            else if (Is(key, MailMinutesKey)) MailMinutes = ParseInt(value, DefaultRefreshMinutes, 1, MaxRefreshMinutes);
            else if (Is(key, MailUrlKey)) MailUrl = MeetingLinks.IsWebLink(value) ? value : "";
        }

        private static int ParseInt(string value, int fallback, int min, int max)
        {
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return fallback;
            return Math.Max(min, Math.Min(max, parsed));
        }

        private static bool Is(string key, string expected)
        {
            return string.Equals(key, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void AppendLine(StringBuilder content, string key, string value)
        {
            content.Append(key).Append(KeyValueSeparator).Append(value).Append(Environment.NewLine);
        }
    }
}
