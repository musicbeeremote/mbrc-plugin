using System.Collections.Generic;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Commands.Logs;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog.PartyModePanel
{
    public class PartyModeViewModel : ViewModelBase
    {
        private RemoteClient _selectedClient;
        private ClientRepository _repository;

        public PartyModeViewModel(ClientRepository repository)
        {
            _repository = repository;
        }


        public bool IsActive
        {
            get => false;
            set { OnPropertyChanged(nameof(IsActive)); }
        }

        public List<RemoteClient> KnownClients => _repository.GetKnownClients();

        public void SelectClient(int index) => _selectedClient = _repository.GetKnownClients()[index];

        public void UpdateSelectedClientPermissions(CommandPermissions permissions, bool @checked)
        {
            _selectedClient.SetPermission(permissions, @checked);
            _repository.UpdateClient(_selectedClient);
        }

        public bool SelectedClientHasPermission(CommandPermissions permissions)
        {
            return _selectedClient.HasPermission(permissions);
        }

        public List<ExecutionLog> GetLogs()
        {
            return new List<ExecutionLog>();
        }
    }
}