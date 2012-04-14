using System;

namespace MusicBeePlugin.Events
{
    class StatusMessenger
    {
        private static readonly StatusMessenger ClassInstance = new StatusMessenger();
        
        private StatusMessenger()
        {
            
        }

        public static StatusMessenger Instance { get { return ClassInstance; } }
        public event EventHandler VolumeLevelChanged;

        public void OnVolumeLevelChanged(EventArgs e)
        {
            EventHandler handler = VolumeLevelChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler VolumeMuteChanged;

        public void OnVolumeMuteChanged(EventArgs e)
        {
            EventHandler handler = VolumeMuteChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler TrackChanged;

        public void OnTrackChanged(EventArgs e)
        {
            EventHandler handler = TrackChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler PlayStateChanged;

        public void OnPlayStateChanged(EventArgs e)
        {
            EventHandler handler = PlayStateChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler RepeatStateChanged;

        public void OnRepeatStateChanged(EventArgs e)
        {
            EventHandler handler = RepeatStateChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler ShuffleStateChanged;

        public void OnShuffleStateChanged(EventArgs e)
        {
            EventHandler handler = ShuffleStateChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler ScrobbleStateChanged;

        public void OnScrobbleStateChanged(EventArgs e)
        {
            EventHandler handler = ScrobbleStateChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<MessageEventArgs> ClientConnected;

        public void OnClientConnected(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = ClientConnected;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<MessageEventArgs> ClientDisconnected;

        public void OnClientDisconnected(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = ClientDisconnected;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<MessageEventArgs> DisconnectClient;

        public void OnDisconnectClient(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = DisconnectClient;
            if (handler != null) handler(this, e);
        }
    }
}
