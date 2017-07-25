using System.Collections.Generic;
using System.Linq;
using LiteDB;
using MusicBeeRemote.Core.Settings;

namespace MusicBeeRemote.Core.Network
{
    public class ClientRepository
    {
        private readonly IStorageLocationProvider _storageLocationProvider;

        public ClientRepository(IStorageLocationProvider storageLocationProvider)
        {
            _storageLocationProvider = storageLocationProvider;
        }

        public void InsertClient(RemoteClient client)
        {
            using (var db = new LiteDatabase(_storageLocationProvider.DatabaseFile))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                var storedClient = collection.FindById(client.ClientId);
                if (storedClient == null)
                {
                    collection.Insert(client);
                }
                else
                {
                    collection.Update(client);
                }
            }
        }

        public List<RemoteClient> GetKnownClients()
        {
            var knownClients = new List<RemoteClient>();

            using (var db = new LiteDatabase(_storageLocationProvider.DatabaseFile))
            {
                var clients = db.GetCollection<RemoteClient>("clients").FindAll();
                knownClients.AddRange(clients.ToList());
            }
            return knownClients;
        }

        public void UpdateClient(RemoteClient client)
        {
            using (var db = new LiteDatabase(_storageLocationProvider.DatabaseFile))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                collection.Update(client);
            }
        }

        public RemoteClient GetClientById(string clientId)
        {
            RemoteClient client;
            using (var db = new LiteDatabase(_storageLocationProvider.DatabaseFile))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                client = collection.FindById(clientId);
            }
            
            return client;
        }
    }
}