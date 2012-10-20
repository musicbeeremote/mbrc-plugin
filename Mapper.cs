using System;
using MusicBeePlugin.AndroidRemote.Enumerations;

namespace MusicBeePlugin
{
    static class Mapper
    {
        public static Repeat MapRepeatEnum(Plugin.RepeatMode mode)
        {
            switch (mode)
            {
                case Plugin.RepeatMode.None:
                    return Repeat.None;
                case Plugin.RepeatMode.All:
                    return Repeat.All;
                case Plugin.RepeatMode.One:
                    return Repeat.One;
                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        public static PlayerState MapPlayStateEnum(Plugin.PlayState state)
        {
            switch(state)
            {
                case Plugin.PlayState.Undefined:
                    return PlayerState.Undefined;
                case Plugin.PlayState.Loading:
                    return PlayerState.Loading;
                case Plugin.PlayState.Playing:
                    return PlayerState.Playing;
                case Plugin.PlayState.Paused:
                    return PlayerState.Paused;
                case Plugin.PlayState.Stopped:
                    return PlayerState.Stopped;
                default:
                    throw new ArgumentOutOfRangeException("state");
            }
        }
    }
}
