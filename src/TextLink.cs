namespace MeetingBell
{
    internal sealed class TextLink
    {
        public TextLink(int start, int length, string url)
        {
            Start = start;
            Length = length;
            Url = url;
        }

        public int Start { get; private set; }

        public int Length { get; private set; }

        public string Url { get; private set; }

        public bool Contains(int index)
        {
            return index >= Start && index < Start + Length;
        }
    }
}
