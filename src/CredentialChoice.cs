namespace MeetingBell
{
    internal sealed class CredentialChoice
    {
        public ExchangeCredential Credential;
        public string Problem;
        public bool Changed;
        public bool Remove;

        public static CredentialChoice Decide(ExchangeCredential saved, string userText, string passwordText)
        {
            string userName = (userText ?? "").Trim();
            string password = passwordText ?? "";
            bool sameUser = saved != null && saved.UserName == userName;
            if (password.Length == 0 && sameUser) password = saved.Password;
            var choice = new CredentialChoice { Credential = new ExchangeCredential { UserName = userName, Password = password } };
            if (userName.Length > 0 && password.Length == 0)
            {
                choice.Problem = saved != null && !sameUser ? "Введи пароль для нового логина Exchange." : "Введи пароль Exchange.";
                return choice;
            }
            choice.Changed = choice.Credential.IsComplete && (saved == null || !sameUser || saved.Password != password);
            choice.Remove = saved != null && userName.Length == 0;
            return choice;
        }
    }
}
