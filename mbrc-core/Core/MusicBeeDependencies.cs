using MusicBeeRemote.Core.ApiAdapters;

namespace MusicBeeRemote.Core
{
    public class MusicBeeDependencies
    {
        public MusicBeeDependencies(ILibraryApiAdapter libraryAdapter,
            INowPlayingApiAdapter nowPlayingAdapter,
            IOutputApiAdapter outputAdapter,
            IPlayerApiAdapter playerAdapter,
            IQueueAdapter queueAdapter,
            ITrackApiAdapter trackAdapter,
            IInvokeHandler invokeHandler,
            string baseStoragePath,
            string currentVersion)
        {
            LibraryAdapter = libraryAdapter;
            NowPlayingAdapter = nowPlayingAdapter;
            OutputAdapter = outputAdapter;
            PlayerAdapter = playerAdapter;
            QueueAdapter = queueAdapter;
            TrackAdapter = trackAdapter;
            InvokeHandler = invokeHandler;
            BaseStoragePath = baseStoragePath;
            CurrentVersion = currentVersion;
        }

        public ILibraryApiAdapter LibraryAdapter { get; }
        public INowPlayingApiAdapter NowPlayingAdapter { get; }
        public IOutputApiAdapter OutputAdapter { get; }
        public IPlayerApiAdapter PlayerAdapter { get; }
        public IQueueAdapter QueueAdapter { get; }
        public ITrackApiAdapter TrackAdapter { get; }
        public IInvokeHandler InvokeHandler { get; }
        public string BaseStoragePath { get; }
        public string CurrentVersion { get; }
    }
}