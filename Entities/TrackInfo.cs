using System;

namespace MusicBeePlugin.Entities
{
    public class TrackInfo
    {

        public TrackInfo(string artist,string title, string album, string year)
        {
            Artist = artist;
            Title = title;
            Album = album;
            Year = year;
        }

        public TrackInfo(string artist, string title)
        {
            Artist = artist;
            Title = title;
            Album = Year = String.Empty;
        }

        public TrackInfo()
        {
            Artist = Title = Artist = Year = String.Empty;
        }

        public string Artist { get; set; }
        public string Title { get; set; }
        public string Album { get; set; }
        public string Year { get; set; }
    }
}
