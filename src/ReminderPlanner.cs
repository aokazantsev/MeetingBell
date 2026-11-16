using System;
using System.Collections.Generic;

namespace MeetingBell
{
    internal sealed class ReminderPlanner
    {
        public static readonly TimeSpan LateLimit = TimeSpan.FromMinutes(5);

        private readonly Dictionary<string, DateTime> snoozed = new Dictionary<string, DateTime>();

        public List<Meeting> Due(IEnumerable<Meeting> meetings, DateTime now, int defaultLead, MeetingPreferences preferences, ICollection<string> shown)
        {
            var due = new List<Meeting>();
            foreach (Meeting meeting in meetings)
            {
                if (meeting.IsAllDay) continue;
                string key = meeting.ReminderKey;
                DateTime until;
                if (snoozed.TryGetValue(key, out until))
                {
                    if (now >= until && now < meeting.End)
                    {
                        snoozed.Remove(key);
                        due.Add(meeting);
                    }
                    continue;
                }
                if (shown.Contains(key)) continue;
                int lead = preferences.EffectiveLead(meeting, defaultLead);
                if (lead == MeetingPreferences.Off) continue;
                if (now >= meeting.Start.AddMinutes(-lead) && now < meeting.Start + LateLimit && now < meeting.End) due.Add(meeting);
            }
            return due;
        }

        public void Snooze(Meeting meeting, DateTime until, ICollection<string> shown)
        {
            snoozed[meeting.ReminderKey] = until;
            shown.Remove(meeting.ReminderKey);
        }

        public void Forget(Meeting meeting)
        {
            snoozed.Remove(meeting.ReminderKey);
        }
    }
}
