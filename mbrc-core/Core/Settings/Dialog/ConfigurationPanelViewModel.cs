using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Settings.Dialog.Commands;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    public class ConfigurationPanelViewModel : ViewModelBase
    {
        private readonly PersistanceManager _persistanceManager;
        private readonly IVersionProvider _versionProvider;

        private readonly UserSettingsModel _userSettings;
        
        public IEnumerable<FilteringSelection> FilterSelection => Enum.GetValues(typeof(FilteringSelection))
            .Cast<FilteringSelection>();

        public string IpAddress { get; set; }

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

        public string PluginVersion => _versionProvider.GetPluginVersion();

        public bool ServiceStatus { get; set; }


        public ConfigurationPanelViewModel(PersistanceManager persistanceManager,
            SaveConfigurationCommand saveConfigurationCommand,
            OpenHelpCommand openHelpCommand,
            OpenLogDirectoryCommand openLogDirectoryCommand,
            IVersionProvider versionProvider)
        {            
            _persistanceManager = persistanceManager;
            _versionProvider = versionProvider;
            _userSettings = persistanceManager.UserSettingsModel;

            var socketTest = new SocketTester(persistanceManager);
            socketTest.ConnectionChangeListener += status =>
            {
                ServiceStatus = status;
                OnPropertyChanged(nameof(ServiceStatus));
            };
            socketTest.VerifyConnection();
        }
    }
}