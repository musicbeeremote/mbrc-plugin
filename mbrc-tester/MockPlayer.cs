using System;
using System.Timers;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Monitoring;

namespace MbrcTester
{
    public class MockPlayer : IDisposable
    {
        private readonly MockPlayerState _playerState;
        private readonly MockNowPlaying _nowPlaying;
        private readonly Timer _timer;
        private readonly Random _rnd = new Random();
        private readonly int _duration;

        private string _rating;

        private int _position;
        private bool _disposed;

        public MockPlayer(MockPlayerState playerState, MockNowPlaying nowPlaying)
        {
            _position = 0;
            _duration = (int)Math.Round(4.6 * 60) * 1000;
            _playerState = playerState;
            _nowPlaying = nowPlaying;
            _timer = new Timer(1000);
            _timer.Elapsed += (sender, args) => { _position += 1000; };
        }

        public MockTrackMetadata PlayingTrack { get; private set; } = new MockTrackMetadata();

        public bool PlayNextTrack()
        {
            var list = _nowPlaying.NowPlayingList;

            if (list.Count == 0)
            {
                Stop();
                return true;
            }

            var next = list.IndexOf(PlayingTrack);

            if (!_playerState.GetShuffle() && !_playerState.GetAutoDjEnabled())
            {
                next += 1;
            }
            else
            {
                next = _rnd.Next(0, list.Count);
            }

            if (list.Count < next)
            {
                PlayIndex(next);
            }
            else
            {
                Stop();
            }

            return true;
        }

        public void PlayIndex(int index)
        {
            _position = 0;
            _playerState.SetPlayerState(PlayerState.Playing);
            _timer.Start();
            PlayingTrack = _nowPlaying.NowPlayingList[index];
        }

        public bool PlayPreviousTrack()
        {
            var list = _nowPlaying.NowPlayingList;

            if (list.Count == 0)
            {
                Stop();
                return true;
            }

            var previous = list.IndexOf(PlayingTrack);

            if (!_playerState.GetShuffle() && !_playerState.GetAutoDjEnabled())
            {
                previous -= 1;
            }
            else
            {
                previous = _rnd.Next(0, list.Count);
            }

            if (previous >= 0)
            {
                PlayIndex(previous);
            }
            else
            {
                Stop();
            }

            return true;
        }

        public bool Stop()
        {
            _timer.Stop();
            _playerState.SetPlayerState(PlayerState.Stopped);
            return true;
        }

        public bool PlayPause()
        {
            if (_playerState.GetPlayerState() == PlayerState.Playing)
            {
                _timer.Stop();
                _playerState.SetPlayerState(PlayerState.Paused);
            }
            else
            {
                _timer.Start();
                _playerState.SetPlayerState(PlayerState.Playing);
            }

            return true;
        }

        public bool StartAutoDj()
        {
            _playerState.SetAutoDj(true);
            return true;
        }

        public void EndAutoDj()
        {
            _playerState.SetAutoDj(false);
        }

        public string SetRating(string rating)
        {
            this._rating = rating;
            return rating;
        }

        public string GetRating()
        {
            return _rating;
        }

        public TrackTemporalInformation GetTemporalInformation()
        {
            return new TrackTemporalInformation(_position, _duration);
        }

        public bool SeekTo(int position)
        {
            this._position = position;
            return true;
        }

        public bool PlayPath(string path)
        {
            var match = _nowPlaying.NowPlayingList.Find(metadata => metadata._id == path);
            if (match == null)
            {
                return false;
            }

            PlayingTrack = match;
            return true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _timer.Dispose();
            }

            _disposed = true;
        }
    }
}
