namespace MeetingBell
{
    internal sealed class ListStatus
    {
        public ListStatus(string text, string action, bool problem)
        {
            Text = text;
            Action = action;
            Problem = problem;
        }

        public string Text { get; private set; }

        public string Action { get; private set; }

        public bool Problem { get; private set; }
    }
}
