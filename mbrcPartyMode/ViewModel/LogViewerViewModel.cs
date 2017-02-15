using System;
using System.ComponentModel;
using mbrcPartyMode.Helper;
using mbrcPartyMode.Model;

namespace mbrcPartyMode.ViewModel
{
    public class LogViewerViewModel : ModelBase, IDisposable
    {
        private readonly PartyModeModel model;

        public LogViewerViewModel(PartyModeModel model)
        {
            this.model = model;
            Logs = new ObservableCollectionEx<ServerMessageView>();
            this.model.PropertyChanged += OnPropertyChanged;
            ServerCommandExecuted();
        }

        public ObservableCollectionEx<ServerMessageView> Logs
        {
            get; private set;
        }

        private void ServerCommandExecuted()
        {

            ServerMessage serverMessage;


            while (model.ServerMessagesQueue.TryDequeue(out serverMessage))
            {
                Logs.Add(new ServerMessageView(Logs.Count, serverMessage));
            }
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PartyModeModel.ServerMessagesQueue))
            { ServerCommandExecuted(); }
        }

        public void Dispose()
        {
            this.model.PropertyChanged -= OnPropertyChanged;
        }
    }
}