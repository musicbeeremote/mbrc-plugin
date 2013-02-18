using MusicBeePlugin.AndroidRemote.Enumerations;

namespace MusicBeePlugin
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Drawing;
    using System.Text.RegularExpressions;
    using System.Windows.Forms;

    using MusicBeePlugin.AndroidRemote.Settings;
    using MusicBeePlugin.Tools;

    /// <summary>
    /// 
    /// </summary>
    public partial class InfoWindow : Form
    {

        private BindingList<String> ipAddressBinding;  
        /// <summary>
        /// 
        /// </summary>
        public InfoWindow()
        {
            this.InitializeComponent();
            this.ipAddressBinding = new BindingList<string>();
        }

        private void HelpButtonClick(object sender, EventArgs e)
        {
            Process.Start("http://kelsos.net/musicbeeremote/#help");
        }

        private void InfoWindowLoad(object sender, EventArgs e)
        {
            this.internalIPList.DataSource = NetworkTools.GetPrivateAddressList();
            this.versionLabel.Text = UserSettings.Instance.CurrentVersion;
            this.portNumericUpDown.Value = UserSettings.Instance.ListeningPort;
            this.UpdateFilteringSelection(UserSettings.Instance.FilterSelection);
            this.nowPlayingListLimit.Value = UserSettings.Instance.NowPlayingListLimit;
            allowedAddressesComboBox.DataSource = ipAddressBinding;
        }

        private void HandleCheckForUpdateButtonClick(object sender, EventArgs e)
        {
            if (UpdateChecker.IsThereAnUpdate(UserSettings.Instance.CurrentVersion, UserSettings.Instance.StoragePath))
            {
                latestVersionLabel.Text = "There is a newer version out (v" + UpdateChecker.LatestVersion + ")";
            }
            else
            {
                latestVersionLabel.Text = "Your plugin is up to date";
            }
            Plugin.Instance.RequestLoveStatusChange("");
        }

        private void SelectionFilteringComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            switch (selectionFilteringComboBox.SelectedIndex)
            {
                case 0:
                    addressLabel.Enabled = false;
                    ipAddressInputTextBox.Enabled = false;
                    rangeNumericUpDown.Enabled = false;
                    addAddressButton.Enabled = false;
                    removeAddressButton.Enabled = false;
                    allowedAddressesComboBox.Enabled = false;
                    allowedLabel.Enabled = false;
                    UserSettings.Instance.FilterSelection = FilteringSelection.All;
                    break;
                case 1:
                    addressLabel.Enabled = true;
                    ipAddressInputTextBox.Enabled = true;
                    rangeNumericUpDown.Enabled = true;
                    addAddressButton.Enabled = false;
                    removeAddressButton.Enabled = false;
                    allowedAddressesComboBox.Enabled = false;
                    allowedLabel.Enabled = false;
                    UserSettings.Instance.FilterSelection = FilteringSelection.Range;
                    break;
                case 2:
                    addressLabel.Enabled = true;
                    ipAddressInputTextBox.Enabled = true;
                    rangeNumericUpDown.Enabled = false;
                    addAddressButton.Enabled = true;
                    removeAddressButton.Enabled = true;
                    allowedAddressesComboBox.Enabled = true;
                    allowedLabel.Enabled = true;
                    UserSettings.Instance.FilterSelection = FilteringSelection.Specific;
                    break;
            }
        }

        private void UpdateFilteringSelection(FilteringSelection selection)
        {
            switch (selection)
            {
                case FilteringSelection.All:
                    selectionFilteringComboBox.SelectedIndex = 0;
                    break;
                case FilteringSelection.Range:
                    ipAddressInputTextBox.Text = UserSettings.Instance.BaseIp;
                    rangeNumericUpDown.Value = UserSettings.Instance.LastOctetMax;
                    selectionFilteringComboBox.SelectedIndex = 1;
                    break;
                case FilteringSelection.Specific:
                    ipAddressBinding = new BindingList<string>(UserSettings.Instance.IpAddressList);
                    selectionFilteringComboBox.SelectedIndex = 2;
                    break;
                default:
                    selectionFilteringComboBox.SelectedIndex = 0;
                    break;
            }
        }

        private void HandleSaveButtonClick(object sender, EventArgs e)
        {
            UserSettings.Instance.ListeningPort = (uint)this.portNumericUpDown.Value;
            UserSettings.Instance.NowPlayingListLimit = (uint)this.nowPlayingListLimit.Value;
            switch (selectionFilteringComboBox.SelectedIndex)
            {
                case 0:
                    break;
                case 1:
                    UserSettings.Instance.BaseIp = ipAddressInputTextBox.Text;
                    UserSettings.Instance.LastOctetMax = (uint)this.rangeNumericUpDown.Value;
                    break;
                case 2:
                    UserSettings.Instance.IpAddressList = new List<string>(ipAddressBinding);
                    break;

            }
            UserSettings.Instance.SaveSettings();
        }

        private void AddAddressButtonClick(object sender, EventArgs e)
        {
            if (!IsAddressValid()) return;
            if (!ipAddressBinding.Contains(ipAddressInputTextBox.Text))
            {
                ipAddressBinding.Add(ipAddressInputTextBox.Text);
            }
        }

        private void RemoveAddressButtonClick(object sender, EventArgs e)
        {
            ipAddressBinding.Remove(allowedAddressesComboBox.Text);
        }

        private bool IsAddressValid()
        {
            const string Pattern = @"\b(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
            return Regex.IsMatch(ipAddressInputTextBox.Text, Pattern);
        }

        private void HandleIpAddressInputTextBoxTextChanged(object sender, EventArgs e)
        {
            var isAddressValid = IsAddressValid();
            ipAddressInputTextBox.BackColor = isAddressValid ? Color.LightGreen : Color.Red;
            if (isAddressValid && selectionFilteringComboBox.SelectedIndex == 1)
            {
                string[] addressSplit = ipAddressInputTextBox.Text.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                rangeNumericUpDown.Minimum = int.Parse(addressSplit[3]);
            }
        }

    }
}
