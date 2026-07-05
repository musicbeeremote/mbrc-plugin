using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Configuration;
using MusicBeePlugin.Services.Configuration;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Services
{
    public class UserSettingsServiceTests : IDisposable
    {
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly MockLogger _logger;
        private readonly UserSettingsService _sut;
        private readonly string _testStoragePath;

        public UserSettingsServiceTests()
        {
            _eventAggregator = new Mock<IEventAggregator>();
            _logger = new MockLogger();
            _sut = new UserSettingsService(_eventAggregator.Object, _logger);

            // Use a unique temp directory for each test
            _testStoragePath = Path.Combine(Path.GetTempPath(), $"mbrc_test_{Guid.NewGuid():N}");
        }

        public void Dispose()
        {
            _sut?.Dispose();
            // Clean up test directory
            if (Directory.Exists(_testStoragePath))
            {
                try
                {
                    Directory.Delete(_testStoragePath, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }

            GC.SuppressFinalize(this);
        }

        #region 6.1 Constructor and Defaults

        [Fact]
        public void Constructor_InitializesWithDefaults()
        {
            // Assert
            _sut.ListeningPort.Should().Be(3000);
            _sut.FilterSelection.Should().Be(FilteringSelection.All);
            _sut.BaseIp.Should().BeEmpty();
            _sut.LastOctetMax.Should().Be(254);
            _sut.IpAddressList.Should().BeEmpty();
            _sut.Source.Should().Be(SearchSource.Library);
            _sut.AlternativeSearch.Should().BeFalse();
            _sut.DebugLogEnabled.Should().BeFalse();
            _sut.UpdateFirewall.Should().BeFalse();
            _sut.CurrentVersion.Should().BeEmpty();
        }

        #endregion

        #region 6.2 SetStoragePath

        [Fact]
        public void SetStoragePath_SetsCorrectPath()
        {
            // Arrange
            var basePath = Path.GetTempPath();

            // Act
            _sut.SetStoragePath(basePath);

            // Assert
            _sut.StoragePath.Should().Be(Path.Combine(basePath, "mb_remote"));
        }

        [Fact]
        public void FullLogPath_ReturnsCorrectPath()
        {
            // Arrange
            var basePath = Path.GetTempPath();
            _sut.SetStoragePath(basePath);

            // Act
            var logPath = _sut.FullLogPath;

            // Assert
            logPath.Should().Be(Path.Combine(basePath, "mb_remote", "mbrc.log"));
        }

        #endregion

        #region 6.3 UpdateSettings and GetSettingsModel

        [Fact]
        public void UpdateSettings_StoresNewSettings()
        {
            // Arrange
            var newSettings = new UserSettingsModel
            {
                ListeningPort = 5000,
                FilterSelection = FilteringSelection.Range,
                BaseIp = "192.168.1",
                LastOctetMax = 100,
                Source = SearchSource.Inbox,
                DebugLogEnabled = true
            };

            // Act
            _sut.UpdateSettings(newSettings);

            // Assert
            _sut.ListeningPort.Should().Be(5000);
            _sut.FilterSelection.Should().Be(FilteringSelection.Range);
            _sut.BaseIp.Should().Be("192.168.1");
            _sut.LastOctetMax.Should().Be(100);
            _sut.Source.Should().Be(SearchSource.Inbox);
            _sut.DebugLogEnabled.Should().BeTrue();
        }

        [Fact]
        public void UpdateSettings_NullSettings_ThrowsArgumentNullException()
        {
            // Act
            Action action = () => _sut.UpdateSettings(null);

            // Assert
            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void GetSettingsModel_ReturnsClonedSettings()
        {
            // Arrange
            var settings = new UserSettingsModel
            {
                ListeningPort = 8080,
                IpAddressList = new List<string> { "192.168.1.1", "192.168.1.2" }
            };
            _sut.UpdateSettings(settings);

            // Act
            var retrieved = _sut.GetSettingsModel();

            // Assert
            retrieved.Should().NotBeNull();
            retrieved.ListeningPort.Should().Be(8080);
            retrieved.IpAddressList.Should().HaveCount(2);
        }

        [Fact]
        public void GetSettingsModel_ReturnsIndependentClone()
        {
            // Arrange
            var settings = new UserSettingsModel
            {
                ListeningPort = 8080,
                IpAddressList = new List<string> { "192.168.1.1" }
            };
            _sut.UpdateSettings(settings);

            // Act
            var retrieved = _sut.GetSettingsModel();
            retrieved.ListeningPort = 9999;
            retrieved.IpAddressList.Add("10.0.0.1");

            // Assert - original should be unchanged
            _sut.ListeningPort.Should().Be(8080);
            _sut.IpAddressList.Should().HaveCount(1);
        }

        #endregion

        #region 6.4 Thread Safety

        [Fact]
        public Task Properties_AreThreadSafe()
        {
            // Arrange
            var settings = new UserSettingsModel { ListeningPort = 3000 };
            _sut.UpdateSettings(settings);

            // Act - concurrent reads should not throw
            var tasks = new Task[10];
            for (var i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    for (var j = 0; j < 100; j++)
                    {
                        var port = _sut.ListeningPort;
                        var filter = _sut.FilterSelection;
                        var source = _sut.Source;
                        var debug = _sut.DebugLogEnabled;
                    }
                });
            }

            // Assert - should complete without deadlock or exception
            return Task.WhenAll(tasks);
        }

        [Fact]
        public async Task ConcurrentReadsAndWrites_DoNotDeadlock()
        {
            // Act
            var readTask = Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    var port = _sut.ListeningPort;
                    var model = _sut.GetSettingsModel();
                }
            });

            var writeTask = Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    var settings = new UserSettingsModel { ListeningPort = (uint)(3000 + i) };
                    _sut.UpdateSettings(settings);
                }
            });

            // Assert - should complete without deadlock (timeout after 5 seconds)
            var allTasks = Task.WhenAll(readTask, writeTask);
            var completedTask = await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(5)));
            completedTask.Should().Be(allTasks, "concurrent reads and writes should complete without deadlock");
        }

        #endregion

        #region 6.5 IpAddressList

        [Fact]
        public void IpAddressList_ReturnsReadOnlyList()
        {
            // Arrange
            var settings = new UserSettingsModel
            {
                IpAddressList = new List<string> { "192.168.1.1", "192.168.1.2" }
            };
            _sut.UpdateSettings(settings);

            // Act
            var list = _sut.IpAddressList;

            // Assert
            list.Should().BeAssignableTo<IReadOnlyList<string>>();
            list.Should().HaveCount(2);
            list.Should().Contain("192.168.1.1");
            list.Should().Contain("192.168.1.2");
        }

        #endregion

        #region 6.6 Load and Save Settings (File I/O Tests)

        [Fact]
        public void LoadSettings_NoFile_UsesDefaults()
        {
            // Arrange
            _sut.SetStoragePath(_testStoragePath);

            // Act
            _sut.LoadSettings();

            // Assert
            _sut.ListeningPort.Should().Be(3000);
            _sut.FilterSelection.Should().Be(FilteringSelection.All);
        }

        [Fact]
        public void SaveSettings_CreatesDirectoryIfNotExists()
        {
            // Arrange
            var uniquePath = Path.Combine(Path.GetTempPath(), $"mbrc_save_test_{Guid.NewGuid():N}");
            _sut.SetStoragePath(uniquePath);

            try
            {
                // Act
                _sut.SaveSettings();

                // Assert
                Directory.Exists(_sut.StoragePath).Should().BeTrue();
                _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(uniquePath))
                    Directory.Delete(uniquePath, true);
            }
        }

        [Fact]
        public void LoadSettings_AfterSave_RestoresSettings()
        {
            // Arrange
            var uniquePath = Path.Combine(Path.GetTempPath(), $"mbrc_roundtrip_test_{Guid.NewGuid():N}");
            _sut.SetStoragePath(uniquePath);

            var settings = new UserSettingsModel
            {
                ListeningPort = 9999,
                DebugLogEnabled = true,
                Source = SearchSource.Inbox,
                FilterSelection = FilteringSelection.All
            };
            _sut.UpdateSettings(settings);

            try
            {
                // Act
                _sut.SaveSettings();
                _sut.UpdateSettings(new UserSettingsModel()); // Reset to defaults
                _sut.LoadSettings();

                // Assert
                _sut.ListeningPort.Should().Be(9999);
                _sut.DebugLogEnabled.Should().BeTrue();
                _sut.Source.Should().Be(SearchSource.Inbox);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(uniquePath))
                    Directory.Delete(uniquePath, true);
            }
        }

        #endregion

        #region 6.7 IsFirstRun

        [Fact]
        public void IsFirstRun_NoSettingsFile_ReturnsTrue()
        {
            // Arrange
            var uniquePath = Path.Combine(Path.GetTempPath(), $"mbrc_firstrun_test_{Guid.NewGuid():N}");
            _sut.SetStoragePath(uniquePath);

            try
            {
                // Act
                var result = _sut.IsFirstRun();

                // Assert
                result.Should().BeTrue();
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(uniquePath))
                    Directory.Delete(uniquePath, true);
            }
        }

        [Fact]
        public void IsFirstRun_AfterFirstRun_ReturnsFalse()
        {
            // Arrange
            var uniquePath = Path.Combine(Path.GetTempPath(), $"mbrc_secondrun_test_{Guid.NewGuid():N}");
            _sut.SetStoragePath(uniquePath);

            // Set a non-empty version so IsFirstRun writes a valid version marker
            var settings = new UserSettingsModel { CurrentVersion = "1.0.0" };
            _sut.UpdateSettings(settings);

            try
            {
                // Act
                var firstResult = _sut.IsFirstRun();
                var secondResult = _sut.IsFirstRun();

                // Assert
                firstResult.Should().BeTrue();
                secondResult.Should().BeFalse();
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(uniquePath))
                    Directory.Delete(uniquePath, true);
            }
        }

        #endregion
    }
}
