using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MeetingBell
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private const string AppTitle = AppIdentity.Name;
        private const int MaxTrayTextLength = 63;
        private const int TickMs = 15 * 1000;
        private const int ReminderGap = 8;
        private static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(1);
        private static readonly int[] LeadChoices = { 0, 1, 5, 10, 15, 30 };
        private static readonly string ReminderSound = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Windows Notify Calendar.wav");

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly Control invoker = new Control();
        private readonly System.Windows.Forms.Timer tickTimer = new System.Windows.Forms.Timer();
        private readonly MeetingListForm listForm = new MeetingListForm();
        private readonly ContextMenuStrip meetingMenu = new ContextMenuStrip();
        private const string ListMenuKey = "list";
        private readonly MenuClickToggle trayMenuToggle = new MenuClickToggle();
        private readonly MenuClickToggle meetingMenuToggle = new MenuClickToggle();
        private readonly ReminderPlanner planner = new ReminderPlanner();
        private readonly List<ReminderForm> reminders = new List<ReminderForm>();
        private readonly Dictionary<string, DescriptionForm> descriptions = new Dictionary<string, DescriptionForm>();
        private AppSettings settings;
        private ExchangeCredential credential;
        private string credentialProblem;
        private MeetingPreferences preferences;
        private TodayCache cache;
        private SettingsForm settingsForm;
        private TrayState shownState = (TrayState)(-1);
        private Icon currentIcon;
        private bool? vpnUp;
        private bool fetching;
        private bool refreshPending;
        private int listOpens;
        private int networkTickQueued;
        private bool exiting;
        private int fetchGeneration;
        private string lastError;
        private bool authenticationFailed;
        private int loggedMeetingCount = -1;
        private DateTime nextRefresh = DateTime.MinValue;
        private DateTime nextMailRefresh = DateTime.MinValue;
        private bool mailFetching;
        private bool mailRefreshPending;
        private bool accessConfirmed;
        private const int RetryMinutes = 5;
        private int mailGeneration;
        private int? mailUnread;
        private string mailError;
        private Point listAnchor;
        private DateTime viewDay;
        private readonly Dictionary<DateTime, OtherDay> otherDays = new Dictionary<DateTime, OtherDay>();
        private int otherDaysGeneration;
        private bool otherDayLoading;
        private bool updateBalloonShown;

        public TrayApplication(AppSettings loaded)
        {
            settings = loaded;
            bool firstRun = !AppSettings.FileExists;
            if (firstRun)
            {
                string saveProblem = settings.Save();
                if (saveProblem != null) AppLog.Append("settings not saved: " + saveProblem);
            }
            credentialProblem = CredentialStore.TryRead(out credential);
            if (credentialProblem != null) AppLog.Append("credential not read: " + credentialProblem);
            DateTime now = DateTime.Now;
            preferences = MeetingPreferences.Load(now);
            if (preferences.LoadProblem != null) AppLog.Append("meeting preferences: " + preferences.LoadProblem);
            cache = TodayCache.Load(now, AppLog.Append);
            AppLog.Append("settings: first run=" + firstRun + ", ews host=" + EwsHost(settings.EwsUrl)
                + ", vpn adapters=" + (settings.ChecksVpn ? AppSettings.JoinList(settings.VpnAdapters) : "not checked")
                + ", lead=" + settings.LeadMinutes + ", credential=" + (HasCredential ? "saved" : "none")
                + ", meeting preferences=" + preferences.Count + ", cached meetings=" + cache.Meetings.Count);

            invoker.CreateControl();
            trayIcon.ContextMenuStrip = new ContextMenuStrip();
            trayIcon.ContextMenuStrip.Opening += OnMenuOpening;
            trayIcon.MouseClick += OnTrayMouseClick;
            trayIcon.MouseDoubleClick += OnTrayDoubleClick;
            trayIcon.BalloonTipClicked += OnBalloonClicked;
            listForm.MeetingClicked += OnMeetingClicked;
            listForm.StatusClicked += OnStatusClicked;
            listForm.MailClicked += OnMailClicked;
            listForm.MenuRequested += OnListMenuRequested;
            listForm.DayStepRequested += OnDayStep;
            listForm.Pressed += (button, rowKey) =>
            {
                int pressedAt = Environment.TickCount;
                trayMenuToggle.PressStarted(button == MouseButtons.Right ? ListMenuKey : null, pressedAt);
                meetingMenuToggle.PressStarted(button == MouseButtons.Left ? rowKey : null, pressedAt);
            };
            meetingMenu.Closed += (sender, e) =>
            {
                meetingMenuToggle.OnClosed(e.CloseReason, Environment.TickCount);
                listForm.ReleaseHeldRow();
            };
            trayIcon.ContextMenuStrip.Closed += (sender, e) => trayMenuToggle.OnClosed(e.CloseReason, Environment.TickCount);
            tickTimer.Interval = TickMs;
            tickTimer.Tick += (sender, e) => Tick();
            UpdateTray();
            trayIcon.Visible = true;

            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnTimeChanged;
            tickTimer.Start();
            Tick();
            UpdateUsageState();
            Usage.Start();
            UpdateWatcher.Start(version => Post(() => ShowUpdateBalloon(version)));
            if (!HasCredential) Post(() => ShowSettings("Чтобы " + AppIdentity.Name + " видел созвоны, введи адрес EWS, логин и пароль Exchange."));
        }

        private bool HasCredential
        {
            get { return credential != null && credential.IsComplete; }
        }

        private void Post(Action action)
        {
            try
            {
                invoker.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            FlushListOpens();
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
            tickTimer.Dispose();
            if (settingsForm != null) settingsForm.Close();
            foreach (ReminderForm reminder in reminders.ToArray()) reminder.Close();
            foreach (DescriptionForm description in new List<DescriptionForm>(descriptions.Values)) description.Close();
            listForm.Dispose();
            meetingMenu.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            invoker.Dispose();
            base.ExitThreadCore();
        }

        private void OnNetworkChanged(object sender, EventArgs e)
        {
            PostNetworkTick();
        }

        private void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            PostNetworkTick();
        }

        private void PostNetworkTick()
        {
            if (Interlocked.Exchange(ref networkTickQueued, 1) == 1) return;
            Post(() =>
            {
                Interlocked.Exchange(ref networkTickQueued, 0);
                Tick();
            });
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            Post(() =>
            {
                AppLog.Append("resume");
                RequestRefresh();
                Tick();
            });
        }

        private void OnTimeChanged(object sender, EventArgs e)
        {
            Post(() =>
            {
                TimeZoneInfo.ClearCachedData();
                RequestRefresh();
                Tick();
            });
        }

        private void Tick()
        {
            DateTime now = DateTime.Now;
            if (cache.Day != now.Date)
            {
                AppLog.Append("new day " + now.ToString("yyyy-MM-dd"));
                HideList();
                ForgetOtherDays();
                cache = new TodayCache { Day = now.Date };
                viewDay = cache.Day;
                SaveCache();
                RequestRefresh();
                loggedMeetingCount = -1;
            }
            bool vpn = VpnDetector.IsUp(settings.VpnAdapters);
            if (vpnUp != vpn)
            {
                AppLog.Append(settings.ChecksVpn ? "vpn " + (vpn ? "up" : "down") : "vpn not checked");
                if (vpn) RequestRefresh();
                vpnUp = vpn;
            }
            if (vpn && HasCredential && !authenticationFailed && !fetching && !mailFetching && !otherDayLoading && now >= nextRefresh) StartFetch(now);
            if (vpn && HasCredential && !authenticationFailed && accessConfirmed && !fetching && !mailFetching && !otherDayLoading && now >= nextMailRefresh) StartMailFetch();
            PumpOtherDays();
            ShowDueReminders(now);
            UpdateTray();
            if (listForm.Visible) PopulateList();
            FlushListOpens();
        }

        private void FlushListOpens()
        {
            if (listOpens == 0) return;
            Usage.Add("list.open", listOpens);
            listOpens = 0;
        }

        private void StartFetch(DateTime now)
        {
            fetching = true;
            int generation = ++fetchGeneration;
            string url = settings.EwsUrl;
            ExchangeCredential used = credential;
            DateTime day = now.Date;
            ThreadPool.QueueUserWorkItem(state =>
            {
                List<Meeting> meetings = null;
                string error = null;
                bool authentication = false;
                List<Meeting> unreadBodies = new List<Meeting>();
                try
                {
                    var client = new EwsClient(url, used);
                    meetings = client.Day(day);
                    unreadBodies = client.UnreadBodies;
                    if (client.BodiesProblem != null) AppLog.Append("invitation texts not loaded: " + client.BodiesProblem);
                }
                catch (CalendarException failure)
                {
                    error = failure.Message;
                    authentication = failure.IsAuthentication;
                }
                catch (Exception failure)
                {
                    AppLog.Append("calendar fetch failed: " + failure);
                    error = "внутренняя ошибка: " + failure.Message;
                }
                Post(() => OnFetched(generation, day, meetings, error, authentication, unreadBodies));
            });
        }

        private void OnFetched(int generation, DateTime day, List<Meeting> meetings, string error, bool authentication, List<Meeting> unreadBodies)
        {
            if (generation != fetchGeneration) return;
            fetching = false;
            Post(PumpOtherDays);
            DateTime now = DateTime.Now;
            bool again = refreshPending || day != cache.Day;
            refreshPending = false;
            nextRefresh = again ? DateTime.MinValue : now.AddMinutes(error == null ? settings.CalendarMinutes : Math.Min(settings.CalendarMinutes, RetryMinutes));
            if (again) Post(Tick);
            if (day != cache.Day) return;
            if (error != null)
            {
                if (error != lastError)
                {
                    AppLog.Append("calendar error: " + error);
                    Usage.Hit("calendar.error");
                }
                if (authentication || !authenticationFailed) lastError = error;
                if (authentication)
                {
                    authenticationFailed = true;
                    AppLog.Append("calendar polling paused until credentials change");
                }
                UpdateTray();
                if (listForm.Visible) PopulateList();
                return;
            }
            if (lastError != null && !authenticationFailed) AppLog.Append("calendar ok again");
            if (!authenticationFailed) lastError = null;
            if (!accessConfirmed)
            {
                accessConfirmed = true;
                nextMailRefresh = DateTime.MinValue;
                Post(Tick);
            }
            MergeLinks(meetings, unreadBodies, cache.Meetings);
            List<Meeting> added = NewMeetingFinder.Find(cache.Fetched, cache.Meetings, meetings, now);
            cache.Meetings = meetings;
            cache.Fetched = now;
            SaveCache();
            if (meetings.Count != loggedMeetingCount)
            {
                AppLog.Append("calendar: " + meetings.Count + " meetings today");
                loggedMeetingCount = meetings.Count;
            }
            string seenProblem = preferences.MarkSeen(meetings, now);
            if (seenProblem != null) AppLog.Append("meeting preferences not saved: " + seenProblem);
            ShowDueReminders(now);
            ShowNewMeetings(added);
            UpdateTray();
            UpdateUsageState();
            if (listForm.Visible)
            {
                LoadNeighbors(false);
                PopulateList();
            }
        }

        private void ShowNewMeetings(List<Meeting> added)
        {
            foreach (Meeting meeting in added)
            {
                if (preferences.EffectiveLead(meeting, settings.LeadMinutes) == MeetingPreferences.Off) continue;
                if (reminders.Exists(open => open.Meeting.ReminderKey == meeting.ReminderKey)) continue;
                ShowReminder(meeting, true);
                AppLog.Append("new meeting notice: meeting at " + meeting.Start.ToString("HH:mm") + ", link " + (meeting.Link != null ? "yes" : "no"));
                Usage.Hit("notice.show");
            }
        }

        internal static void KeepKnownLinks(List<Meeting> fresh, List<Meeting> previous)
        {
            foreach (Meeting meeting in fresh)
            {
                Meeting known = previous.Find(old => old.ReminderKey == meeting.ReminderKey);
                if (known == null) continue;
                if (meeting.Link == null) meeting.Link = known.Link;
                if (meeting.Description == null) meeting.Description = known.Description;
                if (meeting.Organizer == null) meeting.Organizer = known.Organizer;
                if (meeting.RequiredAttendees == null) meeting.RequiredAttendees = known.RequiredAttendees;
                if (meeting.OptionalAttendees == null) meeting.OptionalAttendees = known.OptionalAttendees;
            }
        }

        internal static void MergeLinks(List<Meeting> meetings, List<Meeting> unread, List<Meeting> previous)
        {
            KeepKnownLinks(unread, previous);
            UseLocationLinks(meetings);
        }

        internal static void UseLocationLinks(List<Meeting> meetings)
        {
            foreach (Meeting meeting in meetings)
            {
                if (meeting.Link == null) meeting.Link = MeetingLinks.AnyWebLink(meeting.Location);
            }
        }

        private void StartMailFetch()
        {
            mailFetching = true;
            int generation = ++mailGeneration;
            string url = settings.EwsUrl;
            ExchangeCredential used = credential;
            ThreadPool.QueueUserWorkItem(state =>
            {
                int? unread = null;
                string error = null;
                bool authentication = false;
                try
                {
                    unread = new EwsClient(url, used).InboxUnread();
                }
                catch (CalendarException failure)
                {
                    error = failure.Message;
                    authentication = failure.IsAuthentication;
                }
                catch (Exception failure)
                {
                    AppLog.Append("mail fetch failed: " + failure);
                    error = "внутренняя ошибка: " + failure.Message;
                }
                Post(() => OnMailFetched(generation, unread, error, authentication));
            });
        }

        private void OnMailFetched(int generation, int? unread, string error, bool authentication)
        {
            if (generation != mailGeneration) return;
            mailFetching = false;
            Post(PumpOtherDays);
            nextMailRefresh = mailRefreshPending ? DateTime.MinValue : DateTime.Now.AddMinutes(error == null ? settings.MailMinutes : Math.Min(settings.MailMinutes, RetryMinutes));
            if (mailRefreshPending) Post(Tick);
            mailRefreshPending = false;
            if (error != null)
            {
                if (error != mailError)
                {
                    AppLog.Append("mail error: " + error);
                    Usage.Hit("mail.error");
                }
                mailError = error;
                if (authentication && !authenticationFailed)
                {
                    authenticationFailed = true;
                    lastError = error;
                    AppLog.Append("calendar polling paused until credentials change");
                }
                UpdateTray();
                if (listForm.Visible) PopulateList();
                return;
            }
            if (unread != mailUnread) AppLog.Append("mail: " + unread + " unread in inbox");
            mailError = null;
            mailUnread = unread;
            if (listForm.Visible) PopulateList();
        }

        internal static string MailLine(int? unread, string error)
        {
            if (error == null) return MailText(unread);
            return unread.HasValue ? MailText(unread) + " · не обновлено" : "Почта не загружена: " + error;
        }

        internal static string MailText(int? unread)
        {
            if (!unread.HasValue) return "Почта ещё не загружена";
            int count = unread.Value;
            if (count == 0) return "Новых писем нет";
            int lastTwo = count % 100;
            int last = count % 10;
            string word = lastTwo >= 11 && lastTwo <= 14 ? "новых писем" : last == 1 ? "новое письмо" : last >= 2 && last <= 4 ? "новых письма" : "новых писем";
            return "У вас " + count + " " + word;
        }

        private void OnMailClicked()
        {
            Usage.Hit("mail.open");
            HideList();
            string url = settings.WebMailUrl;
            string problem = url == null ? "адрес веб-почты не задан" : MeetingLinks.Open(url);
            AppLog.Append(problem == null ? "mail opened" : "mail not opened: " + problem);
            if (problem != null) MessageBox.Show("Не удалось открыть почту: " + problem, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void RequestRefresh()
        {
            nextRefresh = DateTime.MinValue;
            nextMailRefresh = DateTime.MinValue;
            if (mailFetching) mailRefreshPending = true;
            if (fetching) refreshPending = true;
        }

        private void RefreshNow(bool abandonRunning)
        {
            if (abandonRunning || authenticationFailed)
            {
                mailError = null;
                accessConfirmed = false;
            }
            authenticationFailed = false;
            lastError = null;
            if (abandonRunning) ForgetOtherDays();
            else foreach (OtherDay other in otherDays.Values) other.Stale = true;
            if (fetching && abandonRunning)
            {
                fetching = false;
                refreshPending = false;
                fetchGeneration++;
            }
            if (mailFetching && abandonRunning)
            {
                mailFetching = false;
                mailRefreshPending = false;
                mailGeneration++;
            }
            RequestRefresh();
            Tick();
        }

        private void SaveCache()
        {
            string problem = cache.Save();
            if (problem != null) AppLog.Append("today list not saved: " + problem);
        }

        private void ShowDueReminders(DateTime now)
        {
            List<Meeting> due = planner.Due(cache.Meetings, now, settings.LeadMinutes, preferences, cache.Shown);
            if (due.Count == 0) return;
            foreach (Meeting meeting in due)
            {
                cache.Shown.Add(meeting.ReminderKey);
                if (reminders.Exists(open => !open.IsNewMeetingNotice && open.Meeting.ReminderKey == meeting.ReminderKey)) continue;
                foreach (ReminderForm notice in reminders.FindAll(open => open.IsNewMeetingNotice && open.Meeting.ReminderKey == meeting.ReminderKey)) notice.Close();
                ShowReminder(meeting, false);
                AppLog.Append("reminder: meeting at " + meeting.Start.ToString("HH:mm") + ", lead " + preferences.EffectiveLead(meeting, settings.LeadMinutes) + " min, link " + (meeting.Link != null ? "yes" : "no"));
                Usage.Hit("reminder.show");
            }
            SaveCache();
        }

        private void ShowReminder(Meeting meeting, bool newMeetingNotice)
        {
            var form = new ReminderForm(meeting, newMeetingNotice);
            bool handled = false;
            form.JoinClicked += reminder =>
            {
                handled = true;
                Usage.Hit(newMeetingNotice ? "notice.join" : "reminder.join");
                OpenLink(reminder.Meeting);
            };
            form.SnoozeClicked += reminder =>
            {
                handled = true;
                Usage.Hit("reminder.snooze");
                planner.Snooze(reminder.Meeting, DateTime.Now + SnoozeFor, cache.Shown);
                SaveCache();
            };
            form.FormClosed += (sender, e) =>
            {
                if (!handled && !exiting && !newMeetingNotice) Usage.Hit("reminder.close");
                reminders.Remove(form);
                form.Dispose();
                Restack();
            };
            reminders.Add(form);
            form.Show();
            Restack();
            PlaySound();
        }

        private void Restack()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int bottom = area.Bottom - ReminderGap;
            foreach (ReminderForm reminder in reminders)
            {
                int top = Math.Max(area.Top, bottom - reminder.Height);
                reminder.Location = new Point(area.Right - reminder.Width - ReminderGap, top);
                bottom = top - ReminderGap;
            }
        }

        private static void PlaySound()
        {
            try
            {
                if (File.Exists(ReminderSound))
                {
                    using (var player = new SoundPlayer(ReminderSound))
                    {
                        player.Play();
                    }
                    return;
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (IOException)
            {
            }
            SystemSounds.Asterisk.Play();
        }

        private void OpenLink(Meeting meeting)
        {
            string problem = MeetingLinks.Open(meeting.Link);
            AppLog.Append(problem == null ? "link opened" : "link not opened: " + problem);
            if (problem != null) MessageBox.Show("Не удалось открыть ссылку: " + problem, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private TrayState CurrentState()
        {
            if (!HasCredential || authenticationFailed) return TrayState.Problem;
            if (vpnUp != true) return TrayState.Offline;
            return lastError != null ? TrayState.Problem : TrayState.Ready;
        }

        private void UpdateTray()
        {
            TrayState state = CurrentState();
            if (state != shownState)
            {
                Icon previous = currentIcon;
                currentIcon = BellPainter.TrayIcon(state);
                trayIcon.Icon = currentIcon;
                if (previous != null) previous.Dispose();
                shownState = state;
            }
            string text = AppIdentity.Name + " — " + Headline(DateTime.Now);
            trayIcon.Text = text.Length > MaxTrayTextLength ? text.Substring(0, MaxTrayTextLength - 1) + "…" : text;
        }

        private string Headline(DateTime now)
        {
            if (!HasCredential) return "нет логина Exchange";
            if (authenticationFailed) return "Exchange отказал во входе";
            Meeting next = cache.Meetings.Find(meeting => !meeting.IsAllDay && meeting.End > now);
            if (next == null) return cache.Fetched.HasValue ? "созвонов больше нет" : "список не загружен";
            return (next.IsGoing(now) ? "идёт " : "") + next.Start.ToString("HH:mm") + " " + next.DisplaySubject;
        }

        private void PopulateList()
        {
            DateTime now = DateTime.Now;
            if (viewDay == DateTime.MinValue) viewDay = cache.Day;
            bool today = viewDay == cache.Day;
            OtherDay other;
            otherDays.TryGetValue(viewDay, out other);
            List<Meeting> shown = today ? cache.Meetings : other != null && other.Meetings != null ? other.Meetings : new List<Meeting>();
            ListStatus status = today || authenticationFailed || !HasCredential ? CurrentStatus() : new ListStatus(other != null && other.Fetched.HasValue ? "Получено в " + other.Fetched.Value.ToString("HH:mm") : "Список не загружен", null, false);
            listForm.Populate(shown, now, DayNavigation.Title(viewDay, cache.Day), CanStep(-1), CanStep(1), today ? "Созвонов на сегодня нет" : "Созвонов нет", status, Describe, MailLine(mailUnread, mailError), settings.WebMailUrl != null, listAnchor);
            if (listForm.Visible) listForm.ShowNear(listAnchor);
        }

        private ListStatus CurrentStatus()
        {
            const string OpenSettings = "Настройки";
            if (credentialProblem != null && credential == null) return new ListStatus("Не прочитан логин Exchange: " + credentialProblem, OpenSettings, true);
            if (!HasCredential) return new ListStatus("Не задан логин Exchange", OpenSettings, true);
            if (authenticationFailed) return new ListStatus(Capitalize(lastError), OpenSettings, true);
            if (vpnUp != true) return new ListStatus("VPN выключен, " + (cache.Fetched.HasValue ? "список на " + cache.Fetched.Value.ToString("HH:mm") : "список не загружен"), null, false);
            if (lastError != null) return new ListStatus(Capitalize(lastError), "Повторить", true);
            if (fetching && !cache.Fetched.HasValue) return new ListStatus("Загружаю…", null, false);
            return new ListStatus(cache.Fetched.HasValue ? "Обновлено в " + cache.Fetched.Value.ToString("HH:mm") : "Список не загружен", "Обновить", false);
        }

        private string Describe(Meeting meeting)
        {
            var parts = new List<string>();
            if (meeting.IsAllDay) parts.Add("без напоминания");
            else
            {
                int custom;
                bool hasCustom = preferences.TryGetLead(meeting.Uid, out custom);
                int lead = hasCustom ? custom : settings.LeadMinutes;
                parts.Add(LeadText(lead) + (hasCustom && lead != MeetingPreferences.Off ? " (своё)" : ""));
            }
            if (meeting.Link != null) parts.Add(MeetingLinks.IsConference(meeting.Link) ? "есть ссылка" : "ссылка: " + MeetingLinks.DisplayHost(meeting.Link));
            else if (!string.IsNullOrWhiteSpace(meeting.Location)) parts.Add(meeting.Location.Trim());
            return string.Join(" · ", parts.ToArray());
        }

        private static string LeadText(int lead)
        {
            if (lead == MeetingPreferences.Off) return "без напоминания";
            if (lead == 0) return "напомнить в начале";
            return "за " + lead + " мин";
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (listForm.Visible)
            {
                HideList();
                return;
            }
            listAnchor = Cursor.Position;
            ShowList();
        }

        private void ShowList()
        {
            viewDay = cache.Day;
            LoadNeighbors(true);
            listOpens++;
            PopulateList();
            listForm.ShowNear(listAnchor);
        }

        private void HideList()
        {
            meetingMenu.Close();
            listForm.Hide();
            viewDay = cache.Day;
            foreach (OtherDay other in otherDays.Values) other.Wanted = false;
        }

        private bool CanStep(int delta)
        {
            DateTime target = viewDay.AddDays(delta);
            if (!DayNavigation.InRange(target, cache.Day)) return false;
            if (target == cache.Day) return true;
            OtherDay other;
            return otherDays.TryGetValue(target, out other) && other.Ready;
        }

        private void OnDayStep(int delta)
        {
            if (!CanStep(delta)) return;
            Usage.Hit(delta < 0 ? "list.day_prev" : "list.day_next");
            meetingMenu.Close();
            viewDay = viewDay.AddDays(delta);
            LoadNeighbors(true);
            PopulateList();
        }

        private void LoadNeighbors(bool retryFailed)
        {
            var days = new List<DateTime> { viewDay.AddDays(-1), viewDay.AddDays(1) };
            if (viewDay != cache.Day) days.Add(viewDay);
            foreach (KeyValuePair<DateTime, OtherDay> pair in otherDays)
            {
                if (!days.Contains(pair.Key)) pair.Value.Wanted = false;
            }
            foreach (DateTime day in days)
            {
                if (day == cache.Day || !DayNavigation.InRange(day, cache.Day)) continue;
                OtherDay other;
                if (!otherDays.TryGetValue(day, out other))
                {
                    other = new OtherDay();
                    otherDays[day] = other;
                }
                bool wanted = day == viewDay ? other.NeedsLoadWhenViewed(retryFailed) : other.NeedsLoad(DateTime.Now, settings.CalendarMinutes, retryFailed);
                if (wanted) other.Wanted = true;
            }
            PumpOtherDays();
        }

        private void PumpOtherDays()
        {
            if (otherDayLoading || fetching || mailFetching) return;
            if (vpnUp != true || !HasCredential || authenticationFailed || !accessConfirmed) return;
            DateTime now = DateTime.Now;
            if (now >= nextRefresh || now >= nextMailRefresh) return;
            foreach (KeyValuePair<DateTime, OtherDay> pair in otherDays)
            {
                if (!pair.Value.Wanted) continue;
                StartOtherDay(pair.Key, pair.Value);
                return;
            }
        }

        private void StartOtherDay(DateTime day, OtherDay other)
        {
            otherDayLoading = true;
            other.Wanted = false;
            other.Loading = true;
            other.Stale = false;
            int generation = otherDaysGeneration;
            string url = settings.EwsUrl;
            ExchangeCredential used = credential;
            int offset = (int)(day - cache.Day).TotalDays;
            ThreadPool.QueueUserWorkItem(state =>
            {
                List<Meeting> meetings = null;
                List<Meeting> unreadBodies = new List<Meeting>();
                string error = null;
                bool authentication = false;
                try
                {
                    var client = new EwsClient(url, used);
                    meetings = client.Day(day);
                    unreadBodies = client.UnreadBodies;
                    if (client.BodiesProblem != null) AppLog.Append("day " + offset + " invitation texts not loaded: " + client.BodiesProblem);
                }
                catch (CalendarException failure)
                {
                    error = failure.Message;
                    authentication = failure.IsAuthentication;
                }
                catch (Exception failure)
                {
                    AppLog.Append("day " + offset + " fetch failed: " + failure);
                    error = "внутренняя ошибка: " + failure.Message;
                }
                Post(() => OnOtherDayLoaded(generation, day, offset, meetings, unreadBodies, error, authentication));
            });
        }

        private void OnOtherDayLoaded(int generation, DateTime day, int offset, List<Meeting> meetings, List<Meeting> unreadBodies, string error, bool authentication)
        {
            if (generation != otherDaysGeneration) return;
            otherDayLoading = false;
            OtherDay other;
            if (otherDays.TryGetValue(day, out other))
            {
                other.Loading = false;
                if (error != null)
                {
                    other.Failed = true;
                    AppLog.Append("day " + offset + " error: " + error);
                    if (authentication && !authenticationFailed)
                    {
                        if (error != lastError) Usage.Hit("calendar.error");
                        authenticationFailed = true;
                        lastError = error;
                        AppLog.Append("calendar polling paused until credentials change");
                        UpdateTray();
                    }
                }
                else
                {
                    MergeLinks(meetings, unreadBodies, other.Meetings ?? new List<Meeting>());
                    other.Meetings = meetings;
                    other.Fetched = DateTime.Now;
                    other.Failed = false;
                    AppLog.Append("day " + offset + ": " + meetings.Count + " meetings");
                }
            }
            Post(Tick);
            if (listForm.Visible) PopulateList();
        }

        private void ForgetOtherDays()
        {
            otherDays.Clear();
            otherDaysGeneration++;
            otherDayLoading = false;
            viewDay = cache.Day;
        }

        private void OnListMenuRequested(Point at)
        {
            if (trayMenuToggle.ConsumeRelease(ListMenuKey)) return;
            trayMenuToggle.Opened(ListMenuKey);
            Usage.Hit("list.menu");
            meetingMenu.Close();
            SetForegroundWindow(listForm.Handle);
            trayIcon.ContextMenuStrip.Show(at);
        }

        private void OnStatusClicked()
        {
            Usage.Hit("list.status");
            if (!HasCredential || authenticationFailed)
            {
                HideList();
                ShowSettings(null);
                return;
            }
            RefreshNow(false);
        }

        private void OnMeetingClicked(Meeting meeting, Point at)
        {
            if (meetingMenuToggle.ConsumeRelease(meeting.ReminderKey))
            {
                listForm.ReleaseHeldRow();
                return;
            }
            meetingMenu.Items.Clear();
            var join = new ToolStripMenuItem(meeting.Link != null ? MeetingLinks.JoinText(meeting.Link) : "Подключиться", null, (s, a) =>
            {
                Usage.Hit("meeting.join");
                HideList();
                OpenLink(meeting);
            });
            join.Enabled = meeting.Link != null;
            meetingMenu.Items.Add(join);
            var details = new ToolStripMenuItem("Описание", null, (s, a) =>
            {
                Usage.Hit("meeting.description");
                HideList();
                ShowDescription(meeting);
            });
            details.Enabled = meeting.HasDetails;
            meetingMenu.Items.Add(details);
            var remind = new ToolStripMenuItem("Напоминание");
            bool canRemind = !meeting.IsAllDay && meeting.Start.Date >= cache.Day;
            remind.Enabled = canRemind;
            if (canRemind)
            {
                int custom;
                bool hasCustom = preferences.TryGetLead(meeting.Uid, out custom);
                remind.DropDownItems.Add(new ToolStripMenuItem("По умолчанию (" + LeadText(settings.LeadMinutes) + ")", null, (s, a) => SetLead(meeting, null)) { Checked = !hasCustom });
                foreach (int lead in LeadChoices)
                {
                    int chosen = lead;
                    remind.DropDownItems.Add(new ToolStripMenuItem(lead == 0 ? "В момент начала" : "За " + lead + " мин", null, (s, a) => SetLead(meeting, chosen)) { Checked = hasCustom && custom == lead });
                }
                remind.DropDownItems.Add(new ToolStripMenuItem("Не напоминать", null, (s, a) => SetLead(meeting, MeetingPreferences.Off)) { Checked = hasCustom && custom == MeetingPreferences.Off });
                remind.DropDownItems.Add(new ToolStripSeparator());
                remind.DropDownItems.Add(Disabled("Действует на все встречи этой серии"));
            }
            meetingMenu.Items.Add(remind);
            SetForegroundWindow(listForm.Handle);
            meetingMenuToggle.Opened(meeting.ReminderKey);
            meetingMenu.Show(at);
        }

        private void ShowDescription(Meeting meeting)
        {
            DescriptionForm open;
            if (descriptions.TryGetValue(meeting.ReminderKey, out open))
            {
                if (open.Meeting == meeting)
                {
                    open.Activate();
                    return;
                }
                open.Close();
            }
            var form = new DescriptionForm(meeting);
            descriptions[meeting.ReminderKey] = form;
            form.FormClosed += (sender, e) =>
            {
                descriptions.Remove(meeting.ReminderKey);
                form.Dispose();
            };
            form.Show();
            form.Activate();
        }

        private static ToolStripMenuItem Disabled(string text)
        {
            return new ToolStripMenuItem(text) { Enabled = false };
        }

        private void SetLead(Meeting meeting, int? lead)
        {
            string problem;
            if (lead == null)
            {
                Usage.Hit("meeting.default");
                problem = preferences.Reset(meeting.Uid);
            }
            else
            {
                Usage.Hit(lead.Value == MeetingPreferences.Off ? "meeting.off" : "meeting.lead");
                problem = preferences.Set(meeting.Uid, lead.Value, DateTime.Now);
                if (lead.Value == MeetingPreferences.Off) planner.Forget(meeting);
            }
            AppLog.Append("meeting at " + meeting.Start.ToString("HH:mm") + ": lead " + (lead == null ? "default" : lead.Value.ToString()) + (problem == null ? "" : ", not saved: " + problem));
            if (problem != null) MessageBox.Show("Не удалось сохранить настройку встречи: " + problem, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateUsageState();
            ShowDueReminders(DateTime.Now);
            if (listForm.Visible) PopulateList();
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ContextMenuStrip menu = trayIcon.ContextMenuStrip;
            menu.Items.Clear();
            bool listShown = listForm.Visible;
            if (!listShown) listAnchor = Cursor.Position;
            menu.Items.Add(new ToolStripMenuItem(listShown ? "Свернуть в трей" : "Развернуть", null, (s, a) =>
            {
                Usage.Hit(listShown ? "menu.list_hide" : "menu.list_show");
                if (listShown) HideList();
                else ShowList();
            }));
            menu.Items.Add(new ToolStripSeparator());
            DateTime now = DateTime.Now;
            menu.Items.Add(Disabled(HeadlineForMenu(now)));
            ListStatus status = CurrentStatus();
            if (status.Problem || vpnUp != true) menu.Items.Add(Disabled(status.Text));
            var refresh = new ToolStripMenuItem("Обновить сейчас", null, (s, a) =>
            {
                Usage.Hit("menu.refresh");
                RefreshNow(false);
            });
            refresh.Enabled = HasCredential && vpnUp == true;
            menu.Items.Add(refresh);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Настройки…", null, (s, a) =>
            {
                Usage.Hit("menu.settings");
                ShowSettings(null);
            }));
            menu.Items.Add(new ToolStripMenuItem("Журнал", null, OnLogClick));
            var startupItem = new ToolStripMenuItem("Запускать при входе в Windows", null, OnStartupClick);
            startupItem.Checked = Autostart.IsEnabled();
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripMenuItem("Проверить обновление…", null, (s, a) =>
            {
                Usage.Hit("menu.update_check");
                UpdateForm.ShowSingle();
            }));
            menu.Items.Add(new ToolStripMenuItem("О приложении…", null, (s, a) =>
            {
                Usage.Hit("menu.about");
                AboutForm.ShowSingle();
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, (s, a) =>
            {
                Usage.Hit("menu.exit");
                ExitThread();
            }));
            e.Cancel = false;
        }

        private string HeadlineForMenu(DateTime now)
        {
            Meeting next = cache.Meetings.Find(meeting => !meeting.IsAllDay && meeting.End > now);
            if (next == null) return cache.Fetched.HasValue || cache.Meetings.Count > 0 ? "Созвонов на сегодня больше нет" : "Список созвонов не загружен";
            string subject = next.DisplaySubject.Length > 50 ? next.DisplaySubject.Substring(0, 49) + "…" : next.DisplaySubject;
            return (next.IsGoing(now) ? "Идёт: " : "Следующий: ") + next.Start.ToString("HH:mm") + " " + subject;
        }

        private void OnTrayDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Usage.Hit("tray.double_click");
            HideList();
            ShowSettings(null);
        }

        private void ShowSettings(string intro)
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(settings.Clone(), credential, intro);
            settingsForm.FormClosed += OnSettingsClosed;
            settingsForm.Show();
            settingsForm.Activate();
        }

        private void OnSettingsClosed(object sender, FormClosedEventArgs e)
        {
            SettingsForm form = settingsForm;
            settingsForm = null;
            if (form.DialogResult != DialogResult.OK) return;
            var problems = new List<string>();
            bool addressChanged = form.Result.EwsUrl != settings.EwsUrl;
            bool accessChanged = addressChanged || authenticationFailed;
            bool accountChanged = addressChanged;
            string previousUser = credential == null ? null : credential.UserName;
            settings = form.Result;
            string saveProblem = settings.Save();
            if (saveProblem != null)
            {
                AppLog.Append("settings not saved: " + saveProblem);
                problems.Add("Не удалось сохранить " + AppSettings.FilePath + ": " + saveProblem);
            }
            if (form.NewCredential != null)
            {
                string credentialSaveProblem = CredentialStore.Write(form.NewCredential);
                if (credentialSaveProblem == null)
                {
                    credential = form.NewCredential;
                    credentialProblem = null;
                    accessChanged = true;
                    if (!string.Equals(previousUser, credential.UserName, StringComparison.OrdinalIgnoreCase)) accountChanged = true;
                    AppLog.Append("credential saved");
                }
                else
                {
                    AppLog.Append("credential not saved: " + credentialSaveProblem);
                    if (!addressChanged) accessChanged = false;
                    problems.Add("Не удалось сохранить логин и пароль в диспетчере учётных данных Windows: " + credentialSaveProblem);
                }
            }
            if (form.RemoveCredential)
            {
                string removeProblem = CredentialStore.Delete();
                if (removeProblem == null)
                {
                    credential = null;
                    credentialProblem = null;
                    accessChanged = true;
                    accountChanged = true;
                    AppLog.Append("credential removed");
                }
                else
                {
                    AppLog.Append("credential not removed: " + removeProblem);
                    problems.Add("Не удалось удалить логин из диспетчера учётных данных Windows: " + removeProblem);
                }
            }
            string optionsProblem = SaveOptions(form);
            if (optionsProblem != null) problems.Add(optionsProblem);
            Usage.Hit("settings.save");
            AppLog.Append("settings: ews host=" + EwsHost(settings.EwsUrl) + ", vpn adapters=" + (settings.ChecksVpn ? AppSettings.JoinList(settings.VpnAdapters) : "not checked") + ", lead=" + settings.LeadMinutes);
            vpnUp = null;
            if (accountChanged)
            {
                cache.Fetched = null;
                cache.Meetings = new List<Meeting>();
                SaveCache();
                mailUnread = null;
            }
            UpdateUsageState();
            if (accessChanged) RefreshNow(true);
            else Tick();
            if (problems.Count > 0) MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, problems.ToArray()), AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private string SaveOptions(SettingsForm form)
        {
            try
            {
                if (form.AutoUpdateChosen != AutoUpdateCheck.IsEnabled) AutoUpdateCheck.Set(form.AutoUpdateChosen);
                if (form.UsageStatisticsChosen != UsageConsent.IsGiven) Usage.SetConsent(form.UsageStatisticsChosen);
                return null;
            }
            catch (IOException error)
            {
                AppLog.Append("options not saved: " + error.Message);
                return "Не удалось сохранить выбор проверки обновлений и статистики: " + error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("options not saved: " + error.Message);
                return "Не удалось сохранить выбор проверки обновлений и статистики: " + error.Message;
            }
        }

        private void OnStartupClick(object sender, EventArgs e)
        {
            bool enabled = Autostart.IsEnabled();
            Usage.Hit(enabled ? "menu.autostart_off" : "menu.autostart_on");
            string problem = enabled ? Autostart.Disable() : Autostart.Enable(Application.ExecutablePath);
            Usage.SetState("autostart", Autostart.IsEnabled());
            if (problem != null) MessageBox.Show(problem, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnLogClick(object sender, EventArgs e)
        {
            Usage.Hit("menu.journal");
            if (!File.Exists(AppLog.FilePath)) AppLog.Append("log created");
            try
            {
                using (Process.Start(SystemTools.Notepad, "\"" + AppLog.FilePath + "\""))
                {
                }
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                AppLog.Append("notepad failed: " + error.Message);
                MessageBox.Show("Не удалось открыть Блокнот: " + error.Message + Environment.NewLine + "Журнал лежит в " + AppLog.FilePath,
                    AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowUpdateBalloon(string version)
        {
            trayIcon.ShowBalloonTip(10000, AppTitle, "Вышла версия " + version + ". Нажми, чтобы скачать и установить.", ToolTipIcon.Info);
            updateBalloonShown = true;
        }

        private void OnBalloonClicked(object sender, EventArgs e)
        {
            if (!updateBalloonShown) return;
            updateBalloonShown = false;
            Usage.Hit("update.notify_open");
            UpdateForm.ShowSingle();
        }

        private void UpdateUsageState()
        {
            Usage.SetState("autostart", Autostart.IsEnabled());
            Usage.SetState("auto_update", AutoUpdateCheck.IsEnabled);
            Usage.SetState("crash_reports", CrashReportConsent.IsGiven);
            Usage.SetState("lead_default", settings.LeadMinutes);
            Usage.SetState("meetings_custom", preferences.Count - preferences.OffCount);
            Usage.SetState("meetings_off", preferences.OffCount);
            Usage.SetState("vpn_check", settings.ChecksVpn);
            Usage.SetState("credential", HasCredential);
            Usage.SetState("meetings_today", cache.Meetings.Count);
            Usage.SetState("calendar_interval", settings.CalendarMinutes);
            Usage.SetState("mail_interval", settings.MailMinutes);
            Usage.SetState("mail_url_custom", settings.MailUrl.Length > 0);
        }

        private static string EwsHost(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri.Host : "invalid";
        }

        private static string Capitalize(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : char.ToUpper(text[0]) + text.Substring(1);
        }
    }
}
