using System;

namespace MusicBeePlugin
{
    class Messenger
    {
        private static readonly Messenger ClassInstance = new Messenger();
        
        private Messenger()
        {
            
        }

        static Messenger()
        {
        
        }

        public static Messenger Instance { get { return ClassInstance; } }
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
    }
}
