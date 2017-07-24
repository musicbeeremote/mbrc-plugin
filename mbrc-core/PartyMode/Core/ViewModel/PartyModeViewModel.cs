using System;
using System.Collections.Generic;
using System.Windows.Threading;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;
using MusicBeeRemote.PartyMode.Core.Model;

namespace MusicBeeRemote.PartyMode.Core.ViewModel
{
    public class PartyModeViewModel : ViewModelBase, IDisposable
    {
        #region vars

        private readonly PartyModeModel _model;
        private RemoteClient _selectedClient;

        #endregion vars

        #region constructor

        public PartyModeViewModel(PartyModeModel model)
        {
            _model = model;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.CurrentDispatcher.UnhandledException += CurrentDispatcher_UnhandledException;
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

        #region exception Handling

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Dispose();
        }

        private void CurrentDispatcher_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Dispose();
        }

        #endregion exception Handling

        #region Disposing       

        public void Dispose()
        {
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            Dispatcher.CurrentDispatcher.UnhandledException -= CurrentDispatcher_UnhandledException;
        }

        #endregion Disposing

        public void SelectClient(int index) => _selectedClient = KnownClients[index];

        public void UpdateSelectedClientPermissions(CommandPermissions permissions, bool @checked)
        {
            _selectedClient.SetPermission(permissions, @checked);
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