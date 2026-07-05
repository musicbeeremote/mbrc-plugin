using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Configuration
{
    [DataContract]
    internal sealed class CoverCacheState
    {
        [DataMember(Name = "covers")] public Dictionary<string, string> Covers { get; set; }

        [DataMember(Name = "paths")] public long LastCheck { get; set; }
    }
}
