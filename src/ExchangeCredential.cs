namespace MeetingBell
{
    internal sealed class ExchangeCredential
    {
        public string UserName;
        public string Password;

        public bool IsComplete
        {
            get { return !string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(Password); }
        }
    }
}
