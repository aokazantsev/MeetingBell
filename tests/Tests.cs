using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Xml;

namespace MeetingBell
{
    internal static class Tests
    {
        private static int failures;
        private static int passed;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "live") return Live(args);
            string sandbox = Path.Combine(Path.GetTempPath(), "MeetingBellTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            Redirect(typeof(MeetingPreferences), "FilePath", Path.Combine(sandbox, "meetings.txt"));
            Redirect(typeof(TodayCache), "FilePath", Path.Combine(sandbox, "today.txt"));
            Redirect(typeof(AppSettings), "FilePath", Path.Combine(sandbox, "settings.txt"));
            try
            {
                PlannerDefaultLead();
                PlannerCustomAndOff();
                PlannerLateAndAllDay();
                PlannerSnooze();
                Links();
                Preferences();
                Cache();
                Settings();
                Words();
                Urls();
                EwsParsing();
                MovedAndSeries();
                KnownLinks();
                Boundaries();
                Credentials();
                Mail();
                NewMeetings();
                Descriptions();
                MoreNewMeetingsAndDescriptions();
                LinkChoice();
            }
            finally
            {
                Directory.Delete(sandbox, true);
            }
            Console.WriteLine("passed " + passed + ", failed " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static void Redirect(Type type, string field, string value)
        {
            type.GetField(field, BindingFlags.Public | BindingFlags.Static).SetValue(null, value);
        }

        private static void Check(bool condition, string name)
        {
            if (condition) passed++;
            else
            {
                failures++;
                Console.WriteLine("FAIL " + name);
            }
        }

        private static Meeting At(DateTime start, int minutes, string uid)
        {
            return new Meeting { Uid = uid, Subject = "s", Start = start, End = start.AddMinutes(minutes) };
        }

        private static MeetingPreferences EmptyPreferences()
        {
            File.Delete(MeetingPreferences.FilePath);
            return MeetingPreferences.Load(DateTime.Today);
        }

        private static void PlannerDefaultLead()
        {
            var start = new DateTime(2026, 11, 16, 14, 0, 0);
            var meetings = new List<Meeting> { At(start, 30, "a") };
            var planner = new ReminderPlanner();
            var shown = new HashSet<string>();
            MeetingPreferences preferences = EmptyPreferences();
            Check(planner.Due(meetings, start.AddMinutes(-2), 1, preferences, shown).Count == 0, "default lead: 2 min before — not yet");
            Check(planner.Due(meetings, start.AddMinutes(-1), 1, preferences, shown).Count == 1, "default lead: 1 min before — due");
            shown.Add(meetings[0].ReminderKey);
            Check(planner.Due(meetings, start.AddSeconds(-30), 1, preferences, shown).Count == 0, "shown reminder is not repeated");
            Check(planner.Due(meetings, start, 0, preferences, new HashSet<string>()).Count == 1, "lead 0: due at start");
        }

        private static void PlannerCustomAndOff()
        {
            var start = new DateTime(2026, 11, 16, 14, 0, 0);
            var series = new List<Meeting> { At(start, 30, "series"), At(start.AddHours(2), 30, "other") };
            MeetingPreferences preferences = EmptyPreferences();
            preferences.Set("series", 5, DateTime.Today);
            var planner = new ReminderPlanner();
            List<Meeting> due = planner.Due(series, start.AddMinutes(-5), 1, preferences, new HashSet<string>());
            Check(due.Count == 1 && due[0].Uid == "series", "custom lead 5 min fires 5 min before");
            preferences.Set("series", MeetingPreferences.Off, DateTime.Today);
            Check(planner.Due(series, start, 1, preferences, new HashSet<string>()).Count == 0, "off meeting never fires");
            var nextDay = new List<Meeting> { At(start.AddDays(1), 30, "series") };
            Check(planner.Due(nextDay, start.AddDays(1), 1, preferences, new HashSet<string>()).Count == 0, "off applies to the whole series");
        }

        private static void PlannerLateAndAllDay()
        {
            var start = new DateTime(2026, 11, 16, 14, 0, 0);
            var meetings = new List<Meeting> { At(start, 60, "late") };
            MeetingPreferences preferences = EmptyPreferences();
            var planner = new ReminderPlanner();
            Check(planner.Due(meetings, start.AddMinutes(4), 1, preferences, new HashSet<string>()).Count == 1, "late start within 5 min still reminds");
            Check(planner.Due(meetings, start.AddMinutes(6), 1, preferences, new HashSet<string>()).Count == 0, "more than 5 min late — no reminder");
            var allDay = new List<Meeting> { new Meeting { Uid = "d", Start = start.Date, End = start.Date.AddDays(1), IsAllDay = true } };
            Check(planner.Due(allDay, start.Date, 60, preferences, new HashSet<string>()).Count == 0, "all-day meeting has no reminder");
            var shortOne = new List<Meeting> { At(start, 2, "short") };
            Check(planner.Due(shortOne, start.AddMinutes(3), 1, preferences, new HashSet<string>()).Count == 0, "finished meeting has no reminder");
        }

        private static void PlannerSnooze()
        {
            var start = new DateTime(2026, 11, 16, 14, 0, 0);
            var meetings = new List<Meeting> { At(start, 30, "z") };
            MeetingPreferences preferences = EmptyPreferences();
            var planner = new ReminderPlanner();
            var shown = new HashSet<string> { meetings[0].ReminderKey };
            planner.Snooze(meetings[0], start.AddMinutes(1), shown);
            Check(planner.Due(meetings, start.AddSeconds(30), 1, preferences, shown).Count == 0, "snoozed: not before the time");
            Check(!shown.Contains(meetings[0].ReminderKey), "snooze removes the shown mark so a restart reminds again");
            Check(planner.Due(meetings, start.AddMinutes(1), 1, preferences, shown).Count == 1, "snoozed: fires again after a minute");
            shown.Add(meetings[0].ReminderKey);
            Check(planner.Due(meetings, start.AddMinutes(2), 1, preferences, shown).Count == 0, "snooze fires once");
            planner.Snooze(meetings[0], start.AddMinutes(40), shown);
            Check(planner.Due(meetings, start.AddMinutes(40), 1, preferences, shown).Count == 0, "snooze after the end does not fire");
            planner.Snooze(meetings[0], start.AddMinutes(7), shown);
            Check(planner.Due(meetings, start.AddMinutes(7), 1, preferences, shown).Count == 1, "snooze fires past the late limit while the meeting goes");
            shown.Add(meetings[0].ReminderKey);
            Check(new ReminderPlanner().Due(meetings, start.AddMinutes(5), 1, preferences, new HashSet<string>()).Count == 0, "exactly 5 min late — no reminder");
            var restarted = new ReminderPlanner();
            var afterSnooze = new HashSet<string>();
            Check(restarted.Due(meetings, start.AddMinutes(2), 1, preferences, afterSnooze).Count == 1, "after restart a snoozed meeting reminds within the late limit");
            Check(restarted.Due(meetings, start.AddMinutes(6), 1, preferences, afterSnooze).Count == 0, "after restart late limit still applies");
        }

        private static void NewMeetings()
        {
            var now = new DateTime(2026, 11, 16, 14, 0, 0);
            var previous = new List<Meeting> { At(now.AddHours(1), 30, "old") };
            Meeting fresh = At(now.AddHours(2), 30, "new");
            Meeting justStarted = At(now.AddMinutes(-30), 60, "started");
            Meeting longAgo = At(now.AddMinutes(-31), 60, "past");
            var allDay = new Meeting { Uid = "day", Start = now.Date, End = now.Date.AddDays(1), IsAllDay = true };
            Meeting movedOld = At(now.AddHours(3), 30, "old");
            List<Meeting> added = NewMeetingFinder.Find(now, previous, new List<Meeting> { previous[0], fresh, justStarted, longAgo, allDay, movedOld }, now);
            Check(added.Count == 2 && added.Contains(fresh) && added.Contains(justStarted), "new meetings from 30 min ago on, known uid and all-day skipped");
            Check(NewMeetingFinder.Find(now, previous, previous, now).Count == 0, "same list gives no notice");
            Check(NewMeetingFinder.Find(now, previous, new List<Meeting> { At(now.AddMinutes(-25), 15, "ended") }, now).Count == 0, "meeting that already ended gives no notice");
        }

        private static void MoreNewMeetingsAndDescriptions()
        {
            var now = new DateTime(2026, 11, 16, 14, 0, 0);
            var fresh = new List<Meeting> { At(now.AddHours(1), 30, "n") };
            Check(NewMeetingFinder.Find(null, new List<Meeting>(), fresh, now).Count == 0, "first load of the day gives no notice");
            Check(NewMeetingFinder.Find(now.AddMinutes(-1), new List<Meeting>(), fresh, now).Count == 1, "after a loaded list a new meeting gives a notice");
            Check(NewMeetingFinder.Find(now, new List<Meeting>(), new List<Meeting> { At(now.AddMinutes(-20), 20, "edge") }, now).Count == 0, "meeting ending right now gives no notice");
            var again = new Dictionary<string, Meeting> { { "X", At(now, 30, "x") }, { "Y", At(now, 30, "y") } };
            XmlDocument partial = Xml("<m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass=\"Success\"><m:Items>"
                + Item("X", null, "", "", "", "<t:Body BodyType=\"Text\">   </t:Body>")
                + "</m:Items></m:GetItemResponseMessage><m:GetItemResponseMessage ResponseClass=\"Error\"><m:ResponseCode>ErrorServerBusy</m:ResponseCode></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>");
            Meeting oldX = At(now, 30, "x");
            oldX.Description = "старое X";
            Meeting oldY = At(now, 30, "y");
            oldY.Description = "старое Y";
            TrayApplication.KeepKnownLinks(EwsClient.Unread(again, EwsClient.ApplyBodies(partial, again)), new List<Meeting> { oldX, oldY });
            Check(again["X"].Description == null && again["Y"].Description == "старое Y", "cleared invitation text drops description, unread keeps it");
            string NL = Environment.NewLine;
            Check(EwsClient.Describe(new string('x', 3995) + "     yyyy") == new string('x', 3995) + "…", "cut trims trailing spaces before ellipsis");
            Check(EwsClient.Describe("a\t\r\rb\n\n\n\nc") == "a" + NL + NL + "b" + NL + NL + "c", "lone CR and tabs normalized");
            var cache = new TodayCache { Day = DateTime.Today, Fetched = DateTime.Today };
            cache.Meetings.Add(new Meeting { Uid = "q", Start = DateTime.Today, End = DateTime.Today.AddHours(1), Description = "a\"b\\c\td\u0001e" });
            cache.Save();
            Check(TodayCache.Load(DateTime.Today, line => { }).Meetings[0].Description == "a\"b\\c\td\u0001e", "description with quotes and control chars survives");
            File.WriteAllText(TodayCache.FilePath, "{\"day\": \"" + DateTime.Today.ToString("yyyy-MM-dd") + "\", \"fetched\": \"\", \"meetings\": [{\"uid\": \"o\", \"start\": \"" + DateTime.Today.ToString("o") + "\", \"end\": \"" + DateTime.Today.AddHours(1).ToString("o") + "\"}], \"shown\": []}");
            TodayCache old = TodayCache.Load(DateTime.Today, line => { });
            Check(old.Meetings.Count == 1 && old.Meetings[0].Description == null, "old today file without description loads");
        }

        private static void Descriptions()
        {
            string text = EwsClient.Describe("Повестка  \r\n\r\n\r\n\r\nПереговорная 5\r\n");
            Check(text == "Повестка" + Environment.NewLine + Environment.NewLine + "Переговорная 5", "description trimmed and blank lines collapsed");
            Check(EwsClient.Describe("   \r\n ") == null && EwsClient.Describe(null) == null, "empty body gives no description");
            Check(EwsClient.Describe(new string('x', 5000)).Length == 4001, "long body cut to the limit");
            Check(EwsClient.Describe(new string('x', 4000)) == new string('x', 4000) && EwsClient.Describe(new string('x', 4001)) == new string('x', 4000) + "…", "cut only above the limit");
            Check(EwsClient.Describe(new string('x', 3999) + "😀tail") == new string('x', 3999) + "…", "cut keeps surrogate pair whole");
            var withLink = new Meeting { Uid = "l", Link = "https://zoom.us/j/own", Start = DateTime.Today, End = DateTime.Today.AddHours(1) };
            var requested = new Dictionary<string, Meeting> { { "L", withLink } };
            XmlDocument body = Xml("<m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass=\"Success\"><m:Items>"
                + Item("L", null, "", "", "", "<t:Body BodyType=\"Text\">Кабинет 12 https://teams.microsoft.com/l/x</t:Body>")
                + "</m:Items></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>");
            EwsClient.ApplyBodies(body, requested);
            Check(withLink.Link == "https://zoom.us/j/own" && withLink.Description.StartsWith("Кабинет 12"), "body gives description, location link kept");
            var unreadMeeting = At(DateTime.Today, 30, "d");
            var known = At(DateTime.Today, 30, "d");
            known.Description = "Старое описание";
            TrayApplication.KeepKnownLinks(new List<Meeting> { unreadMeeting }, new List<Meeting> { known });
            Check(unreadMeeting.Description == "Старое описание", "known description kept when body did not arrive");
            var cache = new TodayCache { Day = DateTime.Today, Fetched = DateTime.Today };
            cache.Meetings.Add(new Meeting { Uid = "c", Start = DateTime.Today, End = DateTime.Today.AddHours(1), Description = "Строка 1" + Environment.NewLine + "Строка 2" });
            cache.Meetings.Add(new Meeting { Uid = "e", Start = DateTime.Today, End = DateTime.Today.AddHours(1) });
            cache.Save();
            TodayCache loaded = TodayCache.Load(DateTime.Today, line => { });
            Check(loaded.Meetings[0].Description == "Строка 1" + Environment.NewLine + "Строка 2" && loaded.Meetings[1].Description == null, "description survives restart");
            var people = At(DateTime.Today, 30, "p");
            var peopleRequest = new Dictionary<string, Meeting> { { "P", people } };
            string many = "";
            for (int index = 0; index < 52; index++) many += "<t:Attendee><t:Mailbox><t:Name>Гость " + index + "</t:Name><t:EmailAddress>g" + index + "@example.com</t:EmailAddress></t:Mailbox></t:Attendee>";
            XmlDocument withPeople = Xml("<m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass=\"Success\"><m:Items>"
                + Item("P", null, "", "", "", "<t:Organizer><t:Mailbox><t:Name>Анна   Петрова</t:Name><t:EmailAddress>anna@example.com</t:EmailAddress></t:Mailbox></t:Organizer>"
                + "<t:RequiredAttendees><t:Attendee><t:Mailbox><t:Name>bob@example.com</t:Name><t:EmailAddress>bob@example.com</t:EmailAddress></t:Mailbox></t:Attendee>"
                + "<t:Attendee><t:Mailbox><t:Name>Отдел</t:Name><t:EmailAddress>/O=EXAMPLE/OU=X</t:EmailAddress></t:Mailbox></t:Attendee>"
                + "<t:Attendee><t:Mailbox><t:EmailAddress>cat@example.com</t:EmailAddress></t:Mailbox></t:Attendee></t:RequiredAttendees>"
                + "<t:OptionalAttendees>" + many + "</t:OptionalAttendees>")
                + "</m:Items></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>");
            EwsClient.ApplyBodies(withPeople, peopleRequest);
            Check(people.Organizer == "Анна Петрова <anna@example.com>", "organizer name and address");
            Check(people.RequiredAttendees == "bob@example.com; Отдел; cat@example.com", "attendees: same name and address once, legacy address dropped, address without name");
            Check(people.OptionalAttendees.StartsWith("Гость 0 <g0@example.com>; ") && people.OptionalAttendees.EndsWith("; и ещё 2") && people.OptionalAttendees.Contains("Гость 49 <") && !people.OptionalAttendees.Contains("Гость 50"), "long attendee list cut with count");
            Check(people.Description == null && people.HasDetails && !At(DateTime.Today, 30, "n").HasDetails, "card without text still has details");
            var oldPeople = At(DateTime.Today, 30, "p");
            oldPeople.Organizer = "Старый";
            oldPeople.RequiredAttendees = "A";
            oldPeople.OptionalAttendees = "B";
            var freshPeople = At(DateTime.Today, 30, "p");
            TrayApplication.KeepKnownLinks(new List<Meeting> { freshPeople }, new List<Meeting> { oldPeople });
            Check(freshPeople.Organizer == "Старый" && freshPeople.RequiredAttendees == "A" && freshPeople.OptionalAttendees == "B", "known people kept when item did not arrive");
            cache.Meetings[0].Organizer = "Анна <anna@example.com>";
            cache.Meetings[0].RequiredAttendees = "Боб \"шеф\"";
            cache.Meetings[0].OptionalAttendees = "Ли";
            cache.Save();
            TodayCache reloaded = TodayCache.Load(DateTime.Today, line => { });
            Check(reloaded.Meetings[0].Organizer == "Анна <anna@example.com>" && reloaded.Meetings[0].RequiredAttendees == "Боб \"шеф\"" && reloaded.Meetings[0].OptionalAttendees == "Ли" && reloaded.Meetings[1].Organizer == null, "people survive restart");
            var morning = new Meeting { Start = new DateTime(2026, 11, 16, 11, 30, 0), End = new DateTime(2026, 11, 16, 12, 0, 0) };
            Check(DescriptionForm.When(morning) == "понедельник, 16 ноября, 11:30–12:00", "card time line");
            Check(DescriptionForm.When(new Meeting { Start = new DateTime(2026, 11, 16, 23, 30, 0), End = new DateTime(2026, 11, 17, 0, 0, 0) }) == "понедельник, 16 ноября, 23:30–00:00", "card ending at midnight stays one day");
            Check(DescriptionForm.When(new Meeting { Start = new DateTime(2026, 11, 16, 23, 30, 0), End = new DateTime(2026, 11, 17, 0, 30, 0) }) == "понедельник, 16 ноября, 23:30 – вторник, 17 ноября, 00:30", "card over midnight shows end day");
            Check(DescriptionForm.When(new Meeting { Start = new DateTime(2026, 11, 16), End = new DateTime(2026, 11, 19), IsAllDay = true }) == "понедельник, 16 ноября – среда, 18 ноября, весь день", "multi-day all-day card");
            var batchInput = new Dictionary<string, Meeting>();
            for (int index = 0; index < 45; index++) batchInput["id" + index] = At(DateTime.Today, 30, "b" + index);
            List<Dictionary<string, Meeting>> batches = EwsClient.Batches(batchInput, 20);
            Check(batches.Count == 3 && batches[0].Count == 20 && batches[2].Count == 5 && batches[2].ContainsKey("id44") && EwsClient.Batches(new Dictionary<string, Meeting>(), 20).Count == 0, "invitation texts requested in batches of 20");
            var organizerOnly = At(DateTime.Today, 30, "o");
            organizerOnly.Organizer = "Я";
            Check(!organizerOnly.HasDetails, "organizer alone does not enable description");
            int hiddenPeople;
            Check(DescriptionForm.Collapse("Ян <y@example.com>", out hiddenPeople) == "Ян <y@example.com>" && hiddenPeople == 0, "short people line shown whole");
            Check(DescriptionForm.Collapse("Ян <y@ex.ru>; Ли <l@ex.ru>; Бо <b@ex.ru>; Ге <g@ex.ru>", out hiddenPeople) == "Ян <y@ex.ru>; Ли <l@ex.ru>" && hiddenPeople == 2, "whole names up to 30 chars, rest counted");
            Check(DescriptionForm.Collapse("Иван Смирнов <ivan.smirnov@example.com>; Ли <l@ex.ru>; и ещё 34", out hiddenPeople) == "Иван Смирнов <ivan.smirnov@ex…" && hiddenPeople == 35, "long first name cut, server overflow added");
            Check(DescriptionForm.Collapse("Команда платежей <payments@example.com>", out hiddenPeople).EndsWith("…") && hiddenPeople == 0, "single long name cut without count");
            string longWhere = "Переговорная 5; https://zoom.us/j/1234567890?pwd=abcdef";
            string shortWhere = DescriptionForm.Shorten(longWhere);
            Check(shortWhere == "Переговорная 5; https://zoom.…" && DescriptionForm.Shorten("Переговорная 5") == "Переговорная 5", "location shortened to 30 chars");
            List<TextLink> cutLinks = DescriptionForm.VisibleLinks(longWhere, shortWhere.Length - 1, 100);
            Check(cutLinks.Count == 1 && cutLinks[0].Start == 116 && cutLinks[0].Length == 13 && cutLinks[0].Url == "https://zoom.us/j/1234567890?pwd=abcdef", "cut link stays clickable with the full address");
            Check(DescriptionForm.VisibleLinks("Зал; " + new string('x', 30) + " https://a.example/", 29, 0).Count == 0, "link beyond the cut is not shown");
            List<TextLink> fullLinks = DescriptionForm.VisibleLinks(longWhere, longWhere.Length, 7);
            Check(fullLinks.Count == 1 && fullLinks[0].Start == 23 && fullLinks[0].Length == 39, "expanded location link covers the whole address");
            string pair = char.ConvertFromUtf32(0x1F600);
            Check(DescriptionForm.Shorten(new string('a', 28) + pair + "bc") == new string('a', 28) + "…" && DescriptionForm.Shorten(new string('a', 27) + pair + "bcd") == new string('a', 27) + pair + "…", "location cut keeps surrogate pair whole");
            Check(DescriptionForm.Shorten(new string('a', 30)) == new string('a', 30) && DescriptionForm.Shorten(new string('a', 31)) == new string('a', 29) + "…", "location limit 30/31");
            string spaced = new string('a', 28) + " https://a.example/x";
            Check(DescriptionForm.Shorten(spaced) == new string('a', 28) + "…" && DescriptionForm.VisibleLinks(spaced, 28, 0).Count == 0, "space before cut trimmed, hidden link dropped");
            string touching = new string('a', 29) + "https://a.example/x";
            Check(DescriptionForm.VisibleLinks(touching, 29, 0).Count == 0, "link starting at the cut is hidden");
            string lastChar = new string('a', 27) + " https://a.example/x";
            List<TextLink> oneChar = DescriptionForm.VisibleLinks(lastChar, DescriptionForm.Shorten(lastChar).Length - 1, 0);
            Check(DescriptionForm.Shorten(lastChar) == new string('a', 27) + " h…" && oneChar.Count == 1 && oneChar[0].Start == 28 && oneChar[0].Length == 1 && oneChar[0].Url == "https://a.example/x", "one visible char of a link opens the full address");
            string three = "https://a.b/ https://c.d/wxyz12 https://e.f/";
            List<TextLink> threeLinks = DescriptionForm.VisibleLinks(three, DescriptionForm.Shorten(three).Length - 1, 10);
            Check(DescriptionForm.Shorten(three) == "https://a.b/ https://c.d/wxyz…" && threeLinks.Count == 2 && threeLinks[0].Start == 10 && threeLinks[0].Length == 12 && threeLinks[1].Start == 23 && threeLinks[1].Length == 16 && threeLinks[1].Url == "https://c.d/wxyz12", "several links: whole, cut, hidden");
            var toggle = new MenuClickToggle();
            toggle.OnClosed(System.Windows.Forms.ToolStripDropDownCloseReason.AppClicked, "row-a", 1000);
            Check(toggle.ConsumeJustClosed("row-a", 1100, 500) && !toggle.ConsumeJustClosed("row-a", 1150, 500), "click that closed the menu on the same row does not reopen it, once");
            toggle.OnClosed(System.Windows.Forms.ToolStripDropDownCloseReason.AppClicked, "row-a", 1000);
            Check(!toggle.ConsumeJustClosed("row-b", 1100, 500), "click on another row reopens the menu");
            toggle.OnClosed(System.Windows.Forms.ToolStripDropDownCloseReason.AppClicked, "row-a", 1000);
            Check(!toggle.ConsumeJustClosed("row-a", 1600, 500), "late click reopens the menu");
            toggle.OnClosed(System.Windows.Forms.ToolStripDropDownCloseReason.ItemClicked, "row-a", 1000);
            Check(!toggle.ConsumeJustClosed("row-a", 1100, 500), "menu closed by its item does not block the next click");
            toggle.OnClosed(System.Windows.Forms.ToolStripDropDownCloseReason.AppClicked, "", int.MaxValue - 10);
            Check(toggle.ConsumeJustClosed("", int.MinValue + 50, 500), "tick counter wrap handled");
            int overflow;
            List<string> split = DescriptionForm.SplitPeople("А; Б; и ещё 7", out overflow);
            Check(split.Count == 2 && overflow == 7 && DescriptionForm.SplitPeople("и ещё кто-то", out overflow).Count == 1 && overflow == 0, "server overflow tail parsed only as number");
            Check(DescriptionForm.When(new Meeting { Start = new DateTime(2026, 11, 16), End = new DateTime(2026, 11, 17), IsAllDay = true }) == "понедельник, 16 ноября, весь день", "card all-day line");
            string smile = char.ConvertFromUtf32(0x1F600);
            var edge = At(DateTime.Today, 30, "q");
            XmlDocument edgePeople = Xml("<m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass=\"Success\"><m:Items>"
                + Item("Q", null, "", "", "", "<t:Organizer><t:Mailbox><t:Name>" + new string('x', 200) + "</t:Name><t:EmailAddress>a@example.com</t:EmailAddress></t:Mailbox></t:Organizer>"
                + "<t:RequiredAttendees><t:Attendee><t:Mailbox><t:Name>" + new string('x', 118) + smile + "tail</t:Name></t:Mailbox></t:Attendee></t:RequiredAttendees>"
                + "<t:OptionalAttendees><t:Attendee><t:Mailbox><t:EmailAddress>/O=EX/OU=X</t:EmailAddress></t:Mailbox></t:Attendee>"
                + "<t:Attendee><t:Mailbox><t:Name></t:Name><t:EmailAddress>nobody</t:EmailAddress></t:Mailbox></t:Attendee></t:OptionalAttendees>")
                + "</m:Items></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>");
            EwsClient.ApplyBodies(edgePeople, new Dictionary<string, Meeting> { { "Q", edge } });
            Check(edge.Organizer.Length == 120 && edge.Organizer.EndsWith("…"), "long person cut to 120");
            Check(edge.RequiredAttendees == new string('x', 118) + "…", "person cut keeps surrogate pair whole");
            Check(edge.OptionalAttendees == null, "only unusable mailboxes give no attendees");
            var caseless = At(DateTime.Today, 30, "r");
            EwsClient.ApplyBodies(Xml("<m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass=\"Success\"><m:Items>"
                + Item("R", null, "", "", "", "<t:Organizer><t:Mailbox><t:Name>Bob@Example.com</t:Name><t:EmailAddress>bob@example.com</t:EmailAddress></t:Mailbox></t:Organizer>")
                + "</m:Items></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>"), new Dictionary<string, Meeting> { { "R", caseless } });
            Check(caseless.Organizer == "Bob@Example.com", "name equal to address ignoring case shown once");
            Check(DescriptionForm.Collapse(new string('x', 28) + smile + "yyyyy <a@b.c>", out hiddenPeople) == new string('x', 28) + "…" && hiddenPeople == 0, "collapse keeps surrogate pair whole");
            Check(DescriptionForm.Collapse("Ян <y@ex.ru>; Ли <l@ex.ru>; и ещё 34", out hiddenPeople) == "Ян <y@ex.ru>; Ли <l@ex.ru>" && hiddenPeople == 34, "only server tail over the limit");
            var freshOrganizer = At(DateTime.Today, 30, "s");
            freshOrganizer.Organizer = "Новый";
            var staleOrganizer = At(DateTime.Today, 30, "s");
            staleOrganizer.Organizer = "Старый";
            TrayApplication.KeepKnownLinks(new List<Meeting> { freshOrganizer }, new List<Meeting> { staleOrganizer });
            Check(freshOrganizer.Organizer == "Новый", "fresh organizer is not overwritten");
        }

        private static void Mail()
        {
            Check(TrayApplication.MailText(null) == "Почта ещё не загружена" && TrayApplication.MailText(0) == "Новых писем нет", "mail text without count");
            Check(BellPainter.ColorOf(TrayState.Ready).ToArgb() == Color.FromArgb(46, 160, 67).ToArgb() && BellPainter.ColorOf(TrayState.Offline).ToArgb() == Color.FromArgb(150, 150, 150).ToArgb() && BellPainter.ColorOf(TrayState.Problem).ToArgb() == Color.FromArgb(218, 54, 51).ToArgb(), "tray colors: green, gray, red");
            Check(TrayApplication.MailText(1) == "У вас 1 новое письмо" && TrayApplication.MailText(3) == "У вас 3 новых письма" && TrayApplication.MailText(5) == "У вас 5 новых писем", "mail plural 1/3/5");
            Check(TrayApplication.MailText(11) == "У вас 11 новых писем" && TrayApplication.MailText(21) == "У вас 21 новое письмо" && TrayApplication.MailText(112) == "У вас 112 новых писем", "mail plural 11/21/112");
            Check(TrayApplication.MailText(2) == "У вас 2 новых письма" && TrayApplication.MailText(4) == "У вас 4 новых письма" && TrayApplication.MailText(22) == "У вас 22 новых письма", "mail plural 2/4/22");
            Check(TrayApplication.MailText(14) == "У вас 14 новых писем" && TrayApplication.MailText(111) == "У вас 111 новых писем", "mail plural 14/111");
            Check(TrayApplication.MailLine(4, null) == "У вас 4 новых письма" && TrayApplication.MailLine(4, "сбой") == "У вас 4 новых письма · не обновлено" && TrayApplication.MailLine(null, "нет прав") == "Почта не загружена: нет прав", "mail line shows stale and failed state");
            var full = new AppSettings { EwsUrl = "https://a.example/EWS/Exchange.asmx", VpnAdapters = new List<string> { "Wg" }, LeadMinutes = 7, CalendarMinutes = 2, MailMinutes = 3, MailUrl = "https://m.example/owa" };
            AppSettings copy = full.Clone();
            Check(copy.EwsUrl == full.EwsUrl && copy.VpnAdapters[0] == "Wg" && !ReferenceEquals(copy.VpnAdapters, full.VpnAdapters) && copy.LeadMinutes == 7 && copy.CalendarMinutes == 2 && copy.MailMinutes == 3 && copy.MailUrl == "https://m.example/owa", "clone keeps every setting");
            Check(new AppSettings { EwsUrl = "not a url" }.WebMailUrl == null && new AppSettings { EwsUrl = "not a url", MailUrl = "https://m.example/" }.WebMailUrl == "https://m.example/", "web mail with broken EWS address");
            Check(new AppSettings { EwsUrl = "https://Mail.Example.com:8443/EWS/Exchange.asmx" }.WebMailUrl == "https://mail.example.com:8443/owa/#path=/mail", "web mail keeps port");
            Check(new AppSettings { EwsUrl = "https://mail.example.com/EWS/Exchange.asmx", MailUrl = "javascript:x" }.WebMailUrl == "https://mail.example.com/owa/#path=/mail" && new AppSettings { EwsUrl = "https://mail.example.com/EWS/Exchange.asmx", MailUrl = "https://u:p@m.example/" }.WebMailUrl == "https://mail.example.com/owa/#path=/mail", "unsafe web mail falls back to derived");
            bool denied = false;
            try
            {
                EwsClient.CheckResponse(Xml("<m:GetFolderResponse><m:ResponseMessages><m:GetFolderResponseMessage ResponseClass=\"Error\"><m:MessageText>denied</m:MessageText><m:ResponseCode>ErrorAccessDenied</m:ResponseCode></m:GetFolderResponseMessage></m:ResponseMessages></m:GetFolderResponse>"));
            }
            catch (CalendarException error)
            {
                denied = !error.IsAuthentication && error.Message.Contains("ErrorAccessDenied");
            }
            Check(denied, "inbox error response reported with its code");
            Check(EwsClient.ReadUnread(Xml("<t:UnreadCount>0</t:UnreadCount>")) == 0, "zero unread parsed");
            bool negative = false;
            try
            {
                EwsClient.ReadUnread(Xml("<t:UnreadCount>-1</t:UnreadCount>"));
            }
            catch (CalendarException)
            {
                negative = true;
            }
            Check(negative, "negative unread rejected");
            File.WriteAllText(AppSettings.FilePath, "calendarMinutes=99999\nmailMinutes=-3\nmailUrl=https://u:p@host/\n");
            AppSettings bounds = AppSettings.Load();
            Check(AppSettings.MaxCalendarMinutes == 30 && AppSettings.MaxMailMinutes == 1440, "refresh limits");
            Check(bounds.CalendarMinutes == AppSettings.MaxCalendarMinutes && bounds.MailMinutes == 1 && bounds.MailUrl == "", "refresh upper and lower bounds, mail url with credentials dropped");
            File.WriteAllText(AppSettings.FilePath, "calendarMinutes=31\nmailMinutes=99999\n");
            AppSettings over = AppSettings.Load();
            Check(over.CalendarMinutes == 30 && over.MailMinutes == 1440, "calendar above 30 and mail above a day clamped");
            File.Delete(AppSettings.FilePath);
            var derived = new AppSettings { EwsUrl = "https://mail.example.com/EWS/Exchange.asmx" };
            Check(derived.WebMailUrl == "https://mail.example.com/owa/#path=/mail", "web mail derived from EWS host");
            var custom = new AppSettings { MailUrl = "https://outlook.example.org/mail" };
            Check(custom.WebMailUrl == "https://outlook.example.org/mail", "custom web mail wins");
            XmlDocument folder = Xml("<m:GetFolderResponse><m:ResponseMessages><m:GetFolderResponseMessage ResponseClass=\"Success\"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id=\"F\"/><t:UnreadCount>7</t:UnreadCount></t:Folder></m:Folders></m:GetFolderResponseMessage></m:ResponseMessages></m:GetFolderResponse>");
            Check(EwsClient.ReadUnread(EwsClient.CheckResponse(folder)) == 7, "inbox unread parsed");
            bool thrown = false;
            try
            {
                EwsClient.ReadUnread(Xml("<m:GetFolderResponse/>"));
            }
            catch (CalendarException)
            {
                thrown = true;
            }
            Check(thrown, "missing unread count is an error");
            File.WriteAllText(AppSettings.FilePath, "calendarMinutes=0\nmailMinutes=abc\nmailUrl=javascript:x\n");
            AppSettings parsed = AppSettings.Load();
            Check(parsed.CalendarMinutes == 1 && parsed.MailMinutes == AppSettings.DefaultRefreshMinutes && parsed.MailUrl == "", "refresh minutes clamped, bad mail url dropped");
            parsed.CalendarMinutes = 2;
            parsed.MailMinutes = 3;
            parsed.MailUrl = "https://m.example/owa";
            parsed.Save();
            AppSettings again = AppSettings.Load();
            Check(again.CalendarMinutes == 2 && again.MailMinutes == 3 && again.MailUrl == "https://m.example/owa", "refresh and mail settings survive");
            File.Delete(AppSettings.FilePath);
        }

        private static void Credentials()
        {
            var saved = new ExchangeCredential { UserName = "u", Password = "p" };
            CredentialChoice same = CredentialChoice.Decide(saved, "u ", "");
            Check(same.Problem == null && same.Credential.Password == "p" && !same.Changed && !same.Remove, "same login keeps saved password");
            Check(CredentialChoice.Decide(saved, "v", "").Problem != null, "new login requires a password");
            CredentialChoice cleared = CredentialChoice.Decide(saved, "", "");
            Check(cleared.Remove && !cleared.Changed && cleared.Problem == null, "empty login removes saved credential");
            CredentialChoice newPassword = CredentialChoice.Decide(saved, "u", "q");
            Check(newPassword.Changed && newPassword.Credential.Password == "q", "new password saved");
            CredentialChoice otherLogin = CredentialChoice.Decide(saved, "v", "x");
            Check(otherLogin.Changed && !otherLogin.Remove && otherLogin.Problem == null && otherLogin.Credential.UserName == "v", "new login with password saved");
            Check(!CredentialChoice.Decide(saved, "u", "p").Changed, "same login and password is not a change");
            CredentialChoice first = CredentialChoice.Decide(null, "u", "p");
            Check(first.Changed && !first.Remove, "first credential saved");
            Check(CredentialChoice.Decide(null, "u", "").Problem != null, "first login requires a password");
            Check(!CredentialChoice.Decide(null, "", "").Changed && !CredentialChoice.Decide(null, "", "").Remove, "nothing entered and nothing saved");
        }

        private static void LinkChoice()
        {
            var requested = new Dictionary<string, Meeting> { { "X", At(DateTime.Today, 30, "x") }, { "Y", At(DateTime.Today, 30, "y") } };
            XmlDocument partial = Xml("<m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass=\"Success\"><m:Items>"
                + Item("X", null, "", "", "", "<t:Body BodyType=\"Text\">Повестка https://example.com/doc</t:Body>")
                + "</m:Items></m:GetItemResponseMessage><m:GetItemResponseMessage ResponseClass=\"Error\"><m:ResponseCode>ErrorServerBusy</m:ResponseCode></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>");
            HashSet<string> read = EwsClient.ApplyBodies(partial, requested);
            Check(read.Contains("X") && requested["X"].Link == null, "read body without conference link stays without link");
            List<Meeting> unread = EwsClient.Unread(requested, read);
            Check(unread.Count == 1 && unread[0].Uid == "y", "only unread bodies are reported");
            Check(MeetingLinks.Find("https://vk.com/callcenter") == null && MeetingLinks.Find("https://vk.com/call") != null, "path entry matches whole segment");
            Check(MeetingLinks.Find("https://zoom.us@evil.example/j/1") == null, "user info cannot fake the host");
            Check(MeetingLinks.Find("https://ZOOM.US/j/1") != null, "host case ignored");
            var room = new Meeting { Uid = "r", Location = "Переговорная, https://rooms.example.com/5", Start = DateTime.Today, End = DateTime.Today.AddHours(1) };
            var conference = new Meeting { Uid = "c", Location = "https://rooms.example.com/6", Link = "https://zoom.us/j/1", Start = DateTime.Today, End = DateTime.Today.AddHours(1) };
            TrayApplication.UseLocationLinks(new List<Meeting> { room, conference });
            Check(room.Link == "https://rooms.example.com/5" && conference.Link == "https://zoom.us/j/1", "location address is a fallback after conference links");
            Check(MeetingLinks.JoinText(room.Link) == "Подключиться (rooms.example.com)" && MeetingLinks.JoinText(conference.Link) == "Подключиться", "non-conference link shows its host");
            Check(MeetingLinks.Find("https://a:b@zoom.us/j/1 https://zoom.us/j/2") == "https://zoom.us/j/2", "conference link with user info skipped");
            var unreadRoom = new Meeting { Uid = "m", Location = "https://rooms.example.com/5", Start = DateTime.Today, End = DateTime.Today.AddHours(1) };
            var knownRoom = new Meeting { Uid = "m", Start = DateTime.Today, End = DateTime.Today.AddHours(1), Link = "https://zoom.us/j/1" };
            TrayApplication.MergeLinks(new List<Meeting> { unreadRoom }, new List<Meeting> { unreadRoom }, new List<Meeting> { knownRoom });
            Check(unreadRoom.Link == "https://zoom.us/j/1", "known conference link wins over location fallback");
            var bare = At(DateTime.Today, 30, "b");
            TrayApplication.UseLocationLinks(new List<Meeting> { bare });
            Check(bare.Link == null, "meeting without location keeps no link");
            Check(MeetingLinks.AnyWebLink("https://u:p@a.example/ https://b.example/") == "https://b.example/", "fallback skips link with credentials");
            List<TextLink> found = MeetingLinks.WebLinks("Док: https://wiki.example.com/a?b=1.\nmailto:x@example.com file://c/d https://u:p@e.example/ (http://f.example/g)");
            Check(found.Count == 2 && found[0].Url == "https://wiki.example.com/a?b=1" && found[0].Start == 5 && found[0].Length == 30 && found[1].Url == "http://f.example/g", "description links: only http(s) without login, trailing dot and bracket cut");
            Check(found[0].Contains(5) && found[0].Contains(34) && !found[0].Contains(35) && !found[0].Contains(4), "link range bounds");
            Check(MeetingLinks.WebLinks(null).Count == 0 && MeetingLinks.WebLinks("нет ссылок").Count == 0, "no links in empty text");
            List<TextLink> quoted = MeetingLinks.WebLinks("См. «https://wiki.example.com/a» и https://b.example/c… и https://evil.example/" + (char)0x202E + "moc.knab");
            Check(quoted.Count == 3 && quoted[0].Url == "https://wiki.example.com/a" && quoted[1].Url == "https://b.example/c" && quoted[2].Url == "https://evil.example/", "link stops at russian quotes, ellipsis and direction marks");
            List<TextLink> marks = MeetingLinks.WebLinks("“https://a.example/x” „https://d.example/w” https://b.example/y" + (char)0x200B + "z https://c.example/" + (char)0x2067 + "q https://e.example/" + (char)0x200F + "r");
            Check(marks.Count == 5 && marks[0].Url == "https://a.example/x" && marks[1].Url == "https://d.example/w" && marks[2].Url == "https://b.example/y" && marks[3].Url == "https://c.example/" && marks[4].Url == "https://e.example/", "link stops at typographic quotes, zero width and isolate marks");
            Check(MeetingLinks.DisplayHost("https://rooms.example.com/5") == "rooms.example.com", "ascii host shown as is");
            var again = new Dictionary<string, Meeting> { { "X", At(DateTime.Today, 30, "x") }, { "Y", At(DateTime.Today, 30, "y") } };
            Meeting oldX = At(DateTime.Today, 30, "x");
            oldX.Link = "https://zoom.us/j/x";
            Meeting oldY = At(DateTime.Today, 30, "y");
            oldY.Link = "https://zoom.us/j/y";
            TrayApplication.KeepKnownLinks(EwsClient.Unread(again, EwsClient.ApplyBodies(partial, again)), new List<Meeting> { oldX, oldY });
            Check(again["X"].Link == null && again["Y"].Link == "https://zoom.us/j/y", "known link returns only to meetings whose body did not arrive");
            Check(MeetingLinks.JoinText("https://zооm.us/j/1").StartsWith("Подключиться (xn--") && MeetingLinks.DisplayHost("https://zооm.us/j/1").StartsWith("xn--"), "homoglyph host shown as punycode");
        }

        private static void Links()
        {
            Check(MeetingLinks.Find("Zoom https://us02web.zoom.us/j/123?pwd=abc).") == "https://us02web.zoom.us/j/123?pwd=abc", "zoom link from location without trailing punctuation");
            Check(MeetingLinks.Find("Переговорная 5") == null, "no link in a room name");
            Check(MeetingLinks.Find("см. https://example.com/room") == null, "non-conference link in location is not a meeting link");
            Check(MeetingLinks.Find("Agenda https://example.com/doc and https://telemost.yandex.ru/j/42 join") == "https://telemost.yandex.ru/j/42", "conference link from body skips other links");
            Check(MeetingLinks.Find("Agenda https://example.com/doc") == null, "body without conference link");
            Check(MeetingLinks.Find("<https://teams.microsoft.com/l/meetup-join/19%3a>") == "https://teams.microsoft.com/l/meetup-join/19%3a", "teams link in angle brackets");
            Check(!MeetingLinks.IsWebLink("file:///C:/Windows/System32/calc.exe"), "file link rejected");
            Check(!MeetingLinks.IsWebLink("javascript:alert(1)"), "javascript link rejected");
            Check(!MeetingLinks.IsWebLink("https://user:pass@example.com/"), "link with credentials rejected");
            Check(MeetingLinks.IsWebLink("https://zoom.us/j/1"), "https link accepted");
            Check(MeetingLinks.Open("ms-settings:") != null, "non-web link is not opened");
        }

        private static void Preferences()
        {
            MeetingPreferences preferences = EmptyPreferences();
            DateTime today = new DateTime(2026, 11, 16);
            Check(preferences.Set("uid-1", 10, today) == null, "preference saved");
            Check(preferences.Set("uid-2", MeetingPreferences.Off, today) == null, "off saved");
            MeetingPreferences reloaded = MeetingPreferences.Load(today);
            int lead;
            Check(reloaded.TryGetLead("uid-1", out lead) && lead == 10, "custom lead survives reload");
            Check(reloaded.TryGetLead("uid-2", out lead) && lead == MeetingPreferences.Off, "off survives reload");
            Check(reloaded.Count == 2 && reloaded.OffCount == 1, "counts");
            reloaded.MarkSeen(new List<Meeting>(), today.AddDays(35));
            Check(MeetingPreferences.Load(today.AddDays(35)).TryGetLead("uid-2", out lead), "monthly series keeps its preference after 35 days");
            reloaded.MarkSeen(new List<Meeting> { At(today.AddDays(101).AddHours(9), 30, "uid-1") }, today.AddDays(101));
            MeetingPreferences later = MeetingPreferences.Load(today.AddDays(101));
            Check(later.TryGetLead("uid-1", out lead) && !later.TryGetLead("uid-2", out lead), "unseen for 100 days is forgotten, seen kept");
            Check(later.Reset("uid-1") == null && !MeetingPreferences.Load(today).TryGetLead("uid-1", out lead), "reset removes preference");
            File.WriteAllText(MeetingPreferences.FilePath, "{ broken");
            MeetingPreferences broken = MeetingPreferences.Load(today);
            Check(broken.LoadProblem != null && File.Exists(MeetingPreferences.FilePath + ".broken"), "broken file set aside");
            Check(broken.Set("uid-3", 5, today) == null, "after broken file new preferences are saved");
            File.WriteAllText(MeetingPreferences.FilePath, "{\"x\": {\"lead\": 999}, \"y\": {\"lead\": 15, \"seen\": \"2026-11-16\"}}");
            MeetingPreferences ranged = MeetingPreferences.Load(today);
            Check(!ranged.TryGetLead("x", out lead) && ranged.TryGetLead("y", out lead) && lead == 15, "out of range lead ignored");
            using (new FileStream(MeetingPreferences.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                MeetingPreferences locked = MeetingPreferences.Load(today);
                Check(locked.LoadProblem != null && locked.Set("z", 5, today) != null, "unreadable file is not overwritten");
            }
        }

        private static void Cache()
        {
            DateTime today = DateTime.Today;
            var cache = new TodayCache { Day = today, Fetched = today.AddHours(9) };
            cache.Meetings.Add(new Meeting { Uid = "u", Subject = "Тема \"в кавычках\"", Start = today.AddHours(10), End = today.AddHours(11), Location = "Комната", Link = "https://zoom.us/j/1" });
            cache.Meetings.Add(new Meeting { Uid = "v", Subject = "", Start = today, End = today.AddDays(1), IsAllDay = true, Link = "javascript:x" });
            cache.Shown.Add("u|key");
            Check(cache.Save() == null, "cache saved");
            TodayCache loaded = TodayCache.Load(today, line => { });
            Check(loaded.Meetings.Count == 2 && loaded.Meetings[0].Subject == "Тема \"в кавычках\"" && loaded.Meetings[0].Start == today.AddHours(10), "cache meetings survive");
            Check(loaded.Meetings[1].IsAllDay && loaded.Meetings[1].Link == null, "all-day flag kept, unsafe link dropped");
            Check(loaded.Shown.Contains("u|key") && loaded.Fetched == today.AddHours(9), "shown keys and fetch time survive");
            TodayCache tomorrow = TodayCache.Load(today.AddDays(1), line => { });
            Check(tomorrow.Meetings.Count == 0 && !tomorrow.Fetched.HasValue, "cache of another day ignored");
            File.WriteAllText(TodayCache.FilePath, "not json");
            Check(TodayCache.Load(today, line => { }).Meetings.Count == 0, "broken cache read as empty");
        }

        private static void Settings()
        {
            File.WriteAllText(AppSettings.FilePath, "ewsUrl=https://mail.example.org/EWS/Exchange.asmx\nvpnAdapters=Fortinet | WireGuard|fortinet\nleadMinutes=500\nunknown=1\n");
            AppSettings loaded = AppSettings.Load();
            Check(loaded.EwsUrl == "https://mail.example.org/EWS/Exchange.asmx", "ews url read");
            Check(loaded.VpnAdapters.Count == 2 && loaded.VpnAdapters[1] == "WireGuard", "adapters parsed without duplicates");
            Check(loaded.LeadMinutes == AppSettings.MaxLeadMinutes, "lead clamped");
            Check(loaded.CalendarMinutes == 5 && loaded.MailMinutes == 5 && loaded.MailUrl == "", "old settings file gets polling defaults");
            loaded.VpnAdapters.Clear();
            loaded.LeadMinutes = 0;
            Check(loaded.Save() == null, "settings saved");
            AppSettings again = AppSettings.Load();
            Check(!again.ChecksVpn && again.LeadMinutes == 0, "empty adapter list and zero lead survive");
            File.Delete(AppSettings.FilePath);
            AppSettings defaults = AppSettings.Load();
            Check(defaults.LeadMinutes == 1 && defaults.VpnAdapters.Count == 1 && defaults.EwsUrl.Contains("example.com"), "neutral defaults");
            Check(defaults.CalendarMinutes == 5 && defaults.MailMinutes == 5 && defaults.MailUrl == "", "polling defaults without file");
        }

        private static void Words()
        {
            Check(ReminderForm.Minutes(1) == "1 минуту", "1 минуту");
            Check(ReminderForm.Minutes(3) == "3 минуты", "3 минуты");
            Check(ReminderForm.Minutes(5) == "5 минут", "5 минут");
            Check(ReminderForm.Minutes(11) == "11 минут", "11 минут");
            Check(ReminderForm.Minutes(21) == "21 минуту", "21 минуту");
            var start = new DateTime(2026, 11, 16, 14, 0, 0);
            Meeting meeting = At(start, 30, "w");
            Check(ReminderForm.Timing(meeting, start.AddMinutes(-1)) == "14:00–14:30 · через 1 минуту", "timing before");
            Check(ReminderForm.Timing(meeting, start) == "14:00–14:30 · начинается сейчас", "timing at start");
            Check(ReminderForm.Timing(meeting, start.AddMinutes(3)) == "14:00–14:30 · идёт 3 минуты", "timing after");
        }

        private const string Envelope = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:m=\"http://schemas.microsoft.com/exchange/services/2006/messages\" xmlns:t=\"http://schemas.microsoft.com/exchange/services/2006/types\"><s:Body>{0}</s:Body></s:Envelope>";

        private static XmlDocument Xml(string body)
        {
            var document = new XmlDocument();
            document.LoadXml(string.Format(Envelope, body));
            return document;
        }

        private static string Item(string id, string uid, string subject, string start, string end, string extra)
        {
            return "<t:CalendarItem><t:ItemId Id=\"" + id + "\" ChangeKey=\"c\"/><t:Subject>" + subject + "</t:Subject><t:Start>" + start + "</t:Start><t:End>" + end + "</t:End>"
                + (uid == null ? "" : "<t:UID>" + uid + "</t:UID>") + extra + "</t:CalendarItem>";
        }

        private static void EwsParsing()
        {
            string items = Item("A", "uid-late", "Поздняя", "2026-11-16T15:00:00Z", "2026-11-16T15:30:00Z", "<t:Location>https://zoom.us/j/9</t:Location>")
                + Item("B", "uid-cancel", "Отменена", "2026-11-16T09:00:00Z", "2026-11-16T10:00:00Z", "<t:IsCancelled>true</t:IsCancelled>")
                + Item("C", "uid-decline", "Отклонена", "2026-11-16T09:00:00Z", "2026-11-16T10:00:00Z", "<t:MyResponseType>Decline</t:MyResponseType>")
                + Item("X", null, "Без UID", "2026-11-16T11:00:00Z", "2026-11-16T11:30:00Z", "<t:Location>Переговорная</t:Location>")
                + Item("D", "uid-day", "Весь день", "2026-11-15T21:00:00Z", "2026-11-16T21:00:00Z", "<t:IsAllDayEvent>true</t:IsAllDayEvent>");
            XmlDocument found = Xml("<m:FindItemResponse><m:ResponseMessages><m:FindItemResponseMessage ResponseClass=\"Success\"><m:ResponseCode>NoError</m:ResponseCode><m:RootFolder><t:Items>"
                + items + "</t:Items></m:RootFolder></m:FindItemResponseMessage></m:ResponseMessages></m:FindItemResponse>");
            var withoutLinks = new Dictionary<string, Meeting>();
            List<Meeting> meetings = EwsClient.ReadCalendarItems(EwsClient.CheckResponse(found), withoutLinks);
            Check(meetings.Count == 3, "cancelled and declined meetings dropped");
            Check(meetings.Count == 3 && meetings[0].IsAllDay && meetings[1].Uid == "X" && meetings[2].Uid == "uid-late", "sorted by start, item id replaces missing uid");
            Check(meetings.Count == 3 && meetings[1].Start == new DateTime(2026, 11, 16, 11, 0, 0, DateTimeKind.Utc).ToLocalTime(), "utc start converted to local time");
            Check(meetings.Count == 3 && meetings[2].Link == "https://zoom.us/j/9" && meetings[1].Link == null, "link from location");
            Check(withoutLinks.ContainsKey("X") && withoutLinks.ContainsKey("A"), "every meeting goes to body fetch for its description");
            XmlDocument bodies = Xml("<m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass=\"Error\"><m:ResponseCode>ErrorItemNotFound</m:ResponseCode></m:GetItemResponseMessage>"
                + "<m:GetItemResponseMessage ResponseClass=\"Success\"><m:Items>" + Item("X", null, "", "", "", "<t:Body BodyType=\"Text\">Join https://example.com/doc https://zoom.us/j/1</t:Body>")
                + "</m:Items></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>");
            HashSet<string> read = EwsClient.ApplyBodies(bodies, withoutLinks);
            Check(meetings.Count == 3 && meetings[1].Link == "https://zoom.us/j/1", "conference link from body applied by item id despite another item error");
            Check(read.Count == 1 && read.Contains("X"), "read bodies reported by item id");
            XmlDocument busy = Xml("<m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass=\"Error\"><m:ResponseCode>ErrorServerBusy</m:ResponseCode></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>");
            Check(EwsClient.ApplyBodies(busy, withoutLinks).Count == 0, "server refusing every body reports nothing read");
            XmlDocument failed = Xml("<m:FindItemResponse><m:ResponseMessages><m:FindItemResponseMessage ResponseClass=\"Error\"><m:MessageText>denied</m:MessageText><m:ResponseCode>ErrorAccessDenied</m:ResponseCode></m:FindItemResponseMessage></m:ResponseMessages></m:FindItemResponse>");
            bool thrown = false;
            try
            {
                EwsClient.CheckResponse(failed);
            }
            catch (CalendarException error)
            {
                thrown = !error.IsAuthentication && error.Message.Contains("ErrorAccessDenied");
            }
            Check(thrown, "error response message raises calendar error");
        }

        private static void MovedAndSeries()
        {
            var start = new DateTime(2026, 11, 16, 14, 0, 0);
            MeetingPreferences preferences = EmptyPreferences();
            var planner = new ReminderPlanner();
            var shown = new HashSet<string> { At(start, 30, "u").ReminderKey };
            Check(planner.Due(new List<Meeting> { At(start.AddMinutes(30), 30, "u") }, start.AddMinutes(29), 1, preferences, shown).Count == 1, "moved meeting reminds again");
            Check(planner.Due(new List<Meeting> { At(start, 30, "u") }, start.AddSeconds(-30), 1, preferences, shown).Count == 0, "same start is not repeated");
            preferences.Set("s", 5, DateTime.Today);
            var series = new List<Meeting> { At(start.AddHours(-4), 30, "s"), At(start.AddHours(2), 30, "s") };
            var seriesShown = new HashSet<string> { series[0].ReminderKey };
            List<Meeting> due = planner.Due(series, start.AddHours(2).AddMinutes(-5), 1, preferences, seriesShown);
            Check(due.Count == 1 && due[0].Start == start.AddHours(2), "second meeting of a series reminds with series lead");
            Meeting snoozed = At(start, 30, "f");
            planner.Snooze(snoozed, start.AddMinutes(1), new HashSet<string>());
            planner.Forget(snoozed);
            Check(planner.Due(new List<Meeting> { snoozed }, start.AddMinutes(1), 1, preferences, new HashSet<string> { snoozed.ReminderKey }).Count == 0, "forgotten snooze does not fire");
        }

        private static void KnownLinks()
        {
            var start = new DateTime(2026, 11, 16, 14, 0, 0);
            Meeting fresh = At(start, 30, "u");
            Meeting own = At(start, 30, "v");
            own.Link = "https://zoom.us/j/own";
            Meeting moved = At(start.AddHours(1), 30, "w");
            Meeting oldU = At(start, 30, "u");
            oldU.Link = "https://zoom.us/j/1";
            Meeting oldV = At(start, 30, "v");
            oldV.Link = "https://zoom.us/j/old";
            Meeting oldW = At(start, 30, "w");
            oldW.Link = "https://zoom.us/j/w";
            TrayApplication.KeepKnownLinks(new List<Meeting> { fresh, own, moved }, new List<Meeting> { oldU, oldV, oldW });
            Check(fresh.Link == "https://zoom.us/j/1", "known link kept when body fetch failed");
            Check(own.Link == "https://zoom.us/j/own", "fresh link is not overwritten");
            Check(moved.Link == null, "link of another start is not carried");
            Check(MeetingLinks.Find("https://zoom.us.evil.example/j/1 https://teams.microsoft.com/l/x") == "https://teams.microsoft.com/l/x", "look-alike host skipped");
            Check(MeetingLinks.Find("https://evil.example/a.zoom.us") == null, "conference name in path is not a conference");
            Check(MeetingLinks.Find("https://vk.com/call/join/1") != null && MeetingLinks.Find("https://vk.com/wall1") == null, "host with path entry");
            Check(MeetingLinks.Find("https://company.trueconf.ru/c/1") != null, "subdomain of conference host");
        }

        private static void Boundaries()
        {
            DateTime today = new DateTime(2026, 11, 16);
            MeetingPreferences preferences = EmptyPreferences();
            preferences.Set("edge", 5, today);
            preferences.MarkSeen(new List<Meeting>(), today.AddDays(100));
            int lead;
            Check(MeetingPreferences.Load(today.AddDays(100)).TryGetLead("edge", out lead), "kept on day 100");
            File.WriteAllText(AppSettings.FilePath, "LEADMINUTES=5\newsUrl=\n");
            AppSettings upper = AppSettings.Load();
            Check(upper.LeadMinutes == 5 && upper.EwsUrl == AppSettings.DefaultEwsUrl, "key case ignored, empty url is default");
            File.WriteAllText(AppSettings.FilePath, "leadMinutes=abc\n");
            Check(AppSettings.Load().LeadMinutes == AppSettings.DefaultLeadMinutes, "garbage lead is default");
            File.WriteAllText(AppSettings.FilePath, "leadMinutes=-5\n");
            Check(AppSettings.Load().LeadMinutes == 0, "negative lead clamped to zero");
            File.Delete(AppSettings.FilePath);
            var update = new AppSettings { LeadMinutes = 7, EwsUrl = "https://old.example/EWS/Exchange.asmx" };
            Check(update.ApplyInstallFields("https://a.example/EWS/Exchange.asmx", "Wg", false) == null && update.LeadMinutes == 7 && update.EwsUrl == "https://a.example/EWS/Exchange.asmx" && update.VpnAdapters[0] == "Wg", "installer fields applied, lead kept");
            Check(update.ApplyInstallFields("http://x", "", false) != null && update.EwsUrl == "https://a.example/EWS/Exchange.asmx" && !update.ChecksVpn, "bad url rejected, empty adapter means no check");
            var unread = new AppSettings { EwsUrl = "https://kept.example/EWS/Exchange.asmx", VpnAdapters = new List<string> { "Kept" } };
            Check(unread.ApplyInstallFields("", "", true) == null && unread.EwsUrl == "https://kept.example/EWS/Exchange.asmx" && unread.VpnAdapters[0] == "Kept", "unread fields keep saved values");
            var start = new DateTime(2026, 11, 16, 14, 0, 0);
            Check(ReminderForm.Timing(At(start, 30, "t"), start.AddSeconds(-30)) == "14:00–14:30 · через 1 минуту", "half a minute before rounds up");
        }

        private static void Urls()
        {
            Check(EwsClient.CheckUrl("https://mail.example.com/EWS/Exchange.asmx") == null, "https url accepted");
            Check(EwsClient.CheckUrl("http://mail.example.com/EWS/Exchange.asmx") != null, "http rejected");
            Check(EwsClient.CheckUrl("https://u:p@mail.example.com/EWS") != null, "credentials in url rejected");
            Check(EwsClient.CheckUrl("https://mail.example.com/EWS?x=1") != null, "query rejected");
            Check(EwsClient.CheckUrl("mail") != null, "garbage rejected");
        }

        private static int Count(string people)
        {
            return people == null ? 0 : people.Split(';').Length;
        }

        private static int Live(string[] args)
        {
            string url = Environment.GetEnvironmentVariable("EXCHANGE_EWS_URL");
            var credential = new ExchangeCredential
            {
                UserName = Environment.GetEnvironmentVariable("EXCHANGE_USERNAME"),
                Password = Environment.GetEnvironmentVariable("EXCHANGE_PASSWORD")
            };
            DateTime day = args.Length > 1 ? DateTime.Parse(args[1]) : DateTime.Today;
            try
            {
                var client = new EwsClient(url, credential);
                List<Meeting> meetings = client.Day(day);
                Console.WriteLine("meetings: " + meetings.Count + ", bodies problem: " + (client.BodiesProblem ?? "none"));
                foreach (Meeting meeting in meetings)
                {
                    Console.WriteLine(meeting.Start.ToString("HH:mm") + "-" + meeting.End.ToString("HH:mm") + " allDay=" + meeting.IsAllDay
                        + " uid=" + (meeting.Uid ?? "").Length + " link=" + (meeting.Link == null ? "none" : new Uri(meeting.Link).Host)
                        + " location=" + (string.IsNullOrEmpty(meeting.Location) ? "empty" : "set") + " description=" + (meeting.Description == null ? 0 : meeting.Description.Length)
                        + " organizer=" + (meeting.Organizer == null ? "none" : "set") + " required=" + Count(meeting.RequiredAttendees) + " optional=" + Count(meeting.OptionalAttendees));
                }
            }
            catch (CalendarException error)
            {
                Console.WriteLine("error: " + error.Message + " auth=" + error.IsAuthentication);
                return 1;
            }
            return 0;
        }
    }
}
