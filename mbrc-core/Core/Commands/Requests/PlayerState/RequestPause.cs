using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerState
{
    public class RequestPause : LimitedCommand
    {
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPause(IPlayerApiAdapter playerApiAdapter)
        {
            _apiAdapter = playerApiAdapter;
        }

        /// <inheritdoc />
        public override string Name()
        {
            return "Player: Pause";
        }

        public override void Execute(IEvent receivedEvent)
        {
            _apiAdapter.Pause();
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.StopPlayback;
        }
    }
}
