namespace MusicBeePlugin.Services.Core
{
    /// <summary>
    ///     Slim surface the C# layer needs from the Rust FFI bridge —
    ///     just enough for the WinForms settings dialog to render its
    ///     status badge and run a connectivity probe. The bridge itself
    ///     lives in the plugin assembly to keep DllImport off the core
    ///     library.
    /// </summary>
    public interface INativeBridge
    {
        /// <summary>
        ///     True between successful StartNetworking and StopNetworking
        ///     calls — i.e. the Rust core has bound a TCP listener.
        /// </summary>
        bool IsNetworkingRunning { get; }

        /// <summary>
        ///     Probe the configured loopback port to confirm the Rust
        ///     server is actually accepting connections. Cheap; safe to
        ///     call from the UI thread.
        /// </summary>
        bool VerifyConnection();
    }
}
