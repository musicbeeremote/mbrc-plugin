using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Threading;
using NLog;
using IntervalTimer = System.Timers.Timer;

namespace MusicBeeRemote.Core.Caching.Monitor
{
    public interface ILibraryScanner
    {
        void Start();

        void Terminate();

        void RefreshLibrary();
    }

    public class LibraryScanner : ILibraryScanner, IDisposable
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ITrackRepository _trackRepository;
        private readonly ICacheInfoRepository _cacheInfoRepository;
        private readonly ILibraryApiAdapter _apiAdapter;
        private readonly IntervalTimer _timer;
        private readonly LimitedTaskScheduler _scheduler;
        private readonly CancellationTokenSource _cts;

        private bool _isDisposed;

        public LibraryScanner(
            ILibraryApiAdapter apiAdapter,
            ITrackRepository trackRepository,
            ICacheInfoRepository cacheInfoRepository)
        {
            _apiAdapter = apiAdapter;
            _trackRepository = trackRepository;
            _cacheInfoRepository = cacheInfoRepository;
            _timer = new IntervalTimer { Interval = 60 * 1000 };
            _timer.Elapsed += (sender, args) => Update();
            _scheduler = new LimitedTaskScheduler(1);
            _cts = new CancellationTokenSource();
        }

        public void RefreshLibrary()
        {
            _trackRepository.RemoveAll();
            BuildCache();
        }

        public void Start()
        {
            var (id, tracksUpdated, _) = _cacheInfoRepository.GetCacheInfo();
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

        public void Terminate()
        {
            _timer.Stop();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                _timer?.Dispose();
                _cts.Dispose();
            }

            _isDisposed = true;
        }

        private void Update()
        {
            var (_, tracksUpdated, _) = _cacheInfoRepository.GetCacheInfo();
            DeltaUpdate(tracksUpdated);
        }

        private void BuildCache()
        {
            _semaphore.Wait();
            Task.Factory.StartNew(
                PerformBuildCache,
                _cts.Token,
                TaskCreationOptions.PreferFairness,
                _scheduler);
        }

        private void DeltaUpdate(DateTime after)
        {
            _semaphore.Wait();
            Task.Factory.StartNew(
                () => PerformDeltaUpdate(after),
                _cts.Token,
                TaskCreationOptions.PreferFairness,
                _scheduler);
        }

        private void PerformDeltaUpdate(DateTime after)
        {
            _logger.Info($"Scanning changes after {after}");
            var libraryPaths = _apiAdapter.GetTrackPaths().ToArray();
            var paths = _trackRepository.GetCachedPaths().ToArray();
            var missingPaths = paths.Except(libraryPaths).ToArray();

            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] == null)
                {
                    _logger.Info(i + " was null");
                }
            }

            if (missingPaths.Length > 0)
            {
                _trackRepository.RemoveAll(missingPaths);
                _logger.Info($"Removed {missingPaths.Length} tracks from cache");
            }

            var delta = _apiAdapter.GetSyncDelta(paths, after);
            _logger.Debug($"Delta contains {delta}");
            _trackRepository.RemoveAll(delta.GetDeletedFiles());
            _trackRepository.Insert(_apiAdapter.GetTracks(delta.GetNewFiles()));
            _trackRepository.Update(_apiAdapter.GetTracks(delta.GetUpdatedFiles()));
            _cacheInfoRepository.Update(new CacheInfo { TracksUpdated = DateTime.Now });
            _logger.Info($"Cache contains {_trackRepository.Count()} tracks.");
            _semaphore.Release();
        }

        private void PerformBuildCache()
        {
            _logger.Info("Scanning for the first time");
            _trackRepository.AddAll(_apiAdapter.GetTracks());
            _cacheInfoRepository.Update(new CacheInfo { TracksUpdated = DateTime.Now });
            _logger.Info($"Cache contains {_trackRepository.Count()} tracks.");
            _semaphore.Release();
        }
    }
}
