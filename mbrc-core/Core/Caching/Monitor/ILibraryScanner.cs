using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBeeRemote.Core.ApiAdapters;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Caching.Monitor
{
    public interface ILibraryScanner
    {
        void Start();
        void Stop();
    }

    internal class LibraryScanner : ILibraryScanner
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ITrackRepository _trackRepository;
        private readonly ICacheInfoRepository _cacheInfoRepository;
        private readonly ILibraryApiAdapter _apiAdapter;
        private readonly ITinyMessengerHub _hub;
        private TinyMessageSubscriptionToken _eventAddedToken;
        
        static SemaphoreSlim semaphore = new SemaphoreSlim(1,1);

        public LibraryScanner(
            ILibraryApiAdapter apiAdapter,
            ITrackRepository trackRepository,
            ITinyMessengerHub hub,
            ICacheInfoRepository cacheInfoRepository
        )
        {
            _apiAdapter = apiAdapter;
            _trackRepository = trackRepository;
            _hub = hub;
            _cacheInfoRepository = cacheInfoRepository;
        }

        public void Start()
        {
            var cacheInfo = _cacheInfoRepository.Get();
            if (cacheInfo.Id == 0)
            {
                BuildCache();
            }
            else
            {
                DeltaUpdate(cacheInfo.TracksUpdated);
            }
            
            _eventAddedToken = _hub.Subscribe<FileAddedEvent>(@event => { OnFilesAdded(); });
        }

        private void BuildCache()
        {
            semaphore.Wait();
            Task.Factory.StartNew(() =>
            {
                _logger.Debug("Doing initial library scan");
                _trackRepository.AddAll(_apiAdapter.GetTracks());
                _cacheInfoRepository.Update(new CacheInfo {TracksUpdated = DateTime.Now});
                semaphore.Release();
            }, TaskCreationOptions.PreferFairness);
        }

        private void DeltaUpdate(DateTime after)
        {
            semaphore.Wait();
            Task.Factory.StartNew(() =>
            {
                _logger.Debug($"Scanning changes after {after}");
                var paths = _trackRepository.GetCachedPaths().ToArray();
                var delta = _apiAdapter.GetSyncDelta(paths, after);
                _trackRepository.RemoveAll(delta.DeletedFiles);
                _trackRepository.Insert(_apiAdapter.GetTracks(delta.NewFiles));
                _trackRepository.Update(_apiAdapter.GetTracks(delta.UpdatedFiles));
                _cacheInfoRepository.Update(new CacheInfo {TracksUpdated = DateTime.Now});
                semaphore.Release();
            }, TaskCreationOptions.PreferFairness);
        }

        public void Stop()
        {
            _hub.Unsubscribe<FileAddedEvent>(_eventAddedToken);
        }

        private void OnFilesAdded()
        {
            var cacheInfo = _cacheInfoRepository.Get();
            DeltaUpdate(cacheInfo.TracksUpdated);
        }
    }
}