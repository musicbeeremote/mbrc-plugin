namespace mbrcPartyMode.Model
{
    public class ServerMessage
    {
        public ServerMessage(string client, string command, bool deny)
        {
            if (Client == string.Empty)
            {
                Client = "---";
            }
            else
            {
                Client = client;
            }

            Command = command;
            Deny = deny;
        }

        public string Client { get; set; }
        public string Command { get; set; }
        public bool Deny { get; set; }
    }
}