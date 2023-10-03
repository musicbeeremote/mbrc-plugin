using System;
using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    /// <summary>
    ///     Class MetaData.
    ///     Represents a packet payload for library meta data.
    /// </summary>
    [DataContract]
    internal class MetaData : IComparable<MetaData>
    {
        private const string Empty = @"[Empty]";
        private string _album;
        private string _artist;
        private string _genre;
        private string _title;

        [IgnoreDataMember] public string File { get; set; }

        [DataMember(Name = "album")]
        public string Album
        {
            get => _album;
            set => _album = string.IsNullOrEmpty(value) ? Empty : value;
        }

        [DataMember(Name = "title")]
        public string Title
        {
            get => _title;
            set =>
                _title = !string.IsNullOrEmpty(value)
                    ? value
                    : string.IsNullOrEmpty(File)
                        ? string.Empty
                        : File.Substring(File.LastIndexOf('\\') + 1);
        }

        [DataMember(Name = "genre")]
        public string Genre
        {
            get => _genre;
            set => _genre = string.IsNullOrEmpty(value) ? Empty : value;
        }

        [DataMember(Name = "year")]
        public string Year { get; set; }

        [DataMember(Name = "track_no")]
        public string TrackNo { get; set; }

        [DataMember(Name = "hash")]
        public string Hash { get; set; }

        [DataMember(Name = "artist")]
        public string Artist
        {
            get => _artist;
            set => _artist = string.IsNullOrEmpty(value) ? Empty : value;
        }

        [DataMember(Name = "album_artist")]
        public string AlbumArtist { get; set; }

        [DataMember(Name = "disc")]
        public string Disc { get; set; }

        public int CompareTo(MetaData other)
        {
            if (!string.IsNullOrEmpty(AlbumArtist) && other.AlbumArtist != AlbumArtist)
                return string.Compare(AlbumArtist, other.AlbumArtist, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(Album) && other.Album != Album)
                return string.Compare(Album, other.Album, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(Disc) && other.Disc != Disc)
            {
                int.TryParse(Disc, out var thisDisc);
                int.TryParse(other.Disc, out var otherDisc);
                return thisDisc - otherDisc;
            }

            int.TryParse(TrackNo, out var thisTrack);
            int.TryParse(other.TrackNo, out var otherTrack);
            return thisTrack - otherTrack;
        }
    }
}