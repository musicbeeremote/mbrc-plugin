using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using MbrcPartyMode.Helper;
using MbrcPartyMode.Model;

namespace MbrcPartyMode.ViewModel
{
    public class ClientViewModel : ModelBase, IDisposable
    {
        private readonly PartyModeModel _model;
        private ObservableCollection<ClientAddress> _connectedClients;
        private ConnectedClientAddress _selectedClient;

        public ClientViewModel(PartyModeModel model)
        {
            _model = model;
            ConnectedClients = new ObservableCollection<ClientAddress>(model.ConnectedAddresses);
            model.PropertyChanged += ModelOnPropertyChangend;
        }

        public ObservableCollection<ClientAddress> ConnectedClients
        {
            get { return _connectedClients; }
            set
            {
                _connectedClients = value;
                OnPropertyChanged(nameof(ConnectedClients));
            }
        }

        public ConnectedClientAddress SelectedClient
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
            if (e.PropertyName != nameof(_model.ConnectedAddresses)) return;
            ConnectedClients = new ObservableCollection<ClientAddress>(_model.ConnectedAddresses);
            OnPropertyChanged(nameof(SelectedClient));
        }

        public void Dispose()
        {
            _model.PropertyChanged -= ModelOnPropertyChangend;
        }
    }
}