using System;
using System.Diagnostics;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Logging;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Settings
{
    /// <summary>
    /// Represents the settings along with all the settings related functionality
    /// </summary>
    public class PersistanceManager
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IJsonSettingsFileManager _jsonSettingsFileManager;
        private readonly IVersionProvider _versionProvider;

        private readonly string _firewallUtility =
            $"{AppDomain.CurrentDomain.BaseDirectory}\\Plugins\\firewall-utility.exe";

        public PersistanceManager(ITinyMessengerHub hub,
            ILegacySettingsMigration legacySettingsMigration,
            IJsonSettingsFileManager jsonSettingsFileManager,
            IPluginLogManager pluginLogManager,
            IVersionProvider versionProvider)
        {
            _hub = hub;
            _jsonSettingsFileManager = jsonSettingsFileManager;
            _versionProvider = versionProvider;
            UserSettingsModel = _jsonSettingsFileManager.Load();
            legacySettingsMigration.MigrateLegacySettings(UserSettingsModel);
            
            pluginLogManager.Initialize(UserSettingsModel.DebugLogEnabled ? LogLevel.Debug : LogLevel.Error);
        }

        public void SaveSettings()
        {
            UserSettingsModel.CurrentVersion = _versionProvider.GetPluginVersion();

            _jsonSettingsFileManager.Save(UserSettingsModel);

            if (UserSettingsModel.UpdateFirewall)
            {
                UpdateFirewallRules();
            }
            _hub.Publish(new RestartSocketEvent());
        }


        public UserSettingsModel UserSettingsModel { get; }


        /// <summary>
        /// Determines if it is the first run of the application.
        /// </summary>
        /// <returns></returns>
        public bool IsFirstRun()
        {
            return !UserSettingsModel.CurrentVersion.Equals(_versionProvider.GetPluginVersion());
        }

        /// <summary>
        ///     When called it will execute the firewall-utility passing the port settings
        ///     needed by the plugin.
        /// </summary>
        public void UpdateFirewallRules()
        {
            var port = UserSettingsModel.ListeningPort;
            var startInfo = new ProcessStartInfo(_firewallUtility)
            {
                Verb = "runas",
                Arguments =
                    $"-s {port}"
            };
            Process.Start(startInfo);
        }
    }
}