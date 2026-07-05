using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Configuration;
using MusicBeePlugin.Protocol.Messages;

namespace MusicBeePlugin.Services.Configuration
{
    /// <summary>
    ///     Service for managing user settings.
    ///     Handles loading, saving, and persistence of user settings to XML.
    /// </summary>
    public class UserSettingsService : IUserSettingsService
    {
        private const string Application = "mbremote";
        private const string PortNumber = "port";
        private const string Values = "values";
        private const string Selection = "selection";
        private const string SFilename = "settings.xml";
        private const string SFolder = "mb_remote";
        private const string LastRunVersion = "lastrunversion";
        private const string LibrarySource = "source";
        private const string LogsEnabled = "logs_enabled";
        private const string UpdateFirewallNode = "update_firewall";
        private const string AlternativeSearchNode = "alternative_search";
        private const string LogFilename = "mbrc.log";

        private static readonly char[] CommaSeparator = { ',' };

        private readonly IEventAggregator _eventAggregator;
        private readonly IPluginLogger _logger;
        private readonly ReaderWriterLockSlim _settingsLock = new ReaderWriterLockSlim();
        private UserSettingsModel _settings;

        public UserSettingsService(IEventAggregator eventAggregator, IPluginLogger logger)
        {
            _eventAggregator = eventAggregator;
            _logger = logger;
            _settings = new UserSettingsModel();
        }

        #region IUserSettings Properties (Read-only access with thread safety)

        public uint ListeningPort => ReadSetting(() => _settings.ListeningPort);
        public FilteringSelection FilterSelection => ReadSetting(() => _settings.FilterSelection);
        public string BaseIp => ReadSetting(() => _settings.BaseIp);
        public uint LastOctetMax => ReadSetting(() => _settings.LastOctetMax);
        public IReadOnlyList<string> IpAddressList => ReadSetting(() => _settings.IpAddressList.AsReadOnly());
        public SearchSource Source => ReadSetting(() => _settings.Source);
        public bool AlternativeSearch => ReadSetting(() => _settings.AlternativeSearch);
        public bool DebugLogEnabled => ReadSetting(() => _settings.DebugLogEnabled);
        public bool UpdateFirewall => ReadSetting(() => _settings.UpdateFirewall);
        public string CurrentVersion => ReadSetting(() => _settings.CurrentVersion);
        public string StoragePath { get; private set; }

        public string FullLogPath => Path.Combine(StoragePath, LogFilename);

        private T ReadSetting<T>(Func<T> accessor)
        {
            _settingsLock.EnterReadLock();
            try
            {
                return accessor();
            }
            finally
            {
                _settingsLock.ExitReadLock();
            }
        }

        #endregion

        #region IUserSettingsService Implementation

        public void SetStoragePath(string path)
        {
            StoragePath = Path.Combine(path, SFolder);
            _logger.Debug($"Settings storage path set to: {StoragePath}");
        }

        public void LoadSettings()
        {
            _settingsLock.EnterWriteLock();
            try
            {
                LoadSettingsInternal();
            }
            finally
            {
                _settingsLock.ExitWriteLock();
            }
        }

        private void LoadSettingsInternal()
        {
            try
            {
                if (!File.Exists(GetSettingsFile()))
                {
                    _logger.Info("Settings file not found, using defaults");
                    _settings = new UserSettingsModel();
                    return;
                }

                _logger.Debug("Loading settings from file");
                var document = new XmlDocument();
                SafeLoadXml(document, GetSettingsFile());

                _settings.ListeningPort =
                    uint.TryParse(ReadNodeValue(document, PortNumber), out var port) ? port : 3000;
                UpdateFilteringSelection(ReadNodeValue(document, Selection));
                SetValues(ReadNodeValue(document, Values));

                _settings.DebugLogEnabled = bool.TryParse(ReadNodeValue(document, LogsEnabled), out var debugEnabled) &&
                                            debugEnabled;
                _settings.UpdateFirewall =
                    bool.TryParse(ReadNodeValue(document, UpdateFirewallNode), out var updateFirewall) &&
                    updateFirewall;
                _settings.AlternativeSearch =
                    bool.TryParse(ReadNodeValue(document, AlternativeSearchNode), out var altSearch) && altSearch;

                _settings.Source = (SearchSource)(short.TryParse(ReadNodeValue(document, LibrarySource), out var source)
                    ? source
                    : 1);

                _logger.Debug(
                    $"Settings loaded successfully - Port: {_settings.ListeningPort}, Source: {_settings.Source}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings, using defaults");
                _settings = new UserSettingsModel();
            }
        }

        public void SaveSettings()
        {
            _settingsLock.EnterReadLock();
            try
            {
                SaveSettingsInternal();
            }
            finally
            {
                _settingsLock.ExitReadLock();
            }
        }

        private void SaveSettingsInternal()
        {
            try
            {
                if (!File.Exists(GetSettingsFile()))
                    CreateEmptySettingsFile(Application);

                _logger.Debug("Saving settings to file");
                var document = new XmlDocument();
                SafeLoadXml(document, GetSettingsFile());
                WriteApplicationSetting(document);
                document.Save(GetSettingsFile());

                _logger.Debug("Settings saved successfully, triggering socket restart");
                var message = MessageSendEvent.Create("SocketRestart", true);
                _eventAggregator.Publish(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                throw;
            }
        }

        public bool IsFirstRun()
        {
            try
            {
                var isFirst = false;
                var document = new XmlDocument();

                if (!File.Exists(GetSettingsFile()))
                {
                    isFirst = true;
                    CreateEmptySettingsFile(Application);
                }
                else
                {
                    SafeLoadXml(document, GetSettingsFile());
                    var lastRun = ReadNodeValue(document, LastRunVersion);
                    if (string.IsNullOrEmpty(lastRun))
                        isFirst = true;
                }

                if (isFirst)
                {
                    SafeLoadXml(document, GetSettingsFile());
                    WriteNodeValue(document, LastRunVersion, _settings.CurrentVersion);
                    document.Save(GetSettingsFile());
                    _logger.Info("First run detected, settings initialized");
                }

                return isFirst;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check first run status");
                return false;
            }
        }

        public void UpdateSettings(UserSettingsModel settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _settingsLock.EnterWriteLock();
            try
            {
                _settings = settings.Clone();
                _logger.Debug("Settings model updated");
            }
            finally
            {
                _settingsLock.ExitWriteLock();
            }
        }

        public UserSettingsModel GetSettingsModel()
        {
            _settingsLock.EnterReadLock();
            try
            {
                return _settings.Clone();
            }
            finally
            {
                _settingsLock.ExitReadLock();
            }
        }

        #endregion

        #region Private Helper Methods

        private string GetSettingsFile()
        {
            return Path.Combine(StoragePath, SFilename);
        }

        private void CreateEmptySettingsFile(string application)
        {
            if (!Directory.Exists(StoragePath))
                Directory.CreateDirectory(StoragePath);

            var document = new XmlDocument();
            var declaration = document.CreateXmlDeclaration("1.0", "utf-8", "yes");
            var root = document.CreateElement(application);
            document.InsertBefore(declaration, document.DocumentElement);
            document.AppendChild(root);
            document.Save(GetSettingsFile());
        }

        private void WriteApplicationSetting(XmlDocument document)
        {
            if (_settings.ListeningPort < 65535)
                WriteNodeValue(document, PortNumber, _settings.ListeningPort.ToString(CultureInfo.InvariantCulture));

            WriteNodeValue(document, Values, GetValues());
            WriteNodeValue(document, Selection, _settings.FilterSelection.ToString());
            WriteNodeValue(document, LibrarySource, ((short)_settings.Source).ToString(CultureInfo.InvariantCulture));
            WriteNodeValue(document, LogsEnabled, _settings.DebugLogEnabled.ToString());
            WriteNodeValue(document, UpdateFirewallNode, _settings.UpdateFirewall.ToString());
            WriteNodeValue(document, AlternativeSearchNode, _settings.AlternativeSearch.ToString());
        }

        private static void WriteNodeValue(XmlDocument document, string name, string value)
        {
            var node = document.SelectSingleNode("//" + name);
            if (node == null)
            {
                var pattern = document.CreateElement(name);
                XmlElement root = document.DocumentElement;
                pattern.InnerText = value;
                root?.AppendChild(pattern);
            }
            else
            {
                node.InnerText = value;
            }
        }

        private static string ReadNodeValue(XmlNode document, string name)
        {
            var node = document.SelectSingleNode("//" + name);
            return node?.InnerText ?? string.Empty;
        }

        private string GetValues()
        {
            switch (_settings.FilterSelection)
            {
                case FilteringSelection.All:
                    return string.Empty;
                case FilteringSelection.Range:
                    return FlattenAllowedRange();
                case FilteringSelection.Specific:
                    return FlattenAllowedAddressList();
                default:
                    return string.Empty;
            }
        }

        private void SetValues(string values)
        {
            switch (_settings.FilterSelection)
            {
                case FilteringSelection.All:
                    break;
                case FilteringSelection.Range:
                    UnFlattenAllowedRange(values);
                    break;
                case FilteringSelection.Specific:
                    UnflattenAllowedAddressList(values);
                    break;
            }
        }

        private string FlattenAllowedRange()
        {
            if (!string.IsNullOrEmpty(_settings.BaseIp) && (_settings.LastOctetMax > 1 || _settings.LastOctetMax < 255))
                return _settings.BaseIp + "," + _settings.LastOctetMax;
            return string.Empty;
        }

        private void UnFlattenAllowedRange(string range)
        {
            if (string.IsNullOrEmpty(range))
                return;
            var splitRange = range.Split(CommaSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (splitRange.Length >= 2)
            {
                _settings.BaseIp = splitRange[0];
                if (uint.TryParse(splitRange[1], out var maxOctet))
                    _settings.LastOctetMax = maxOctet;
            }
        }

        private string FlattenAllowedAddressList()
        {
            if (_settings.IpAddressList == null || _settings.IpAddressList.Count == 0)
                return string.Empty;
            return string.Join(",", _settings.IpAddressList) + ",";
        }

        private void UpdateFilteringSelection(string selection)
        {
            switch (selection)
            {
                case "All":
                    _settings.FilterSelection = FilteringSelection.All;
                    break;
                case "Range":
                    _settings.FilterSelection = FilteringSelection.Range;
                    break;
                case "Specific":
                    _settings.FilterSelection = FilteringSelection.Specific;
                    break;
                default:
                    _settings.FilterSelection = FilteringSelection.All;
                    break;
            }
        }

        private void UnflattenAllowedAddressList(string allowedAddresses)
        {
            _settings.IpAddressList = new List<string>(
                allowedAddresses.Trim().Split(CommaSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Safely loads an XML document from a file path with secure settings to prevent XXE attacks
        /// </summary>
        /// <param name="document">The XmlDocument to load into</param>
        /// <param name="filePath">The file path to load from</param>
        private static void SafeLoadXml(XmlDocument document, string filePath)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using (var reader = XmlReader.Create(filePath, settings))
            {
                document.Load(reader);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _settingsLock?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
