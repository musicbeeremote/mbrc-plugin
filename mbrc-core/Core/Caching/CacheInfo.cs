using System;
using System.Runtime.Serialization;
using LiteDB;

namespace MusicBeeRemote.Core.Caching
{
    public class CacheInfo
    {
        /// <summary>
        /// Gets or sets the id of the entry.
        /// </summary>
        [BsonId]
        [DataMember]
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the time of most recent update for the tracks table.
        /// </summary>
        [DataMember]
        public DateTime TracksUpdated { get; set; }

        /// <summary>
        ///  Gets or sets the time of the most recent update for the playlists table.
        /// </summary>
        [DataMember]
        public DateTime PlaylistsUpdated { get; set; }

        public void Deconstruct(out long id, out DateTime tracksUpdated, out DateTime playlistsUpdated)
        {
            id = Id;
            tracksUpdated = TracksUpdated;
            playlistsUpdated = PlaylistsUpdated;
        }
    }
}
