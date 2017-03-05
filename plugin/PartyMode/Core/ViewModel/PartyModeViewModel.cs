using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using MusicBeePlugin.PartyMode.Core.Model;
using MusicBeePlugin.PartyMode.Core.ViewModel.Commands;

namespace MusicBeePlugin.PartyMode.Core.ViewModel
{
    public class PartyModeViewModel : ModelBase, IDisposable
    {
        #region vars

        private readonly PartyModeModel _model;

        #endregion vars

        #region constructor

        public PartyModeViewModel()
        {
            _model = PartyModeModel.Instance;
            ClientViewModel = new ClientViewModel(_model);
            ClientDetailViewModel = new ClientDetailViewModel(ClientViewModel.SelectedClient);
            LogViewerViewModel = new LogViewerViewModel(_model);
            ClientViewModel.PropertyChanged += OnPropertyChanged;
            _model.PropertyChanged += OnPropertyChanged;
            _model.RequestAllServerMessages();

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.CurrentDispatcher.UnhandledException += CurrentDispatcher_UnhandledException;
        }

        #endregion constructor

        #region ViewModels

        public ClientViewModel ClientViewModel { get; }

        public ClientDetailViewModel ClientDetailViewModel { get; private set; }

        public LogViewerViewModel LogViewerViewModel { get; }

        #endregion ViewModels

        #region Commands

        public ICommand SaveCommand
        {
            get { return new SaveCommand(); }
        }

        #endregion Commands

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClientViewModel.SelectedClient))
            {
                SelectedClientChanged();
            }
        }

        private void SelectedClientChanged()
        {
            ClientDetailViewModel = new ClientDetailViewModel(ClientViewModel.SelectedClient);
            OnPropertyChanged(nameof(ClientDetailViewModel));
        }

        public bool IsActive
        {
            get { return _model.Settings.IsActive; }
            set
            {
                _model.Settings.IsActive = value;
                OnPropertyChanged(nameof(IsActive));
            }
        }

        public bool IsDebug
        {
            get
            {
#if DEBUG
                return true;
#else
               return false;
#endif
            }
        }

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

        public ICommand UnloadedCmd => new UnloadedCommand(Dispose);

        public void Dispose()
        {
            ClientViewModel.PropertyChanged -= OnPropertyChanged;
            _model.PropertyChanged -= OnPropertyChanged;
            LogViewerViewModel.Dispose();
            ClientViewModel.Dispose();
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            Dispatcher.CurrentDispatcher.UnhandledException -= CurrentDispatcher_UnhandledException;
        }

        #endregion Disposing
    }
}