using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Podcasts
{
    [DataContract]
    public class PodcastSubscription
    {
        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "description")]
        public string Description { get; set; }
    }
}