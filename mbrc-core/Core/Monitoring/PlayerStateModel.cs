using MusicBeeRemote.Core.Enumerations;

namespace MusicBeeRemote.Core.Monitoring
{
    public class PlayerStateModel
    {
        /// <summary>
        /// The current player shuffle state.
        /// </summary>
        public ShuffleState Shuffle { get; set; }

        /// <summary>
        /// The current player repeat mode.
        /// </summary>
        public Repeat RepeatMode { get; set; }

        /// <summary>
        /// The last.fm scrobble state.
        /// If enabled the tracks are currently scrobbled to last.fm.
        /// </summary>
        public bool Scrobble { get; set; }
    }
}
