using System.Globalization;
using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Commands
{
    /// <summary>
    /// Payload for cover responses. When Cover is null, it will be omitted from
    /// serialization due to the global NullValueHandling.Ignore setting in SocketMessage.
    /// </summary>
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
                Cover = null; // Will be omitted from JSON due to NullValueHandling.Ignore
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
                    Cover = null; // Will be omitted from JSON due to NullValueHandling.Ignore
                }
            }
        }

        [DataMember(Name = "status")]
        public int Status { get; set; }

        [DataMember(Name = "cover")]
        public string Cover { get; set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1}", nameof(Status), Status);
        }
    }
}
