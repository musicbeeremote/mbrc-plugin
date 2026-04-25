using System;
using System.Globalization;
using MusicBeePlugin.Infrastructure.Logging.Contracts;

namespace MusicBeePlugin.Infrastructure.Logging.Implementations
{
    /// <summary>
    ///     <see cref="IPluginLogger" /> implementation that forwards each
    ///     log call into the Rust core via <see cref="RustLogBridge" /> —
    ///     so the plugin produces a single, unified log file. When the
    ///     Rust core isn't ready yet (early init) or has already torn
    ///     down (late shutdown), entries fall through to
    ///     <see cref="BootstrapLogger" />.
    /// </summary>
    public class PluginLogger : IPluginLogger
    {
        private static readonly IFormatProvider FormatProvider = CultureInfo.InvariantCulture;
        private readonly string _target;

        public PluginLogger(string name) => _target = name ?? string.Empty;

        public PluginLogger(Type type) => _target = type?.FullName ?? string.Empty;

        public void Debug(string message) => Send(RustLogBridge.LevelDebug, message, null);

        public void Debug(string messageTemplate, params object[] args) =>
            Send(RustLogBridge.LevelDebug, Format(messageTemplate, args), null);

        public void Info(string message) => Send(RustLogBridge.LevelInfo, message, null);

        public void Info(string messageTemplate, params object[] args) =>
            Send(RustLogBridge.LevelInfo, Format(messageTemplate, args), null);

        public void Warn(string message) => Send(RustLogBridge.LevelWarn, message, null);

        public void Warn(string messageTemplate, params object[] args) =>
            Send(RustLogBridge.LevelWarn, Format(messageTemplate, args), null);

        public void LogError(string message) => Send(RustLogBridge.LevelError, message, null);

        public void LogError(string messageTemplate, params object[] args) =>
            Send(RustLogBridge.LevelError, Format(messageTemplate, args), null);

        public void LogError(Exception exception, string message = null) =>
            Send(RustLogBridge.LevelError, message, exception);

        public void LogError(Exception exception, string messageTemplate, params object[] args) =>
            Send(RustLogBridge.LevelError, Format(messageTemplate, args), exception);

        public void Fatal(string message) => Send(RustLogBridge.LevelError, "FATAL: " + message, null);

        public void Fatal(string messageTemplate, params object[] args) =>
            Send(RustLogBridge.LevelError, "FATAL: " + Format(messageTemplate, args), null);

        public void Fatal(Exception exception, string message = null) =>
            Send(RustLogBridge.LevelError, "FATAL: " + (message ?? string.Empty), exception);

        public void Fatal(Exception exception, string messageTemplate, params object[] args) =>
            Send(RustLogBridge.LevelError, "FATAL: " + Format(messageTemplate, args), exception);

        private void Send(int level, string message, Exception ex)
        {
            // Bake the exception into the message so a single FFI call
            // carries everything Rust's tracing layer needs to render.
            var rendered = ex == null
                ? (message ?? string.Empty)
                : string.IsNullOrEmpty(message)
                    ? ex.ToString()
                    : message + Environment.NewLine + ex;

            if (RustLogBridge.TryLog(level, _target, rendered))
                return;

            // Rust isn't up — fall back to the bootstrap file so we
            // never silently drop init/shutdown errors.
            switch (level)
            {
                case RustLogBridge.LevelTrace:
                    BootstrapLogger.Trace(_target, rendered);
                    break;
                case RustLogBridge.LevelDebug:
                    BootstrapLogger.Debug(_target, rendered);
                    break;
                case RustLogBridge.LevelInfo:
                    BootstrapLogger.Info(_target, rendered);
                    break;
                case RustLogBridge.LevelWarn:
                    BootstrapLogger.Warn(_target, rendered);
                    break;
                default:
                    if (ex == null)
                        BootstrapLogger.Error(_target, message ?? string.Empty);
                    else
                        BootstrapLogger.Error(_target, message ?? string.Empty, ex);
                    break;
            }
        }

        private static string Format(string template, object[] args)
        {
            if (string.IsNullOrEmpty(template) || args == null || args.Length == 0)
                return template ?? string.Empty;
            try
            {
                return string.Format(FormatProvider, template, args);
            }
            catch (FormatException)
            {
                // Templates with named placeholders ("{Name}") aren't
                // valid String.Format inputs. Keep the raw template
                // rather than dropping the line.
                return template;
            }
        }
    }
}