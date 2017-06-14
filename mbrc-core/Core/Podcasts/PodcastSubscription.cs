using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Podcasts
{
    [DataContract(Name = "subscription")]
    public class PodcastSubscription
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "grouping")]
        public string Grouping { get; set; }

        [DataMember(Name = "genre")]
        public string Genre { get; set; }

        [DataMember(Name = "description")]
        public string Description { get; set; }

        [DataMember(Name = "downloaded")]
        public uint Downloaded { get; set; }
    }
}