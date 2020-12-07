using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;

    internal class RequestTagChange : ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            var tagName = data.Get<string>("tag");
            var newValue = data.Get<string>("value");

            Plugin.Instance.SetTrackTag(tagName, newValue, eEvent.ClientId);
        }
    }
}