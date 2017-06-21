using System.Collections.Generic;
using System.Runtime.Serialization;
using MusicBeeRemote.Core.Network;

namespace MusicBeeRemote.PartyMode.Core.Helper
{
    [DataContract]
    public class Settings
    {
        public Settings(List<RemoteClient> knownClients, uint addressStorageDays)
        {
            KnownClients = knownClients;
            AddressStoreDays = addressStorageDays;
        }

        public Settings()
        {
            KnownClients = new List<RemoteClient>();
            AddressStoreDays = 90;
        }

        [DataMember(Name = "known_clients")]
        public List<RemoteClient> KnownClients { get; set; }

        [DataMember(Name = "delete_after")]
        public uint AddressStoreDays { get; set; }

        [DataMember(Name = "active")]
        public bool IsActive { get; set; }
    }
}