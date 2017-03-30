using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeeRemoteCore.Remote.Model.Entities
{
  [DataContract]
  public class Page<T>
  {
    [DataMember(Name = "total")]
    public long Total { get; set; }

    [DataMember(Name = "offset")]
    public long Offset { get; set; }

    [DataMember(Name = "limit")]
    public long Limit { get; set; }

    [DataMember(Name = "data")]
    public List<T> Data { get; set; }
  }
}