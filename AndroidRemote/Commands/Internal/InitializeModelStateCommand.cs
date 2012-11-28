using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    class InitializeModelStateCommand:ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            Plugin plugin = Plugin.Instance;
            plugin.RequestNowPlayingTrackCover();
            plugin.RequestNowPlayingTrackLyrics();
        }
    }
}
