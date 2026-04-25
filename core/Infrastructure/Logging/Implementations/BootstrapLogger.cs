using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace MusicBeePlugin.Infrastructure.Logging.Implementations
{
    /// <summary>
    ///     File-backed fallback logger for messages that fire before the
    ///     Rust core is initialised, after it has shut down, or while the
    ///     <c>mbrc_log</c> FFI call returns "not initialised".
    ///
    ///     The primary log destination is the Rust <c>tracing</c> file
    ///     (<c>rust_core.log</c>); this writer is a tiny, dependency-free
    ///     fallback that only ever holds bootstrap and shutdown failures.
    ///     Each line is timestamped and flushed immediately so the file
    ///     is useful even when the host crashes seconds later.
    /// </summary>
    public static class BootstrapLogger
    {
        private static readonly object Gate = new object();
        private static string _path = Path.Combine(Path.GetTempPath(), "mbrc_bootstrap.log");

        /// <summary>
        ///     Point the bootstrap log at <c>{storagePath}/mb_remote/bootstrap.log</c>.
        ///     Safe to call before or after the first write — subsequent
        ///     entries land in the new location.
        /// </summary>
        public static void Configure(string storagePath)
        {
            if (string.IsNullOrEmpty(storagePath))
                return;
            try
            {
                var dir = Path.Combine(storagePath, "mb_remote");
                Directory.CreateDirectory(dir);
                lock (Gate)
                {
                    _path = Path.Combine(dir, "bootstrap.log");
                }
            }
            catch
            {
                // If we can't even prepare the bootstrap path, keep the
                // temp-folder default — better than throwing on a logger.
            }
        }

        public static void Trace(string target, string message) => Write("TRACE", target, message, null);
        public static void Debug(string target, string message) => Write("DEBUG", target, message, null);
        public static void Info(string target, string message) => Write("INFO", target, message, null);
        public static void Warn(string target, string message) => Write("WARN", target, message, null);
        public static void Error(string target, string message) => Write("ERROR", target, message, null);

        public static void Error(string target, string message, Exception ex) =>
            Write("ERROR", target, message, ex);

        public static void Fatal(string target, string message, Exception ex) =>
            Write("FATAL", target, message, ex);

        private static void Write(string level, string target, string message, Exception ex)
        {
            // Best-effort: a logger that throws is worse than a missed line.
            try
            {
                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [tid={2}] {3} : {4}{5}{6}",
                    DateTime.Now,
                    level,
                    Thread.CurrentThread.ManagedThreadId,
                    string.IsNullOrEmpty(target) ? "<unknown>" : target,
                    message ?? string.Empty,
                    ex == null ? string.Empty : Environment.NewLine,
                    ex?.ToString() ?? string.Empty);

                lock (Gate)
                {
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
            }
            catch
            {
                // Swallow — see comment above.
            }
        }
    }
}