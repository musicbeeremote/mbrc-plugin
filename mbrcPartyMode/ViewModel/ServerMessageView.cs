using mbrcPartyMode.Model;

namespace mbrcPartyMode.ViewModel
{
    public class ServerMessageView : ServerMessage
    {
        public ServerMessageView(string client, string command, bool deny) : base(client, command, deny)
        {
            MessageCount = 0;
        }

        public ServerMessageView(int count, ServerMessage msg) : base(msg.Client, msg.Command, msg.Deny)
        {
            MessageCount = count;
        }

        public int MessageCount { get; set; }
    }
}