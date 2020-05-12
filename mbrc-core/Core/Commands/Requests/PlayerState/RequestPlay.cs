using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerState
{
    internal class RequestPlay : LimitedCommand
    {
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPlay(IPlayerApiAdapter playerApiAdapter)
        {
            _apiAdapter = playerApiAdapter;
        }

        /// <inheritdoc />
        public override string Name()
        {
            return "Player: Play";
        }

        public override void Execute(IEvent receivedEvent)
        {
            _apiAdapter.Play();
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.StartPlayback;
        }
    }
}
