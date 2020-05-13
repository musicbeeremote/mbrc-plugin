using System;

namespace MusicBeeRemote.Core.Settings.Dialog.Whitelist
{
    public class WhitelistManagementPresenter : IWhitelistManagementPresenter
    {
        private readonly WhitelistManagementViewModel _viewModel;
        private IWhitelistManagementView _view;

        public WhitelistManagementPresenter(WhitelistManagementViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
        }

        public void Load()
        {
            _view.UpdateWhitelist(_viewModel.Whitelist);
        }

        public void Attach(IWhitelistManagementView view)
        {
            _view = view;
        }

        public void AddAddress(string ipAddress)
        {
            _viewModel.AddAddress(ipAddress);
        }

        public void RemoveAddress(string ipAddress)
        {
            _viewModel.RemoveAddress(ipAddress);
        }

        private void ViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_viewModel.Whitelist))
            {
                _view.UpdateWhitelist(_viewModel.Whitelist);
            }
        }
    }
}
