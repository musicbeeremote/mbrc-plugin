using System;
using System.Globalization;
using System.IO;
using System.Text;
using MusicBeePlugin.Logging;
using MusicBeePlugin.Ffi.Generated;

namespace MusicBeePlugin.Ffi
{
    /// <summary>
    ///     <see cref="IPluginLogger" /> that routes C# logs through the Rust core's
    ///     logger (<c>mbrc_log</c>), so the whole plugin logs to one place
    ///     (<c>mbrc-core.log</c>).
    ///
    ///     Before the core is initialized (see <see cref="MarkReady" />) the Rust
    ///     logger isn't up yet, so those early wiring logs - and any that occur if
    ///     init fails outright - go to a bootstrap file (<c>mbrc-bootstrap.log</c>)
    ///     next to the core log instead of being lost. A logging failure never
    ///     throws.
    /// </summary>
    public sealed class FfiLogger : IPluginLogger
    {
        // Core log levels: 0=trace, 1=debug, 2=info, 3=warn, 4=error.
        private const int LevelDebug = 1;
        private const int LevelInfo = 2;
        private const int LevelWarn = 3;
        private const int LevelError = 4;

        private const string BootstrapFileStem = "mbrc-bootstrap";
        private const long MaxBootstrapBytes = 5 * 1024 * 1024; // 5 MiB
        private const int KeepGenerations = 5;

        // The Rust logger is only usable after mbrc_initialize (which installs the
        // tracing subscriber). Shared across all logger instances.
        private static volatile bool _ready;
        private static readonly object BootstrapLock = new object();
        private static bool _rolled;

        private readonly string _name;
        private readonly string _bootstrapPath;

        public FfiLogger(string name, string storageDirectory)
        {
            _name = name ?? "mbrc";
            _bootstrapPath = string.IsNullOrEmpty(storageDirectory)
                ? null
                : Path.Combine(storageDirectory, BootstrapFileStem + ".log");
            RollBootstrapOnce();
        }

        /// <summary>Enable forwarding once the core has been initialized.</summary>
        public static void MarkReady() => _ready = true;

        public void Debug(string message) => Emit(LevelDebug, message);
        public void Debug(string messageTemplate, params object[] args) => Emit(LevelDebug, Format(messageTemplate, args));
        public void Info(string message) => Emit(LevelInfo, message);
        public void Info(string messageTemplate, params object[] args) => Emit(LevelInfo, Format(messageTemplate, args));
        public void Warn(string message) => Emit(LevelWarn, message);
        public void Warn(string messageTemplate, params object[] args) => Emit(LevelWarn, Format(messageTemplate, args));

        public void LogError(string message) => Emit(LevelError, message);
        public void LogError(string messageTemplate, params object[] args) => Emit(LevelError, Format(messageTemplate, args));
        public void LogError(Exception exception, string message = null) => Emit(LevelError, WithException(message, exception));
        public void LogError(Exception exception, string messageTemplate, params object[] args) => Emit(LevelError, WithException(Format(messageTemplate, args), exception));

        public void Fatal(string message) => Emit(LevelError, message);
        public void Fatal(string messageTemplate, params object[] args) => Emit(LevelError, Format(messageTemplate, args));
        public void Fatal(Exception exception, string message = null) => Emit(LevelError, WithException(message, exception));
        public void Fatal(Exception exception, string messageTemplate, params object[] args) => Emit(LevelError, WithException(Format(messageTemplate, args), exception));

        private void Emit(int level, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_ready)
                EmitToCore(level, message);
            else
                WriteBootstrap(level, message);
        }

        private unsafe void EmitToCore(int level, string message)
        {
            try
            {
                var targetBytes = Encoding.UTF8.GetBytes(_name + "\0");
                var messageBytes = Encoding.UTF8.GetBytes(message + "\0");
                fixed (byte* targetPtr = targetBytes)
                fixed (byte* messagePtr = messageBytes)
                {
                    _ = NativeMethods.mbrc_log(level, targetPtr, messagePtr);
                }
            }
            catch
            {
                // Logging must never take down the host.
            }
        }

        // Pre-init fallback: append to mbrc-bootstrap.log next to the core log.
        private void WriteBootstrap(int level, string message)
        {
            if (_bootstrapPath == null) return;
            try
            {
                // UTC RFC3339 (matching the Rust core log's timestamps) so the two
                // logs share one clock and correlate directly.
                var line = $"{DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture)} " +
                           $"[{LevelName(level)}] [{_name}] {message}{Environment.NewLine}";
                lock (BootstrapLock)
                {
                    File.AppendAllText(_bootstrapPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Best-effort bootstrap logging; never throw.
            }
        }

        // Size-based roll at startup, mirroring the Rust core's mbrc-core.N.log
        // scheme: if the current bootstrap log is over the cap, shift
        // .N-1 -> .N and the live file to .1, keeping the last N generations.
        // Runs once per process.
        private void RollBootstrapOnce()
        {
            if (_bootstrapPath == null) return;
            lock (BootstrapLock)
            {
                if (_rolled) return;
                _rolled = true;
                try
                {
                    var dir = Path.GetDirectoryName(_bootstrapPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    if (!File.Exists(_bootstrapPath) ||
                        new FileInfo(_bootstrapPath).Length < MaxBootstrapBytes)
                        return;

                    var rolled = new Func<int, string>(n => Path.Combine(dir, $"{BootstrapFileStem}.{n}.log"));
                    if (File.Exists(rolled(KeepGenerations)))
                        File.Delete(rolled(KeepGenerations));
                    for (var n = KeepGenerations - 1; n >= 1; n--)
                        if (File.Exists(rolled(n)))
                            File.Move(rolled(n), rolled(n + 1));
                    File.Move(_bootstrapPath, rolled(1));
                }
                catch
                {
                    // Rotation is best-effort; logging still comes up.
                }
            }
        }

        private static string LevelName(int level)
        {
            switch (level)
            {
                case LevelDebug: return "DEBUG";
                case LevelInfo: return "INFO";
                case LevelWarn: return "WARN";
                default: return "ERROR";
            }
        }

        // The call sites use positional placeholders ({0}); fall back to the raw
        // template if a named-placeholder template can't be formatted positionally.
        private static string Format(string template, object[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrEmpty(template))
                return template;
            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        private static string WithException(string message, Exception exception)
        {
            if (exception == null) return message ?? string.Empty;
            return string.IsNullOrEmpty(message) ? exception.ToString() : $"{message}: {exception}";
        }
    }
}
