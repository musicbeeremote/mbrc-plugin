using System;
using System.Runtime.Serialization;
using LiteDB;

namespace MusicBeeRemote.Core.Model.Entities
{
    [DataContract]
    public class Track : IEquatable<Track>, IComparable<Track>
    {
        public Track()
        {
        }

        public Track(string artist, string title, int trackNo, string src)
        {
            Artist = artist;
            Title = title;
            Src = src;
            Trackno = trackNo;
        }
        
        [BsonId]
        [DataMember(Name = "src")]
        public string Src { get; set; }

        [DataMember(Name = "artist")]
        public string Artist { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "trackno")]
        public int Trackno { get; set; }

        [DataMember(Name = "disc")]
        public int Disc { get; set; }

        [DataMember(Name = "album")]
        public string Album { get; set; }

        [DataMember(Name = "album_artist")]
        public string AlbumArtist { get; set; }

        [DataMember(Name = "genre")]
        public string Genre { get; set; }

        [DataMember(Name = "year")]
        public string Year { get; set; }

        public bool Equals(Track other)
        {
            return other != null && other.Artist.Equals(Artist) && other.Title.Equals(Title);
        }

        public int CompareTo(Track other)
        {
            return other == null ? 1 : Trackno.CompareTo(other.Trackno);
        }
    }
}