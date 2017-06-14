using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    public class ConfigurationPanelViewModel : ViewModelBase
    {
        private readonly UserSettingsModel _userSettings;
        private readonly IVersionProvider _versionProvider;
        private readonly SocketTester _socketTester;

        public ConfigurationPanelViewModel(PersistanceManager persistanceManager,
            IVersionProvider versionProvider)
        {
            _versionProvider = versionProvider;
            _userSettings = persistanceManager.UserSettingsModel;

            _socketTester = new SocketTester(persistanceManager);
            _socketTester.ConnectionChangeListener += status =>
            {
                ServiceStatus = status;
                OnPropertyChanged(nameof(ServiceStatus));
            };
            
            _socketTester.VerifyConnection();
        }

        public void VerifyConnection()
        {
            _socketTester.VerifyConnection();
        }

        public IEnumerable<FilteringSelection> FilteringData => Enum.GetValues(typeof(FilteringSelection))
            .Cast<FilteringSelection>();

        public FilteringSelection FilteringSelection
        {
            get => _userSettings.FilterSelection;
            set
            {
                _userSettings.FilterSelection = value;
                switch (value)
                {
                    case FilteringSelection.All:
                        _userSettings.IpAddressList = new List<string>();
                        _userSettings.BaseIp = string.Empty;
                        _userSettings.LastOctetMax = 0;
                        break;
                    case FilteringSelection.Range:
                        _userSettings.IpAddressList = new List<string>();
                        break;
                    case FilteringSelection.Specific:
                        _userSettings.BaseIp = string.Empty;
                        _userSettings.LastOctetMax = 0;
                        break;
                }


                OnPropertyChanged(nameof(FilteringSelection));
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
    }
}