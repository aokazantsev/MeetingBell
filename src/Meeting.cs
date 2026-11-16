using System;

namespace MeetingBell
{
    internal sealed class Meeting
    {
        public string Uid;
        public string Subject;
        public DateTime Start;
        public DateTime End;
        public string Location;
        public string Link;
        public string Description;
        public bool IsAllDay;

        public string ReminderKey
        {
            get { return Uid + "|" + Start.ToUniversalTime().ToString("yyyyMMddHHmm"); }
        }

        public string DisplaySubject
        {
            get { return string.IsNullOrWhiteSpace(Subject) ? "Без темы" : Subject.Trim(); }
        }

        public bool IsOver(DateTime now)
        {
            return End <= now;
        }

        public bool IsGoing(DateTime now)
        {
            return Start <= now && now < End;
        }
    }
}
