using System;
using System.IO;
using Autofac;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Events.Infrastructure;
using MusicBeePlugin.Infrastructure.DependencyInjection;
using MusicBeePlugin.Infrastructure.Logging.Implementations;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.Services.UI;

namespace MusicBeePlugin.Core
{
    /// <summary>
    ///     Core plugin implementation that handles dependency injection container setup.
    /// </summary>
    public class PluginCore : IPluginCore, IDisposable
    {
        private readonly IContainer _container;
        private readonly NativeBridgeHolder _nativeBridgeHolder;
        private readonly IUserSettingsService _userSettingsService;
        private readonly IWindowManager _windowManager;

        public IUserSettings UserSettings => _userSettingsService;

        public ICoverService CoverService => _container.Resolve<ICoverService>();

        public IEventAggregator EventAggregator => _container.Resolve<IEventAggregator>();

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

            // Late-binding slot for the FFI bridge. The actual NativeBridge
            // is constructed by the Plugin host after the container is built,
            // because it pulls data providers out of this container.
            var nativeBridgeHolder = new NativeBridgeHolder();
            builder.RegisterInstance(nativeBridgeHolder).As<NativeBridgeHolder>().As<INativeBridge>()
                .SingleInstance();

            // Register modules in order of dependencies. The legacy
            // CommandModule/NetworkingModule have been removed: Rust owns
            // the socket server, command dispatch and protocol handling.
            builder.RegisterModule<LoggingModule>();
            builder.RegisterModule(new ServicesModule(storagePath, version));
            builder.RegisterModule(new UIModule(this));

            // Build the container
            _container = builder.Build();

            // Get required services (these are now initialized by modules)
            _userSettingsService = _container.Resolve<IUserSettingsService>();
            _windowManager = _container.Resolve<IWindowManager>();
            _nativeBridgeHolder = nativeBridgeHolder;
        }

        public void RegisterNativeBridge(INativeBridge bridge) => _nativeBridgeHolder.Set(bridge);

        /// <summary>
        ///     Disposes the PluginCore and cleans up resources.
        /// </summary>
        public void Dispose()
        {
            // Dispose container which will dispose all registered IDisposable services
            _container?.Dispose();

            GC.SuppressFinalize(this);
        }

        public void Initialize()
        {
            // Check if this is the first run and show settings window if needed
            if (_userSettingsService.IsFirstRun())
                _windowManager.OpenInfoWindow();

            // Initialize cover cache in background
            // The InitializeCacheAsync method will handle checking if the cache is empty
            var coverService = _container.Resolve<ICoverService>();
            var systemOperations = _container.Resolve<ISystemOperations>();
            systemOperations.CreateBackgroundTask(() => coverService.InitializeCacheAsync());
        }

        /// <summary>
        ///     Toggle debug-level logging at runtime by reconfiguring
        ///     the Rust <c>EnvFilter</c> through the FFI. Mirrors the
        ///     legacy NLog-toggle behaviour from the settings UI:
        ///     <c>true</c> → <c>"debug"</c>, <c>false</c> → <c>"info"</c>.
        ///     Silent if the core isn't initialised yet — the requested
        ///     level will be re-applied on the next call.
        /// </summary>
        public void SetLogging(bool enabled)
        {
            RustLogBridge.TrySetLevel(enabled ? "debug" : "info");
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
