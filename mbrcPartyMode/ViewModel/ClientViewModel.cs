using System;
using mbrcPartyMode.Helper;
using mbrcPartyMode.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace mbrcPartyMode.ViewModel
{
    public class ClientViewModel : ModelBase, IDisposable
    {
        private PartyModeModel model;
        private ObservableCollection<ClientAdress> connectedClients = null;
        private ConnectedClientAddress selectedClient;

        public ClientViewModel(PartyModeModel model)
        {
            this.model = model;
            this.ConnectedClients = new ObservableCollection<ClientAdress>(model.ConnectedAddresses);
            model.PropertyChanged += ModelOnPropertyChangend;
        }

        public ObservableCollection<ClientAdress> ConnectedClients
        {
            get
            {
                return connectedClients;
            }
            set
            {
                connectedClients = value;
                OnPropertyChanged(nameof(ConnectedClients));
            }
        }

        public ConnectedClientAddress SelectedClient
        {
            get
            {
                return selectedClient;
            }
            set
            {
                selectedClient = value;
                OnPropertyChanged(nameof(SelectedClient));
            }
        }


        public void ModelOnPropertyChangend(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(model.ConnectedAddresses))
            {
                this.ConnectedClients = new ObservableCollection<ClientAdress>(model.ConnectedAddresses);
                OnPropertyChanged(nameof(SelectedClient));
            }
        }

        public void Dispose()
        {
            model.PropertyChanged -= ModelOnPropertyChangend;
        }
    }
}