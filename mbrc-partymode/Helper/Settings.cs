using System;
using System.Collections.Generic;

namespace MbrcPartyMode.Helper
{
    [Serializable]
    public class Settings
    {
        #region constructor

        public Settings(List<ClientAddress> knownAdresses, uint addressStorageDays)
        {
            _knownAdresses = knownAdresses;
            _addressStorageDays = addressStorageDays;
        }

        public Settings()
        {
            _knownAdresses = new List<ClientAddress>();
            _addressStorageDays = 90;
        }

        #endregion

        #region vars

        private List<ClientAddress> _knownAdresses;
        private uint _addressStorageDays;
        private bool _isActive;

        #endregion


        public List<ClientAddress> KnownAdresses
        {
            get { return _knownAdresses; }
            set { _knownAdresses = value; }
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