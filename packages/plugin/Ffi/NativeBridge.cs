using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Logging;
using MusicBeePlugin.Settings;
using MusicBeePlugin.Ffi.Generated;

namespace MusicBeePlugin.Ffi
{
    /// <summary>
    ///     The FFI mechanics between MusicBee (C#) and the Rust core: native
    ///     library preload, delegate pinning, lifecycle, and the callback
    ///     marshaling. The read/write dispatch lives in <see cref="QueryHandlers"/>
    ///     and <see cref="CommandHandlers"/>; this class owns the boundary.
    ///
    ///     Safety: every callback body is wrapped so a managed exception never
    ///     crosses the native boundary, and all provider access runs under a
    ///     single lock (MusicBee's API is not thread-safe and Rust calls from
    ///     tokio threads). P/Invoke signatures + the MbrcCallbacks layout are
    ///     generated into <see cref="NativeMethods"/> from the Rust source.
    /// </summary>
    public sealed class NativeBridge : IDisposable
    {
        /// <summary>Must equal MBRC_ABI_VERSION in mbrc-core/src/ffi/types.rs.</summary>
        private const int MbrcAbiVersion = 1;

        #region FFI delegate types (match MbrcCallbacks in ffi/types.rs)

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int QueryCallbackDelegate(
            int queryType, IntPtr paramsBuf, uint paramsLen, out IntPtr outResultBuf, out uint outResultLen);

        // One-way: status only, no result buffer.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CommandCallbackDelegate(int commandType, IntPtr paramsBuf, uint paramsLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreeBufferDelegate(IntPtr buf);

        // Core -> host push (one-way). The core calls this from a background
        // thread when host-relevant state changes. See HostEventType.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EventCallbackDelegate(int eventType, IntPtr payloadBuf, uint payloadLen);

        #endregion

        /// <summary>
        ///     Raised (on a background/core thread) when the core pushes an event.
        ///     Subscribers must marshal to their UI thread. The argument is the
        ///     <see cref="HostEventType"/> value.
        /// </summary>
        public event Action<HostEventType> CoreEvent;

        #region Native library preload

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        /// <summary>The native core, as the loader knows it.</summary>
        private const string NativeLibraryFileName = "mbrc_core.dll";

        /// <summary>
        ///     Enough to unwind any plausible reference count, and a bound rather
        ///     than a `while (true)` because this runs inside MusicBee.
        /// </summary>
        private const int MaxUnloadAttempts = 64;

        #endregion

        private readonly QueryHandlers _queries;
        private readonly CommandHandlers _commands;
        private readonly IPluginLogger _logger;

        // MusicBee's API is not thread-safe and its Library_Query* cursors are
        // process-global; serialize every provider access under one lock.
        private readonly object _apiLock = new object();

        // Pinned so the GC cannot collect them while Rust holds their pointers.
        private QueryCallbackDelegate _queryDataCallback;
        private CommandCallbackDelegate _executeCommandCallback;
        private FreeBufferDelegate _freeBufferCallback;
        private EventCallbackDelegate _onEventCallback;

        private IntPtr _libraryHandle;
        private bool _initialized;
        private bool _disposed;
        private bool _networkingRunning;
        private string _storagePath;

        /// <summary>True between a successful StartNetworking and StopNetworking.</summary>
        public bool IsNetworkingRunning => _networkingRunning;

        public NativeBridge(
            IPlayerDataProvider player,
            ITrackDataProvider track,
            IPlaylistDataProvider playlist,
            ILibraryDataProvider library,
            IUserSettings userSettings,
            ISystemOperations system,
            IPluginLogger logger)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (playlist == null) throw new ArgumentNullException(nameof(playlist));
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (userSettings == null) throw new ArgumentNullException(nameof(userSettings));
            if (system == null) throw new ArgumentNullException(nameof(system));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _queries = new QueryHandlers(player, track, playlist, library, userSettings);
            _commands = new CommandHandlers(player, track, playlist, userSettings, system, logger);
            PreloadNativeLibrary();
        }

        #region Lifecycle

        /// <summary>
        ///     Initialize the Rust core. Never throws: a missing DLL or an init
        ///     failure leaves the bridge un-initialized (remote disabled) so the
        ///     plugin loads degraded rather than crashing MusicBee.
        /// </summary>
        public unsafe void Initialize(string storagePath)
        {
            if (_initialized)
            {
                _logger.Warn("NativeBridge already initialized");
                return;
            }

            try
            {
                _storagePath = storagePath;
                _queryDataCallback = OnQueryData;
                _executeCommandCallback = OnExecuteCommand;
                _freeBufferCallback = OnFreeBuffer;
                _onEventCallback = OnCoreEvent;

                var callbacks = new MbrcCallbacks
                {
                    query_data = Marshal.GetFunctionPointerForDelegate(_queryDataCallback).ToPointer(),
                    execute_command = Marshal.GetFunctionPointerForDelegate(_executeCommandCallback).ToPointer(),
                    free_buffer = Marshal.GetFunctionPointerForDelegate(_freeBufferCallback).ToPointer(),
                    on_event = Marshal.GetFunctionPointerForDelegate(_onEventCallback).ToPointer(),
                };

                var pathBytes = Encoding.UTF8.GetBytes(storagePath + "\0");
                fixed (byte* pathPtr = pathBytes)
                {
                    var result = NativeMethods.mbrc_initialize(MbrcAbiVersion, callbacks, pathPtr);
                    if (result == 0)
                    {
                        _initialized = true;
                        // The core's logger is up now; let FfiLogger forward C# logs.
                        FfiLogger.MarkReady();
                        _logger.Info("Rust core initialized (storage path: {0})", storagePath);
                    }
                    else
                    {
                        _logger.LogError("Rust core initialization failed (code {0})", result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Rust core; remote disabled");
            }
        }

        public void StartNetworking()
        {
            if (!_initialized)
            {
                _logger.Warn("Cannot start networking: Rust core not initialized");
                return;
            }

            try
            {
                var result = NativeMethods.mbrc_start_networking();
                if (result == 0)
                {
                    _networkingRunning = true;
                    _logger.Info("Rust server started");
                }
                else
                {
                    _logger.LogError("Failed to start Rust server (code {0})", result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Rust networking");
            }
        }

        /// <summary>
        ///     Tell the core's background work to wind down. Stops nothing by
        ///     itself and returns immediately.
        ///     Notice, given ahead of a teardown the caller knows is coming: the
        ///     cover build is minutes of blocking work on a first run and
        ///     <see cref="StopNetworking" /> waits for it, so anything the caller
        ///     still has to do first is time the build can spend stopping.
        /// </summary>
        public void BeginStopping()
        {
            if (!_initialized) return;
            try
            {
                _ = NativeMethods.mbrc_begin_stopping();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to signal the core to stop");
            }
        }

        public void StopNetworking()
        {
            if (!_initialized) return;
            try
            {
                _ = NativeMethods.mbrc_stop_networking();
                _networkingRunning = false;
                _logger.Info("Rust server stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop Rust networking");
            }
        }

        /// <summary>
        ///     Reload the core so a changed port/filter takes effect: stop
        ///     networking, shut the core down, then re-initialize (which re-reads
        ///     core_settings.json) and start again. The native library stays
        ///     loaded. Used by the settings apply when a restart is required.
        /// </summary>
        public void Reload()
        {
            StopNetworking();
            if (_initialized)
            {
                try
                {
                    _ = NativeMethods.mbrc_shutdown();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to shut down core for reload");
                }

                _initialized = false;
            }

            Initialize(_storagePath);
            StartNetworking();
        }

        #endregion

        #region Settings (Rust-owned, read/written via FFI)

        /// <summary>
        ///     Read the core's current settings (Rust owns them). Returns null if
        ///     the core is not initialized or the read fails.
        /// </summary>
        public unsafe CoreSettings ReadSettings()
        {
            if (!_initialized) return null;
            try
            {
                uint len;
                var ptr = NativeMethods.mbrc_read_settings(&len);
                if (ptr == null)
                {
                    _logger.Warn("mbrc_read_settings returned null");
                    return null;
                }

                try
                {
                    var bytes = new byte[len];
                    if (len > 0)
                        Marshal.Copy((IntPtr)ptr, bytes, 0, (int)len);
                    return Msgpack.Deserialize<CoreSettings>(bytes);
                }
                finally
                {
                    NativeMethods.mbrc_free_bytes(ptr, len);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read settings from core");
                return null;
            }
        }

        /// <summary>
        ///     Ask the core to apply a staged update: it verifies the staged helper,
        ///     prompts for elevation if the plugins directory needs it, and starts
        ///     the helper, which waits for MusicBee to exit before swapping files.
        ///     Takes no arguments by design - every path is derived inside the core,
        ///     so there is nothing here that could name a directory to overwrite.
        ///     On <see cref="UpdateLaunch.Launched" /> the caller is expected to shut
        ///     MusicBee down; <see cref="UpdateLaunch.Cancelled" /> means the user
        ///     declined the prompt and the staged update is still there to retry.
        /// </summary>
        public UpdateLaunch ApplyStagedUpdate()
        {
            if (!_initialized) return UpdateLaunch.Failed;
            try
            {
                var result = (UpdateLaunch)NativeMethods.mbrc_apply_staged_update();
                _logger.Info("Update launch result: {0}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch the update helper");
                return UpdateLaunch.Failed;
            }
        }

        /// <summary>
        ///     Persist new settings to the Rust core (which validates then writes
        ///     core_settings.json). Returns false if the core rejected them.
        /// </summary>
        public unsafe bool WriteSettings(CoreSettings settings)
        {
            if (!_initialized || settings == null) return false;
            try
            {
                var bytes = Msgpack.Serialize(settings);
                fixed (byte* p = bytes)
                {
                    var result = NativeMethods.mbrc_write_settings(p, (uint)bytes.Length);
                    if (result != 0)
                    {
                        _logger.LogError("mbrc_write_settings rejected settings (code {0})", result);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write settings to core");
                return false;
            }
        }

        /// <summary>
        ///     Generic host -> core query (request/response). Serializes optional
        ///     params to MessagePack, calls <c>mbrc_query</c>, and deserializes the
        ///     reply to <typeparamref name="T"/>. Returns null when the core is not
        ///     initialized, the query is unknown, or the read fails.
        /// </summary>
        public unsafe T Query<T>(HostQueryType kind, byte[] paramsBytes = null) where T : class
        {
            if (!_initialized) return null;
            try
            {
                uint len;
                fixed (byte* p = paramsBytes)
                {
                    var ptr = NativeMethods.mbrc_query(
                        (int)kind, p, (uint)(paramsBytes?.Length ?? 0), &len);
                    if (ptr == null) return null;
                    try
                    {
                        var bytes = new byte[len];
                        if (len > 0)
                            Marshal.Copy((IntPtr)ptr, bytes, 0, (int)len);
                        return Msgpack.Deserialize<T>(bytes);
                    }
                    finally
                    {
                        NativeMethods.mbrc_free_bytes(ptr, len);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "host query {0} failed", kind);
                return null;
            }
        }

        /// <summary>
        ///     Generic host -> core command (fire-and-forget). Returns false when
        ///     the core is not initialized or the command was rejected.
        /// </summary>
        public unsafe bool Command(HostCommandType kind, byte[] paramsBytes = null)
        {
            if (!_initialized) return false;
            try
            {
                fixed (byte* p = paramsBytes)
                {
                    var result = NativeMethods.mbrc_command(
                        (int)kind, p, (uint)(paramsBytes?.Length ?? 0));
                    return result == 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "host command {0} failed", kind);
                return false;
            }
        }

        /// <summary>Read the core's cache status for the settings panel.</summary>
        public CoreCacheStatus ReadCacheStatus() => Query<CoreCacheStatus>(HostQueryType.CacheStatus);

        /// <summary>
        ///     The addresses a client can reach the server on (candidate
        ///     interface IPv4s + the bound port), for the settings panel to show
        ///     the user what to point the phone client at. Null if the core is
        ///     not initialized or the read fails.
        /// </summary>
        public ListeningInfo ReadListeningAddresses() =>
            Query<ListeningInfo>(HostQueryType.ListeningAddresses);

        /// <summary>Trigger a background rebuild of the metadata (browse) cache.</summary>
        public bool RebuildMetadata() => Command(HostCommandType.RebuildMetadata);

        /// <summary>Trigger a background rebuild of the cover cache.</summary>
        public bool RebuildCovers() => Command(HostCommandType.RebuildCovers);

        /// <summary>
        ///     Recent rejected connection attempts (newest first) for the settings
        ///     panel's blocked-connections view. Never null - an empty or
        ///     unavailable log yields an empty list.
        /// </summary>
        public List<BlockedConnection> ReadBlockedConnections() =>
            Query<List<BlockedConnection>>(HostQueryType.RecentBlocked) ?? new List<BlockedConnection>();

        /// <summary>Clear the core's in-memory blocked-connection log.</summary>
        public bool ClearBlockedConnections() => Command(HostCommandType.ClearBlockedLog);

        /// <summary>
        ///     Where the update flow stands, for the settings panel's Updates
        ///     group. Always answers while the core is up, including before any
        ///     check has run. Null if the core is not initialized.
        /// </summary>
        public UpdateStatus ReadUpdateStatus() => Query<UpdateStatus>(HostQueryType.UpdateStatus);

        /// <summary>
        ///     Start a background check for a newer release. Forced: the panel's
        ///     button is a direct instruction, so it runs whatever the automatic-
        ///     check preference says. False if a check or download is already
        ///     running (or the core is down); the result arrives as an
        ///     <see cref="HostEventType.UpdateStatusChanged" /> event.
        /// </summary>
        public bool CheckForUpdate() => Command(HostCommandType.CheckForUpdate);

        /// <summary>
        ///     Start a background download of the update the last check found and
        ///     stage it for the next restart. False when no check has produced one
        ///     - the host cannot name a release, only accept the verified one.
        /// </summary>
        public bool DownloadUpdate() => Command(HostCommandType.DownloadUpdate);

        /// <summary>Record that the user does not want this version offered again.</summary>
        public bool SkipUpdate() => Command(HostCommandType.SkipUpdate);

        /// <summary>
        ///     Where the diagnostics capture stands, for the settings panel's
        ///     Diagnostics group. Answers even before the core is initialized (the
        ///     capture state is the core's own, not the running core's), so the
        ///     panel always has something to render.
        /// </summary>
        public CaptureStatus ReadCaptureStatus() => Query<CaptureStatus>(HostQueryType.CaptureStatus);

        /// <summary>
        ///     Begin a diagnostics capture: the core raises its log level for the
        ///     session only and starts the window the bundle will be sliced to.
        ///     <paramref name="environment" /> carries what the core cannot see
        ///     for itself (MusicBee build, Windows version, CLR version). False if
        ///     a capture is already running.
        /// </summary>
        public bool StartCapture(List<CaptureEnvEntry> environment) =>
            Command(HostCommandType.StartCapture, Msgpack.Serialize(new CaptureRequest
            {
                destination_dir = string.Empty,
                host_environment = environment ?? new List<CaptureEnvEntry>()
            }));

        /// <summary>
        ///     End the capture and write the bundle into
        ///     <paramref name="destinationDir" />. The host resolves the folder
        ///     because a redirected Desktop is something only the CLR's known-folder
        ///     lookup gets right. The write happens in the background; the result
        ///     arrives as an <see cref="HostEventType.CaptureStatusChanged" /> event
        ///     with the path in <see cref="CaptureStatus.bundle_path" />.
        /// </summary>
        public bool StopCapture(string destinationDir) =>
            Command(HostCommandType.StopCapture, Msgpack.Serialize(new CaptureRequest
            {
                destination_dir = destinationDir ?? string.Empty,
                host_environment = new List<CaptureEnvEntry>()
            }));

        /// <summary>Abandon the capture: restore the log level and write nothing.</summary>
        public bool CancelCapture() => Command(HostCommandType.CancelCapture);

        /// <summary>
        ///     Apply the log level to the core's filter live (no restart needed).
        ///     <paramref name="logLevel"/> is the settings value (<c>info</c> /
        ///     <c>debug</c> / <c>trace</c>), mapped to a tracing filter directive.
        /// </summary>
        public unsafe void SetLogLevel(string logLevel)
        {
            if (!_initialized) return;
            try
            {
                string directive;
                switch ((logLevel ?? "info").Trim().ToLowerInvariant())
                {
                    case "trace":
                        directive = "info,mbrc_core=trace,mbrc=trace";
                        break;
                    case "debug":
                        directive = "info,mbrc_core=debug,mbrc=debug";
                        break;
                    default:
                        directive = "mbrc_core=info,info";
                        break;
                }

                var bytes = Encoding.UTF8.GetBytes(directive + "\0");
                fixed (byte* p = bytes)
                {
                    _ = NativeMethods.mbrc_set_log_level(p);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set core log level");
            }
        }

        #endregion

        #region Lifecycle (continued)

        /// <summary>Forward a MusicBee notification (0-7) to the core.</summary>
        public unsafe void HandleNotification(int notificationType)
        {
            if (!_initialized) return;
            try
            {
                _ = NativeMethods.mbrc_handle_notification(notificationType, null, 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to forward notification {0}", notificationType);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_initialized)
                {
                    _ = NativeMethods.mbrc_shutdown();
                    _initialized = false;
                    _logger.Info("Rust core shut down");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Rust core shutdown");
            }

            if (_libraryHandle != IntPtr.Zero)
            {
                FreeLibrary(_libraryHandle);
                _libraryHandle = IntPtr.Zero;
            }

            _queryDataCallback = null;
            _executeCommandCallback = null;
            _freeBufferCallback = null;
        }

        #endregion

        #region Callbacks

        private int OnQueryData(int queryType, IntPtr paramsBuf, uint paramsLen,
            out IntPtr outResultBuf, out uint outResultLen)
        {
            outResultBuf = IntPtr.Zero;
            outResultLen = 0;

            try
            {
                var p = CopyParams(paramsBuf, paramsLen);
                byte[] result;
                lock (_apiLock)
                {
                    result = _queries.Handle(queryType, p);
                }
                if (result == null)
                {
                    _logger.Warn("Unknown query type {0}", queryType);
                    return -1;
                }

                outResultBuf = Marshal.AllocHGlobal(result.Length);
                Marshal.Copy(result, 0, outResultBuf, result.Length);
                outResultLen = (uint)result.Length;
                return 0;
            }
            catch (Exception ex)
            {
                // Non-zero status = the C# provider threw (contract with the core).
                _logger.LogError(ex, "Query callback error (type {0})", queryType);
                return -1;
            }
        }

        private int OnExecuteCommand(int commandType, IntPtr paramsBuf, uint paramsLen)
        {
            try
            {
                var p = CopyParams(paramsBuf, paramsLen);
                bool ok;
                lock (_apiLock)
                {
                    ok = _commands.Handle(commandType, p);
                }
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command callback error (type {0})", commandType);
                return -1;
            }
        }

        private void OnFreeBuffer(IntPtr buf)
        {
            // Called from the core across the FFI boundary; never throw back into
            // native code (a managed exception unwinding into Rust is UB).
            try
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "free buffer callback threw");
            }
        }

        // Core -> host push. Called from a core/background thread; just re-raise
        // as a managed event and let subscribers marshal to their UI thread.
        // Never throw back across the FFI boundary.
        private void OnCoreEvent(int eventType, IntPtr payloadBuf, uint payloadLen)
        {
            try
            {
                CoreEvent?.Invoke((HostEventType)eventType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "core event handler threw (event {0})", eventType);
            }
        }

        private static byte[] CopyParams(IntPtr buf, uint len)
        {
            // Array.Empty is a shared singleton: safe here because callers only
            // hand the result to MessagePack, which reads it and never writes.
            if (buf == IntPtr.Zero || len == 0) return Array.Empty<byte>();
            var bytes = new byte[len];
            Marshal.Copy(buf, bytes, 0, (int)len);
            return bytes;
        }

        #endregion

        /// <summary>
        ///     Unmap mbrc_core.dll from this process so the file can be deleted.
        ///     Windows will not unlink a loaded image, and the plugin cannot outlive
        ///     MusicBee to do it later, so an uninstall that wants to remove the core
        ///     has to unload it first.
        ///     Freed in a loop rather than once: this class holds one reference from
        ///     its own preload, and the CLR holds another from resolving the DllImports
        ///     - which it never releases, since a P/Invoke module stays for the life of
        ///     the process. Dropping ours alone leaves the file mapped.
        ///     After this returns 0 modules loaded, **every** NativeMethods call is an
        ///     access violation waiting to happen: the IL stubs hold cached addresses
        ///     into an image that is no longer there. Only call it from
        ///     <see cref="Plugin.Uninstall" />, after the host is disposed and
        ///     <see cref="FfiLogger.MarkUnavailable" /> has stopped log forwarding.
        /// </summary>
        /// <returns>true if the module is no longer loaded.</returns>
        public static bool UnloadNativeLibrary()
        {
            try
            {
                for (var attempt = 0; attempt < MaxUnloadAttempts; attempt++)
                {
                    var module = GetModuleHandle(NativeLibraryFileName);
                    if (module == IntPtr.Zero) return true;
                    if (!FreeLibrary(module)) return false;
                }

                return GetModuleHandle(NativeLibraryFileName) == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Pre-load mbrc_core.dll from the plugin's own directory. Never throws.</summary>
        private void PreloadNativeLibrary()
        {
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (dir == null)
                {
                    _logger.Warn("Could not determine assembly directory for native preload");
                    return;
                }
                var dllPath = Path.Combine(dir, "mbrc_core.dll");
                if (!File.Exists(dllPath))
                {
                    _logger.Warn("mbrc_core.dll not found at {0}", dllPath);
                    return;
                }
                _libraryHandle = LoadLibrary(dllPath);
                if (_libraryHandle == IntPtr.Zero)
                    _logger.LogError("Failed to pre-load mbrc_core.dll (Win32 error {0})", Marshal.GetLastWin32Error());
                else
                    _logger.Info("Pre-loaded mbrc_core.dll from {0}", dllPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pre-loading mbrc_core.dll");
            }
        }
    }
}
