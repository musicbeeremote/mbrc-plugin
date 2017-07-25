using MusicBeeRemote.Core.Network;

namespace MusicBeeRemote.Core.Commands
{
    public class ExecutorHelper
    {
        private readonly ClientRepository _repository;

        public ExecutorHelper(ClientRepository repository)
        {
            _repository = repository;
        }

        public bool PermissionMode { get; set; } = false;

        public CommandPermissions ClientPermissions(string clientId)
        {
            var client = _repository.GetClientById(clientId);
            return client?.ClientPermissions ?? CommandPermissions.None;
        }
    }
}