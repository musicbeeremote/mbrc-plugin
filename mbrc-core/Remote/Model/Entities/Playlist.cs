using System.Runtime.Serialization;

namespace MusicBeeRemoteCore.Remote.Model.Entities
{
  [DataContract]
  public class Playlist
  {
    [DataMember(Name = "url")]
    public string Url { get; set; }

    [DataMember(Name = "name")]
    public string Name { get; set; }
  }
}