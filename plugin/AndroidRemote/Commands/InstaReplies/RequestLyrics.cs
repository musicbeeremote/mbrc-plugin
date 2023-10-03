using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    internal class RequestLyrics : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestNowPlayingTrackLyrics();
        }
    }
}