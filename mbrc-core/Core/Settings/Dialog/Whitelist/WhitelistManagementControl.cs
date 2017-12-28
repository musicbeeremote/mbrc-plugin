using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MusicBeeRemote.Core.Settings.Dialog.Validations;

namespace MusicBeeRemote.Core.Settings.Dialog.Whitelist
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
            addressAddButton.Enabled = false;
        }

        public void UpdateWhitelist(List<string> whitelist)
        {
            var bindingSource = new BindingSource {DataSource = whitelist};
            whitelistComboBox.DataSource = bindingSource;
            addressRemoveButton.Enabled = whitelist.Count > 0;           
        }

        private void AddressAddButtonClick(object sender, EventArgs e)
        {
            var input = newAddressTextBox.Text;
            _presenter.AddAddress(input);
            newAddressTextBox.Text = "";
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

        private void NewAddressTextBoxTextChanged(object sender, EventArgs e)
        {
            var input = newAddressTextBox.Text;

            if (string.IsNullOrWhiteSpace(input))
            {
                addressAddButton.Enabled = false;
                newAddressTextBox.BackColor = DefaultBackColor;
                return;
            }
            
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
}