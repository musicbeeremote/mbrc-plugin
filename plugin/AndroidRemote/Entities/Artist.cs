namespace MusicBeePlugin.AndroidRemote.Entities
{
    internal class Artist
    {
        public string artist { get; set; }
        public int count { get; set; }

        public Artist(string artist, int count)
        {
            this.artist = artist;
            this.count = count;
        }
    }
}
