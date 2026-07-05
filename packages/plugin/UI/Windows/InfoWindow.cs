using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Properties;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.Services.UI;
using MusicBeePlugin.Tools;
using MusicBeePlugin.Utilities.Network;

namespace MusicBeePlugin.UI.Windows
{
    /// <summary>
    ///     Represents the Settings and monitoring dialog of the plugin.
    /// </summary>
    public partial class InfoWindow : Form, IInfoWindow, IConnectionListener
    {
        private readonly ICoverCache _coverCache;
        private readonly IPluginLogger _logger;
        private readonly INetworkingManager _networkingManager;
        private readonly ISocketTester _socketTester;
        private static readonly char[] DotSeparator = { '.' };
        private readonly IUserSettingsService _userSettingsService;
        private BindingList<string> _ipAddressBinding;
        private IOnDebugSelectionChanged _onDebugSelectionChangedListener;
        private IOnInvalidateCacheListener _onInvalidateCacheListener;

        /// <summary>
        ///     Represents the settings and monitoring interface window of the plugin.
        ///     This window provides the user options for configuring and monitoring
        ///     the plugin's network connection, debugging, and cache functionalities.
        /// </summary>
        /// <remarks>
        ///     This class implements the `ISocketTester.IConnectionListener` interface
        ///     to respond to network connection status updates.
        /// </remarks>
        public InfoWindow(
            IUserSettingsService userSettingsService,
            INetworkingManager networkingManager,
            ISocketTester socketTester,
            ICoverService coverService,
            ICoverCache coverCache,
            IPluginLogger logger)
        {
            _userSettingsService = userSettingsService;
            _networkingManager = networkingManager;
            _socketTester = socketTester;
            _coverCache = coverCache;
            _logger = logger;

            InitializeComponent();
            _ipAddressBinding = new BindingList<string>();
        }

        public void OnConnectionResult(bool isConnected)
        {
            UpdateSocketStatus(isConnected);
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

        public void UpdateCacheState(string cached)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(() => UpdateCacheState(cached)));
                return;
            }

            coversCacheValue.Text = cached;
        }

        private void HelpButtonClick(object sender, EventArgs e)
        {
            Process.Start("https://mbrc.kelsos.net/help/");
        }

        private void InfoWindowLoad(object sender, EventArgs e)
        {
            var settings = _userSettingsService;
            internalIPList.DataSource = NetworkTools.GetPrivateAddressList();
            versionLabel.Text = settings.CurrentVersion;
            portNumericUpDown.Value = settings.ListeningPort;
            UpdateFilteringSelection(settings.FilterSelection);

            UpdateSocketStatus(_networkingManager.IsRunning);
            allowedAddressesComboBox.DataSource = _ipAddressBinding;

            if (settings.Source == SearchSource.None)
            {
                var currentSettings = _userSettingsService.GetSettingsModel();
                currentSettings.Source |= SearchSource.Library;
                _userSettingsService.UpdateSettings(currentSettings);
            }

            debugEnabled.Checked = settings.DebugLogEnabled;
            firewallCheckbox.Checked = settings.UpdateFirewall;

            _logger.Debug($"Selected source is -> {settings.Source}");

            _socketTester.ConnectionListener = this;
            _socketTester.VerifyConnection();

            // Initialize cover cache state display
            InitializeCacheStateDisplay();
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
                    // Filter selection will be updated when saving
                    break;
                case 1:
                    addressLabel.Enabled = true;
                    ipAddressInputTextBox.Enabled = true;
                    rangeNumericUpDown.Enabled = true;
                    addAddressButton.Enabled = false;
                    removeAddressButton.Enabled = false;
                    allowedAddressesComboBox.Enabled = false;
                    allowedLabel.Enabled = false;
                    // Filter selection will be updated when saving
                    break;
                case 2:
                    addressLabel.Enabled = true;
                    ipAddressInputTextBox.Enabled = true;
                    rangeNumericUpDown.Enabled = false;
                    addAddressButton.Enabled = true;
                    removeAddressButton.Enabled = true;
                    allowedAddressesComboBox.Enabled = true;
                    allowedLabel.Enabled = true;
                    // Filter selection will be updated when saving
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
                    ipAddressInputTextBox.Text = _userSettingsService.BaseIp;
                    rangeNumericUpDown.Value = _userSettingsService.LastOctetMax;
                    selectionFilteringComboBox.SelectedIndex = 1;
                    break;
                case FilteringSelection.Specific:
                    _ipAddressBinding = new BindingList<string>(_userSettingsService.IpAddressList.ToList());
                    selectionFilteringComboBox.SelectedIndex = 2;
                    break;
                default:
                    selectionFilteringComboBox.SelectedIndex = 0;
                    break;
            }
        }

        private void HandleSaveButtonClick(object sender, EventArgs e)
        {
            // Get current settings to modify
            var currentSettings = _userSettingsService.GetSettingsModel();
            currentSettings.ListeningPort = (uint)portNumericUpDown.Value;

            switch (selectionFilteringComboBox.SelectedIndex)
            {
                case 0:
                    break;
                case 1:
                    currentSettings.BaseIp = ipAddressInputTextBox.Text;
                    currentSettings.LastOctetMax = (uint)rangeNumericUpDown.Value;
                    currentSettings.FilterSelection = FilteringSelection.Range;
                    break;
                case 2:
                    currentSettings.IpAddressList = new List<string>(_ipAddressBinding);
                    currentSettings.FilterSelection = FilteringSelection.Specific;
                    break;
            }

            // Set other settings
            if (selectionFilteringComboBox.SelectedIndex == 0)
                currentSettings.FilterSelection = FilteringSelection.All;

            currentSettings.UpdateFirewall = firewallCheckbox.Checked;

            // Update and save settings
            _userSettingsService.UpdateSettings(currentSettings);
            _userSettingsService.SaveSettings();

            if (firewallCheckbox.Checked)
                UpdateFirewallRules(currentSettings.ListeningPort);

            _socketTester.VerifyConnection();
        }

        private void AddAddressButtonClick(object sender, EventArgs e)
        {
            if (!IsAddressValid())
                return;
            if (!_ipAddressBinding.Contains(ipAddressInputTextBox.Text))
                _ipAddressBinding.Add(ipAddressInputTextBox.Text);
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
            if (!isAddressValid || selectionFilteringComboBox.SelectedIndex != 1)
                return;
            var addressSplit = ipAddressInputTextBox.Text.Split(DotSeparator,
                StringSplitOptions.RemoveEmptyEntries);
            rangeNumericUpDown.Minimum = int.Parse(addressSplit[3], CultureInfo.InvariantCulture);
        }

        private void DebugCheckboxCheckedChanged(object sender, EventArgs e)
        {
            var currentSettings = _userSettingsService.GetSettingsModel();
            currentSettings.DebugLogEnabled = debugEnabled.Checked;
            _userSettingsService.UpdateSettings(currentSettings);
            _onDebugSelectionChangedListener?.SelectionChanged(currentSettings.DebugLogEnabled);
        }

        private void OpenLogButtonClick(object sender, EventArgs e)
        {
            if (File.Exists(_userSettingsService.FullLogPath))
                Process.Start(_userSettingsService.FullLogPath);
            else
                MessageBox.Show(Resources.InfoWindow_OpenLogButtonClick_Log_file_doesn_t_exist);
        }

        private void OnCacheInvalidateButtonPressed(object sender, EventArgs e)
        {
            _onInvalidateCacheListener?.InvalidateCache();
        }

        public void SetOnDebugSelectionListener(IOnDebugSelectionChanged onDebugSelectionChangedListener)
        {
            _onDebugSelectionChangedListener = onDebugSelectionChangedListener;
        }

        /// <summary>
        ///     When called it will execute the firewall-utility passing the port settings
        ///     needed by the plugin.
        /// </summary>
        private static void UpdateFirewallRules(uint port)
        {
            var cmd = $"{AppDomain.CurrentDomain.BaseDirectory}\\Plugins\\firewall-utility.exe";
            if (!File.Exists(cmd))
                return;
            var startInfo = new ProcessStartInfo(cmd)
            {
                Verb = "runas",
                Arguments = $"-s {port}"
            };
            Process.Start(startInfo);
        }

        public void SetOnInvalidateCacheListener(IOnInvalidateCacheListener onInvalidateCacheListener)
        {
            _onInvalidateCacheListener = onInvalidateCacheListener;
        }

        /// <summary>
        ///     Initializes the cover cache state display with the current cache count.
        /// </summary>
        private void InitializeCacheStateDisplay()
        {
            try
            {
                // Get the current cache state (number of cached covers)
                var cacheState = _coverCache.State;
                UpdateCacheState(cacheState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize cache state display");
                UpdateCacheState("0");
            }
        }

        public interface IOnDebugSelectionChanged
        {
            void SelectionChanged(bool enabled);
        }

        public interface IOnInvalidateCacheListener
        {
            void InvalidateCache();
        }
    }
}
