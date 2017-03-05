namespace MusicBeePlugin.PartyMode.Core.Model
{
    public class PartyModeLogs
    {
        public PartyModeLogs(string client, string command, bool deny)
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