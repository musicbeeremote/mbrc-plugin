using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.PartyMode.Core.View;
using MusicBeeRemote.PartyMode.Core.ViewModel;
using NLog;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    /// <summary>
    ///     Represents the Settings and monitoring dialog of the plugin.
    /// </summary>
    public partial class InfoWindow : Form, SocketTester.IConnectionListener
    {
        private readonly PersistanceManager _settings;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private BindingList<string> _ipAddressBinding;
        private IOnDebugSelectionChanged _listener;
        private SocketTester _socketTester;

        /// <summary>
        /// </summary>
        public InfoWindow(PersistanceManager settings, PartyModeViewModel viewModel)
        {
            _settings = settings;
            InitializeComponent();
            _ipAddressBinding = new BindingList<string>();
            var partyModeView = new PartyModeView {DataContext = viewModel};
            partyModeView.InitializeComponent();
            elementHost1.Dock = DockStyle.Fill;
            elementHost1.Child = partyModeView;
            helpButton.Click += HelpButtonClick;
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
            internalIPList.DataSource = Tools.GetPrivateAddressList();
            versionLabel.Text = _settings.UserSettingsModel.CurrentVersion;
            portNumericUpDown.Value = _settings.UserSettingsModel.ListeningPort;
            UpdateFilteringSelection(_settings.UserSettingsModel.FilterSelection);

            allowedAddressesComboBox.DataSource = _ipAddressBinding;


            debugEnabled.Checked = _settings.UserSettingsModel.DebugLogEnabled;
            firewallCheckbox.Checked = _settings.UserSettingsModel.UpdateFirewall;


            _socketTester = new SocketTester(_settings) {ConnectionListener = this};
            _socketTester.VerifyConnection();
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
                    _settings.UserSettingsModel.FilterSelection = FilteringSelection.All;
                    break;
                case 1:
                    addressLabel.Enabled = true;
                    ipAddressInputTextBox.Enabled = true;
                    rangeNumericUpDown.Enabled = true;
                    addAddressButton.Enabled = false;
                    removeAddressButton.Enabled = false;
                    allowedAddressesComboBox.Enabled = false;
                    allowedLabel.Enabled = false;
                    _settings.UserSettingsModel.FilterSelection = FilteringSelection.Range;
                    break;
                case 2:
                    addressLabel.Enabled = true;
                    ipAddressInputTextBox.Enabled = true;
                    rangeNumericUpDown.Enabled = false;
                    addAddressButton.Enabled = true;
                    removeAddressButton.Enabled = true;
                    allowedAddressesComboBox.Enabled = true;
                    allowedLabel.Enabled = true;
                    _settings.UserSettingsModel.FilterSelection = FilteringSelection.Specific;
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
                    ipAddressInputTextBox.Text = _settings.UserSettingsModel.BaseIp;
                    rangeNumericUpDown.Value = _settings.UserSettingsModel.LastOctetMax;
                    selectionFilteringComboBox.SelectedIndex = 1;
                    break;
                case FilteringSelection.Specific:
                    _ipAddressBinding = new BindingList<string>(_settings.UserSettingsModel.IpAddressList);
                    selectionFilteringComboBox.SelectedIndex = 2;
                    break;
                default:
                    selectionFilteringComboBox.SelectedIndex = 0;
                    break;
            }
        }

        private void HandleSaveButtonClick(object sender, EventArgs e)
        {
            _settings.UserSettingsModel.ListeningPort = (uint) portNumericUpDown.Value;

            switch (selectionFilteringComboBox.SelectedIndex)
            {
                case 0:
                    break;
                case 1:
                    _settings.UserSettingsModel.BaseIp = ipAddressInputTextBox.Text;
                    _settings.UserSettingsModel.LastOctetMax = (uint) rangeNumericUpDown.Value;
                    break;
                case 2:
                    _settings.UserSettingsModel.IpAddressList = new List<string>(_ipAddressBinding);
                    break;
            }

            _settings.UserSettingsModel.UpdateFirewall = firewallCheckbox.Checked;
            _settings.SaveSettings();

            if (firewallCheckbox.Checked)
            {
                UpdateFirewallRules(_settings.UserSettingsModel.ListeningPort);
            }

            _socketTester = new SocketTester(_settings) {ConnectionListener = this};
            _socketTester.VerifyConnection();
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
            var settings = _settings;
            settings.UserSettingsModel.DebugLogEnabled = debugEnabled.Checked;
            _listener?.SelectionChanged(settings.UserSettingsModel.DebugLogEnabled);
        }

        private void OpenLogButtonClick(object sender, EventArgs e)
        {
//            if (File.Exists(_settings.FullLogPath))
//            {
//                //Process.Start(_settings.FullLogPath);
//            }
//            else
//            {
//                MessageBox.Show(Resources.InfoWindow_OpenLogButtonClick_Log_file_doesn_t_exist);
//            }
        }

        public interface IOnDebugSelectionChanged
        {
            void SelectionChanged(bool enabled);
        }

        public void SetOnDebugSelectionListener(IOnDebugSelectionChanged listener)
        {
            _listener = listener;
        }

        /// <summary>
        ///     When called it will execute the firewall-utility passing the port settings
        ///     needed by the plugin.
        /// </summary>
        public void UpdateFirewallRules(uint port)
        {
            var startInfo = new ProcessStartInfo(
                $"{AppDomain.CurrentDomain.BaseDirectory}\\Plugins\\firewall-utility.exe")
            {
                Verb = "runas",
                Arguments =
                    $"-s {port}"
            };
            Process.Start(startInfo);
        }

        public void OnConnectionResult(bool isConnnected)
        {
            UpdateSocketStatus(isConnnected);
        }
    }
}