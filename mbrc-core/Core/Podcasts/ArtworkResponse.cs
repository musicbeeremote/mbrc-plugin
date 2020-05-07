using System.Runtime.Serialization;
using MusicBeeRemote.Core.Network.Http;

namespace MusicBeeRemote.Core.Podcasts
{
    [DataContract]
    public class ArtworkResponse : Response
    {
        [DataMember(Name = "artwork")]
        public string Artwork { get; set; }
    }
}
