using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    [DataContract]
    public class NowPlayingDetails
    {
        private string _albumArtist;

        public NowPlayingDetails()
        {
            _albumArtist = TrackNo = TrackCount = DiscNo = DiscCount = Publisher = Composer = Comment = Grouping =
                RatingAlbum = Encoder = Kind = Format = Size = Channels = SampleRate = Bitrate = DateModified =
                    DateAdded = LastPlayed = PlayCount = SkipCount = Duration = string.Empty;
        }

        [DataMember(Name = "albumArtist")]
        public string AlbumArtist
        {
            get { return _albumArtist; }
            set { _albumArtist = string.IsNullOrEmpty(value) ? "Unknown Artist" : value; }
        }
        [DataMember(Name = "genre")]
        public string Genre { get; set; }
        [DataMember(Name = "trackNo")]
        public string TrackNo { get; set; }
        [DataMember(Name = "trackCount")]
        public string TrackCount { get; set; }
        [DataMember(Name = "discNo")]
        public string DiscNo { get; set; }
        [DataMember(Name = "discCount")]
        public string DiscCount { get; set; }
        [DataMember(Name = "publisher")]
        public string Publisher { get; set; }
        [DataMember(Name = "composer")]
        public string Composer { get; set; }
        [DataMember(Name = "comment")]
        public string Comment { get; set; }
        [DataMember(Name = "grouping")]
        public string Grouping { get; set; }
        [DataMember(Name = "ratingAlbum")]
        public string RatingAlbum { get; set; }
        [DataMember(Name = "encoder")]
        public string Encoder { get; set; }

        [DataMember(Name = "kind")]
        public string Kind { get; set; }
        [DataMember(Name = "format")]
        public string Format { get; set; }
        [DataMember(Name = "size")]
        public string Size { get; set; }
        [DataMember(Name = "channels")]
        public string Channels { get; set; }
        [DataMember(Name = "sampleRate")]
        public string SampleRate { get; set; }
        [DataMember(Name = "bitrate")]
        public string Bitrate { get; set; }
        [DataMember(Name = "dateModified")]
        public string DateModified { get; set; }
        [DataMember(Name = "dateAdded")]
        public string DateAdded { get; set; }
        [DataMember(Name = "lastPlayed")]
        public string LastPlayed { get; set; }
        [DataMember(Name = "playCount")]
        public string PlayCount { get; set; }
        [DataMember(Name = "skipCount")]
        public string SkipCount { get; set; }
        [DataMember(Name = "duration")]
        public string Duration { get; set; }
    }
}
