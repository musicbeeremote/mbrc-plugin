using System.Net;

namespace MbrcPartyMode.Helper
{

    public sealed class ConnectedClientAddress : ClientAdress
    {
        public ConnectedClientAddress(IPAddress ipadr, string clintId) : base(ipadr)
        {
            ClientId = clintId;
        }

        public ConnectedClientAddress(ClientAdress adr, string clintId) : this(adr.IpAddress, clintId)
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