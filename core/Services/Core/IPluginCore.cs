using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Media;

namespace MusicBeePlugin.Services.Core
{
    /// <summary>
    ///     Defines the contract for the plugin core initialization.
    /// </summary>
    public interface IPluginCore
    {
        /// <summary>
        ///     Read-only access to user-configured settings. Exposed so that
        ///     the Rust-FFI bridge can query the configured search source
        ///     without taking a second dependency on the DI container.
        /// </summary>
        IUserSettings UserSettings { get; }

        /// <summary>
        ///     Album-cover service used by the Rust FFI bridge to answer
        ///     AlbumCover and CoverCacheBuildStatus queries without leaking
        ///     the DI container out of the plugin assembly.
        /// </summary>
        ICoverService CoverService { get; }

        /// <summary>
        ///     Cross-component event bus. Exposed so the Plugin host can
        ///     subscribe to core-level events (e.g. CoreRestartRequested)
        ///     without holding a second reference to the DI container.
        /// </summary>
        IEventAggregator EventAggregator { get; }

        /// <summary>
        ///     Register the FFI bridge with the container. Called by the
        ///     Plugin host once <see cref="INativeBridge"/> has been
        ///     constructed — the container is built before the bridge
        ///     exists, so DI can't auto-resolve it.
        /// </summary>
        void RegisterNativeBridge(INativeBridge bridge);

        void Initialize();

        /// <summary>
        ///     Enables or disables logging.
        /// </summary>
        /// <param name="enabled">True to enable logging, false to disable.</param>
        void SetLogging(bool enabled);

        /// <summary>
        ///     Uninstalls the plugin by cleaning up resources and deleting related data folders.
        /// </summary>
        void Uninstall();

        void OpenSettingsWindow();
    }
}
