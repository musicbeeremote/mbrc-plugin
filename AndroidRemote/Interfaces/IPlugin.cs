using System;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Interfaces
{
    /// <summary>
    /// Represents the basic functionality of the plugin
    /// </summary>
    public interface IPlugin
    {
        /// <summary>
        /// Represents a change in the state of the player.
        /// </summary>
        event EventHandler<DataEventArgs> PlayerStateChanged;

        /// <summary>
        /// When called plays the next track.
        /// </summary>
        /// <returns></returns>
        void RequestNextTrack(int clientId);

        /// <summary>
        /// When called stops the playback.
        /// </summary>
        /// <returns></returns>
        void RequestStopPlayback(int clientId);

        /// <summary>
        /// When called changes the play/pause state or starts playing a track if the status is stopped.
        /// </summary>
        /// <returns></returns>
        void RequestPlayPauseTrack(int clientId);

        /// <summary>
        /// When called plays the previous track.
        /// </summary>
        /// <returns></returns>
        void RequestPreviousTrack(int clientId);

        /// <summary>
        /// When called if the volume string is an integer in the range [0,100] it 
        /// changes the volume to the specific value and returns the new value.
        /// In any other case it just returns the current value for the volume.
        /// </summary>
        /// <param name="volume"> </param>
        void RequestVolumeChange(int volume);

        /// <summary>
        /// Changes the player shuffle state. If the StateAction is Toggle then the current state is switched with it's opposite,
        /// if it is State the current state is dispatched with an Event.
        /// </summary>
        /// <param name="action"></param>
        void RequestShuffleState(StateAction action);

        /// <summary>
        /// Changes the player mute state. If the StateAction is Toggle then the current state is switched with it's opposite,
        /// if it is State the current state is dispatched with an Event.
        /// </summary>
        /// <param name="action"></param>
        void RequestMuteState(StateAction action);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        void RequestScrobblerState(StateAction action);

        /// <summary>
        /// If the action equals toggle then it changes the repeat state, in any other case
        /// it just returns the current value of the repeat.
        /// </summary>
        /// <param name="action">toggle or state</param>
        /// <returns>Repeat state: None, All, One</returns>
        void RequestRepeatState(StateAction action);

        /// <summary>
        /// It gets the 100 first tracks of the playlist and returns them in an XML formated String without a root element.
        /// </summary>
        /// <param name="clientProtocolVersion"> </param>
        /// <param name="clientId"> </param>
        /// <returns>XML formated string without root element</returns>
        void RequestNowPlayingList(double clientProtocolVersion, int clientId);

        /// <summary>
        /// Searches in the Now playing list for the track specified and plays it.
        /// </summary>
        /// <param name="trackInfo">The track to play</param>
        /// <returns></returns>
        string PlaylistGoToSpecifiedTrack(string trackInfo);

        /// <summary>
        /// If the given rating string is not null or empty and the value of the string is a float number in the [0,5]
        /// the function will set the new rating as the current track's new track rating. In any other case it will
        /// just return the rating for the current track.
        /// </summary>
        /// <param name="rating">New Track Rating</param>
        /// <param name="clientId"> </param>
        /// <returns>Track Rating</returns>
        void RequestTrackRating(string rating, int clientId);

        /// <summary>
        /// Requests the Now Playing track lyrics. If the lyrics are available then they are dispatched along with
        /// and event. If not, and the ApiRevision is equal or greater than r17 a request for the downloaded lyrics
        /// is initiated. The lyrics are dispatched along with and event when ready.
        /// </summary>
        void RequestNowPlayingTrackLyrics();

        /// <summary>
        /// Requests the Now Playing Track Cover. If the cover is available it is dispatched along with an event.
        /// If not, and the ApiRevision is equal or greater than r17 a request for the downloaded artwork is
        /// initiated. The cover is dispatched along with an event when ready.
        /// </summary>
        void RequestNowPlayingTrackCover();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        void RequestPlayPosition(string request);

        /// <summary>
        /// 
        /// </summary>
        void RemoveTrackFromNowPlayingList(int index,int clientId);
    }
}
