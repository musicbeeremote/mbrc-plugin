using System;
using System.Diagnostics;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Settings
{
    /// <summary>
    /// Represents the settings along with all the settings related functionality
    /// </summary>
    public class PersistanceManager
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IJsonSettingsFileManager _jsonSettingsFileManager;

        private readonly string _firewallUtility =
            $"{AppDomain.CurrentDomain.BaseDirectory}\\Plugins\\firewall-utility.exe";

        public PersistanceManager(ITinyMessengerHub hub,
            ILegacySettingsMigration legacySettingsMigration,
            IJsonSettingsFileManager jsonSettingsFileManager)
        {
            _hub = hub;
            _jsonSettingsFileManager = jsonSettingsFileManager;
            UserSettingsModel = _jsonSettingsFileManager.Load();
            legacySettingsMigration.MigrateLegacySettings(UserSettingsModel);
        }

        public void SaveSettings()
        {
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
            return false;
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