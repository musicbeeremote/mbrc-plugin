using System;
using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    [DataContract]
    internal class Album : IEquatable<Album>
    {

        public Album(string artist, string name)
        {
            Name = name;
            Artist = artist;
            TrackCount = 1;
        }

        [DataMember(Name = "album")]
        public string Name { get; }

        [DataMember(Name = "artist")]
        public string Artist { get; }

        public void IncreaseCount()
        {
            TrackCount++;
        }

        [DataMember(Name = "count")]
        public int TrackCount { get; private set; }

        public bool Equals(Album other)
        {
            return other != null && (other.Artist.Equals(Artist) && other.Name.Equals(Name));
        }

        public override int GetHashCode()
        {
            return Artist.GetHashCode() ^ Name.GetHashCode();
        }
    }
}
