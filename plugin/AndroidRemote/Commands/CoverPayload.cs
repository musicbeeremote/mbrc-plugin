using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    [DataContract]
    public class CoverPayload
    {
        public CoverPayload(string cover, bool include)
        {
            if (string.IsNullOrEmpty(cover))
            {
                Status = NotFound;
            }
            else
            {
                if (include)
                {
                    Status = CoverAvailable;
                    Cover = cover;
                }
                else
                {
                    Status = CoverReady;
                }
            }
        }


        public const int CoverReady = 1;
        public const int NotFound = 404;
        public const int CoverAvailable = 200;

        [DataMember(Name = "status")]
        public int Status { get; set; }

        [DataMember(Name = "cover")]
        public string Cover { get; set; }

        public override string ToString()
        {
            return $"{nameof(Status)}: {Status}";
        }
    }
}