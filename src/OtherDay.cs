using System;
using System.Collections.Generic;

namespace MeetingBell
{
    internal sealed class OtherDay
    {
        public List<Meeting> Meetings;
        public DateTime? Fetched;
        public bool Loading;
        public bool Failed;
        public bool Stale;

        public bool Ready
        {
            get { return Meetings != null && !Loading && !Failed; }
        }

        public bool NeedsLoad(DateTime now, int freshMinutes)
        {
            if (Loading) return false;
            if (Meetings == null || Failed || Stale || !Fetched.HasValue) return true;
            return now - Fetched.Value >= TimeSpan.FromMinutes(freshMinutes);
        }
    }
}
