using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBeeRemote.Core.ApiAdapters;
using NLog;
using IntervalTimer = System.Timers.Timer;

namespace MusicBeeRemote.Core.Caching.Monitor
{
    public interface ILibraryScanner
    {
        void Start();
        void Stop();
        void RefreshLibrary();
    }

    internal class LibraryScanner : ILibraryScanner
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ITrackRepository _trackRepository;
        private readonly ICacheInfoRepository _cacheInfoRepository;
        private readonly ILibraryApiAdapter _apiAdapter;
        private readonly IntervalTimer _timer;

        private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);

        public LibraryScanner(
            ILibraryApiAdapter apiAdapter,
            ITrackRepository trackRepository,
            ICacheInfoRepository cacheInfoRepository
        )
        {
            _apiAdapter = apiAdapter;
            _trackRepository = trackRepository;
            _cacheInfoRepository = cacheInfoRepository;
            _timer = new IntervalTimer
            {
                Interval = 60 * 1000,
            };
            _timer.Elapsed += (sender, args) => Update();
        }

        private void Update()
        {
            var ( _, tracksUpdated, _) = _cacheInfoRepository.Get();
            DeltaUpdate(tracksUpdated);
        }

        public void RefreshLibrary()
        {
            _trackRepository.RemoveAll();
            BuildCache();
        }

        public void Start()
        {
            var (id, tracksUpdated, _) = _cacheInfoRepository.Get();
            if (id == 0)
            {
                BuildCache();
            }
            else
            {
                DeltaUpdate(tracksUpdated);
            }

            _timer.Start();
        }

        private void BuildCache()
        {
            Semaphore.Wait();
            Task.Factory.StartNew(() =>
            {
                _logger.Info("Scanning for the first time");
                _trackRepository.AddAll(_apiAdapter.GetTracks());
                _cacheInfoRepository.Update(new CacheInfo {TracksUpdated = DateTime.Now});
                _logger.Info($"Cache contains {_trackRepository.Count()} tracks.");
                Semaphore.Release();
            }, TaskCreationOptions.PreferFairness);
        }

        private void DeltaUpdate(DateTime after)
        {
            Semaphore.Wait();
            Task.Factory.StartNew(() =>
            {
                _logger.Info($"Scanning changes after {after}");
                var libraryPaths = _apiAdapter.GetTrackPaths();
                var paths = _trackRepository.GetCachedPaths().ToArray();
                var missingPaths = paths.Except(libraryPaths).ToList();
                if (missingPaths.Count > 0)
                {
                    _trackRepository.RemoveAll(missingPaths);
                    _logger.Info($"Removed {missingPaths.Count} tracks from cache");
                }

                var delta = _apiAdapter.GetSyncDelta(paths, after);
                _trackRepository.RemoveAll(delta.DeletedFiles);
                _trackRepository.Insert(_apiAdapter.GetTracks(delta.NewFiles));
                _trackRepository.Update(_apiAdapter.GetTracks(delta.UpdatedFiles));
                _cacheInfoRepository.Update(new CacheInfo {TracksUpdated = DateTime.Now});
                _logger.Info($"Cache contains {_trackRepository.Count()} tracks. {delta}");
                Semaphore.Release();
            }, TaskCreationOptions.PreferFairness);
        }

        public void Stop()
        {
            _timer.Stop();
        }
    }
}