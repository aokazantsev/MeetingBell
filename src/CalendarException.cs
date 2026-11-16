using System;

namespace MeetingBell
{
    internal sealed class CalendarException : Exception
    {
        public readonly bool IsAuthentication;

        public CalendarException(string message, bool isAuthentication)
            : base(message)
        {
            IsAuthentication = isAuthentication;
        }
    }
}
