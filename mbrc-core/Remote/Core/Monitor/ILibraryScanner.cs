using MusicBeeRemoteCore.Core.ApiAdapters;

namespace MusicBeeRemoteCore.Remote.Core.Monitor
{
    internal interface ILibraryScanner
    {
        void Start();
        void Stop();
    }

    internal class LibraryScanner : ILibraryScanner
    {

        private readonly ITrackRepository _trackRepository;
        private readonly ILibraryApiAdapter _apiAdapter;

        public LibraryScanner(ILibraryApiAdapter apiAdapter, ITrackRepository trackRepository)
        {
            _apiAdapter = apiAdapter;
            _trackRepository = trackRepository;
        }

        public void Start()
        {
            _trackRepository.AddAll(_apiAdapter.GetTracks());
        }

        public void Stop()
        {
            throw new System.NotImplementedException();
        }
    }
}