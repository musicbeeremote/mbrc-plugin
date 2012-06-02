using System;

namespace MusicBeePlugin.Events
{
    public class MessageEventArgs:EventArgs
    {
        public MessageEventArgs(int clientId)
        {
            ClientId = clientId;
            Message = null;
        }

        public MessageEventArgs(string message)
        {
            ClientId = -1;
            Message = message;
        }

        public MessageEventArgs(String message, int clientId)
        {
            ClientId = clientId;
            Message = message;
        }

        public string Message { get; private set; }
        public int ClientId { get; private set; }

    }
}
