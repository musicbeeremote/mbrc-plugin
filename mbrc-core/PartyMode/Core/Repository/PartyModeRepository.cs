using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Settings;
using MusicBeeRemote.PartyMode.Core.Model;

namespace MusicBeeRemote.PartyMode.Core.Repository
{
    public class PartyModeRepository
    {
        private readonly IStorageLocationProvider _storageLocationProvider;
        private readonly string PartyModeBD;

        public PartyModeRepository(IStorageLocationProvider storageLocationProvider)
        {
            _storageLocationProvider = storageLocationProvider;
            PartyModeBD = $"{storageLocationProvider.StorageLocation()}{Path.DirectorySeparatorChar}partymode.db";
        }

        public void InsertClient(RemoteClient client)
        {
            using (var db = new LiteDatabase(PartyModeBD))
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

            using (var db = new LiteDatabase(PartyModeBD))
            {
                var clients = db.GetCollection<RemoteClient>("clients").FindAll();
                knownClients.AddRange(clients.ToList());
            }
            return knownClients;
        }

        public void InsertLog(PartyModeLog log)
        {
            using (var db = new LiteDatabase(PartyModeBD))
            {
                var collection = db.GetCollection<PartyModeLog>("logs");
                collection.Insert(log);
            }
        }

        public List<PartyModeLog> GetLogs()
        {
            IEnumerable<PartyModeLog> logs;
            using (var db = new LiteDatabase(PartyModeBD))
            {
                logs = db.GetCollection<PartyModeLog>("logs").FindAll();
            }
            return logs.ToList();
        }

        public void UpdateClient(RemoteClient client)
        {
            using (var db = new LiteDatabase(PartyModeBD))
            {
                var collection = db.GetCollection<RemoteClient>("clients");
                collection.Update(client);
            }            
        }
    }
}