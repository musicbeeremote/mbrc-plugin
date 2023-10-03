using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingPlay : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var socketClient = Authenticator.Client(eEvent.ClientId);
            var isAndroid = socketClient?.ClientPlatform == ClientOS.Android;
            Plugin.Instance.NowPlayingPlay(eEvent.DataToString(), isAndroid);
        }
    }
}