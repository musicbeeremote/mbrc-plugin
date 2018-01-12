using System.Collections.Generic;
using LiteDB;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Settings;

namespace MusicBeeRemote.Core.Caching
{
    public interface IRepository<T>
    {
        void AddAll(IEnumerable<T> items);
        void RemoveAll();
        IEnumerable<T> GetRange(int offset, int limit);
        int Count();
    }

    public interface ITrackRepository : IRepository<Track>
    {
    }

    internal class TrackRepository : ITrackRepository
    {
        private readonly IStorageLocationProvider _storageProvider;

        public TrackRepository(IStorageLocationProvider storageProvider)
        {
            _storageProvider = storageProvider;
        }

        public void AddAll(IEnumerable<Track> items)
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                var collection = db.GetCollection<Track>("tracks");
                collection.InsertBulk(items);
            }
        }

        public void RemoveAll()
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                db.DropCollection("tracks");
            }
        }

        public IEnumerable<Track> GetRange(int offset, int limit)
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                var collection = db.GetCollection<Track>("tracks");
                return collection.Find(Query.All(), offset, limit);
            }
        }

        public int Count()
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                var collection = db.GetCollection<Track>("tracks");
                return collection.Count();
            }
        }
    }
}