using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPluginVersion:ICommand
    {
        public void Dispose()
        {
        
        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(new SocketMessage(Constants.PluginVersion, Constants.Reply, UserSettings.Instance.CurrentVersion).toJsonString());
        }
    }
}