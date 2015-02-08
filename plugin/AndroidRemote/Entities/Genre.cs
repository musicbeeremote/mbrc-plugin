
namespace MusicBeePlugin.AndroidRemote.Entities
{
    class Genre
    {
        public string genre { get; set; }
        public int count { get; set; }
        public Genre(string genre, int count)
        {
            this.genre = genre;
            this.count = count;
        }
    }
}
