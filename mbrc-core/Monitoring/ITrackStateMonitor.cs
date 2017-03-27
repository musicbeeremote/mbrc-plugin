using System.Timers;

namespace MusicBeeRemoteCore.Monitoring
{
    public interface ITrackStateMonitor : IStateMonitor
    {

    }

    class TrackStateMonitor : ITrackStateMonitor
    {

        private Timer _positionUpdateTimer;

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