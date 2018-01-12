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
        private readonly ILibraryApiAdapter _apiAdapter;
        private readonly ITinyMessengerHub _hub;
        private TinyMessageSubscriptionToken eventAddedToken;

        public LibraryScanner(ILibraryApiAdapter apiAdapter, ITrackRepository trackRepository, ITinyMessengerHub hub)
        {
            _apiAdapter = apiAdapter;
            _trackRepository = trackRepository;
            _hub = hub;
        }

        public void Start()
        {           
            _logger.Debug("Starting library scanning");
            _trackRepository.AddAll(_apiAdapter.GetTracks());           
            eventAddedToken = _hub.Subscribe<FileAddedEvent>(@event => { OnFilesAdded(); });
        }

        public void Stop()
        {
            _hub.Unsubscribe<FileAddedEvent>(eventAddedToken);
        }

        private void OnFilesAdded()
        {
            _logger.Debug("Files added to library");
        }
    }
}