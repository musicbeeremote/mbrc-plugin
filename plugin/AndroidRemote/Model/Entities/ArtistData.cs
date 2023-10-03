namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    internal class ArtistData
    {
        public ArtistData(string artist, int count)
        {
            Artist = artist;
            Count = count;
        }

        public string Artist { get; set; }
        public int Count { get; set; }
    }
}