using System;
using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model.Entities
{
    [DataContract]
    public class Album : IEquatable<Album>
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

        [DataMember(Name = "count")]
        public int TrackCount { get; private set; }

        public bool Equals(Album other)
        {
            return other != null
                   && other.Artist.Equals(Artist, StringComparison.InvariantCultureIgnoreCase)
                   && other.Name.Equals(Name, StringComparison.InvariantCultureIgnoreCase);
        }

        public override int GetHashCode()
        {
            return Artist.GetHashCode() ^ Name.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Album);
        }
    }
}
