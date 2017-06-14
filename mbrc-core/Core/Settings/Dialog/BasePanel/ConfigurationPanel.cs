using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MusicBeeRemote.Core.Settings.Dialog.Converters;
using MusicBeeRemote.Core.Settings.Dialog.Range;
using MusicBeeRemote.Core.Settings.Dialog.Validations;
using MusicBeeRemote.Core.Settings.Dialog.Whitelist;

namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    public partial class ConfigurationPanel : Form, IConfigurationPanelView
    {
        private readonly IConfigurationPanelPresenter _presenter;
        private readonly PortValidationRule _validationRule;
        private readonly WhitelistManagementControl _whitelistManagementControl;
        private readonly RangeManagementControl _rangeManagementControl;

        public ConfigurationPanel(
            IConfigurationPanelPresenter presenter,
            WhitelistManagementControl whitelistManagementControl,
            RangeManagementControl rangeManagementControl
            )
        {
            _whitelistManagementControl = whitelistManagementControl;
            _rangeManagementControl = rangeManagementControl;
            _presenter = presenter;
            _validationRule = new PortValidationRule();
            InitializeComponent();
            _presenter.Attach(this);
            _presenter.Load();
        }

        public void UpdateLocalIpAddresses(List<string> localIpAddresses)
        {
            clientAddressList.DataSource = localIpAddresses;
        }

        public void UpdateListeningPort(uint modelListeningPort)
        {
            listeningPortNumber.Text = modelListeningPort.ToString();
        }

        public void UpdateStatus(SocketStatus socketStatus)
        {
            statusValueLabel.Text = socketStatus.TextLabel;
            statusValueLabel.ForeColor = socketStatus.LabelColor;
        }

        public void UpdateLoggingStatus(bool enabled)
        {
            enableDebugLoggingCheckbox.SetChecked(enabled);
        }

        public void UpdateFirewallStatus(bool enabled)
        {
            updateFirewallSettingsCheckbox.SetChecked(enabled);
        }             

        public void UpdatePluginVersion(string pluginVersion)
        {
            versionValueLabel.Text = pluginVersion;
        }

        public void UpdateFilteringData(IEnumerable<FilteringSelection> filteringData, FilteringSelection filteringSelection)
        {
            filteringOptionsComboBox.SelectedIndexChanged -= FilteringOptionsComboBox_SelectedIndexChanged;
            filteringOptionsComboBox.DataSource = filteringData;
            filteringOptionsComboBox.SelectedItem = filteringSelection;
            filteringOptionsComboBox.SelectedIndexChanged += FilteringOptionsComboBox_SelectedIndexChanged;
            UpdateAddressFilteringPanel(filteringSelection);
        }

        private void OpenHelpButtonClick(object sender, EventArgs e)
        {
            _presenter.OpenHelp();
        }

        private void SaveButtonClick(object sender, EventArgs e)
        {
            _presenter.SaveSettings();
        }

        private void OpenLogDirectoryButtonClick(object sender, EventArgs e)
        {
            _presenter.OpenLogDirectory();
        }

        private void UpdateFirewallSettingsCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _presenter.UpdateFirewallSettingsChanged(updateFirewallSettingsCheckbox.Checked);
        }

        private void EnableDebugLoggingCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _presenter.LoggingStatusChanged(enableDebugLoggingCheckbox.Checked);
        }

        private void listeningPortNumber_TextChanged(object sender, EventArgs e)
        {
            var portValue = listeningPortNumber.Text;
            var isValid = _validationRule.Validate(portValue);
            if (isValid)
            {
                listeningPortErrorProvider.Clear();
                var listeningPort = uint.Parse(portValue);
                _presenter.UpdateListeningPort(listeningPort);
            }
            else
            {
                listeningPortErrorProvider.SetError(listeningPortNumber, "Invalid Port Number");
            }
        }

        private void FilteringOptionsComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selected = (FilteringSelection)filteringOptionsComboBox.SelectedItem;           
            UpdateAddressFilteringPanel(selected);
            _presenter.UpdateFilteringSelection(selected);
        }

        private void UpdateAddressFilteringPanel(FilteringSelection selected)
        {
            foreach (Control panel1Control in filteringPanel.Controls)
            {
                filteringPanel.Controls.Remove(panel1Control);
            }

            switch (selected)
            {
                case FilteringSelection.Range:
                    filteringPanel.Controls.Add(_rangeManagementControl);
                    break;
                case FilteringSelection.Specific:
                    filteringPanel.Controls.Add(_whitelistManagementControl);
                    break;
            }
        }
    }
}