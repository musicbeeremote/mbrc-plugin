namespace MusicBeeRemoteCore.Remote.Core.Monitor
{
    internal interface ILibraryScanner
    {
        void Start();
        void Stop();
    }

    internal class LibraryScanner : ILibraryScanner
    {

        private readonly TrackRepository _repository;
        private readonly LibraryApiAdapter _apiAdapter;

        public LibraryScanner(LibraryApiAdapter apiAdapter, TrackRepository repository)
        {
            _apiAdapter = apiAdapter;
            _repository = repository;
        }

        public void Start()
        {
            _repository.AddAll(_apiAdapter.GetTracks());
        }

        public void Stop()
        {
            throw new System.NotImplementedException();
        }
    }
}