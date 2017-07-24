using System.Runtime.Serialization;

namespace MusicBeeRemote.PartyMode.Core.Helper
{
    [DataContract]
    public class Settings
    {
        public Settings(uint addressStorageDays)
        {
            AddressStoreDays = addressStorageDays;
        }

        public Settings()
        {            
            AddressStoreDays = 90;
        }      

        [DataMember(Name = "delete_after")]
        public uint AddressStoreDays { get; set; }

        [DataMember(Name = "active")]
        public bool IsActive { get; set; }
    }
}