using MusicBeeRemote.Core.Settings;
using NLog;
using NLog.Config;
using NLog.Targets;
using TinyMessenger;

namespace MusicBeeRemote.Core.Logging
{
    class PluginLogManager : IPluginLogManager
    {
        private readonly IStorageLocationProvider _storageLocationProvider;

        public PluginLogManager(IStorageLocationProvider storageLocationProvider, ITinyMessengerHub hub)
        {
            _storageLocationProvider = storageLocationProvider;
            hub.Subscribe<DebugSettingsModifiedEvent>(OnDebugSettingsModifiedEvent);
        }

        private void OnDebugSettingsModifiedEvent(DebugSettingsModifiedEvent ev)
        {
            Initialize(ev.DebugLogEnabled ? LogLevel.Debug : LogLevel.Error);
        }

        public void Initialize(LogLevel logLevel)
        {
            var config = new LoggingConfiguration();
            var logLayout = "${longdate} [${level:uppercase=true}] Thread: [${threadid}]${newline}" +
                            "${logger}:${callsite-linenumber} ${when:when=length('${threadname}') > 0: [${threadname}]}${newline}" +
                            "${message}${newline}" +
                            "${when:when=length('${exception}') > 0: ${exception}${newline}}";
#if DEBUG
            var consoleTarget = new ColoredConsoleTarget();
            var debugger = new DebuggerTarget();
            var sentinalTarget = new NLogViewerTarget()
            {
                Name = "sentinel",
                Address = "udp://127.0.0.1:9999",
                IncludeNLogData = true,
                IncludeSourceInfo = true
            };

            var sentinelRule = new LoggingRule("*", LogLevel.Trace, sentinalTarget);
            config.AddTarget("sentinel", sentinalTarget);
            config.LoggingRules.Add(sentinelRule);
            config.AddTarget("console", consoleTarget);
            config.AddTarget("debugger", debugger);
            consoleTarget.Layout = @"${date:format=HH\\:MM\\:ss} ${logger} [${threadid}]:${message} ${exception}";

            debugger.Layout = logLayout;

            var consoleRule = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(consoleRule);

            var debuggerRule = new LoggingRule("*", LogLevel.Debug, debugger);
            config.LoggingRules.Add(debuggerRule);
#else
            var fileTarget = new FileTarget
            {
                ArchiveAboveSize = 2097152,
                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 5, 
                EnableArchiveFileCompression = true,
                FileName = _storageLocationProvider.LogFile,
                Layout = logLayout
            };

            config.AddTarget("file", fileTarget);
            var fileRule = new LoggingRule("*", logLevel, fileTarget);
            config.LoggingRules.Add(fileRule);
#endif
            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }
    }
}