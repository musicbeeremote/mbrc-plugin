using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.Caching;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    public class ConfigurationPanelViewModel : ViewModelBase
    {
        private readonly UserSettingsModel _userSettings;
        private readonly IVersionProvider _versionProvider;
        private readonly SocketTester _socketTester;
        private readonly ITrackRepository _trackRepository;

        public ConfigurationPanelViewModel(
            PersistenceManager persistenceManager,
            IVersionProvider versionProvider,
            ITrackRepository trackRepository)
        {
            _versionProvider = versionProvider;
            _trackRepository = trackRepository;
            _userSettings = persistenceManager?.UserSettingsModel ??
                            throw new ArgumentNullException(nameof(persistenceManager));

            _socketTester = new SocketTester(persistenceManager);
            _socketTester.ConnectionChangeListener += status =>
            {
                ServiceStatus = status;
                OnPropertyChanged(nameof(ServiceStatus));
            };

            _socketTester.VerifyConnection();
        }

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

        public int CachedTracks => _trackRepository.Count();

        public bool ServiceStatus { get; set; }

        public static List<string> GetLocalIpAddresses() => Tools.GetPrivateAddressList();

        public static IEnumerable<FilteringSelection> GetFilteringData() =>
            Enum.GetValues(typeof(FilteringSelection))
                .Cast<FilteringSelection>();

        public void VerifyConnection()
        {
            _socketTester.VerifyConnection();
        }
    }
}
