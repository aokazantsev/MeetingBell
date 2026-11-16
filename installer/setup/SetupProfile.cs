using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MeetingBell
{
    internal static class SetupProfile
    {
        private const string ResetSettingsKey = "resetSettings";
        private const string EwsUrlKey = "ewsUrl";
        private const string VpnAdapterKey = "vpnAdapter";

        private static AppSettings pendingSettings;
        private static bool fieldsUnread;

        public const string Intro =
            "Значок в трее показывает созвоны на сегодня из календаря Exchange (Outlook) и напоминает о них заранее. "
            + "Календарь опрашивается, только когда поднят VPN. Логин и пароль Exchange вводятся в программе после установки.";

        public static string Notice()
        {
            return null;
        }

        public static List<SetupField> Fields()
        {
            AppSettings current = AppSettings.FileExists ? AppSettings.Load() : new AppSettings();
            bool known = current.ReadError == null;
            fieldsUnread = !known;
            var ews = new SetupField
            {
                Key = EwsUrlKey,
                Label = "Адрес EWS сервера Exchange:",
                Value = known ? current.EwsUrl : "",
                Hint = "Обычно https://<почтовый сервер>/EWS/Exchange.asmx. Можно поменять потом в настройках."
            };
            var vpn = new SetupField
            {
                Key = VpnAdapterKey,
                Label = "Адаптер VPN:",
                Value = known ? AppSettings.JoinList(current.VpnAdapters) : "",
                Hint = "Часть названия сетевого адаптера VPN, несколько — через |. Пока он не поднят, Exchange не опрашивается. Пусто — VPN не проверяется."
            };
            vpn.Suggestions.AddRange(VpnDetector.AdapterDescriptions());
            return new List<SetupField> { ews, vpn };
        }

        public static List<SetupOption> Options()
        {
            return new List<SetupOption>
            {
                new SetupOption
                {
                    Key = ResetSettingsKey,
                    Text = "Сбросить настройки",
                    Hint = "Время напоминания по умолчанию и настройки отдельных встреч вернутся к стандартным. Адрес EWS и адаптер VPN "
                        + "возьмутся из полей выше, логин Exchange сохранится.",
                    Checked = false
                },
                new SetupOption
                {
                    Key = CrashReportConsent.OptionKey,
                    Text = "Отправлять автору отчёты о сбоях",
                    Hint = "В отчёт попадает журнал программы, а в нём — имя пользователя и домена Windows, имя сервера Exchange и названия "
                        + "адаптеров VPN. Не включай, если это запрещают правила твоей компании.",
                    DetailsTitle = "Что уходит в отчёте",
                    Details = "Отчёт уходит один раз — при падении программы — на aokazantsev.ru (сервер в России), без повторных попыток:\n"
                        + "• версия программы, Windows и .NET;\n"
                        + "• время и текст ошибки;\n"
                        + "• последние 100 КБ журнала %LOCALAPPDATA%\\MeetingBell\\log.txt: имя пользователя и домена Windows, путь к программе и её данным, "
                        + "имя сервера Exchange (без логина и пароля), названия адаптеров VPN из настроек, включение и выключение VPN, сохранён ли логин (да или нет), "
                        + "тексты ошибок Exchange и самой программы, число встреч за день и встреч со своим временем напоминания, время начала встреч, "
                        + "о которых было напоминание или у которых менялось время напоминания, это время и есть ли у встречи ссылка (да или нет), путь к скачанному обновлению;\n"
                        + "• IP-адрес, с которого пришёл отчёт.\n"
                        + "Логин и пароль Exchange, темы, места, участники и ссылки встреч в журнал не попадают; имя пользователя Windows попадает, а оно часто "
                        + "совпадает с логином Exchange. Отчёты видит только автор, хранятся последние 50 МБ. "
                        + "Изменить выбор — переустановить программу.",
                    Checked = CrashReportConsent.IsGiven
                },
                new SetupOption
                {
                    Key = UsageConsent.OptionKey,
                    Text = "Отправлять автору анонимную статистику использования",
                    Hint = "Помогает понять, сколько людей пользуется программой и какие функции нужны. Без имён, адресов и встреч. "
                        + "Выключить можно и потом — в настройках программы.",
                    DetailsTitle = "Что уходит в статистике",
                    Details = "Раз в час, пока программа запущена, на aokazantsev.ru (сервер в России) уходят счётчики за текущие и неотправленные прошлые сутки (до недели):\n"
                        + "• случайный номер установки — он не связан с тобой и компьютером;\n"
                        + "• версия программы и Windows;\n"
                        + "• сколько раз за сутки программа запускалась, сама находила новую версию, показывала список и напоминания, сколько раз ты "
                        + "подключался по ссылке, закрывал и откладывал напоминание, менял время напоминания у встречи, пользовался значком, пунктами меню, "
                        + "настройками и обновлением, сколько раз Exchange начинал отвечать ошибкой;\n"
                        + "• текущие настройки: время напоминания по умолчанию, сколько встреч со своим временем и без напоминания, проверяется ли VPN, "
                        + "сохранён ли логин (да или нет), сколько встреч сегодня, автозапуск, проверка обновлений, отчёты о сбоях.\n"
                        + "Имена пользователя и компьютера, адреса, логин, темы и участники встреч в статистику не попадают, IP-адрес сервер не хранит. "
                        + "Статистика хранится 400 суток, дальше — только итоги по дням.",
                    Checked = UsageConsent.InstallerDefault
                }
            };
        }

        public static void BeforeExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            bool reset = request.Has(ResetSettingsKey);
            SetupLog.Append("reset settings: " + reset);
            if (!reset) report(5, "Читаю настройки…");
            AppSettings settings = reset ? new AppSettings() : AppSettings.LoadPatiently(SetupLog.Append);
            if (settings.ReadError != null) throw new InvalidOperationException("Не удалось прочитать настройки " + AppSettings.FilePath + ": " + settings.ReadError.TrimEnd('.') + ". " + settings.ReadAdvice);
            pendingSettings = settings;
            WriteConsent("отчётах о сбоях", () => CrashReportConsent.Set(request.Has(CrashReportConsent.OptionKey)));
            SetupLog.Append("crash reports: " + request.Has(CrashReportConsent.OptionKey));
            WriteConsent("статистике использования", () => UsageConsent.Set(request.Has(UsageConsent.OptionKey)));
            SetupLog.Append("usage statistics: " + request.Has(UsageConsent.OptionKey));
        }

        public static void AfterExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            report(80, "Настройки…");
            AppSettings settings = pendingSettings;
            string url = request.Value(EwsUrlKey);
            string vpn = request.Value(VpnAdapterKey);
            bool keepUrl = fieldsUnread && url.Length == 0;
            string urlProblem = settings.ApplyInstallFields(url, vpn, fieldsUnread);
            if (urlProblem != null) notes.Add("Адрес EWS не сохранён (" + urlProblem + "): укажи его в настройках программы.");
            string saveProblem = settings.Save();
            if (saveProblem != null) throw new InvalidOperationException("Не удалось записать настройки в " + AppSettings.FilePath + ": " + saveProblem.TrimEnd('.') + ".");
            if (request.Has(ResetSettingsKey)) ResetMeetingPreferences(notes);
            SetupLog.Append("settings: ews url " + (keepUrl ? "kept" : urlProblem == null ? "saved" : "rejected: " + urlProblem) + ", vpn adapters=" + AppSettings.JoinList(settings.VpnAdapters));
            if (!settings.ChecksVpn) notes.Add("Адаптер VPN не указан: Exchange будет опрашиваться всегда.");
        }

        public static void AfterLaunch(InstallRequest request, Action<int, string> report, List<string> notes)
        {
        }

        public static void Launch(string executable)
        {
            Process.Start(new ProcessStartInfo(SystemTools.Explorer, "\"" + executable + "\"") { UseShellExecute = false });
        }

        private static void ResetMeetingPreferences(List<string> notes)
        {
            try
            {
                File.Delete(MeetingPreferences.FilePath);
                SetupLog.Append("meeting preferences reset");
            }
            catch (IOException error)
            {
                notes.Add("Настройки отдельных встреч не сброшены: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                notes.Add("Настройки отдельных встреч не сброшены: " + error.Message);
            }
        }

        private static void WriteConsent(string what, Action write)
        {
            try
            {
                write();
            }
            catch (IOException error)
            {
                throw new InvalidOperationException("Не удалось записать выбор о " + what + " в папку " + AppIdentity.DataDirectory + " (" + error.Message.TrimEnd('.') + ").");
            }
            catch (UnauthorizedAccessException error)
            {
                throw new InvalidOperationException("Не удалось записать выбор о " + what + " в папку " + AppIdentity.DataDirectory + " (" + error.Message.TrimEnd('.') + ").");
            }
        }
    }
}
