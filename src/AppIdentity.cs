using System;
using System.IO;

namespace MeetingBell
{
    internal static class AppIdentity
    {
        public const string Name = "MeetingBell";
        public const string Version = "1.0";
        public const string UpdateSetupArguments = "";
        public const string UpdateKeeps = "настройки, логин Exchange и настройки встреч сохранятся";
        public const string ExecutableName = "MeetingBell.exe";
        public const string ProcessName = "MeetingBell";
        public const string UninstallerName = "Uninstall.exe";
        public const string SingleInstanceMutex = "MeetingBell.SingleInstance";
        public const string GitHubUrl = "https://github.com/aokazantsev/MeetingBell";
        public const string SiteUrl = "https://aokazantsev.ru/pets/meetingbell/";
        public static readonly string[] PayloadFiles = { ExecutableName, ExecutableName + ".config", UninstallerName, "README.md", "LICENSE", "CHANGELOG.md" };

        public static string DefaultInstallDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name); }
        }

        public static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name); }
        }
    }
}
