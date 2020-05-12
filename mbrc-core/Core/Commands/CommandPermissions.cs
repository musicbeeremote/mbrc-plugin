using System;

namespace MusicBeeRemote.Core.Commands
{
    [Flags]
    public enum CommandPermissions
    {
        /// <summary>
        /// No special permissions
        /// </summary>
        None = 0,

        /// <summary>
        /// The permission to start playback.
        /// </summary>
        StartPlayback = 1,

        /// <summary>
        /// The permission to stop playback
        /// </summary>
        StopPlayback = 1 << 1,

        /// <summary>
        /// The permission to change track to the next available track.
        /// </summary>
        PlayNext = 1 << 2,

        /// <summary>
        /// The permission to change track to the previous available track.
        /// </summary>
        PlayPrevious = 1 << 3,

        /// <summary>
        /// The permission to add a track to the now playing list.
        /// </summary>
        AddTrack = 1 << 4,

        /// <summary>
        /// The permission to remove a track from the now playing list.
        /// </summary>
        RemoveTrack = 1 << 5,

        /// <summary>
        /// The permission to change the volume.
        /// </summary>
        ChangeVolume = 1 << 6,

        /// <summary>
        /// The permission to change the shuffle mode.
        /// </summary>
        ChangeShuffle = 1 << 7,

        /// <summary>
        /// The permission to change the repeat mode.
        /// </summary>
        ChangeRepeat = 1 << 8,

        /// <summary>
        /// The permission to mute.
        /// </summary>
        CanMute = 1 << 9,
    }
}
