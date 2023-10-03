using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestTagChange : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            var tagName = data.Get<string>("tag");
            var newValue = data.Get<string>("value");

            Plugin.Instance.SetTrackTag(tagName, newValue, eEvent.ClientId);
        }
    }
}