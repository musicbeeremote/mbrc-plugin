using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MusicBeePlugin.Infrastructure.Logging.Implementations
{
    /// <summary>
    ///     P/Invoke wrapper around <c>mbrc_log</c>. Routes C# log entries
    ///     into the Rust <c>tracing</c> pipeline so the plugin only
    ///     writes one log file (<c>rust_core.log</c>).
    ///
    ///     Returns <c>false</c> when the Rust core is not initialised yet
    ///     (or has already shut down); callers should fall back to
    ///     <see cref="BootstrapLogger" /> in that window.
    /// </summary>
    public static class RustLogBridge
    {
        public const int LevelTrace = 0;
        public const int LevelDebug = 1;
        public const int LevelInfo = 2;
        public const int LevelWarn = 3;
        public const int LevelError = 4;

        [DllImport("mbrc_core.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mbrc_log")]
        private static extern int mbrc_log(int level, byte[] target, byte[] message);

        [DllImport("mbrc_core.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mbrc_set_log_level")]
        private static extern int mbrc_set_log_level(byte[] directive);

        /// <summary>
        ///     Try to send a log line through the Rust pipeline. Returns
        ///     <c>true</c> if the call was accepted, <c>false</c> if the
        ///     core wasn't ready or the FFI threw (DLL missing, etc.).
        /// </summary>
        public static bool TryLog(int level, string target, string message)
        {
            try
            {
                var rc = mbrc_log(level, ToUtf8(target), ToUtf8(message));
                return rc == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch
            {
                // Any other failure (BadImageFormat, AccessViolation
                // mid-shutdown) is also a "fall back to bootstrap" signal.
                return false;
            }
        }

        /// <summary>
        ///     Swap the active <c>EnvFilter</c> directive at runtime.
        ///     Used by the settings-UI debug-logging checkbox: pass
        ///     <c>"debug"</c> when enabled, <c>"info"</c> otherwise.
        ///     Accepts any string <c>EnvFilter</c> understands (e.g.
        ///     <c>"mbrc_core=trace,info"</c>).
        ///
        ///     Returns <c>true</c> on success, <c>false</c> if the
        ///     core isn't ready or the directive failed to parse.
        /// </summary>
        public static bool TrySetLevel(string directive)
        {
            try
            {
                return mbrc_set_log_level(ToUtf8(directive)) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] ToUtf8(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new byte[] { 0 };
            var bytes = Encoding.UTF8.GetBytes(value);
            var nullTerminated = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, nullTerminated, 0, bytes.Length);
            nullTerminated[bytes.Length] = 0;
            return nullTerminated;
        }
    }
}