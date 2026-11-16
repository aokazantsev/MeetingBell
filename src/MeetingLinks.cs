using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MeetingBell
{
    internal static class MeetingLinks
    {
        private static readonly Regex UrlPattern = new Regex(@"https?://[^\s<>""'\]\)]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly string[] ConferenceHosts =
        {
            "zoom.us", "zoom.com", "teams.microsoft.com", "teams.live.com", "meet.google.com", "telemost.yandex.ru",
            "telemost.360.yandex.ru", "ktalk.ru", "jazz.sber.ru", "salutejazz.ru", "webex.com", "mts-link.ru",
            "meet.jit.si", "whereby.com", "skype.com", "vk.com/call", "dion.vc", "trueconf.ru", "trueconf.com"
        };

        public static string Find(string text)
        {
            return FindConference(text);
        }

        public static string AnyWebLink(string text)
        {
            foreach (Match match in UrlPattern.Matches(text ?? ""))
            {
                string candidate = Clean(match.Value);
                if (IsWebLink(candidate)) return candidate;
            }
            return null;
        }

        public static List<TextLink> WebLinks(string text)
        {
            var links = new List<TextLink>();
            foreach (Match match in UrlPattern.Matches(text ?? ""))
            {
                string candidate = Clean(match.Value);
                if (IsWebLink(candidate)) links.Add(new TextLink(match.Index, candidate.Length, candidate));
            }
            return links;
        }

        public static bool IsConference(string link)
        {
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri)) return false;
            foreach (string entry in ConferenceHosts)
            {
                if (IsConference(uri, entry)) return true;
            }
            return false;
        }

        public static string JoinText(string link)
        {
            Uri uri;
            if (IsConference(link) || !Uri.TryCreate(link, UriKind.Absolute, out uri)) return "Подключиться";
            return "Подключиться (" + uri.IdnHost + ")";
        }

        public static string DisplayHost(string link)
        {
            Uri uri;
            return Uri.TryCreate(link, UriKind.Absolute, out uri) ? uri.IdnHost : "";
        }

        public static bool IsWebLink(string link)
        {
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri)) return false;
            return (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && string.IsNullOrEmpty(uri.UserInfo);
        }

        public static string Open(string link)
        {
            if (!IsWebLink(link)) return "ссылка не похожа на адрес сайта";
            try
            {
                using (Process.Start(new ProcessStartInfo(new Uri(link).AbsoluteUri) { UseShellExecute = true }))
                {
                }
                return null;
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                return error.Message;
            }
            catch (InvalidOperationException error)
            {
                return error.Message;
            }
        }

        private static string FindConference(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (Match match in UrlPattern.Matches(text))
            {
                string candidate = Clean(match.Value);
                if (IsWebLink(candidate) && IsConference(candidate)) return candidate;
            }
            return null;
        }

        private static bool IsConference(Uri uri, string entry)
        {
            int slash = entry.IndexOf('/');
            string host = slash < 0 ? entry : entry.Substring(0, slash);
            bool hostMatches = uri.Host == host || uri.Host.EndsWith("." + host);
            if (!hostMatches || slash < 0) return hostMatches;
            string path = entry.Substring(slash);
            return uri.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string link)
        {
            return link.TrimEnd('.', ',', ';', ':', '!', '?');
        }
    }
}
