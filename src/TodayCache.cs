using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MeetingBell
{
    internal sealed class TodayCache
    {
        private const string DayKey = "day";
        private const string FetchedKey = "fetched";
        private const string MeetingsKey = "meetings";
        private const string ShownKey = "shown";
        private const string DayFormat = "yyyy-MM-dd";
        private const string TimeFormat = "o";

        public static readonly string FilePath = UserDataPaths.File("today.txt");

        public DateTime Day = DateTime.Today;
        public DateTime? Fetched;
        public List<Meeting> Meetings = new List<Meeting>();
        public readonly HashSet<string> Shown = new HashSet<string>();

        public static TodayCache Load(DateTime today, Action<string> log)
        {
            var cache = new TodayCache { Day = today.Date };
            try
            {
                if (!File.Exists(FilePath)) return cache;
                Dictionary<string, object> root = JsonText.ParseObject(File.ReadAllText(FilePath, Encoding.UTF8));
                if (Text(root, DayKey) != today.ToString(DayFormat, CultureInfo.InvariantCulture)) return cache;
                DateTime fetched;
                if (TryTime(Text(root, FetchedKey), out fetched)) cache.Fetched = fetched;
                object list;
                if (root.TryGetValue(MeetingsKey, out list) && list is IList)
                {
                    foreach (object entry in (IList)list)
                    {
                        Meeting meeting = ReadMeeting(entry as Dictionary<string, object>);
                        if (meeting != null) cache.Meetings.Add(meeting);
                    }
                }
                object shown;
                if (root.TryGetValue(ShownKey, out shown) && shown is IList)
                {
                    foreach (object key in (IList)shown)
                    {
                        var text = key as string;
                        if (text != null) cache.Shown.Add(text);
                    }
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is FormatException) && !(error is ArgumentException) && !(error is InvalidOperationException)) throw;
                log("today cache not read: " + error.Message);
                return new TodayCache { Day = today.Date };
            }
            return cache;
        }

        public string Save()
        {
            var meetings = new List<object>();
            foreach (Meeting meeting in Meetings)
            {
                meetings.Add(new Dictionary<string, object>
                {
                    { "uid", meeting.Uid ?? "" },
                    { "subject", meeting.Subject ?? "" },
                    { "start", meeting.Start.ToString(TimeFormat, CultureInfo.InvariantCulture) },
                    { "end", meeting.End.ToString(TimeFormat, CultureInfo.InvariantCulture) },
                    { "location", meeting.Location ?? "" },
                    { "link", meeting.Link ?? "" },
                    { "description", meeting.Description ?? "" },
                    { "allDay", meeting.IsAllDay }
                });
            }
            var root = new Dictionary<string, object>
            {
                { DayKey, Day.ToString(DayFormat, CultureInfo.InvariantCulture) },
                { FetchedKey, Fetched.HasValue ? Fetched.Value.ToString(TimeFormat, CultureInfo.InvariantCulture) : "" },
                { MeetingsKey, meetings },
                { ShownKey, new List<object>(Shown) }
            };
            return DataFile.Write(FilePath, JsonText.Write(root));
        }

        private static Meeting ReadMeeting(Dictionary<string, object> entry)
        {
            if (entry == null) return null;
            DateTime start;
            DateTime end;
            if (!TryTime(Text(entry, "start"), out start) || !TryTime(Text(entry, "end"), out end)) return null;
            string uid = Text(entry, "uid");
            if (string.IsNullOrEmpty(uid)) return null;
            string link = Text(entry, "link");
            string description = Text(entry, "description");
            object allDay;
            return new Meeting
            {
                Uid = uid,
                Subject = Text(entry, "subject"),
                Start = start,
                End = end,
                Location = Text(entry, "location"),
                Link = MeetingLinks.IsWebLink(link) ? link : null,
                Description = string.IsNullOrEmpty(description) ? null : description,
                IsAllDay = entry.TryGetValue("allDay", out allDay) && allDay is bool && (bool)allDay
            };
        }

        private static bool TryTime(string value, out DateTime local)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(value) && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            {
                local = parsed.Kind == DateTimeKind.Unspecified ? parsed : parsed.ToLocalTime();
                return true;
            }
            local = DateTime.MinValue;
            return false;
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) ? value as string : null;
        }
    }
}
