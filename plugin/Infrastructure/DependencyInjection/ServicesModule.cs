using System;
using Autofac;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Implementations;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Media;

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
        ///     Apply the user's debug-logging preference to the Rust
        ///     <c>EnvFilter</c>. The actual sink (file path, format,
        ///     rotation) is owned by the Rust core; this entry point
        ///     just nudges the level. The <c>userSettingsService</c>
        ///     parameter is retained so the call signature matches the
        ///     legacy NLog wiring callers expect.
        /// </summary>
        private static void InitializeLogging(UserSettingsService userSettingsService, bool enabled)
        {
            _ = userSettingsService;
            RustLogBridge.TrySetLevel(enabled ? "debug" : "info");
        }
    }
}
