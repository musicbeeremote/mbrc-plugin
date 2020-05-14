using System;
using System.Collections.Generic;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Commands.Logs;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog.PartyMode
{
    public class PartyModeViewModel : ViewModelBase
    {
        private readonly ClientRepository _repository;
        private readonly LogRepository _logRepository;
        private RemoteClient _selectedClient;
        private bool _isActive;

        public PartyModeViewModel(ClientRepository repository, LogRepository logRepository)
        {
            _repository = repository;
            _logRepository = logRepository;
        }

        public event EventHandler ClientDataUpdated;

        public List<RemoteClient> KnownClients => _repository.GetKnownClients();

        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                OnPropertyChanged(nameof(IsActive));
            }
        }

        public void SelectClient(int index)
        {
            _selectedClient = _repository.GetKnownClients()[index];
        }

        public void UpdateSelectedClientPermissions(CommandPermissions permissions, bool @checked)
        {
            _selectedClient.SetPermission(permissions, @checked);
            _repository.UpdateClient(_selectedClient);
            OnClientDataUpdated(EventArgs.Empty);
        }

        public bool SelectedClientHasPermission(CommandPermissions permissions)
        {
            return _selectedClient.HasPermission(permissions);
        }

        public List<ExecutionLog> GetLogs()
        {
            return _logRepository.GetLogs();
        }

        protected virtual void OnClientDataUpdated(EventArgs e)
        {
            ClientDataUpdated?.Invoke(this, e);
        }
    }
}
