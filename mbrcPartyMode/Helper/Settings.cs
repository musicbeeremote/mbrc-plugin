using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mbrcPartyMode.Helper
{
    [Serializable]
    public class Settings
    {

        #region constructor
        public Settings(List<ClientAdress> knownAdresses, uint addressStorageDays)
        {
            this.knownAdresses = knownAdresses;
            this.addressStorageDays = addressStorageDays;
        }

        public Settings()
        {
            this.knownAdresses = new List<ClientAdress>();
            this.addressStorageDays = 90;
        }

        #endregion

        #region vars


        private List<ClientAdress> knownAdresses;
        private uint addressStorageDays;
        private bool isActive;
        #endregion


        public List<ClientAdress> KnownAdresses
        {
            get { return knownAdresses; }

            set { knownAdresses = value; }
        }

        public uint AddressStoreDays
        {
            get { return addressStorageDays; }

            set { addressStorageDays = value; }
        }

        public bool IsActive
        {
            get { return isActive; }
            set { isActive = value; }
        }


    }
}
