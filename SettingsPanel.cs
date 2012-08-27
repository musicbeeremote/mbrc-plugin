using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MusicBeePlugin.AndroidRemote.Events;

namespace MusicBeePlugin
{
    /// <summary>
    /// Represents the plugins Settings Panel
    /// </summary>
    public partial class SettingsPanel : UserControl
    {
        /// <summary>
        /// This event gets fired when the filtering selection changes.
        /// </summary>
        public event EventHandler<MessageEventArgs> SelectionChanged;

        /// <summary>
        /// This event gets fired when a new address is added to the allowed addresses specified.
        /// </summary>
        public event EventHandler<MessageEventArgs> AddressAdded;

        /// <summary>
        /// This event gets fired when an address is removed from the allowed addresses specified.
        /// </summary>
        public event EventHandler<MessageEventArgs> AddressRemoved;

        /// <summary>
        /// This event gets fired when the range of allowed addresses changes.
        /// </summary>
        public event EventHandler<MessageEventArgs> RangeChanged;

        /// <summary>
        /// This event gets fired when the default port is changed. 
        /// </summary>
        public event EventHandler<MessageEventArgs> PortChanged;

        private void OnSelectionChanged(MessageEventArgs args)
        {
            EventHandler<MessageEventArgs> handler = SelectionChanged;
            if (handler != null) handler(this, args);
        }

        private void OnAddressAdded(MessageEventArgs args)
        {
            EventHandler<MessageEventArgs> handler = AddressAdded;
            if (handler != null) handler(this, args);
        }

        private void OnAddressRemoved(MessageEventArgs args)
        {
            EventHandler<MessageEventArgs> handler = AddressRemoved;
            if (handler != null) handler(this, args);
        }

        private void OnRangeChanged(MessageEventArgs args)
        {
            EventHandler<MessageEventArgs> handler = RangeChanged;
            if (handler != null) handler(this, args);
        }

        private void OnPortChanged(MessageEventArgs args)
        {
            EventHandler<MessageEventArgs> handler = PortChanged;
            if (handler != null) handler(this, args);
        }

        /// <summary>
        /// Default Constructor
        /// </summary>
        public SettingsPanel()
        {
            InitializeComponent();
            // Add event listeners for the available controls.
            selectionFilteringComboBox.SelectedIndexChanged += HandleSelectionFilteringComboBoxSelectedIndexChanged;
            portNumericUpDown.ValueChanged += HandlePortNumericUpDownValueChange;
            addAddressButton.Click += HandleAddAddressButtonClick;
            removeAddressButton.Click += HandleRemoveAddressButtonClick;
            ipAddressInputTextBox.TextChanged += HandleIpAddressInputTextBoxTextChanged;
            rangeNumericUpDown.ValueChanged += HandleRangeNumericUpDownValueChanged;
        }

        private void HandlePortNumericUpDownValueChange(object sender, EventArgs e)
        {
            OnPortChanged(new MessageEventArgs(portNumericUpDown.Text));
        }

        private void HandleRangeNumericUpDownValueChanged(object sender, EventArgs e)
        {
            if (IsAddressValid())
            {
                OnRangeChanged(new MessageEventArgs(ipAddressInputTextBox.Text + "," + rangeNumericUpDown.Text));
            }
        }

        private void HandleIpAddressInputTextBoxTextChanged(object sender, EventArgs e)
        {
            var isAddressValid = IsAddressValid();
            ipAddressInputTextBox.BackColor = isAddressValid ? Color.Green : Color.Red;
            if (isAddressValid && selectionFilteringComboBox.SelectedIndex == 1)
            {
                string[] addressSplit = ipAddressInputTextBox.Text.Split(".".ToCharArray(),
                                                                         StringSplitOptions.RemoveEmptyEntries);
                rangeNumericUpDown.Minimum = int.Parse(addressSplit[3]);
                OnRangeChanged(new MessageEventArgs(ipAddressInputTextBox.Text + "," + rangeNumericUpDown.Text));
            }
        }

        private bool IsAddressValid()
        {
            const string pattern =
                @"\b(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
            return Regex.IsMatch(ipAddressInputTextBox.Text, pattern);
        }

        private void HandleRemoveAddressButtonClick(object sender, EventArgs e)
        {
            if (!String.IsNullOrEmpty(allowedAddressesComboBox.Text)){}
                OnAddressRemoved(new MessageEventArgs(allowedAddressesComboBox.Text));
        }

        private void HandleAddAddressButtonClick(object sender, EventArgs e)
        {
            if (!IsAddressValid()) return;
            OnAddressAdded(new MessageEventArgs(ipAddressInputTextBox.Text));
            ipAddressInputTextBox.Text = String.Empty;
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
            OnSelectionChanged(
                new MessageEventArgs(selectionFilteringComboBox.SelectedIndex.ToString(CultureInfo.InvariantCulture)));
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

        /// <summary>
        /// Given a comma seperated string of values it puts the right values in the right controls depending on the selection.
        /// </summary>
        /// <param name="values"></param>
        public void UpdateValues(string values)
        {
            if (String.IsNullOrEmpty(values)) return;
            switch (selectionFilteringComboBox.SelectedIndex)
            {
                case 0:
                    break;
                case 1:
                    string[] splitRange = values.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    ipAddressInputTextBox.Text = splitRange[0];
                    rangeNumericUpDown.Value = int.Parse(splitRange[1]);
                    break;
                case 2:
                    allowedAddressesComboBox.DataSource = new List<string>(values.Trim().Split(",".ToCharArray(),
                                                                                               StringSplitOptions.
                                                                                                   RemoveEmptyEntries));
                    break;
            }
        }

        /// <summary>
        /// Given a port number it sets the selected port number on the NumericUpDown.
        /// </summary>
        /// <param name="port"></param>
        public void UpdatePortNumber(int port)
        {
            portNumericUpDown.Value = port;
        }
    }
}