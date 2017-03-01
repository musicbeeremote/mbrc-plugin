namespace MbrcPartyMode.Model
{
    public class ServerMessage
    {
        public ServerMessage(string client, string command, bool deny)
        {
            Client = Client == string.Empty ? "---" : client;
            Command = command;
            Deny = deny;
        }

        public string Client { get; set; }
        public string Command { get; set; }
        public bool Deny { get; set; }
    }
}