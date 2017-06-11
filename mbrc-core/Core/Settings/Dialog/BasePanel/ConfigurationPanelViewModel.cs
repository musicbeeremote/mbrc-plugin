using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    public class ConfigurationPanelViewModel : ViewModelBase
    {       
        private readonly IVersionProvider _versionProvider;

        private readonly UserSettingsModel _userSettings;
        
        public IEnumerable<FilteringSelection> FilteringData => Enum.GetValues(typeof(FilteringSelection))
            .Cast<FilteringSelection>();       

        public FilteringSelection FilteringSelection
        {
            get => _userSettings.FilterSelection;
            set
            {
                _userSettings.FilterSelection = value;
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
        
        public List<string> Whitelist { get; } = new List<string>();

        public void AddAddress(string address)
        {
            Whitelist.Add(address);
            OnPropertyChanged(nameof(Whitelist));
        }

        public void RemoveAddress(string address)
        {
            Whitelist.Remove(address);
            OnPropertyChanged(nameof(Whitelist));
        }


        public ConfigurationPanelViewModel(PersistanceManager persistanceManager,                                
            IVersionProvider versionProvider)
        {                       
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