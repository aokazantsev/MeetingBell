using System;
using System.Collections.Generic;

namespace MeetingBell
{
    internal static class NewMeetingFinder
    {
        private static readonly TimeSpan StartedLimit = TimeSpan.FromMinutes(30);

        public static List<Meeting> Find(DateTime? previousFetched, List<Meeting> previous, List<Meeting> fresh, DateTime now)
        {
            if (!previousFetched.HasValue) return new List<Meeting>();
            var known = new HashSet<string>();
            foreach (Meeting meeting in previous) known.Add(meeting.Uid ?? "");
            var added = new List<Meeting>();
            foreach (Meeting meeting in fresh)
            {
                if (meeting.IsAllDay || known.Contains(meeting.Uid ?? "")) continue;
                if (meeting.Start < now - StartedLimit || meeting.End <= now) continue;
                added.Add(meeting);
            }
            return added;
        }
    }
}
