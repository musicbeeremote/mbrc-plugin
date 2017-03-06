using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.PartyMode.Core.Model;

namespace MusicBeePlugin.PartyMode.Core.ViewModel
{
    public class ClientViewModel : ModelBase, IDisposable
    {
        private readonly PartyModeModel _model;
        private ObservableCollection<RemoteClient> _connectedClients;
        private RemoteClient _selectedClient;

        public ClientViewModel(PartyModeModel model)
        {
            _model = model;
            ConnectedClients = new ObservableCollection<RemoteClient>(model.KnownClients);
            model.PropertyChanged += ModelOnPropertyChangend;
        }

        public ObservableCollection<RemoteClient> ConnectedClients
        {
            get { return _connectedClients; }
            set
            {
                _connectedClients = value;
                OnPropertyChanged(nameof(ConnectedClients));
            }
        }

        public RemoteClient SelectedClient
        {
            get { return _selectedClient; }
            set
            {
                _selectedClient = value;
                OnPropertyChanged(nameof(SelectedClient));
            }
        }


        public void ModelOnPropertyChangend(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(_model.KnownClients)) return;
            ConnectedClients = new ObservableCollection<RemoteClient>(_model.KnownClients);
            OnPropertyChanged(nameof(SelectedClient));
        }

        public void Dispose()
        {
            _model.PropertyChanged -= ModelOnPropertyChangend;
        }
    }
}