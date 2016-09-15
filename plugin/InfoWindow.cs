using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.Properties;
using MusicBeePlugin.Tools;
using NLog;

namespace MusicBeePlugin
{
    /// <summary>
    ///     Represents the Settings and monitoring dialog of the plugin.
    /// </summary>
    public partial class InfoWindow : Form
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private BindingList<string> _ipAddressBinding;
        private IOnDebugSelectionChanged listener;

        /// <summary>
        /// </summary>
        public InfoWindow()
        {
            InitializeComponent();
            _ipAddressBinding = new BindingList<string>();
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
   
            UpdateSocketStatus(SocketServer.Instance.IsRunning);
            allowedAddressesComboBox.DataSource = _ipAddressBinding;

            if (settings.Source == SearchSource.None) 
            {
                settings.Source |= SearchSource.Library;
            }

            debugEnabled.Checked = settings.DebugLogEnabled;

            _logger.Debug($"Selected source is -> {settings.Source}");
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
                    _ipAddressBinding = new BindingList<string>(UserSettings.Instance.IpAddressList);
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
     
            switch (selectionFilteringComboBox.SelectedIndex)
            {
                case 0:
                    break;
                case 1:
                    UserSettings.Instance.BaseIp = ipAddressInputTextBox.Text;
                    UserSettings.Instance.LastOctetMax = (uint) rangeNumericUpDown.Value;
                    break;
                case 2:
                    UserSettings.Instance.IpAddressList = new List<string>(_ipAddressBinding);
                    break;
            }
            UserSettings.Instance.SaveSettings();
        }

        private void AddAddressButtonClick(object sender, EventArgs e)
        {
            if (!IsAddressValid()) return;
            if (!_ipAddressBinding.Contains(ipAddressInputTextBox.Text))
            {
                _ipAddressBinding.Add(ipAddressInputTextBox.Text);
            }
        }

        private void RemoveAddressButtonClick(object sender, EventArgs e)
        {
            _ipAddressBinding.Remove(allowedAddressesComboBox.Text);
        }

        private bool IsAddressValid()
        {
            const string pattern =
                @"\b(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
            return Regex.IsMatch(ipAddressInputTextBox.Text, pattern);
        }

        private void HandleIpAddressInputTextBoxTextChanged(object sender, EventArgs e)
        {
            var isAddressValid = IsAddressValid();
            ipAddressInputTextBox.BackColor = isAddressValid ? Color.LightGreen : Color.Red;
            if (!isAddressValid || selectionFilteringComboBox.SelectedIndex != 1) return;
            var addressSplit = ipAddressInputTextBox.Text.Split(".".ToCharArray(),
                StringSplitOptions.RemoveEmptyEntries);
            rangeNumericUpDown.Minimum = int.Parse(addressSplit[3]);
        }

        private void DebugCheckboxCheckedChanged(object sender, EventArgs e)
        {
            var settings = UserSettings.Instance;
            settings.DebugLogEnabled = debugEnabled.Checked;
            listener?.SelectionChanged(settings.DebugLogEnabled);
        }

        private void OpenLogButtonClick(object sender, EventArgs e)
        {
            if (File.Exists(UserSettings.Instance.FullLogPath))
            {
                Process.Start(UserSettings.Instance.FullLogPath);
            }
            else
            {
                MessageBox.Show(Resources.InfoWindow_OpenLogButtonClick_Log_file_doesn_t_exist);
            }
           
        }

        public interface IOnDebugSelectionChanged
        {
            void SelectionChanged(bool enabled);
        }

        public void SetOnDebugSelectionListener(IOnDebugSelectionChanged listener)
        {
            this.listener = listener;
        }
    }
}