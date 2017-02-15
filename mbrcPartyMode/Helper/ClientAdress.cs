using mbrcPartyMode.Tools;
using System;
using System.Net;
using System.Net.NetworkInformation;

namespace mbrcPartyMode.Helper
{
    [Serializable]
    public class ClientAdress : IEquatable<ClientAdress>
    {
        public ClientAdress(IPAddress ipadr) : this(PartyModeNetworkTools.GetMACAddress(ipadr))
        {
            ipAddress = ipadr;
        }

        private ClientAdress(PhysicalAddress macAdr)
        {
            this.macAdress = macAdr;
            this.LastLogIn = DateTime.Now;
            this.canAddToPlayList = false;
            this.skipBackwards = false;
            this.skipForwards = false;
            this.startStopPlayer = false;
            this.canDeleteFromPlayList = false;
        }

        #region vars


        private bool canAddToPlayList;
        private bool canDeleteFromPlayList;
        private bool skipForwards;
        private bool skipBackwards;
        private bool startStopPlayer;
        private bool canVolumeUpDown;
        private bool canMute;
        private bool canShuffel;
        private bool canReplay;

        private PhysicalAddress macAdress;
        private DateTime lastLogIn;
        private IPAddress ipAddress;

        #endregion vars

        #region Properties

        public virtual bool CanAddToPlayList
        {
            get
            {
                return canAddToPlayList;
            }

            set
            {
                canAddToPlayList = value;
            }
        }

        public virtual bool CanDeleteFromPlayList
        {
            get
            {
                return canDeleteFromPlayList;
            }

            set
            {
                canDeleteFromPlayList = value;
            }
        }

        public virtual bool CanSkipForwards
        {
            get
            {
                return skipForwards;
            }

            set
            {
                skipForwards = value;
            }
        }

        public virtual bool CanSkipBackwards
        {
            get
            {
                return skipBackwards;
            }

            set
            {
                skipBackwards = value;
            }
        }

        public virtual bool CanStartStopPlayer
        {
            get
            {
                return startStopPlayer;
            }

            set
            {
                startStopPlayer = value;
            }
        }

        public virtual bool CanVolumeUpDown
        {
            get
            {
                return canVolumeUpDown;
            }

            set
            {
                canVolumeUpDown = value;
            }
        }

        public virtual bool CanMute
        {
            get
            {
                return canMute;
            }

            set
            {
                canMute = value;
            }
        }

        public virtual bool CanShuffle
        {
            get
            {
                return canShuffel;
            }

            set
            {
                canShuffel = value;
            }
        }

        public virtual bool CanReplay
        {
            get
            {
                return canReplay;
            }

            set
            {
                canReplay = value;
            }
        }

        public virtual PhysicalAddress MacAdress
        {
            get { return macAdress; }
            set { macAdress = value; }
        }

        public virtual IPAddress IpAddress
        {
            get { return ipAddress; }
            set { ipAddress = value; }
        }
        public DateTime LastLogIn
        {
            get { return lastLogIn; }
            set { lastLogIn = value; }
        }


        #endregion Properties

        #region Equatable

        bool IEquatable<ClientAdress>.Equals(ClientAdress other)
        {
            return this.Equals(other);
        }

        public bool Equals(ClientAdress obj)
        {
            ClientAdress other;

            other = (ClientAdress)obj;

            return macAdress.ToString() == other.macAdress.ToString();
        }

        #endregion Equatable
    }
}