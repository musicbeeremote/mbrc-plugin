using System.Net;

namespace MbrcPartyMode.Helper
{

    public sealed class ConnectedClientAddress : ClientAddress
    {
        public ConnectedClientAddress(IPAddress ipadr, string clintId) : base(ipadr)
        {
            ClientId = clintId;
        }

        public ConnectedClientAddress(ClientAddress adr, string clintId) : this(adr.IpAddress, clintId)
        {
            CanAddToPlayList = adr.CanAddToPlayList;
            CanDeleteFromPlayList = adr.CanDeleteFromPlayList;
            CanSkipBackwards = adr.CanSkipBackwards;
            CanSkipForwards = adr.CanSkipForwards;
            CanStartStopPlayer = adr.CanStartStopPlayer;
        }

        public string ClientId { get; set; }
    }

}