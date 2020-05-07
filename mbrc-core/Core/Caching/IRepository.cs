using System.Collections.Generic;
using System.Linq;
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
        IEnumerable<string> GetCachedPaths();

        int RemoveAll(IEnumerable<string> paths);

        void Insert(IEnumerable<Track> tracks);

        void Update(IEnumerable<Track> tracks);
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
                if (collection.Count() == 0)
                {
                    collection.InsertBulk(items);
                }
                else
                {
                    foreach (var track in items)
                    {
                        if (collection.Exists(saved => saved.Src == track.Src))
                        {
                            collection.Update(track);
                        }
                        else
                        {
                            collection.Insert(track);
                        }
                    }
                }
            }
        }

        public IEnumerable<string> GetCachedPaths()
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                var collection = db.GetCollection<Track>("tracks");
                return collection.Find(Query.All()).Select(x => x.Src);
            }
        }

        public void RemoveAll()
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                db.DropCollection("tracks");
            }
        }

        public int RemoveAll(IEnumerable<string> paths)
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                var collection = db.GetCollection<Track>("tracks");
                return paths.Select(path => collection.Delete(path)).Count(deleted => deleted);
            }
        }

        public void Insert(IEnumerable<Track> tracks)
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                var collection = db.GetCollection<Track>("tracks");
                collection.InsertBulk(tracks);
            }
        }

        public void Update(IEnumerable<Track> tracks)
        {
            using (var db = new LiteDatabase(_storageProvider.CacheDatabase))
            {
                var collection = db.GetCollection<Track>("tracks");
                foreach (var track in tracks)
                {
                    collection.Update(track);
                }
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
