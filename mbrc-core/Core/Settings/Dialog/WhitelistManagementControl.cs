using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MusicBeeRemote.Core.Settings.Dialog.Validations;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    public partial class WhitelistManagementControl : UserControl, IWhitelistManagementView
    {
        private readonly IWhitelistManagementPresenter _presenter;
        private readonly AddressValidationRule _validationRule;

        public WhitelistManagementControl(IWhitelistManagementPresenter presenter)
        {
            _validationRule = new AddressValidationRule();
            _presenter = presenter;
            InitializeComponent();
            _presenter.Attach(this);
            _presenter.Load();
        }

        public void UpdateWhitelist(List<string> whitelist)
        {
            var bindingSource = new BindingSource {DataSource = whitelist};
            whitelistComboBox.DataSource = bindingSource;
        }

        private void AddressAddButtonClick(object sender, EventArgs e)
        {
            var input = newAddressTextBox.Text;
            _presenter.AddAddress(input);
        }

        private void AddressRemoveButtonClick(object sender, EventArgs e)
        {
            var addresses = whitelistComboBox.Items;
            if (addresses.Count > 0)
            {
                _presenter.RemoveAddress(addresses[0].ToString());            
            }
            else
            {
                
            }
        }

        private void newAddressTextBox_TextChanged(object sender, EventArgs e)
        {
            var input = newAddressTextBox.Text;
            if (_validationRule.Validate(input))
            {
                addressAddButton.Enabled = true;
                newAddressTextBox.BackColor = Color.LawnGreen;
            }
            else
            {
                newAddressTextBox.BackColor = Color.Red;
                addressAddButton.Enabled = false;
            }
        }
    }

    public interface IWhitelistManagementPresenter
    {
        void Load();
        void Attach(IWhitelistManagementView view);
        void AddAddress(string ipAddress);
        void RemoveAddress(string ipAddress);
    }

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
                view.UpdateWhitelist(_viewModel.Whitelist);
            }
        }

        private IWhitelistManagementView view;

        public void Load()
        {
            view.UpdateWhitelist(_viewModel.Whitelist);
        }

        public void Attach(IWhitelistManagementView view)
        {
            this.view = view;
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

    public interface IWhitelistManagementView
    {
        void UpdateWhitelist(List<string> whitelist);
    }
}