﻿using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    [DataContract]
    public class NowPlayingTrackV2 : NowPlayingTrackBase
    {
        private string _album;
        private string _artist;


        public NowPlayingTrackV2()
        {
            _artist = Title = _album = Year = string.Empty;
        }

        [DataMember(Name = "artist")]
        public override string Artist
        {
            get => _artist;
            set => _artist = GetArtistText(value);
        }

        [DataMember(Name = "title")] public sealed override string Title { get; set; }

        [DataMember(Name = "album")]
        public override string Album
        {
            get => _album;
            set => _album = GetAlbumValue(value);
        }

        [DataMember(Name = "year")] public sealed override string Year { get; set; }

        [DataMember(Name = "path")] public string Path { get; set; }
    }
}