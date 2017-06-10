using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MusicBeeRemote.Core.Settings.Dialog.Converters;
using System.Diagnostics;
using MusicBeeRemote.Core.Settings.Dialog.Commands;
using MusicBeeRemote.Core.Settings.Dialog.Validations;

namespace MusicBeeRemote.Core.Settings.Dialog
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

        public void UpdateFilteringData(IEnumerable<FilteringSelection> modelFilterSelection)
        {
            filteringOptionsComboBox.DataSource = modelFilterSelection;
        }

        public void UpdateFilterSelection(FilteringSelection filteringSelection)
        {
            filteringOptionsComboBox.SelectedItem = filteringSelection;
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

    public interface IConfigurationPanelPresenter
    {
        void Load();
        void Attach(IConfigurationPanelView view);
        void OpenHelp();
        void SaveSettings();
        void OpenLogDirectory();
        void LoggingStatusChanged(bool @checked);
        void UpdateFirewallSettingsChanged(bool @checked);
        void UpdateListeningPort(uint listeningPort);
    }

    public class ConfigurationPanelPresenter : IConfigurationPanelPresenter
    {
        private IConfigurationPanelView _view;
        private readonly ConfigurationPanelViewModel _model;
        private readonly OpenHelpCommand _openHelpCommand;
        private readonly OpenLogDirectoryCommand _openLogDirectoryCommand;
        private readonly SaveConfigurationCommand _saveConfigurationCommand;

        public ConfigurationPanelPresenter(
            ConfigurationPanelViewModel model,
            OpenHelpCommand openHelpCommand,
            OpenLogDirectoryCommand openLogDirectoryCommand,
            SaveConfigurationCommand saveConfigurationCommand
        )
        {
            _model = model;
            _openHelpCommand = openHelpCommand;
            _openLogDirectoryCommand = openLogDirectoryCommand;
            _saveConfigurationCommand = saveConfigurationCommand;
        }

        public void Load()
        {
            _model.PropertyChanged += OnPropertyChanged;
            CheckIfAttached();
            _view.UpdateLocalIpAddresses(_model.LocalIpAddresses);
            _view.UpdateListeningPort(_model.ListeningPort);
            _view.UpdateStatus(new SocketStatus(_model.ServiceStatus));
            _view.UpdateFirewallStatus(_model.FirewallUpdateEnabled);
            _view.UpdateLoggingStatus(_model.DebugEnabled);
            _view.UpdateFilteringData(_model.FilteringData);
            _view.UpdateFilterSelection(_model.FilteringSelection);
        }

        private void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.WriteLine(sender.ToString() + " _ " + e.ToString());
        }

        public void Attach(IConfigurationPanelView view)
        {
            _view = view;
        }

        public void OpenHelp()
        {
            _openHelpCommand.Execute(null);
        }

        public void SaveSettings()
        {
            _saveConfigurationCommand.Execute(null);
        }

        public void OpenLogDirectory()
        {
            _openLogDirectoryCommand.Execute(null);
        }

        public void LoggingStatusChanged(bool @checked)
        {
            _model.DebugEnabled = @checked;
        }

        public void UpdateFirewallSettingsChanged(bool @checked)
        {
            _model.FirewallUpdateEnabled = @checked;
        }

        public void UpdateListeningPort(uint listeningPort)
        {
            _model.ListeningPort = listeningPort;
        }

        private void CheckIfAttached()
        {
            if (_view == null)
            {
                throw new ViewNotAttachedException();
            }
        }

        private class ViewNotAttachedException : Exception
        {
            public ViewNotAttachedException() : base("View was not attached")
            {
            }
        }
    }

    public interface IConfigurationPanelView
    {
        void UpdateLocalIpAddresses(List<string> localIpAddresses);
        void UpdateListeningPort(uint modelListeningPort);
        void UpdateStatus(SocketStatus socketStatus);
        void UpdateLoggingStatus(bool enabled);
        void UpdateFirewallStatus(bool enabled);
        void UpdateFilteringData(IEnumerable<FilteringSelection> modelFilterSelection);
        void UpdateFilterSelection(FilteringSelection filteringSelection);
    }
}