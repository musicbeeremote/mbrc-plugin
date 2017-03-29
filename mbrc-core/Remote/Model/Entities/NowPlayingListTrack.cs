namespace MusicBeeRemoteCore.Remote.Model.Entities
{
    [DataContract]
    public class NowPlayingListTrack
    {
        public NowPlayingListTrack(string artist, string title, int position)
        {
            Position = position;
            Artist = artist;
            Title = title;
        }

        public NowPlayingListTrack()
        {
        }

        [DataMember(Name = "Artist")]
        public string Artist { get; set; }

        [DataMember(Name = "Title")]
        public string Title { get; set; }

        [DataMember(Name = "Path")]
        public string Path { get; set; }

        [DataMember(Name = "Position")]
        public int Position { get; set; }
    }
}