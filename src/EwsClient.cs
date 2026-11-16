using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace MeetingBell
{
    internal sealed class EwsClient
    {
        private const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
        private const string TypesNamespace = "http://schemas.microsoft.com/exchange/services/2006/types";
        private const string MessagesNamespace = "http://schemas.microsoft.com/exchange/services/2006/messages";
        private const int TimeoutMs = 30 * 1000;
        private const int MaxEntries = 200;
        private const int MaxResponseBytes = 8 * 1024 * 1024;
        private const string DeclinedResponse = "Decline";
        private const int MaxDescription = 4000;
        private const int MaxPeople = 50;
        private const int MaxPersonLength = 120;

        private readonly Uri endpoint;
        private readonly ExchangeCredential credential;

        public EwsClient(string url, ExchangeCredential credential)
        {
            string problem = CheckUrl(url);
            if (problem != null) throw new CalendarException(problem, false);
            endpoint = new Uri(url.Trim());
            this.credential = credential;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public static string CheckUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out uri)) return "адрес EWS не похож на адрес сайта";
            if (uri.Scheme != Uri.UriSchemeHttps) return "адрес EWS должен начинаться с https://";
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)) return "в адресе EWS не должно быть логина, пароля и параметров";
            return null;
        }

        public string BodiesProblem { get; private set; }

        public List<Meeting> UnreadBodies { get; private set; }

        public List<Meeting> Day(DateTime localDay)
        {
            BodiesProblem = null;
            UnreadBodies = new List<Meeting>();
            DateTime from = localDay.Date;
            DateTime to = from.AddDays(1);
            XmlDocument found = CheckResponse(Call(FindItemBody(from.ToUniversalTime(), to.ToUniversalTime())));
            var bodyRequests = new Dictionary<string, Meeting>();
            List<Meeting> meetings = ReadCalendarItems(found, bodyRequests);
            if (bodyRequests.Count > 0) ReadBodies(bodyRequests);
            return meetings;
        }

        internal static List<Meeting> ReadCalendarItems(XmlDocument found, Dictionary<string, Meeting> bodyRequests)
        {
            var meetings = new List<Meeting>();
            foreach (XmlElement item in found.GetElementsByTagName("CalendarItem", TypesNamespace))
            {
                if (Text(item, "IsCancelled") == "true" || Text(item, "MyResponseType") == DeclinedResponse) continue;
                Meeting meeting = Parse(item);
                if (meeting == null) continue;
                meetings.Add(meeting);
                string id = ItemId(item);
                if (id != null && !bodyRequests.ContainsKey(id)) bodyRequests[id] = meeting;
            }
            meetings.Sort((left, right) => left.Start != right.Start ? left.Start.CompareTo(right.Start) : left.End.CompareTo(right.End));
            return meetings;
        }

        internal static HashSet<string> ApplyBodies(XmlDocument items, Dictionary<string, Meeting> meetings)
        {
            var read = new HashSet<string>();
            foreach (XmlElement item in items.GetElementsByTagName("CalendarItem", TypesNamespace))
            {
                string id = ItemId(item);
                Meeting meeting;
                if (id == null || !meetings.TryGetValue(id, out meeting)) continue;
                string body = Text(item, "Body");
                meeting.Description = Describe(body);
                meeting.Organizer = People(item, "Organizer");
                meeting.RequiredAttendees = People(item, "RequiredAttendees");
                meeting.OptionalAttendees = People(item, "OptionalAttendees");
                if (meeting.Link == null) meeting.Link = MeetingLinks.Find(body);
                read.Add(id);
            }
            return read;
        }

        public int InboxUnread()
        {
            return ReadUnread(CheckResponse(Call("<m:GetFolder><m:FolderShape><t:BaseShape>IdOnly</t:BaseShape><t:AdditionalProperties>"
                + "<t:FieldURI FieldURI=\"folder:UnreadCount\"/></t:AdditionalProperties></m:FolderShape>"
                + "<m:FolderIds><t:DistinguishedFolderId Id=\"inbox\"/></m:FolderIds></m:GetFolder>")));
        }

        internal static int ReadUnread(XmlDocument folder)
        {
            foreach (XmlElement count in folder.GetElementsByTagName("UnreadCount", TypesNamespace))
            {
                int unread;
                if (int.TryParse(count.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out unread) && unread >= 0) return unread;
            }
            throw new CalendarException("Exchange не отдал число непрочитанных писем", false);
        }

        internal static string People(XmlElement item, string field)
        {
            var names = new List<string>();
            int total = 0;
            foreach (XmlNode child in item.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element || child.LocalName != field || child.NamespaceURI != TypesNamespace) continue;
                foreach (XmlElement mailbox in ((XmlElement)child).GetElementsByTagName("Mailbox", TypesNamespace))
                {
                    string person = Person(Text(mailbox, "Name"), Text(mailbox, "EmailAddress"));
                    if (person == null) continue;
                    total++;
                    if (names.Count < MaxPeople) names.Add(person);
                }
            }
            if (names.Count == 0) return null;
            string joined = string.Join("; ", names.ToArray());
            return total > names.Count ? joined + "; и ещё " + (total - names.Count) : joined;
        }

        private static string Person(string name, string email)
        {
            name = Regex.Replace(name ?? "", @"\s+", " ").Trim();
            email = Regex.Replace(email ?? "", @"\s+", "").Trim();
            if (email.StartsWith("/") || email.IndexOf('@') < 0) email = "";
            string person = name.Length == 0 ? email : email.Length == 0 || name.Equals(email, StringComparison.OrdinalIgnoreCase) ? name : name + " <" + email + ">";
            if (person.Length == 0) return null;
            if (person.Length <= MaxPersonLength) return person;
            int cut = char.IsHighSurrogate(person[MaxPersonLength - 2]) ? MaxPersonLength - 2 : MaxPersonLength - 1;
            return person.Substring(0, cut) + "…";
        }

        internal static string Describe(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            string text = Regex.Replace(body.Replace("\r\n", "\n").Replace('\r', '\n'), "[ \t]+\n", "\n");
            text = Regex.Replace(text, "\n{3,}", "\n\n").Trim();
            if (text.Length > MaxDescription)
            {
                int cut = char.IsHighSurrogate(text[MaxDescription - 1]) ? MaxDescription - 1 : MaxDescription;
                text = text.Substring(0, cut).TrimEnd() + "…";
            }
            return text.Length == 0 ? null : text.Replace("\n", Environment.NewLine);
        }

        internal static List<Meeting> Unread(Dictionary<string, Meeting> requested, HashSet<string> read)
        {
            var unread = new List<Meeting>();
            foreach (KeyValuePair<string, Meeting> pair in requested)
            {
                if (!read.Contains(pair.Key)) unread.Add(pair.Value);
            }
            return unread;
        }

        private void ReadBodies(Dictionary<string, Meeting> meetings)
        {
            XmlDocument items;
            try
            {
                items = Call(GetItemBody(meetings.Keys));
            }
            catch (CalendarException error)
            {
                if (error.IsAuthentication) throw;
                BodiesProblem = error.Message;
                UnreadBodies.AddRange(meetings.Values);
                return;
            }
            UnreadBodies.AddRange(Unread(meetings, ApplyBodies(items, meetings)));
            if (UnreadBodies.Count > 0) BodiesProblem = "Exchange не отдал текст приглашения у " + UnreadBodies.Count + " из " + meetings.Count + " встреч";
        }

        private static Meeting Parse(XmlElement item)
        {
            DateTime start;
            DateTime end;
            if (!TryParseTime(Text(item, "Start"), out start) || !TryParseTime(Text(item, "End"), out end)) return null;
            string location = Text(item, "Location");
            string uid = Text(item, "UID");
            return new Meeting
            {
                Uid = string.IsNullOrEmpty(uid) ? ItemId(item) : uid,
                Subject = Text(item, "Subject"),
                Start = start,
                End = end,
                Location = location,
                Link = MeetingLinks.Find(location),
                IsAllDay = Text(item, "IsAllDayEvent") == "true"
            };
        }

        private static bool TryParseTime(string value, out DateTime local)
        {
            DateTime utc;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
            {
                local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
                return true;
            }
            local = DateTime.MinValue;
            return false;
        }

        private static string ItemId(XmlElement item)
        {
            foreach (XmlElement child in item.GetElementsByTagName("ItemId", TypesNamespace))
            {
                string id = child.GetAttribute("Id");
                return id.Length == 0 ? null : id;
            }
            return null;
        }

        private static string Text(XmlElement item, string name)
        {
            foreach (XmlNode child in item.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element && child.LocalName == name && child.NamespaceURI == TypesNamespace) return child.InnerText;
            }
            return null;
        }

        private static string FindItemBody(DateTime fromUtc, DateTime toUtc)
        {
            var body = new StringBuilder();
            body.Append("<m:FindItem Traversal=\"Shallow\"><m:ItemShape><t:BaseShape>IdOnly</t:BaseShape><t:AdditionalProperties>");
            foreach (string field in new[] { "item:Subject", "calendar:Start", "calendar:End", "calendar:Location", "calendar:IsAllDayEvent", "calendar:IsCancelled", "calendar:UID", "calendar:MyResponseType" })
            {
                body.Append("<t:FieldURI FieldURI=\"").Append(field).Append("\"/>");
            }
            body.Append("</t:AdditionalProperties></m:ItemShape>");
            body.Append("<m:CalendarView MaxEntriesReturned=\"").Append(MaxEntries.ToString(CultureInfo.InvariantCulture))
                .Append("\" StartDate=\"").Append(IsoUtc(fromUtc)).Append("\" EndDate=\"").Append(IsoUtc(toUtc)).Append("\"/>");
            body.Append("<m:ParentFolderIds><t:DistinguishedFolderId Id=\"calendar\"/></m:ParentFolderIds></m:FindItem>");
            return body.ToString();
        }

        private static string GetItemBody(IEnumerable<string> ids)
        {
            var body = new StringBuilder();
            body.Append("<m:GetItem><m:ItemShape><t:BaseShape>IdOnly</t:BaseShape><t:BodyType>Text</t:BodyType><t:AdditionalProperties>");
            body.Append("<t:FieldURI FieldURI=\"item:Body\"/><t:FieldURI FieldURI=\"calendar:Organizer\"/><t:FieldURI FieldURI=\"calendar:RequiredAttendees\"/>");
            body.Append("<t:FieldURI FieldURI=\"calendar:OptionalAttendees\"/></t:AdditionalProperties></m:ItemShape><m:ItemIds>");
            foreach (string id in ids)
            {
                body.Append("<t:ItemId Id=\"").Append(SecurityElement.Escape(id)).Append("\"/>");
            }
            body.Append("</m:ItemIds></m:GetItem>");
            return body.ToString();
        }

        private static string IsoUtc(DateTime utc)
        {
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private XmlDocument Call(string operation)
        {
            string envelope = "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<soap:Envelope xmlns:soap=\"" + SoapNamespace + "\" xmlns:t=\"" + TypesNamespace + "\" xmlns:m=\"" + MessagesNamespace + "\">"
                + "<soap:Header><t:RequestServerVersion Version=\"Exchange2013\"/></soap:Header>"
                + "<soap:Body>" + operation + "</soap:Body></soap:Envelope>";
            byte[] payload = new UTF8Encoding(false).GetBytes(envelope);
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "text/xml; charset=utf-8";
            request.Accept = "text/xml";
            request.UserAgent = AppIdentity.Name + "/" + AppIdentity.Version;
            request.AllowAutoRedirect = false;
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.Headers[HttpRequestHeader.Authorization] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + credential.Password));
            request.ContentLength = payload.Length;
            try
            {
                using (Stream body = request.GetRequestStream())
                {
                    body.Write(payload, 0, payload.Length);
                }
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300) throw new CalendarException("Exchange ответил кодом " + (int)response.StatusCode + " — проверь адрес EWS", false);
                    return Read(response);
                }
            }
            catch (WebException error)
            {
                throw Describe(error);
            }
            catch (IOException error)
            {
                throw new CalendarException("связь с Exchange оборвалась: " + error.Message, false);
            }
        }

        private static XmlDocument Read(HttpWebResponse response)
        {
            using (Stream stream = response.GetResponseStream())
            using (var limited = new MemoryStream())
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (limited.Length + read > MaxResponseBytes) throw new CalendarException("ответ Exchange слишком большой", false);
                    limited.Write(buffer, 0, read);
                }
                limited.Position = 0;
                var document = new XmlDocument { XmlResolver = null };
                try
                {
                    using (XmlReader reader = XmlReader.Create(limited, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    {
                        document.Load(reader);
                    }
                }
                catch (XmlException)
                {
                    throw new CalendarException("Exchange ответил не XML — проверь адрес EWS", false);
                }
                return document;
            }
        }

        internal static XmlDocument CheckResponse(XmlDocument document)
        {
            foreach (XmlElement message in document.GetElementsByTagName("*"))
            {
                if (message.NamespaceURI != MessagesNamespace || !message.LocalName.EndsWith("ResponseMessage")) continue;
                if (message.GetAttribute("ResponseClass") != "Error") continue;
                string code = ChildText(message, "ResponseCode");
                string text = ChildText(message, "MessageText");
                throw new CalendarException("Exchange вернул ошибку " + (code ?? "") + (string.IsNullOrEmpty(text) ? "" : ": " + text), false);
            }
            return document;
        }

        private static string ChildText(XmlElement parent, string name)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element && child.LocalName == name) return child.InnerText;
            }
            return null;
        }

        private static CalendarException Describe(WebException error)
        {
            var response = error.Response as HttpWebResponse;
            if (response != null)
            {
                using (response)
                {
                    int code = (int)response.StatusCode;
                    if (code == 401) return new CalendarException("Exchange не принял логин или пароль", true);
                    if (code == 403) return new CalendarException("Exchange отказал в доступе (403)", true);
                    if (code >= 300 && code < 400) return new CalendarException("Exchange перенаправляет запрос (" + code + ") — проверь адрес EWS", false);
                    if (code == 404) return new CalendarException("по адресу EWS ничего нет (404)", false);
                    return new CalendarException("Exchange ответил кодом " + code, false);
                }
            }
            if (error.Status == WebExceptionStatus.Timeout) return new CalendarException("Exchange не ответил за 30 с", false);
            if (error.Status == WebExceptionStatus.NameResolutionFailure) return new CalendarException("адрес сервера Exchange не найден — VPN поднят?", false);
            if (error.Status == WebExceptionStatus.TrustFailure || error.Status == WebExceptionStatus.SecureChannelFailure) return new CalendarException("не удалось проверить сертификат сервера Exchange", false);
            return new CalendarException("нет связи с Exchange: " + error.Message, false);
        }
    }
}
