namespace MusicBeeRemote.Core.Settings.Dialog.Whitelist
{
    class WhitelistManagementPresenter : IWhitelistManagementPresenter
    {
        private readonly WhitelistManagementViewModel _viewModel;

        public WhitelistManagementPresenter(WhitelistManagementViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.PropertyChanged += _viewModel_PropertyChanged;
        }

        private void _viewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_viewModel.Whitelist))
            {
                _view.UpdateWhitelist(_viewModel.Whitelist);
            }
        }

        private IWhitelistManagementView _view;

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
    }
}