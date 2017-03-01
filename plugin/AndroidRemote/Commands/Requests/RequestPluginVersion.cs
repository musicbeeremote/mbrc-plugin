using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPluginVersion : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(new SocketMessage(Constants.PluginVersion, UserSettings.Instance.CurrentVersion)
                .ToJsonString());
        }
    }
}