using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    /// <summary>
    /// Represents the plugins Settings Panel
    /// </summary>
    public partial class SettingsPanel : UserControl
    {
        /// <summary>
        /// 
        /// </summary>
        public SettingsPanel()
        {
            InitializeComponent();
            // Add event listeners for the available controls.
            selectionFilteringComboBox.SelectedIndexChanged +=HandleSelectionFilteringComboBoxSelectedIndexChanged;
            addAddressButton.Click += HandleAddAddressButtonClick;
            removeAddressButton.Click += HandleRemoveAddressButtonClick;
            ipAddressInputTextBox.TextChanged += HandleIpAddressInputTextBoxTextChanged;
        }

        private void HandleIpAddressInputTextBoxTextChanged(object sender, EventArgs e)
        {
            const string pattern = @"\b(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
            bool isAddressValid = Regex.IsMatch(ipAddressInputTextBox.Text, pattern);
            ipAddressInputTextBox.BackColor = isAddressValid ? Color.Green: Color.Red;
            if(isAddressValid && selectionFilteringComboBox.SelectedIndex==1)
            {
                string[] addressSplit = ipAddressInputTextBox.Text.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                rangeNumericUpDown.Minimum = int.Parse(addressSplit[3]);
            }
        }

        private void HandleRemoveAddressButtonClick(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void HandleAddAddressButtonClick(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void HandleSelectionFilteringComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            switch (selectionFilteringComboBox.SelectedIndex)
            {
                case 0:
                    addressLabel.Visible = false;
                    ipAddressInputTextBox.Visible = false;
                    dashLabel.Visible = false;
                    rangeNumericUpDown.Visible = false;
                    addAddressButton.Visible = false;
                    removeAddressButton.Visible = false;
                    allowedAddressesComboBox.Visible = false;
                    allowedLabel.Visible = false;
                    break;
                case 1:
                    addressLabel.Visible = true;
                    ipAddressInputTextBox.Visible = true;
                    dashLabel.Visible = true;
                    rangeNumericUpDown.Visible = true;
                    addAddressButton.Visible = false;
                    removeAddressButton.Visible = false;
                    allowedAddressesComboBox.Visible = false;
                    allowedLabel.Visible = false;
                    break;
                case 2:
                    addressLabel.Visible = true;
                    ipAddressInputTextBox.Visible = true;
                    dashLabel.Visible = false;
                    rangeNumericUpDown.Visible = false;
                    addAddressButton.Visible = true;
                    removeAddressButton.Visible = true;
                    allowedAddressesComboBox.Visible = true;
                    allowedLabel.Visible = true;
                    break;

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="selection"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void UpdateFilteringSelection(FilteringSelection selection)
        {
            switch (selection)
            {
                case FilteringSelection.All:
                    selectionFilteringComboBox.SelectedIndex = 0;
                    break;
                case FilteringSelection.Range:
                    selectionFilteringComboBox.SelectedIndex = 1;
                    break;
                case FilteringSelection.Specific:
                    selectionFilteringComboBox.SelectedIndex = 2;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("selection");
            }
        }
    }
}
