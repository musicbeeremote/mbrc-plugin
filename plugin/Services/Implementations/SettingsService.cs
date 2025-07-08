using System;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.Services.Implementations
{
    /// <summary>
    /// Settings service implementation that delegates to the Plugin instance
    /// </summary>
    public class SettingsService : ISettingsService
    {
        public void SaveSettings()
        {
            Plugin.Instance.SaveSettings();
        }

        public void InvalidateCache()
        {
            Plugin.Instance.InvalidateCache();
        }

        public bool Configure(IntPtr panelHandle)
        {
            return Plugin.Instance.Configure(panelHandle);
        }

        public void OpenInfoWindow()
        {
            Plugin.Instance.OpenInfoWindow();
        }

        public void UpdateWindowStatus(bool status)
        {
            Plugin.Instance.UpdateWindowStatus(status);
        }

        public void BroadcastCoverCacheBuildStatus(string clientId = null)
        {
            Plugin.Instance.BroadcastCoverCacheBuildStatus(clientId);
        }
    }
}