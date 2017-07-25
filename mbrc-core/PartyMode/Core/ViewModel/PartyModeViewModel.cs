using System.Collections.Generic;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;
using MusicBeeRemote.PartyMode.Core.Model;

namespace MusicBeeRemote.PartyMode.Core.ViewModel
{
    public class PartyModeViewModel : ViewModelBase
    {
        #region vars

        private readonly PartyModeModel _model;
        private RemoteClient _selectedClient;

        #endregion vars

        #region constructor

        public PartyModeViewModel(PartyModeModel model)
        {
            _model = model;
        }

        #endregion constructor

        public bool IsActive
        {
            get => _model.Settings.IsActive;
            set
            {
                _model.Settings.IsActive = value;
                OnPropertyChanged(nameof(IsActive));
            }
        }

        public IList<RemoteClient> KnownClients => _model.KnownClients;

        public void SelectClient(int index) => _selectedClient = KnownClients[index];

        public void UpdateSelectedClientPermissions(CommandPermissions permissions, bool @checked)
        {
            _selectedClient.SetPermission(permissions, @checked);
            _model.UpdateClient(_selectedClient);
        }

        public bool SelectedClientHasPermission(CommandPermissions permissions)
        {
            return _selectedClient.HasPermission(permissions);
        }

        public List<PartyModeLog> GetLogs()
        {
            return _model.GetLogs();
        }
              
    }
}