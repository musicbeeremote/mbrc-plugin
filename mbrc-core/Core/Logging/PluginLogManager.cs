using System;
using MusicBeeRemote.Core.Settings;
using NLog;
using NLog.Config;
using NLog.Targets;
using TinyMessenger;

namespace MusicBeeRemote.Core.Logging
{
    internal sealed class PluginLogManager : IPluginLogManager, IDisposable
    {
        private readonly IStorageLocationProvider _storageLocationProvider;
        private bool _isDisposed;
        private ColoredConsoleTarget _consoleTarget;
        private DebuggerTarget _debuggerTarget;
        private FileTarget _fileTarget;
        private NLogViewerTarget _nLogViewerTarget;

        public PluginLogManager(IStorageLocationProvider storageLocationProvider, ITinyMessengerHub hub)
        {
            _storageLocationProvider = storageLocationProvider;
            hub.Subscribe<DebugSettingsModifiedEvent>(OnDebugSettingsModifiedEvent);
        }

        ~PluginLogManager()
        {
            Dispose(false);
        }

        public void Initialize(LogLevel logLevel)
        {
            var config = new LoggingConfiguration();
            const string logLayout = "${longdate} [${level:uppercase=true}] Thread: [${threadid}]${newline}" +
                                     "${logger}:${callsite-linenumber} ${when:when=length('${threadname}') > 0: [${threadname}]}${newline}" +
                                     "${message}${newline}" +
                                     "${when:when=length('${exception}') > 0: ${exception}${newline}}";

            if (logLevel == LogLevel.Debug)
            {
                _consoleTarget = new ColoredConsoleTarget();
                _debuggerTarget = new DebuggerTarget();
                _nLogViewerTarget = new NLogViewerTarget
                {
                    Name = "sentinel",
                    Address = "udp://127.0.0.1:9999",
                    IncludeNLogData = true,
                    IncludeSourceInfo = true,
                };

                var sentinelRule = new LoggingRule("*", LogLevel.Trace, _nLogViewerTarget);
                config.AddTarget("sentinel", _nLogViewerTarget);
                config.LoggingRules.Add(sentinelRule);
                config.AddTarget("console", _consoleTarget);
                config.AddTarget("debugger", _debuggerTarget);
                _consoleTarget.Layout = @"${date:format=HH\\:MM\\:ss} ${logger} [${threadid}]:${message} ${exception}";
                _debuggerTarget.Layout = logLayout;

                var consoleRule = new LoggingRule("*", LogLevel.Debug, _consoleTarget);
                config.LoggingRules.Add(consoleRule);

                var debuggerRule = new LoggingRule("*", LogLevel.Debug, _debuggerTarget);
                config.LoggingRules.Add(debuggerRule);
            }
            else
            {
                _fileTarget = new FileTarget
                {
                    ArchiveAboveSize = 2097152,
                    ArchiveEvery = FileArchivePeriod.Day,
                    ArchiveNumbering = ArchiveNumberingMode.Rolling,
                    MaxArchiveFiles = 5,
                    EnableArchiveFileCompression = true,
                    FileName = _storageLocationProvider.LogFile,
                    Layout = logLayout,
                };

                config.AddTarget("file", _fileTarget);
                var fileRule = new LoggingRule("*", logLevel, _fileTarget);
                config.LoggingRules.Add(fileRule);
            }

            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                _debuggerTarget?.Dispose();
                _consoleTarget?.Dispose();
                _fileTarget?.Dispose();
                _nLogViewerTarget?.Dispose();
            }

            _isDisposed = true;
        }

        private void OnDebugSettingsModifiedEvent(DebugSettingsModifiedEvent ev)
        {
            Initialize(ev.DebugLogEnabled ? LogLevel.Debug : LogLevel.Error);
        }
    }
}
