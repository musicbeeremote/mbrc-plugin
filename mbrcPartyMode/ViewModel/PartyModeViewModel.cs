using mbrcPartyMode.Model;
using mbrcPartyMode.ViewModel.Commands;
using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace mbrcPartyMode.ViewModel
{
    public class PartyModeViewModel : ModelBase, IDisposable
    {
        #region vars

        private readonly PartyModeModel model;
        private readonly ClientViewModel clientViewModel;
        private ClientDetailViewModel clientDetailViewModel;
        private readonly LogViewerViewModel logViewerViewModel;
        private bool isActive = false;
        private readonly Dispatcher _dispatcher;
        private static object _syncLock = new object();

        #endregion vars

        #region constructor

        public PartyModeViewModel()
        {
            this.model = PartyModeModel.Instance;
            this.clientViewModel = new ClientViewModel(model);
            this.clientDetailViewModel = new ClientDetailViewModel(clientViewModel.SelectedClient);
            this.logViewerViewModel = new LogViewerViewModel(model);
            this.clientViewModel.PropertyChanged += OnPropertyChanged;
            this.model.PropertyChanged += OnPropertyChanged;
            this.isActive = model.Settings.IsActive;

            this.SaveCommand = new SaveCommand();
            this.model.RequestAllServerMessages();

            if (AppDomain.CurrentDomain != null)
            {
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            }

            if (Dispatcher.CurrentDispatcher != null)
            {
                Dispatcher.CurrentDispatcher.UnhandledException += CurrentDispatcher_UnhandledException;
            }

            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        #endregion constructor

        #region ViewModels

        public ClientViewModel ClientViewModel
        {
            get { return clientViewModel; }
        }

        public ClientDetailViewModel ClientDetailViewModel
        {
            get { return clientDetailViewModel; }
        }

        public LogViewerViewModel LogViewerViewModel
        {
            get { return logViewerViewModel; }
        }

        #endregion ViewModels

        #region Commands

        public ICommand SaveCommand
        {
            get;
            private set;
        }

        #endregion Commands

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClientViewModel.SelectedClient))
            { SelectedClientChanged(); }
        }

        private void SelectedClientChanged()
        {
            this.clientDetailViewModel = new ClientDetailViewModel(clientViewModel.SelectedClient);
            OnPropertyChanged(nameof(ClientDetailViewModel));
        }

        public bool IsActive
        {
            get
            {
                return model.Settings.IsActive;
            }
            set
            {
                model.Settings.IsActive = value;
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

        public ICommand UnloadedCmd
        {
            get { return new UnloadedCommand(Dispose); }
        }

        public void Dispose()
        {
            this.clientViewModel.PropertyChanged -= OnPropertyChanged;
            this.model.PropertyChanged -= OnPropertyChanged;
            this.logViewerViewModel.Dispose();
            this.clientViewModel.Dispose();
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            Dispatcher.CurrentDispatcher.UnhandledException -= CurrentDispatcher_UnhandledException;
        }

        #endregion Disposing
    }
}