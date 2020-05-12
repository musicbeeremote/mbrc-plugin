using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model
{
    [DataContract]
    public class CoverPayload
    {
        [DataMember(Name = "status")]
        public int Status { get; set; }

        [DataMember(Name = "cover", EmitDefaultValue = false)]
        public string Cover { get; set; }

        public override string ToString()
        {
            return $"{nameof(Status)}: {Status}";
        }
    }
}
