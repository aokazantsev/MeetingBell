using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MeetingBell
{
    internal sealed class MeetingPreferences
    {
        public const int Off = -1;

        private const string LeadKey = "lead";
        private const string SeenKey = "seen";
        private const string DayFormat = "yyyy-MM-dd";
        private static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(100);

        public static readonly string FilePath = UserDataPaths.File("meetings.txt");

        private readonly Dictionary<string, int> leads = new Dictionary<string, int>();
        private readonly Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>();
        private bool writable = true;

        public string LoadProblem { get; private set; }

        public int Count
        {
            get { return leads.Count; }
        }

        public int OffCount
        {
            get
            {
                int count = 0;
                foreach (int lead in leads.Values) if (lead == Off) count++;
                return count;
            }
        }

        public static MeetingPreferences Load(DateTime today)
        {
            var preferences = new MeetingPreferences();
            string text;
            try
            {
                if (!File.Exists(FilePath)) return preferences;
                text = File.ReadAllText(FilePath, Encoding.UTF8);
            }
            catch (IOException error)
            {
                preferences.writable = false;
                preferences.LoadProblem = error.Message;
                return preferences;
            }
            catch (UnauthorizedAccessException error)
            {
                preferences.writable = false;
                preferences.LoadProblem = error.Message;
                return preferences;
            }
            try
            {
                foreach (KeyValuePair<string, object> pair in JsonText.ParseObject(text)) preferences.Read(pair.Key, pair.Value as Dictionary<string, object>, today);
            }
            catch (Exception error)
            {
                if (!(error is FormatException) && !(error is ArgumentException) && !(error is InvalidOperationException)) throw;
                preferences.LoadProblem = "файл испорчен, отложен в " + Path.GetFileName(FilePath) + ".broken";
                preferences.leads.Clear();
                preferences.seen.Clear();
                DataFile.SetAside(FilePath);
            }
            return preferences;
        }

        public bool TryGetLead(string uid, out int lead)
        {
            return leads.TryGetValue(uid ?? "", out lead);
        }

        public int EffectiveLead(Meeting meeting, int defaultLead)
        {
            int lead;
            return leads.TryGetValue(meeting.Uid ?? "", out lead) ? lead : defaultLead;
        }

        public string Set(string uid, int lead, DateTime today)
        {
            leads[uid] = lead;
            seen[uid] = today.Date;
            return Save();
        }

        public string Reset(string uid)
        {
            if (!leads.Remove(uid)) return null;
            seen.Remove(uid);
            return Save();
        }

        public string MarkSeen(IEnumerable<Meeting> meetings, DateTime today)
        {
            bool changed = false;
            foreach (Meeting meeting in meetings)
            {
                if (meeting.Uid == null || !leads.ContainsKey(meeting.Uid)) continue;
                DateTime last;
                if (seen.TryGetValue(meeting.Uid, out last) && last == today.Date) continue;
                seen[meeting.Uid] = today.Date;
                changed = true;
            }
            foreach (string uid in new List<string>(leads.Keys))
            {
                DateTime last;
                if (seen.TryGetValue(uid, out last) && today.Date - last <= ForgetAfter) continue;
                leads.Remove(uid);
                seen.Remove(uid);
                changed = true;
            }
            return changed ? Save() : null;
        }

        private void Read(string uid, Dictionary<string, object> entry, DateTime today)
        {
            if (string.IsNullOrEmpty(uid) || entry == null) return;
            object leadValue;
            object seenValue;
            if (!entry.TryGetValue(LeadKey, out leadValue) || !(leadValue is int)) return;
            int lead = (int)leadValue;
            if (lead != Off && (lead < 0 || lead > AppSettings.MaxLeadMinutes)) return;
            DateTime last = today.Date;
            DateTime parsed;
            if (entry.TryGetValue(SeenKey, out seenValue) && DateTime.TryParseExact(seenValue as string, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) last = parsed;
            leads[uid] = lead;
            seen[uid] = last;
        }

        private string Save()
        {
            if (!writable) return "файл " + FilePath + " не прочитан при запуске (" + LoadProblem + "), сохранять поверх нельзя";
            var root = new Dictionary<string, object>();
            foreach (KeyValuePair<string, int> pair in leads)
            {
                DateTime last;
                if (!seen.TryGetValue(pair.Key, out last)) last = DateTime.Today;
                root[pair.Key] = new Dictionary<string, object>
                {
                    { LeadKey, pair.Value },
                    { SeenKey, last.ToString(DayFormat, CultureInfo.InvariantCulture) }
                };
            }
            return DataFile.Write(FilePath, JsonText.Write(root));
        }
    }
}
