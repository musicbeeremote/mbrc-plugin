using System;
using MusicBeePlugin.Models.Configuration;

namespace MusicBeePlugin.Services.Configuration
{
    /// <summary>
    ///     Interface for user settings management service.
    ///     Handles loading, saving, and modifying user settings.
    /// </summary>
    public interface IUserSettingsService : IUserSettings, IDisposable
    {
        /// <summary>
        ///     Sets the storage path for settings files.
        /// </summary>
        /// <param name="path">The base storage path</param>
        void SetStoragePath(string path);

        /// <summary>
        ///     Loads settings from storage.
        /// </summary>
        void LoadSettings();

        /// <summary>
        ///     Saves current settings to storage and triggers necessary events.
        /// </summary>
        void SaveSettings();

        /// <summary>
        ///     Determines if this is the first run of the plugin.
        /// </summary>
        /// <returns>True if this is the first run</returns>
        bool IsFirstRun();

        /// <summary>
        ///     Updates the settings with a new model.
        ///     This does not automatically save - call SaveSettings() to persist.
        /// </summary>
        /// <param name="settings">The new settings model</param>
        void UpdateSettings(UserSettingsModel settings);

        /// <summary>
        ///     Gets a copy of the current settings model.
        /// </summary>
        /// <returns>A copy of the current settings</returns>
        UserSettingsModel GetSettingsModel();
    }
}
