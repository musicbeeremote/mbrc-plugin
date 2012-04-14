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
            ClientId = -1;
            Message = message;
        }

        public string Message { get; private set; }
        public int ClientId { get; private set; }

    }
}
