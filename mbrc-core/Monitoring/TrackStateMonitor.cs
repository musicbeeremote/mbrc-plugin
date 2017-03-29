using System.Timers;
using TinyMessenger;

namespace MusicBeeRemoteCore.Monitoring
{
    class TrackStateMonitor : ITrackStateMonitor
    {
        private readonly ITinyMessengerHub _hub;

        private Timer _positionUpdateTimer;

        //todo track cover lyrics changes and track info changes and notify client when needed
        public TrackStateMonitor(ITinyMessengerHub hub)
        {
            _hub = hub;
        }

        public void Start()
        {
            _positionUpdateTimer = new Timer(20000);
            _positionUpdateTimer.Elapsed += PositionUpdateTimerOnElapsed;
            _positionUpdateTimer.Enabled = true;
        }

        public void Stop()
        {
            _positionUpdateTimer.Enabled = false;
            _positionUpdateTimer.Stop();
            _positionUpdateTimer.Close();
        }

        private void PositionUpdateTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (_api.Player_GetPlayState() == PlayState.Playing)
            {
                RequestPlayPosition("status");
            }
        }
    }
}