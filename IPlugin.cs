namespace MusicBeePlugin
{
    public interface IPlugin
    {
        bool SongChanged { get; set; }

        /// <summary>
        /// Returns the artist name for the track playing.
        /// </summary>
        /// <returns>Track artist string</returns>
        string GetCurrentTrackArtist();

        /// <summary>
        /// Returns the album for the track playing.
        /// </summary>
        /// <returns>Track album string</returns>
        string GetCurrentTrackAlbum();

        /// <summary>
        /// Returns the title for the track playing.
        /// </summary>
        /// <returns>Track title string</returns>
        string GetCurrentTrackTitle();

        /// <summary>
        /// Returns the Year for the track playing.
        /// </summary>
        /// <returns>Track year string</returns>
        string GetCurrentTrackYear();

        /// <summary>
        /// It retrieves the album cover as a Base64 encoded string for the track playing it resizes it to
        /// 300x300 and returns the resized image in a Base64 encoded string.
        /// </summary>
        /// <returns></returns>
        string GetCurrentTrackCover();

        /// <summary>
        /// Retrieves the lyrics for the track playing.
        /// </summary>
        /// <returns>Lyrics String</returns>
        string RetrieveCurrentTrackLyrics();

        /// <summary>
        /// When called plays the next track.
        /// </summary>
        /// <returns></returns>
        string PlayerPlayNextTrack();

        /// <summary>
        /// When called stops the playback.
        /// </summary>
        /// <returns></returns>
        string PlayerStopPlayback();

        /// <summary>
        /// When called changes the play/pause state or starts playing a track if the status is stopped.
        /// </summary>
        /// <returns></returns>
        string PlayerPlayPauseTrack();

        /// <summary>
        /// When called plays the previous track.
        /// </summary>
        /// <returns></returns>
        string PlayerPlayPreviousTrack();

        /// <summary>
        /// When called if the volume string is an integer in the range [0,100] it 
        /// changes the volume to the specific value and returns the new value.
        /// In any other case it just returns the current value for the volume.
        /// </summary>
        /// <param name="vol">New volume String</param>
        /// <returns>Volume int [0,100]</returns>
        string PlayerVolume(string vol);

        /// <summary>
        /// Returns the state of the player.
        /// </summary>
        /// <returns>possible values: undefined, loading, playing, paused, stopped</returns>
        string PlayerPlayState();

        /// <summary>
        /// If the action equals toggle then it changes the shuffle state, in any other case
        /// it just returns the current value of the shuffle.
        /// </summary>
        /// <param name="action">toggle or state</param>
        /// <returns>Shuffle state: True or False</returns>
        string PlayerShuffleState(string action);

        /// <summary>
        /// If the action equals toggle then it changes the repeat state, in any other case
        /// it just returns the current value of the repeat.
        /// </summary>
        /// <param name="action">toggle or state</param>
        /// <returns>Repeat state: None, All, One</returns>
        string PlayerRepeatState(string action);

        /// <summary>
        /// If the action is toggle then the function changes the repeat state, in any other case
        /// it just returns the current value of the Mute.
        /// </summary>
        /// <param name="action">toggle or state</param>
        /// <returns>Mute state: True or False</returns>
        string PlayerMuteState(string action);

        /// <summary>
        /// It gets the 100 first tracks of the playlist and returns them in an XML formated String without a root element.
        /// </summary>
        /// <param name="clientProtocolVersion"> </param>
        /// <param name="serverProtocolVersion"> </param>
        /// <returns>XML formated string without root element</returns>
        string PlaylistGetTracks(double clientProtocolVersion, double serverProtocolVersion);

        /// <summary>
        /// Searches in the Now playing list for the track specified and plays it.
        /// </summary>
        /// <param name="trackInfo">The track to play</param>
        /// <returns></returns>
        string PlaylistGoToSpecifiedTrack(string trackInfo);

        /// <summary>
        /// If the action is toggle then the function changes the scrobbler state, in any other case
        /// it just returns the current value of the Scrobbler.
        /// </summary>
        /// <param name="action">toggle or action</param>
        /// <returns>Scrobbler state</returns>
        string ScrobblerState(string action);

        /// <summary>
        /// If the given rating string is not null or empty and the value of the string is a float number in the [0,5]
        /// the function will set the new rating as the current track's new track rating. In any other case it will
        /// just return the rating for the current track.
        /// </summary>
        /// <param name="rating">New Track Rating</param>
        /// <returns>Track Rating</returns>
        string TrackRating(string rating);
    }
}