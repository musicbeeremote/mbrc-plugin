using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    [DataContract]
    public class NowPlayingTrack : NowPlayingTrackBase
    {
        private string _album;
        private string _artist;

        public NowPlayingTrack()
        {
            _artist = Title = _album = Year = string.Empty;
        }

        [DataMember(Name = "artist")]
        public override string Artist
        {
            get => _artist;
            set => _artist = GetArtistText(value);
        }

        [DataMember(Name = "title")]
        public sealed override string Title { get; set; }

        [DataMember(Name = "album")]
        public override string Album
        {
            get => _album;
            set => _album = GetAlbumValue(value);
        }

        [DataMember(Name = "year")]
        public sealed override string Year { get; set; }
    }
}
