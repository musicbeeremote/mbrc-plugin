using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    using Entities;
    using Model;
    using Interfaces;
    using Networking;

    internal class RequestLyrics : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestNowPlayingTrackLyrics();
        }
    }
}