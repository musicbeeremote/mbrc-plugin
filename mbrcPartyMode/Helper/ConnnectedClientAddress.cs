using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Net;

namespace mbrcPartyMode.Helper
{

    public class ConnectedClientAddress : ClientAdress
    {
        string clintId;

        public ConnectedClientAddress(IPAddress ipadr, string clintId) : base(ipadr)
        {
            ClientId = clintId;
        }

        public ConnectedClientAddress(ClientAdress adr, string clintId) : this(adr.IpAddress, clintId)
        {
            this.CanAddToPlayList = adr.CanAddToPlayList;
            this.CanDeleteFromPlayList = adr.CanDeleteFromPlayList;
            this.CanSkipBackwards = adr.CanSkipBackwards;
            this.CanSkipForwards = adr.CanSkipForwards;
            this.CanStartStopPlayer = adr.CanStartStopPlayer;
            
        }
        public string ClientId
        {
            get
            {
                return clintId;
            }

            set
            {
                clintId = value;
            }
        }


    }

}