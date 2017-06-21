using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Threading;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;
using MusicBeeRemote.PartyMode.Core.Helper;
using MusicBeeRemote.PartyMode.Core.Model;

namespace MusicBeeRemote.PartyMode.Core.ViewModel
{
    public class PartyModeViewModel : ViewModelBase, IDisposable
    {
        #region vars

        private readonly PartyModeModel _model;

        #endregion vars

        #region constructor

        public PartyModeViewModel(PartyModeModel model)
        {
            _model = model;
            _model.PropertyChanged += OnPropertyChanged;
            _model.RequestAllServerMessages();

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.CurrentDispatcher.UnhandledException += CurrentDispatcher_UnhandledException;
        }

        #endregion constructor


        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        private void SelectedClientChanged()
        {
        }


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
        public IList<PartyModeLogs> Logs => _model.Logs;
        public IEnumerable<CommandPermissions> Permissions => _model.Permissions; 

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
            _model.PropertyChanged -= OnPropertyChanged;
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            Dispatcher.CurrentDispatcher.UnhandledException -= CurrentDispatcher_UnhandledException;
        }

        #endregion Disposing
    }
}