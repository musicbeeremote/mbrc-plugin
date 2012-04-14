using System;

namespace MusicBeePlugin.Events
{
    class ServerMessenger
    {        private static readonly ServerMessenger ClassInstance = new ServerMessenger();
        
        private ServerMessenger()
        {
            
        }

        public static ServerMessenger Instance { get { return ClassInstance; } }

        public event EventHandler<MessageEventArgs> ReplyAvailable;

        public void OnReplyAvailable(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = ReplyAvailable;
            if (handler != null) handler(this, e);
        }
    }
}
