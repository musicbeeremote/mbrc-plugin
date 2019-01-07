using System;
using System.Timers;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Monitoring;

namespace MbrcTester
{
    public class MockPlayer
    {
        private readonly MockPlayerState _playerState;
        private readonly MockNowPlaying _nowPlaying;
        private string rating;
        private Timer timer;

        private int position;
        private int duration;

        private Random rnd = new Random();

        public MockPlayer(MockPlayerState playerState, MockNowPlaying nowPlaying)
        {
            position = 0;
            duration = (int) Math.Round(4.6 * 60) * 1000;
            _playerState = playerState;
            _nowPlaying = nowPlaying;
            timer = new Timer(1000);
            timer.Elapsed += (sender, args) => { position += 1000; };
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
                next = rnd.Next(0, list.Count);
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
            position = 0;
            _playerState.SetPlayerState(PlayerState.Playing);
            timer.Start();
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
                previous = rnd.Next(0, list.Count);
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
            timer.Stop();
            _playerState.SetPlayerState(PlayerState.Stopped);
            return true;
        }

        public bool PlayPause()
        {
            if (_playerState.GetPlayerState() == PlayerState.Playing)
            {
                timer.Stop();
                _playerState.SetPlayerState(PlayerState.Paused);
            }
            else
            {
                timer.Start();
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
            this.rating = rating;
            return rating;
        }

        public string GetRating()
        {
            return rating;
        }

        public TrackTemporalnformation GetTemporalInformation()
        {
            return new TrackTemporalnformation(position, duration);
        }

        public bool SeekTo(int position)
        {
            this.position = position;
            return true;
        }
    }
}