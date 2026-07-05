using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Requests
{
    /// <summary>
    ///     Request payload for queueing tracks
    /// </summary>
    [DataContract]
    public class QueueRequest
    {
        [DataMember(Name = "queue")] public string Queue { get; set; }

        [DataMember(Name = "play")] public string Play { get; set; }

        [DataMember(Name = "data")] public List<string> Data { get; set; }
    }
}
