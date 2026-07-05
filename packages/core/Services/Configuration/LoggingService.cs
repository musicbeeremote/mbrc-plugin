using NLog;
using NLog.Config;
using NLog.Targets;

namespace MusicBeePlugin.Services.Configuration
{
    /// <summary>
    ///     Service responsible for configuring logging.
    /// </summary>
    public static class LoggingService
    {
        /// <summary>
        ///     Maximum log file size before archiving (2 MB).
        /// </summary>
        private const int MaxLogFileSizeBytes = 2097152;

        /// <summary>
        ///     Maximum number of archived log files to keep.
        /// </summary>
        private const int MaxLogArchiveFiles = 5;

#if DEBUG
        /// <summary>
        ///     UDP port for NLog Sentinel viewer.
        /// </summary>
        private const int SentinelUdpPort = 9999;
#endif

        /// <summary>
        ///     Initializes the logging configuration.
        /// </summary>
        /// <param name="logFilePath">The path to the log file.</param>
        /// <param name="logLevel">The minimum log level.</param>
        public static void InitializeLoggingConfiguration(string logFilePath, LogLevel logLevel)
        {
            var config = new LoggingConfiguration();

            var fileTarget = new FileTarget
            {
                ArchiveAboveSize = MaxLogFileSizeBytes,
                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = MaxLogArchiveFiles,
                EnableArchiveFileCompression = true,
                FileName = logFilePath,
                Layout = "${longdate} [${level:uppercase=true}]${newline}" +
                         "${logger} : ${callsite-linenumber} ${when:when=length('${threadname}') > 0: [${threadname}]}${newline}" +
                         "${message}${newline}" +
                         "${when:when=length('${exception}') > 0: ${exception}${newline}}"
            };

#if DEBUG
            var consoleTarget = new ColoredConsoleTarget();
            var debugger = new DebuggerTarget();
            var sentinalTarget = new NLogViewerTarget
            {
                Name = "sentinel",
                Address = $"udp://127.0.0.1:{SentinelUdpPort}",
                IncludeNLogData = true,
                IncludeSourceInfo = true
            };

            var sentinelRule = new LoggingRule("*", LogLevel.Trace, sentinalTarget);
            config.AddTarget("sentinel", sentinalTarget);
            config.LoggingRules.Add(sentinelRule);
            config.AddTarget("console", consoleTarget);
            config.AddTarget("debugger", debugger);
            consoleTarget.Layout = @"${date:format=HH\\:MM\\:ss} ${logger} ${message} ${exception}";

            debugger.Layout = fileTarget.Layout;

            var consoleRule = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(consoleRule);

            var debuggerRule = new LoggingRule("*", LogLevel.Debug, debugger);
            config.LoggingRules.Add(debuggerRule);
#endif
            config.AddTarget("file", fileTarget);

            var fileRule = new LoggingRule("*", logLevel, fileTarget);

            config.LoggingRules.Add(fileRule);

            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }
    }
}
