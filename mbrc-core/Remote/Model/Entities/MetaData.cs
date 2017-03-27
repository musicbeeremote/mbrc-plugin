using System;

namespace MusicBeeRemoteCore.Remote.Model.Entities
{
    /// <summary>
    /// Class MetaData. 
    /// Represents a packet payload for library meta data.
    /// </summary>
    internal class MetaData : IComparable<MetaData>
    {
        private const string Empty = @"[Empty]";
        private string _artist;
        private string _album;
        private string _title;
        private string _genre;

        [IgnoreDataMember]
        public string File { get; set; }

        public string Album
        {
            get { return _album; }
            set { _album = string.IsNullOrEmpty(value) ? Empty : value; }
        }

        public string Title
        {
            get { return _title; }
            set
            {
                _title = !string.IsNullOrEmpty(value)
                    ? value
                    : (string.IsNullOrEmpty(File)
                        ? string.Empty
                        : File.Substring(File.LastIndexOf('\\') + 1));
            }
        }

        public string Genre
        {
            get { return _genre; }
            set { _genre = string.IsNullOrEmpty(value) ? Empty : value; }
        }

        public string Year { get; set; }

        public string TrackNo { get; set; }

        public string Hash { get; set; }

        public string Artist
        {
            get { return _artist; }
            set { _artist = string.IsNullOrEmpty(value) ? Empty : value; }
        }

        public string AlbumArtist { get; set; }

        public string Disc { get; set; }

        public int CompareTo(MetaData other)
        {
            if (!string.IsNullOrEmpty(AlbumArtist) && other.AlbumArtist != AlbumArtist)
            {
                return string.Compare(AlbumArtist, other.AlbumArtist, StringComparison.OrdinalIgnoreCase);
            }
            if (!string.IsNullOrEmpty(Album) && other.Album != Album)
            {
                return string.Compare(Album, other.Album, StringComparison.OrdinalIgnoreCase);
            }
            if (!string.IsNullOrEmpty(Disc) && other.Disc != Disc)
            {
                int thisDisc;
                int otherDisc;
                int.TryParse(Disc, out thisDisc);
                int.TryParse(other.Disc, out otherDisc);
                return thisDisc - otherDisc;
            }

            int thisTrack;
            int otherTrack;
            int.TryParse(TrackNo, out thisTrack);
            int.TryParse(other.TrackNo, out otherTrack);
            return thisTrack - otherTrack;

        }
    }
}
