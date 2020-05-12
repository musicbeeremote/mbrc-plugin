using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MusicBeeRemote.Core.Settings.Dialog.Validations;

namespace MusicBeeRemote.Core.Settings.Dialog.Range
{
    /// <summary>
    /// A control used to insert an IP range.
    /// </summary>
    public partial class RangeManagementControl : UserControl
    {
        private readonly RangeManagementViewModel _viewModel;

        public RangeManagementControl(RangeManagementViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            baseIpTextBox.Text = _viewModel.BaseIp;

            if (_viewModel.LastOctetMax > 0)
            {
                lastOctetTextBox.Text = _viewModel.LastOctetMax.ToString(CultureInfo.CurrentCulture);
            }
        }

        private void BaseIpTextBoxTextChanged(object sender, EventArgs e)
        {
            var address = baseIpTextBox.Text;
            if (AddressValidationRule.Validate(address))
            {
                _viewModel.BaseIp = address;
                baseIpTextBox.BackColor = DefaultBackColor;
            }
            else
            {
                baseIpTextBox.BackColor = Color.Red;
            }
        }

        private void LastOctetTextBoxTextChanged(object sender, EventArgs e)
        {
            if (LastOctetValidator.Validate(baseIpTextBox.Text, lastOctetTextBox.Text))
            {
                lastOctetTextBox.BackColor = DefaultBackColor;
                _viewModel.LastOctetMax = uint.Parse(lastOctetTextBox.Text, CultureInfo.CurrentCulture);
            }
            else
            {
                lastOctetTextBox.BackColor = Color.Red;
            }
        }
    }
}
