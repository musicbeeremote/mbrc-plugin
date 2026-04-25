using System;
using Autofac;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Media;
using NLog;

namespace MusicBeePlugin.Infrastructure.DependencyInjection
{
    /// <summary>
    ///     Autofac module for registering business logic services.
    ///     Contains services for cover management, settings, notifications, and state monitoring.
    /// </summary>
    public class ServicesModule : Module
    {
        private readonly string _storagePath;
        private readonly Version _version;

        public ServicesModule(string storagePath, Version version)
        {
            _storagePath = storagePath;
            _version = version;
        }

        protected override void Load(ContainerBuilder builder)
        {
            // Register Cover Cache service
            builder.Register(c => new CoverCache(
                    c.Resolve<IPluginLogger>(),
                    c.Resolve<IUserSettingsService>().StoragePath))
                .As<ICoverCache>()
                .SingleInstance();

            // Register Cover Service
            builder.Register(c => new CoverService(
                    c.Resolve<ICoverCache>(),
                    c.Resolve<ILibraryDataProvider>(),
                    c.Resolve<ITrackDataProvider>(),
                    c.Resolve<ISystemOperations>(),
                    c.Resolve<IPluginLogger>(),
                    c.Resolve<IUserSettingsService>().StoragePath))
                .As<ICoverService>()
                .SingleInstance();

            // Register User Settings Service (replaces UserSettings singleton)
            builder.RegisterType<UserSettingsService>()
                .As<IUserSettingsService>()
                .As<IUserSettings>()
                .SingleInstance()
                .OnActivated(e =>
                {
                    // Initialize UserSettings Service
                    var userSettingsService = e.Instance;
                    userSettingsService.SetStoragePath(_storagePath);
                    userSettingsService.LoadSettings();

                    // Update current version
                    var currentSettings = userSettingsService.GetSettingsModel();
                    currentSettings.CurrentVersion = _version.ToString();
                    userSettingsService.UpdateSettings(currentSettings);

                    // Note: StoragePath is now injected directly into services that need it

                    // Initialize logging configuration
#if DEBUG
                    InitializeLogging(userSettingsService, true);
#else
                    InitializeLogging(userSettingsService, userSettingsService.DebugLogEnabled);
#endif
                });

        }

        /// <summary>
        ///     Enables or disables logging.
        /// </summary>
        /// <param name="userSettingsService">The user settings service</param>
        /// <param name="enabled">True to enable logging, false to disable.</param>
        private static void InitializeLogging(UserSettingsService userSettingsService, bool enabled)
        {
            LoggingService.InitializeLoggingConfiguration(
                userSettingsService.FullLogPath,
                enabled ? LogLevel.Debug : LogLevel.Error);
        }
    }
}
