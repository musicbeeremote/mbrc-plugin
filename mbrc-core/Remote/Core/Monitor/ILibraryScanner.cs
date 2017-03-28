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
        private readonly LibraryDataAdapter _dataAdapter;

        public LibraryScanner(LibraryDataAdapter dataAdapter, TrackRepository repository)
        {
            _dataAdapter = dataAdapter;
            _repository = repository;
        }

        public void Start()
        {
            _repository.AddAll(_dataAdapter.GetTracks());
        }

        public void Stop()
        {
            throw new System.NotImplementedException();
        }
    }
}