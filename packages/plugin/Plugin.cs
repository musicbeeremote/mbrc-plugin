using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using MusicBeePlugin.Host;
using MusicBeePlugin.Settings;
using FfiGen = MusicBeePlugin.Ffi.Generated;

namespace MusicBeePlugin
{
    /// <summary>
    ///     The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    public partial class Plugin
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private readonly PluginInfo _about = new PluginInfo();

        /// <summary>
        ///     The mb api interface.
        /// </summary>
        private MusicBeeApiInterface _api;

        /// <summary>
        ///     The hand-wired composition root (providers + services + FFI bridge).
        /// </summary>
        private PluginHost _host;

        /// <summary>The preferences panel, built lazily in <see cref="Configure" />.</summary>
        private ConfigurationPanel _configPanel;

        /// <summary>Plugin version string, shown in the preferences panel.</summary>
        private string _version;

        /// <summary>
        ///     The version this build actually is, prerelease suffix included.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Not <c>AssemblyVersion</c>: MSBuild strips the suffix from it, so a
        ///         <c>1.5.0-beta.1</c> build reports <c>1.5.0.0</c> - identical to the
        ///         final release as far as the updater's comparison is concerned. A
        ///         beta would then never be offered anything, including the release it
        ///         is a beta of, which defeats the point of shipping one.
        ///         <c>AssemblyInformationalVersion</c> keeps the whole string.
        ///     </para>
        ///     <para>
        ///         Build metadata (<c>+&lt;sha&gt;</c>, which the SDK appends when
        ///         source revision is included) is cut: semver ignores it in
        ///         comparisons, and it is noise in the panel's footer.
        ///     </para>
        ///     <para>
        ///         This does not reach the V4 wire. <c>pluginversion</c> there is
        ///         pinned to 1.4.1.0 by the protocol layer, because the iOS client
        ///         changes its behaviour based on it.
        ///     </para>
        /// </remarks>
        private static string ProductVersion(Version assemblyVersion)
        {
            try
            {
                var informational = Assembly
                    .GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(informational))
                {
                    var plus = informational.IndexOf('+');
                    return plus >= 0 ? informational.Substring(0, plus) : informational;
                }
            }
            catch (Exception)
            {
                // Fall through to the assembly version below; a plugin that cannot
                // read its own metadata should still load.
            }

            return assemblyVersion.ToString();
        }

        /// <summary>
        ///     This function initialized the Plugin.
        /// </summary>
        /// <param name="apiInterfacePtr"></param>
        /// <returns></returns>
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            // MusicBee calls this directly; an exception escaping here would take
            // the host down. The whole body is guarded so a failure leaves the
            // plugin degraded (remote off) but MusicBee running, returning the
            // (field-initialized) _about either way.
            try
            {
                _api = new MusicBeeApiInterface();
                _api.Initialise(apiInterfacePtr);

                var version = Assembly.GetExecutingAssembly().GetName().Version;
                _version = ProductVersion(version);

                _about.PluginInfoVersion = PluginInfoVersion;
                _about.Name = "MusicBee Remote: Plugin";
                _about.Description = "Remote Control for server to be used with android application.";
                _about.Author = "Konstantinos Paparas (aka Kelsos)";
                _about.TargetApplication = "MusicBee Remote";
                _about.Type = PluginType.General;
                _about.VersionMajor = Convert.ToInt16(version.Major);
                _about.VersionMinor = Convert.ToInt16(version.Minor);
                _about.Revision = Convert.ToInt16(version.Build);
                _about.MinInterfaceVersion = MinInterfaceVersion;
                _about.MinApiRevision = MinApiRevision;
                // PlayerEvents drives the now-playing/transport broadcasts; TagEvents
                // delivers the library-change notifications the Scanner reacts to
                // (FileAddedToLibrary, TagsChanged, FileDeleted, LibrarySwitched) so a
                // tag/artwork edit refreshes the metadata + cover caches without a
                // restart. Unhandled types are ignored in ReceiveNotification.
                _about.ReceiveNotifications =
                    ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents;
                // Non-zero height tells MusicBee this plugin has a preferences panel;
                // MusicBee then calls Configure(panelHandle) to populate it. The panel
                // now holds only a Configure button, so it needs little room.
                _about.ConfigurationPanelHeight = 120;

                if (_api.ApiRevision < MinApiRevision)
                {
                    ReportUnsupportedHost(_api.ApiRevision);
                    return _about;
                }

                InitializeHost();

                // A Tools menu entry opens the same settings dialog as the Configure
                // button, matching the classic plugin's layout.
                _api.MB_AddMenuItem(
                    "mnuTools/MusicBee Remote",
                    "MusicBee Remote: open settings",
                    (sender, args) => OpenSettingsDialog());
            }
            catch (Exception ex)
            {
                LogToFallback("FATAL: Plugin Initialise failed", ex);
            }

            return _about;
        }

        /// <summary>
        ///     Open the settings dialog (shared by the Configure button and the
        ///     Tools menu entry). No-op if the host failed to start.
        /// </summary>
        private void OpenSettingsDialog()
        {
            if (_host == null)
                return;

            // Invoked by MusicBee (Tools-menu callback) and by the Configure
            // button; guard so a dialog-construction failure never escapes to the
            // host.
            try
            {
                using (var dialog = new SettingsDialog(_host, _version))
                    dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                LogToFallback("Settings dialog failed", ex);
            }
        }

        /// <summary>
        ///     Builds the composition root and boots the Rust core. Wrapped in a
        ///     catch-all so a startup failure leaves the plugin degraded (remote
        ///     off) rather than crashing MusicBee.
        /// </summary>
        private void InitializeHost()
        {
            try
            {
                _host = new PluginHost(_api, _api.Setting_GetPersistentStoragePath(), _version);
                _host.Start();
            }
            catch (Exception ex)
            {
                // Log the error to a fallback location and ensure the plugin
                // never crashes MusicBee.
                LogToFallback("FATAL: Plugin initialization failed", ex);

                // Ensure _host is null so other methods handle it gracefully.
                _host = null;
            }
        }

        /// <summary>
        ///     Best-effort error log to a fallback file, used by the MusicBee-facing
        ///     entry points so a failure is recorded but never propagates into the
        ///     host. Swallows its own errors (including a missing/failed API).
        /// </summary>
        private void LogToFallback(string context, Exception ex)
        {
            LogToFallback(context, ex.ToString());
        }

        /// <summary>
        ///     As <see cref="LogToFallback(string, Exception)" />, for a condition that
        ///     is not an exception. The core's own log does not exist yet at this
        ///     point - it is created by the host - so this file is the only place an
        ///     early refusal can be recorded.
        /// </summary>
        private void LogToFallback(string context, string detail)
        {
            try
            {
                var fallbackPath = Path.Combine(
                    _api.Setting_GetPersistentStoragePath(),
                    "mb_remote",
                    "initialization_error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath));
                File.AppendAllText(fallbackPath,
                    $"[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.ffffffZ}] {context}: {detail}\n");
            }
            catch
            {
                // If logging fails, continue silently to prevent MusicBee crash.
            }
        }

        /// <summary>
        ///     Explains a MusicBee too old to run this plugin, in the two places
        ///     someone will actually look.
        /// </summary>
        /// <remarks>
        ///     Without this the refusal is completely silent: the plugin still appears
        ///     in MusicBee's plugin list because <c>_about</c> is populated, but no
        ///     server starts, Configure does nothing (there is no host to configure),
        ///     and no log is written anywhere - the core's log does not exist because
        ///     the host was never created. That is the hardest possible support case,
        ///     so the description carries the reason to where the user is already
        ///     looking, and the fallback file carries it to where a bug report can
        ///     find it.
        /// </remarks>
        private void ReportUnsupportedHost(short apiRevision)
        {
            var reason =
                $"Disabled: needs MusicBee API revision {MinApiRevision} or newer, but this MusicBee reports {apiRevision}. Update MusicBee from https://getmusicbee.com";

            _about.Description = reason;
            LogToFallback("Unsupported MusicBee version", reason);
        }

        /// <summary>
        ///     Creates the MusicBee plugin Configuration panel.
        /// </summary>
        /// <param name="panelHandle">
        ///     The panel handle.
        /// </param>
        /// <returns>
        ///     The <see cref="bool" />.
        /// </returns>
        public bool Configure(IntPtr panelHandle)
        {
            // The core owns settings; the panel reads/writes them over the FFI.
            if (_host == null || panelHandle == IntPtr.Zero)
                return false;

            // MusicBee calls this directly; guard so a panel-construction failure
            // returns false instead of propagating into the host.
            try
            {
                var panel = Control.FromHandle(panelHandle);
                if (panel == null)
                    return false;

                _configPanel = new ConfigurationPanel(_host, _version);
                _configPanel.AttachTo(panel);
                return true;
            }
            catch (Exception ex)
            {
                LogToFallback("Configure panel failed", ex);
                return false;
            }
        }

        /// <summary>
        ///     Called when MusicBee closes or the plugin is disabled.
        /// </summary>
        /// <param name="reason">The reason for closing.</param>
        public void Close(PluginCloseReason reason)
        {
            // MusicBee calls this on its way out; an exception escaping here is a
            // crash dialog on exit, after the user has already asked to leave.
            try
            {
                _host?.Dispose();
            }
            catch (Exception ex)
            {
                LogToFallback("Plugin Close failed", ex);
            }
            finally
            {
                _host = null;
            }
        }

        /// <summary>
        ///     Cleans up any persisted files during the plugin uninstall.
        /// </summary>
        public void Uninstall()
        {
            try
            {
                var settingsFolder = Path.Combine(
                    _api.Setting_GetPersistentStoragePath(),
                    "mb_remote");
                if (Directory.Exists(settingsFolder))
                    Directory.Delete(settingsFolder, true);
            }
            catch
            {
                // Best-effort cleanup; never throw out of Uninstall.
            }
        }

        /// <summary>
        ///     Called by MusicBee when the user clicks Save/Apply in the Preferences
        ///     screen. The embedded panel holds only a Configure button now; settings
        ///     are edited and persisted in the settings dialog, so there is nothing to
        ///     apply here.
        /// </summary>
        public void SaveSettings()
        {
        }

        /// <summary>
        ///     Receives event Notifications from MusicBee. It is only required if the about.ReceiveNotificationFlags =
        ///     PlayerEvents.
        /// </summary>
        /// <param name="sourceFileUrl"></param>
        /// <param name="type"></param>
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (_host == null)
                return;

            // Map MusicBee's notification to the core's NotificationType (the
            // generated FFI enum). Only the events the core re-queries are
            // forwarded; anything else is ignored.
            FfiGen.NotificationType coreType;
            switch (type)
            {
                case NotificationType.TrackChanged:
                    coreType = FfiGen.NotificationType.TrackChanged;
                    break;
                case NotificationType.PlayStateChanged:
                    coreType = FfiGen.NotificationType.PlayStateChanged;
                    break;
                case NotificationType.VolumeLevelChanged:
                    coreType = FfiGen.NotificationType.VolumeLevelChanged;
                    break;
                case NotificationType.VolumeMuteChanged:
                    coreType = FfiGen.NotificationType.VolumeMuteChanged;
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    coreType = FfiGen.NotificationType.NowPlayingLyricsReady;
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    coreType = FfiGen.NotificationType.NowPlayingArtworkReady;
                    break;
                case NotificationType.PlayingTracksChanged:
                    coreType = FfiGen.NotificationType.NowPlayingListChanged;
                    break;
                case NotificationType.FileAddedToLibrary:
                // Tag edits (artwork included) and deletions change the library
                // exactly like an add from the core's view: nudge the Scanner to
                // run a metadata + cover delta. Mapped to the same core
                // notification so no extra FFI variant is needed.
                case NotificationType.TagsChanged:
                case NotificationType.FileDeleted:
                    coreType = FfiGen.NotificationType.FileAddedToLibrary;
                    break;
                case NotificationType.LibrarySwitched:
                    coreType = FfiGen.NotificationType.LibrarySwitched;
                    break;
                default:
                    return;
            }

            // Guarded like every other MusicBee-facing entry point, and this is the
            // busiest of them: it fires for every player event and every tag or
            // library change. The FFI call itself is guarded inside the bridge, so
            // this is the belt to that braces - a notification must never be able
            // to throw back into MusicBee's event dispatch.
            try
            {
                _host.HandleNotification((int)coreType);
            }
            catch (Exception ex)
            {
                LogToFallback("ReceiveNotification failed", ex);
            }
        }
    }
}
