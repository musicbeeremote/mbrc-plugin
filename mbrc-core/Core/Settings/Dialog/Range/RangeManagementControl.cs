using System.Drawing;
using System.Windows.Forms;
using MusicBeeRemote.Core.Settings.Dialog.Validations;

namespace MusicBeeRemote.Core.Settings.Dialog.Range
{
    public partial class RangeManagementControl : UserControl
    {
        private readonly RangeManagementViewModel _viewModel;
        private readonly AddressValidationRule _addressValidationRule;

        public RangeManagementControl(RangeManagementViewModel viewModel)
        {
            _addressValidationRule = new AddressValidationRule();
            _viewModel = viewModel;
            InitializeComponent();
            baseIpTextBox.Text = _viewModel.BaseIp;
            lastOctetTextBox.Text = _viewModel.LastOctetMax.ToString();
        }

        private void BaseIpTextBox_TextChanged(object sender, System.EventArgs e)
        {
            var address = baseIpTextBox.Text;
            if (_addressValidationRule.Validate(address))
            {
                _viewModel.BaseIp = address;
                baseIpTextBox.BackColor = DefaultBackColor;
            }
            else
            {
                baseIpTextBox.BackColor = Color.Red;
            }
        }

        private void LastOctetTextBox_TextChanged(object sender, System.EventArgs e)
        {

        }
    }
}
