using System;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    /// <summary>
    /// 
    /// </summary>
    public class TrackInfo
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="artist"></param>
        /// <param name="title"></param>
        /// <param name="album"></param>
        /// <param name="year"></param>
        public TrackInfo(string artist,string title, string album, string year)
        {
            Artist = artist;
            Title = title;
            Album = album;
            Year = year;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="artist"></param>
        /// <param name="title"></param>
        public TrackInfo(string artist, string title)
        {
            Artist = artist;
            Title = title;
            Album = Year = String.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        public TrackInfo()
        {
            Artist = Title = Artist = Year = String.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        public string Artist { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string Album { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string Year { get; set; }
    }
}
