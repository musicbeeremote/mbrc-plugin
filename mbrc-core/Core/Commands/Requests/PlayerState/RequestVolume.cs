using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using Newtonsoft.Json.Linq;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerState
{
    public class RequestVolume : LimitedCommand
    {
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestVolume(IPlayerApiAdapter apiAdapter)
        {
            _apiAdapter = apiAdapter;
        }

        public override string Name() => "Player: Change Volume";

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            if (!(receivedEvent.Data is JToken token) || token.Type != JTokenType.Integer)
            {
                return;
            }

            var newVolume = token.Value<int>();
            var volume = _apiAdapter.GetVolume();
            if (newVolume == volume)
            {
                return;
            }

            _apiAdapter.SetVolume(newVolume);
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.ChangeVolume;
        }
    }
}
