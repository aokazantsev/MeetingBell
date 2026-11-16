using System;
using System.Globalization;

namespace MeetingBell
{
    internal static class DayNavigation
    {
        private static readonly CultureInfo Russian = new CultureInfo("ru-RU");

        public static DateTime First(DateTime today)
        {
            DateTime day = today.Date;
            int sinceMonday = ((int)day.DayOfWeek + 6) % 7;
            return day.AddDays(-sinceMonday - 7);
        }

        public static DateTime Last(DateTime today)
        {
            DateTime day = today.Date;
            int toSunday = (7 - (int)day.DayOfWeek) % 7;
            return day.AddDays(toSunday + 7);
        }

        public static bool InRange(DateTime day, DateTime today)
        {
            return day.Date >= First(today) && day.Date <= Last(today);
        }

        public static string Title(DateTime day, DateTime today)
        {
            string date = day.ToString("d MMMM, dddd", Russian);
            int offset = (int)(day.Date - today.Date).TotalDays;
            if (offset == 0) return "Сегодня, " + date;
            if (offset == -1) return "Вчера, " + date;
            if (offset == 1) return "Завтра, " + date;
            return date;
        }
    }
}
