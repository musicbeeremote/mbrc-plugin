using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Model
{
    [DataContract]
    class CoverCacheState
    {
        [DataMember(Name = "covers")]
        public Dictionary<string, string> Covers { get; set; }
        [DataMember(Name = "paths")]
        public long LastCheck { get; set; }
    }
}
