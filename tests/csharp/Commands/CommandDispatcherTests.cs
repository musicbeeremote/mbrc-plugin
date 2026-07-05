using System;
using System.Threading.Tasks;
using FluentAssertions;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeeRemote.Core.Tests.Fixtures;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class CommandDispatcherTests
    {
        private readonly DelegateCommandDispatcher _sut;
        private readonly MockLogger _logger;

        public CommandDispatcherTests()
        {
            _logger = new MockLogger();
            _sut = new DelegateCommandDispatcher(_logger);
        }

        [Fact]
        public void Execute_RegisteredCommand_ReturnsTrue()
        {
            // Arrange
            var handlerCalled = false;
            _sut.RegisterCommand("test.command", ctx =>
            {
                handlerCalled = true;
                return true;
            });

            var context = new TestCommandContext("test.command");

            // Act
            var result = _sut.Execute(context);

            // Assert
            result.Should().BeTrue();
            handlerCalled.Should().BeTrue();
        }

        [Fact]
        public void Execute_UnregisteredCommand_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("unknown.command");

            // Act
            var result = _sut.Execute(context);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void Execute_NullContext_ReturnsFalse()
        {
            // Act
            var result = _sut.Execute(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void Execute_NullCommandType_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext(null);

            // Act
            var result = _sut.Execute(context);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void Execute_EmptyCommandType_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext(string.Empty);

            // Act
            var result = _sut.Execute(context);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void Execute_HandlerThrowsException_ReturnsFalseAndLogsError()
        {
            // Arrange
            _sut.RegisterCommand("error.command", (DelegateCommandDispatcher.SyncCommandHandler)(ctx =>
            {
                throw new InvalidOperationException("Test exception");
            }));

            var context = new TestCommandContext("error.command");

            // Act
            var result = _sut.Execute(context);

            // Assert
            result.Should().BeFalse();
            _logger.ErrorMessages.Should().NotBeEmpty();
        }

        [Fact]
        public void Execute_PassesContextToHandler()
        {
            // Arrange
            string receivedCommandType = null;
            object receivedData = null;

            _sut.RegisterCommand("data.command", ctx =>
            {
                receivedCommandType = ctx.CommandType;
                receivedData = ctx.Data;
                return true;
            });

            var context = new TestCommandContext("data.command", "test-data");

            // Act
            _sut.Execute(context);

            // Assert
            receivedCommandType.Should().Be("data.command");
            receivedData.Should().Be("test-data");
        }

        [Fact]
        public async Task ExecuteAsync_RegisteredCommand_ReturnsTrue()
        {
            // Arrange
            var handlerCalled = false;
            _sut.RegisterCommand("async.command", ctx =>
            {
                handlerCalled = true;
                return true;
            });

            var context = new TestCommandContext("async.command");

            // Act
            var result = await _sut.ExecuteAsync(context);

            // Assert
            result.Should().BeTrue();
            handlerCalled.Should().BeTrue();
        }

        [Fact]
        public async Task ExecuteAsync_UnregisteredCommand_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("unknown.command");

            // Act
            var result = await _sut.ExecuteAsync(context);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ExecuteAsync_NullContext_ReturnsFalse()
        {
            // Act
            var result = await _sut.ExecuteAsync(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HasCommand_RegisteredCommand_ReturnsTrue()
        {
            // Arrange
            _sut.RegisterCommand("existing.command", ctx => true);

            // Act
            var result = _sut.HasCommand("existing.command");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void HasCommand_UnregisteredCommand_ReturnsFalse()
        {
            // Act
            var result = _sut.HasCommand("nonexistent.command");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HasCommand_NullCommandId_ReturnsFalse()
        {
            // Act
            var result = _sut.HasCommand(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HasCommand_EmptyCommandId_ReturnsFalse()
        {
            // Act
            var result = _sut.HasCommand(string.Empty);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void RegisterCommand_NullCommandId_ThrowsArgumentException()
        {
            // Act
            Action act = () => _sut.RegisterCommand(null, ctx => true);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void RegisterCommand_EmptyCommandId_ThrowsArgumentException()
        {
            // Act
            Action act = () => _sut.RegisterCommand(string.Empty, ctx => true);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void RegisterCommand_NullHandler_ThrowsArgumentNullException()
        {
            // Act
            Action act = () => _sut.RegisterCommand("test.command", (DelegateCommandDispatcher.SyncCommandHandler)null);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void RegisterCommand_SameCommandTwice_OverwritesPrevious()
        {
            // Arrange
            var firstHandlerCalled = false;
            var secondHandlerCalled = false;

            _sut.RegisterCommand("override.command", ctx =>
            {
                firstHandlerCalled = true;
                return true;
            });

            _sut.RegisterCommand("override.command", ctx =>
            {
                secondHandlerCalled = true;
                return true;
            });

            var context = new TestCommandContext("override.command");

            // Act
            _sut.Execute(context);

            // Assert
            firstHandlerCalled.Should().BeFalse();
            secondHandlerCalled.Should().BeTrue();
        }

        [Fact]
        public void CommandCount_ReturnsCorrectCount()
        {
            // Arrange
            _sut.RegisterCommand("command1", ctx => true);
            _sut.RegisterCommand("command2", ctx => true);
            _sut.RegisterCommand("command3", ctx => true);

            // Act
            var count = _sut.CommandCount;

            // Assert
            count.Should().Be(3);
        }
    }
}
