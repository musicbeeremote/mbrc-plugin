using System;

namespace MusicBeePlugin.Services.Interfaces
{
    /// <summary>
    /// Service interface for plugin settings and configuration
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Saves current settings to storage
        /// </summary>
        void SaveSettings();
        
        /// <summary>
        /// Invalidates cached data
        /// </summary>
        void InvalidateCache();
        
        /// <summary>
        /// Opens the plugin configuration window
        /// </summary>
        bool Configure(IntPtr panelHandle);
        
        /// <summary>
        /// Opens the plugin information window
        /// </summary>
        void OpenInfoWindow();
        
        /// <summary>
        /// Updates window status
        /// </summary>
        void UpdateWindowStatus(bool status);
        
        /// <summary>
        /// Broadcasts cover cache build status
        /// </summary>
        void BroadcastCoverCacheBuildStatus(string clientId = null);
    }
}