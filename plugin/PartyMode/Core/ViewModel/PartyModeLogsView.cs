using MusicBeePlugin.PartyMode.Core.Model;

namespace MusicBeePlugin.PartyMode.Core.ViewModel
{
    public class PartyModeLogsView : PartyModeLogs
    {
        public PartyModeLogsView(string client, string command, bool deny) : base(client, command, deny)
        {
            MessageCount = 0;
        }

        public PartyModeLogsView(int count, PartyModeLogs msg) : base(msg.Client, msg.Command, msg.Deny)
        {
            MessageCount = count;
        }

        public int MessageCount { get; set; }
    }
}