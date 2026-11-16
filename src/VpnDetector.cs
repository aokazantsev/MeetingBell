using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace MeetingBell
{
    internal static class VpnDetector
    {
        public static bool IsUp(List<string> patterns)
        {
            if (patterns.Count == 0) return true;
            foreach (NetworkInterface adapter in Adapters())
            {
                if (adapter.OperationalStatus == OperationalStatus.Up && Matches(adapter, patterns)) return true;
            }
            return false;
        }

        public static List<string> AdapterDescriptions()
        {
            var names = new List<string>();
            foreach (NetworkInterface adapter in Adapters())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (!names.Contains(adapter.Description)) names.Add(adapter.Description);
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        private static bool Matches(NetworkInterface adapter, List<string> patterns)
        {
            foreach (string pattern in patterns)
            {
                if (Contains(adapter.Description, pattern) || Contains(adapter.Name, pattern)) return true;
            }
            return false;
        }

        private static bool Contains(string text, string pattern)
        {
            return text != null && text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static NetworkInterface[] Adapters()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                return new NetworkInterface[0];
            }
        }
    }
}
