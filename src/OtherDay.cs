using System;
using System.Collections.Generic;

namespace MeetingBell
{
    internal sealed class OtherDay
    {
        public List<Meeting> Meetings;
        public DateTime? Fetched;
        public bool Loading;
        public bool Wanted;
        public bool Failed;
        public bool Stale;

        public bool Ready
        {
            get { return Meetings != null && !Failed; }
        }

        public bool NeedsLoadWhenViewed(bool retryFailed)
        {
            if (Loading || Wanted) return false;
            return Stale || Meetings == null || Failed && retryFailed;
        }

        public bool NeedsLoad(DateTime now, int freshMinutes, bool retryFailed)
        {
            if (Loading || Wanted) return false;
            if (Failed) return retryFailed || Stale;
            if (Meetings == null || Stale || !Fetched.HasValue) return true;
            return now - Fetched.Value >= TimeSpan.FromMinutes(freshMinutes);
        }
    }
}
