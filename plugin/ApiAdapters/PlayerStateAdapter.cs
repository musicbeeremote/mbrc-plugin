using System;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Core.Model;
using MusicBeeRemoteCore.Remote.Enumerations;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    public class PlayerStateAdapter : IPlayerStateAdapter
    {
        private readonly MusicBeeApiInterface _api;

        public PlayerStateAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        public ShuffleState GetShuffleState()
        {
            var shuffleEnabled = _api.Player_GetShuffle();
            var autoDjEnabled = _api.Player_GetAutoDjEnabled();
            var state = ShuffleState.off;
            if (shuffleEnabled && !autoDjEnabled)
            {
                state = ShuffleState.shuffle;
            }
            else if (autoDjEnabled)
            {
                state = ShuffleState.autodj;
            }

            return state;
        }

        public Repeat GetRepeatMode()
        {
            var repeat = _api.Player_GetRepeat();
            Repeat repeatMode;
            switch (repeat)
            {
                case RepeatMode.None:
                    repeatMode = Repeat.None;
                    break;
                case RepeatMode.All:
                    repeatMode = Repeat.All;
                    break;
                case RepeatMode.One:
                    repeatMode = Repeat.One;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return repeatMode;
        }


        public bool ScrobblingEnabled()
        {
            return _api.Player_GetScrobbleEnabled();
        }

        public bool PlayNext()
        {
            return _api.Player_PlayNextTrack();
        }

        public bool PlayPrevious()
        {
            return _api.Player_PlayPreviousTrack();
        }

        public bool StopPlayback()
        {
            return _api.Player_Stop();
        }

        public bool PlayPause()
        {
            return _api.Player_PlayPause();
        }

        public bool Play()
        {
            return _api.Player_GetPlayState() != PlayState.Playing && _api.Player_PlayPause();
        }

        public bool Pause()
        {
            return _api.Player_GetPlayState() == PlayState.Playing && _api.Player_PlayPause();
        }

        public PlayerStatus GetStatus()
        {
            throw new NotImplementedException();
        }

        public PlayerState GetState()
        {
            switch (_api.Player_GetPlayState())
            {
                case PlayState.Undefined:
                    return PlayerState.Undefined;
                case PlayState.Loading:
                    return PlayerState.Loading;
                case PlayState.Playing:
                    return PlayerState.Playing;
                case PlayState.Paused:
                    return PlayerState.Paused;
                case PlayState.Stopped:
                    return PlayerState.Paused;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}