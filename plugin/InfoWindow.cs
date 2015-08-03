using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.Tools;

namespace MusicBeePlugin
{
    /// <summary>
    ///     Represents the Settings and monitoring dialog of the plugin.
    /// </summary>
    public partial class InfoWindow : Form
    {
        private BindingList<string> ipAddressBinding;

        /// <summary>
        /// </summary>
        public InfoWindow()
        {
            InitializeComponent();
            ipAddressBinding = new BindingList<string>();
        }

        /// <summary>
        ///     Updates the visual indicator with the current Socket server status.
        /// </summary>
        /// <param name="isRunning"></param>
        public void UpdateSocketStatus(bool isRunning)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(() => UpdateSocketStatus(isRunning)));
                return;
            }
            if (isRunning)
            {
                statusLabel.Text = @"Running";
                statusLabel.ForeColor = Color.Green;
            }
            else
            {
                statusLabel.Text = @"Stopped";
                statusLabel.ForeColor = Color.Red;
            }
        }

        private void HelpButtonClick(object sender, EventArgs e)
        {
            Process.Start("http://kelsos.net/musicbeeremote/help/");
        }

        private void InfoWindowLoad(object sender, EventArgs e)
        {
            var settings = UserSettings.Instance;
            internalIPList.DataSource = NetworkTools.GetPrivateAddressList();
            versionLabel.Text = settings.CurrentVersion;
            portNumericUpDown.Value = settings.ListeningPort;
            UpdateFilteringSelection(settings.FilterSelection);
            nowPlayingListLimit.Value = settings.NowPlayingListLimit;
            UpdateSocketStatus(SocketServer.Instance.IsRunning);
            allowedAddressesComboBox.DataSource = ipAddressBinding;

            if (settings.Source == SearchSource.None) 
            {
                settings.Source |= SearchSource.Library;
            }

            libraryCheckbox.SetChecked((settings.Source & SearchSource.Library) == SearchSource.Library);
            audiobookCheckbox.SetChecked((settings.Source & SearchSource.Audiobooks) == SearchSource.Audiobooks);
            podcastCheckbox.SetChecked((settings.Source & SearchSource.Podcasts) == SearchSource.Podcasts);
            inboxCheckbox.SetChecked((settings.Source & SearchSource.Inbox) == SearchSource.Inbox);
            videoCheckbox.SetChecked((settings.Source & SearchSource.Videos) == SearchSource.Videos);

            Debug.WriteLine((int) settings.Source);
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
            UserSettings.Instance.ListeningPort = (uint) portNumericUpDown.Value;
            UserSettings.Instance.NowPlayingListLimit = (uint) nowPlayingListLimit.Value;
            switch (selectionFilteringComboBox.SelectedIndex)
            {
                case 0:
                    break;
                case 1:
                    UserSettings.Instance.BaseIp = ipAddressInputTextBox.Text;
                    UserSettings.Instance.LastOctetMax = (uint) rangeNumericUpDown.Value;
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
            const string Pattern =
                @"\b(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
            return Regex.IsMatch(ipAddressInputTextBox.Text, Pattern);
        }

        private void HandleIpAddressInputTextBoxTextChanged(object sender, EventArgs e)
        {
            var isAddressValid = IsAddressValid();
            ipAddressInputTextBox.BackColor = isAddressValid ? Color.LightGreen : Color.Red;
            if (isAddressValid && selectionFilteringComboBox.SelectedIndex == 1)
            {
                var addressSplit = ipAddressInputTextBox.Text.Split(".".ToCharArray(),
                    StringSplitOptions.RemoveEmptyEntries);
                rangeNumericUpDown.Minimum = int.Parse(addressSplit[3]);
            }
        }

        private void LibraryCheckboxCheckedChanged(object sender, EventArgs e)
        {
            if (libraryCheckbox.Checked)
            {
                UserSettings.Instance.Source |= SearchSource.Library;
            }
            else
            {
                UserSettings.Instance.Source &= ~SearchSource.Library;
            }
        }

        private void audiobookCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (audiobookCheckbox.Checked)
            {
                UserSettings.Instance.Source |= SearchSource.Audiobooks;
            }
            else
            {
                UserSettings.Instance.Source &= ~SearchSource.Audiobooks;
            }
        }

        private void inboxCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (inboxCheckbox.Checked)
            {
                UserSettings.Instance.Source |= SearchSource.Inbox;
            }
            else
            {
                UserSettings.Instance.Source &= ~SearchSource.Inbox;
            }
        }

        private void podcastCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (podcastCheckbox.Checked)
            {
                UserSettings.Instance.Source |= SearchSource.Podcasts;
            }
            else
            {
                UserSettings.Instance.Source &= ~SearchSource.Podcasts;
            }
        }

        private void videoCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (videoCheckbox.Checked)
            {
                UserSettings.Instance.Source |= SearchSource.Videos;
            }
            else
            {
                UserSettings.Instance.Source &= ~SearchSource.Videos;
            }

            Debug.WriteLine((int )UserSettings.Instance.Source);
        }
    }
}