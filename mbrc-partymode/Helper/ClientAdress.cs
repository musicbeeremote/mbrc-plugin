using System;
using System.Net;
using System.Net.NetworkInformation;
using MbrcPartyMode.Tools;

namespace MbrcPartyMode.Helper
{
    [Serializable]
    public class ClientAdress : IEquatable<ClientAdress>
    {
        public ClientAdress(IPAddress ipadr) : this(PartyModeNetworkTools.GetMACAddress(ipadr))
        {
            _ipAddress = ipadr;
        }

        private ClientAdress(PhysicalAddress macAdr)
        {
            _macAddress = macAdr;
            LastLogIn = DateTime.Now;
            _canAddToPlayList = false;
            _skipBackwards = false;
            _skipForwards = false;
            _startStopPlayer = false;
            _canDeleteFromPlayList = false;
        }

        #region vars

        private bool _canAddToPlayList;
        private bool _canDeleteFromPlayList;
        private bool _skipForwards;
        private bool _skipBackwards;
        private bool _startStopPlayer;
        private bool _canVolumeUpDown;
        private bool _canMute;
        private bool _canShuffle;
        private bool _canReplay;

        private PhysicalAddress _macAddress;
        private DateTime _lastLogIn;
        private IPAddress _ipAddress;

        #endregion vars

        #region Properties

        public virtual bool CanAddToPlayList
        {
            get { return _canAddToPlayList; }
            set { _canAddToPlayList = value; }
        }

        public virtual bool CanDeleteFromPlayList
        {
            get { return _canDeleteFromPlayList; }
            set { _canDeleteFromPlayList = value; }
        }

        public virtual bool CanSkipForwards
        {
            get { return _skipForwards; }
            set { _skipForwards = value; }
        }

        public virtual bool CanSkipBackwards
        {
            get { return _skipBackwards; }
            set { _skipBackwards = value; }
        }

        public virtual bool CanStartStopPlayer
        {
            get { return _startStopPlayer; }
            set { _startStopPlayer = value; }
        }

        public virtual bool CanVolumeUpDown
        {
            get { return _canVolumeUpDown; }
            set { _canVolumeUpDown = value; }
        }

        public virtual bool CanMute
        {
            get { return _canMute; }
            set { _canMute = value; }
        }

        public virtual bool CanShuffle
        {
            get { return _canShuffle; }
            set { _canShuffle = value; }
        }

        public virtual bool CanReplay
        {
            get { return _canReplay; }
            set { _canReplay = value; }
        }

        public virtual PhysicalAddress MacAdress
        {
            get { return _macAddress; }
            set { _macAddress = value; }
        }

        public virtual IPAddress IpAddress
        {
            get { return _ipAddress; }
            set { _ipAddress = value; }
        }

        public DateTime LastLogIn
        {
            get { return _lastLogIn; }
            set { _lastLogIn = value; }
        }

        #endregion Properties

        #region Equatable

        bool IEquatable<ClientAdress>.Equals(ClientAdress other)
        {
            return Equals(other);
        }

        public bool Equals(ClientAdress obj)
        {
            var other = obj;

            return _macAddress.ToString() == other._macAddress.ToString();
        }

        #endregion Equatable
    }
}