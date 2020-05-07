using System;
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
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            using (var db = new LiteDatabase(_storageLocationProvider.CacheDatabase))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                var storedClient = collection.FindById(client.ClientId);
                if (storedClient == null)
                {
                    collection.Insert(client);
                }
                else
                {
                    storedClient.IpAddress = client.IpAddress;
                    storedClient.AddConnection();
                    collection.Update(storedClient);
                }
            }
        }

        public List<RemoteClient> GetKnownClients()
        {
            var knownClients = new List<RemoteClient>();

            using (var db = new LiteDatabase(_storageLocationProvider.CacheDatabase))
            {
                var clients = db.GetCollection<RemoteClient>("clients").FindAll();
                knownClients.AddRange(clients.ToList());
            }

            return knownClients;
        }

        public void UpdateClient(RemoteClient client)
        {
            using (var db = new LiteDatabase(_storageLocationProvider.CacheDatabase))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                collection.Update(client);
            }
        }

        public RemoteClient GetClientById(string clientId)
        {
            RemoteClient client;
            using (var db = new LiteDatabase(_storageLocationProvider.CacheDatabase))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                client = collection.FindById(clientId);
            }

            return client;
        }

        public void ReduceConnections(string clientId)
        {
            using (var db = new LiteDatabase(_storageLocationProvider.CacheDatabase))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                var client = collection.FindById(clientId);
                client.RemoveConnection();
                collection.Update(client);
            }
        }

        public void ResetClientConnections(string clientId)
        {
            using (var db = new LiteDatabase(_storageLocationProvider.CacheDatabase))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                var client = collection
                    .Find(x => x.ClientId == clientId)
                    .FirstOrDefault();

                if (client == null)
                {
                    return;
                }

                client.ResetConnection();
                collection.Update(client);
            }
        }
    }
}
