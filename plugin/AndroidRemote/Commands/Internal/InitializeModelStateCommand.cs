using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class InitializeModelStateCommand : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var plugin = Plugin.Instance;
            plugin.RequestNowPlayingTrackCover();
            plugin.RequestNowPlayingTrackLyrics();
        }
    }
}