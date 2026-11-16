using System.Collections.Generic;

namespace MeetingBell
{
    internal static class UninstallProfile
    {
        public const string ConfirmDetails =
            "Логин и пароль Exchange будут удалены из диспетчера учётных данных Windows, настройки встреч — вместе с данными программы. "
            + "Это делается для учётной записи Windows, под которой идёт удаление: если окно UAC подтверждала другая учётная запись, "
            + "сначала очисти логин в настройках программы.";

        public static void Remove(List<string> problems)
        {
            string problem = CredentialStore.Delete();
            if (problem != null) problems.Add("логин Exchange в диспетчере учётных данных не удалён: " + problem);
        }
    }
}
