using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    [DataContract]
    public class CoverPayload
    {
        private const int CoverReady = 1;
        private const int NotFound = 404;
        private const int CoverAvailable = 200;

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

        [DataMember(Name = "status")] public int Status { get; set; }

        [DataMember(Name = "cover")] public string Cover { get; set; }

        public override string ToString()
        {
            return $"{nameof(Status)}: {Status}";
        }
    }
}