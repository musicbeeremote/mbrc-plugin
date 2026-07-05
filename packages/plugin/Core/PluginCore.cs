using System;
using System.IO;
using Autofac;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Events.Infrastructure;
using MusicBeePlugin.Infrastructure.DependencyInjection;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.Services.UI;
using MusicBeePlugin.Utilities.Network;
using NLog;

namespace MusicBeePlugin.Core
{
    /// <summary>
    ///     Core plugin implementation that handles dependency injection container setup.
    /// </summary>
    public class PluginCore : IPluginCore, IDisposable
    {
        private readonly IContainer _container;
        private readonly INetworkingManager _networkingManager;
        private readonly INotificationHandler _notificationHandler;
        private readonly IUserSettingsService _userSettingsService;
        private readonly IWindowManager _windowManager;
        private readonly IStateMonitor _stateMonitor;

        public PluginCore(IMusicBeeApiAdapter adapters, IDataProviders dataProviders, string storagePath, Version version)
        {
            var builder = new ContainerBuilder();

            // Register MusicBee API system operations
            builder.RegisterInstance(adapters).As<IMusicBeeApiAdapter>().SingleInstance();
            builder.RegisterInstance(adapters.System).As<ISystemOperations>().SingleInstance();

            // Register data providers from the passed composite
            builder.RegisterInstance(dataProviders).As<IDataProviders>().SingleInstance();
            builder.RegisterInstance(dataProviders.Player).As<IPlayerDataProvider>().SingleInstance();
            builder.RegisterInstance(dataProviders.Track).As<ITrackDataProvider>().SingleInstance();
            builder.RegisterInstance(dataProviders.Playlist).As<IPlaylistDataProvider>().SingleInstance();
            builder.RegisterInstance(dataProviders.Library).As<ILibraryDataProvider>().SingleInstance();

            // Register EventAggregator as singleton
            builder.RegisterType<EventAggregator>().As<IEventAggregator>().SingleInstance();

            // Register this PluginCore instance for modules that need it
            builder.RegisterInstance(this).As<IPluginCore>().SingleInstance();

            // Register modules in order of dependencies
            builder.RegisterModule<LoggingModule>();
            builder.RegisterModule(new ServicesModule(storagePath, version));
            builder.RegisterModule<CommandModule>();
            builder.RegisterModule<NetworkingModule>();
            builder.RegisterModule(new UIModule(this));

            // Build the container
            _container = builder.Build();

            // Get required services (these are now initialized by modules)
            _userSettingsService = _container.Resolve<IUserSettingsService>();
            _networkingManager = _container.Resolve<INetworkingManager>();
            _notificationHandler = _container.Resolve<INotificationHandler>();
            _windowManager = _container.Resolve<IWindowManager>();
            _stateMonitor = _container.Resolve<IStateMonitor>();
        }

        /// <summary>
        ///     Disposes the PluginCore and cleans up resources.
        /// </summary>
        public void Dispose()
        {
            // Stop monitoring first before disposing container
            _stateMonitor?.StopMonitoring();

            // Dispose container which will dispose all registered IDisposable services
            _container?.Dispose();

            GC.SuppressFinalize(this);
        }

        public void Initialize()
        {

            _stateMonitor.StartMonitoring();

            // Check if this is the first run and show settings window if needed
            if (_userSettingsService.IsFirstRun())
                _windowManager.OpenInfoWindow();

            // Initialize cover cache in background
            // The InitializeCacheAsync method will handle checking if the cache is empty
            var coverService = _container.Resolve<ICoverService>();
            var systemOperations = _container.Resolve<ISystemOperations>();
            systemOperations.CreateBackgroundTask(() => coverService.InitializeCacheAsync());

            // Broadcast initial now playing state (cover and lyrics)
            // This replaces the old InitializeModelState functionality
            _notificationHandler.BroadcastInitialNowPlayingState();
        }

        /// <summary>
        ///     Enables or disables logging.
        /// </summary>
        /// <param name="enabled">True to enable logging, false to disable.</param>
        public void SetLogging(bool enabled)
        {
            LoggingService.InitializeLoggingConfiguration(
                _userSettingsService.FullLogPath,
                enabled ? LogLevel.Debug : LogLevel.Error);
        }

        /// <summary>
        ///     Starts the networking services.
        /// </summary>
        public void StartNetworking()
        {
            _networkingManager.StartListening();
        }

        /// <summary>
        ///     Stops the networking services.
        /// </summary>
        public void StopNetworking()
        {
            _networkingManager.StopListening();
        }

        /// <summary>
        ///     Gets the notification handler for processing MusicBee notifications.
        /// </summary>
        /// <returns>The notification handler instance</returns>
        public INotificationHandler GetNotificationHandler()
        {
            return _notificationHandler;
        }

        public void Uninstall()
        {
            var storagePath = _userSettingsService.StoragePath;
            var settingsFolder = Path.Combine(storagePath, "mb_remote");
            if (Directory.Exists(settingsFolder))
                Directory.Delete(settingsFolder);
        }

        public void OpenSettingsWindow()
        {
            _windowManager.OpenInfoWindow();
        }
    }
}
