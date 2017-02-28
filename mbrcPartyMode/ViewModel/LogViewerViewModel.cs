using System;
using System.ComponentModel;
using mbrcPartyMode.Helper;
using mbrcPartyMode.Model;

namespace mbrcPartyMode.ViewModel
{
    public class LogViewerViewModel : ModelBase, IDisposable
    {
        private readonly PartyModeModel _model;

        public LogViewerViewModel(PartyModeModel model)
        {
            _model = model;
            Logs = new ObservableCollectionEx<ServerMessageView>();
            _model.PropertyChanged += OnPropertyChanged;
            ServerCommandExecuted();
        }

        public ObservableCollectionEx<ServerMessageView> Logs
        {
            get;
        }

        private void ServerCommandExecuted()
        {

            ServerMessage serverMessage;


            while (_model.ServerMessagesQueue.TryDequeue(out serverMessage))
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
            _model.PropertyChanged -= OnPropertyChanged;
        }
    }
}