using System;

namespace MusicBeeRemote.Core.Commands
{
    [Flags]
    public enum CommandPermissions
    {
        None = 0,
        StartPlayback = 1,
        StopPlayback = 1 << 1,
        PlayNext = 1 << 2,
        PlayPrevious = 1 << 3,
        AddTrack = 1 << 4,
        RemoveTrack = 1 << 5,
        ChangeVolume = 1 << 6,
        ChangeShuffle = 1 << 7,
        ChangeRepeat = 1 << 8,
        CanMute = 1 << 9
    }
}