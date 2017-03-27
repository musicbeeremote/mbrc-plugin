using System;
using System.Collections.Generic;
using MusicBeeRemoteCore.Remote.Networking;

namespace MusicBeeRemoteCore.PartyMode.Core.Helper
{
    [Serializable]
    public class Settings
    {
        #region constructor

        public Settings(List<RemoteClient> knownClients, uint addressStorageDays)
        {
            _knownClients = knownClients;
            _addressStorageDays = addressStorageDays;
        }

        public Settings()
        {
            _knownClients = new List<RemoteClient>();
            _addressStorageDays = 90;
        }

        #endregion

        #region vars

        private List<RemoteClient> _knownClients;
        private uint _addressStorageDays;
        private bool _isActive;

        #endregion


        public List<RemoteClient> KnownClients
        {
            get { return _knownClients; }
            set { _knownClients = value; }
        }

        public uint AddressStoreDays
        {
            get { return _addressStorageDays; }
            set { _addressStorageDays = value; }
        }

        public bool IsActive
        {
            get { return _isActive; }
            set { _isActive = value; }
        }
    }
}