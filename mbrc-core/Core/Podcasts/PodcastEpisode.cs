using System;
using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Podcasts
{
    [DataContract(Name = "episode")]
    public class PodcastEpisode
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "date")]
        public DateTime Date { get; set; }

        [DataMember(Name = "description")]
        public string Description { get; set; }

        [DataMember(Name = "duration")]
        public string Duration { get; set; }

        [DataMember(Name = "downloaded")]
        public bool Downloaded { get; set; }

        [DataMember(Name = "played")]
        public bool Played { get; set; }
    }
}
