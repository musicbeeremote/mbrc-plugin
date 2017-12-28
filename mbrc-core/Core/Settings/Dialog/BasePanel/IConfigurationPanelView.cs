using System.Collections.Generic;
using MusicBeeRemote.Core.Settings.Dialog.Converters;

namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    public interface IConfigurationPanelView
    {
        void UpdateLocalIpAddresses(List<string> localIpAddresses);
        void UpdateListeningPort(uint modelListeningPort);
        void UpdateStatus(SocketStatus socketStatus);
        void UpdateLoggingStatus(bool enabled);
        void UpdateFirewallStatus(bool enabled);        
        void UpdatePluginVersion(string pluginVersion);
        void UpdateFilteringData(IEnumerable<FilteringSelection> filteringData, FilteringSelection filteringSelection);
    }
}