using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MusicBeeRemote.Core.Settings.Dialog.Validations;

namespace MusicBeeRemote.Core.Settings.Dialog.Whitelist
{
    /// <summary>
    /// A control used to manage the whitelisted IP addresses.
    /// </summary>
    public partial class WhitelistManagementControl : UserControl, IWhitelistManagementView
    {
        private readonly IWhitelistManagementPresenter _presenter;

        public WhitelistManagementControl(IWhitelistManagementPresenter presenter)
        {
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            InitializeComponent();
            _presenter.Attach(this);
            _presenter.Load();
            addressAddButton.Enabled = false;
        }

        public void UpdateWhitelist(List<string> whitelist)
        {
            if (whitelist == null)
            {
                throw new ArgumentNullException(nameof(whitelist));
            }

            var bindingSource = new BindingSource { DataSource = whitelist };
            whitelistComboBox.DataSource = bindingSource;
            addressRemoveButton.Enabled = whitelist.Count > 0;
        }

        private void AddressAddButtonClick(object sender, EventArgs e)
        {
            var input = newAddressTextBox.Text;
            _presenter.AddAddress(input);
            newAddressTextBox.Text = string.Empty;
        }

        private void AddressRemoveButtonClick(object sender, EventArgs e)
        {
            var addresses = whitelistComboBox.Items;
            if (addresses.Count > 0)
            {
                _presenter.RemoveAddress(addresses[0].ToString());
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

            if (AddressValidationRule.Validate(input))
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
