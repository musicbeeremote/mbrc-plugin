using System;

namespace MusicBeePlugin.Events
{
    class MessageEventArgs:EventArgs
    {
        public MessageEventArgs(int clientId)
        {
            ClientId = clientId;
            Message = null;
        }

        public MessageEventArgs(string message)
        {
            ClientId = 0;
            Message = message;
        }

        public MessageEventArgs(string message, int clientId)
        {
            Message = message;
            ClientId = clientId;
        }

        public string Message { get; private set; }
        public int ClientId { get; private set; }

    }
}
