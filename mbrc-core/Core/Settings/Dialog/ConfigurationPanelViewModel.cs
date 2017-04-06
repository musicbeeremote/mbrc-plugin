using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Settings.Dialog.Commands;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    public class ConfigurationPanelViewModel : ViewModelBase
    {
        private readonly PersistanceManager _persistanceManager;

        private readonly UserSettingsModel _userSettings;

        public ICommand SaveConfigurationCommand { get; }

        public ICommand OpenHelpCommand { get; }

        public ICommand OpenLogDirectoryCommand { get; }

        public IEnumerable<FilteringSelection> FilterSelection => Enum.GetValues(typeof(FilteringSelection))
            .Cast<FilteringSelection>();

        public FilteringSelection UserFilteringSelection
        {
            get => _userSettings.FilterSelection;
            set
            {
                _userSettings.FilterSelection = value;
                OnPropertyChanged(nameof(UserFilteringSelection));
            }
        }

        public bool DebugEnabled
        {
            get => _userSettings.DebugLogEnabled;
            set
            {
                _userSettings.DebugLogEnabled = value;
                OnPropertyChanged(nameof(DebugEnabled));
            }
        }

        public List<string> LocalIpAddresses => Tools.GetPrivateAddressList();

        public bool FirewallUpdateEnabled
        {
            get => _userSettings.UpdateFirewall;
            set
            {
                _userSettings.UpdateFirewall = value;
                OnPropertyChanged(nameof(FirewallUpdateEnabled));
            }
        }

        public uint ListeningPort
        {
            get => _userSettings.ListeningPort;
            set
            {
                _userSettings.ListeningPort = value;
                OnPropertyChanged(nameof(ListeningPort));
            }
        }


        public ConfigurationPanelViewModel(PersistanceManager persistanceManager,
            SaveConfigurationCommand saveConfigurationCommand,
            OpenHelpCommand openHelpCommand,
            OpenLogDirectoryCommand openLogDirectoryCommand
        )
        {
            SaveConfigurationCommand = saveConfigurationCommand;
            OpenHelpCommand = openHelpCommand;
            OpenLogDirectoryCommand = openLogDirectoryCommand;
            _persistanceManager = persistanceManager;
            _userSettings = persistanceManager.UserSettingsModel;
        }
    }
}