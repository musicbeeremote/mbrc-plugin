using System;
using System.ComponentModel;
using MusicBeeRemoteCore.PartyMode.Core.Helper;
using MusicBeeRemoteCore.PartyMode.Core.Model;

namespace MusicBeeRemoteCore.PartyMode.Core.ViewModel
{
    public class LogViewerViewModel : ModelBase, IDisposable
    {
        private readonly PartyModeModel _model;

        public LogViewerViewModel(PartyModeModel model)
        {
            _model = model;
            Logs = new ObservableCollectionEx<PartyModeLogsView>();
            _model.PropertyChanged += OnPropertyChanged;
            ServerCommandExecuted();
        }

        public ObservableCollectionEx<PartyModeLogsView> Logs
        {
            get;
        }

        private void ServerCommandExecuted()
        {

            PartyModeLogs partyModeLogs;


            while (_model.ServerMessagesQueue.TryDequeue(out partyModeLogs))
            {
                Logs.Add(new PartyModeLogsView(Logs.Count, partyModeLogs));
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