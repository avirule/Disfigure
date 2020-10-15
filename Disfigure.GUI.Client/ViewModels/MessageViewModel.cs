namespace Disfigure.GUI.Client.ViewModels
{
    public class MessageViewModel
    {
        public string Sender { get; }
        public string UtcTimestampString { get; }
        public string Contents { get; }

        public MessageViewModel(string sender, string utcTimestampString, string contents)
        {
            Sender = sender;
            UtcTimestampString = utcTimestampString;
            Contents = contents;
        }
    }
}