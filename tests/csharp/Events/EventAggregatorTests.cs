using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using MusicBeePlugin.Events.Infrastructure;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Events
{
    public class EventAggregatorTests
    {
        private readonly EventAggregator _sut;

        public EventAggregatorTests()
        {
            _sut = new EventAggregator();
        }

        public class TestEvent
        {
            public string Message { get; set; }
            public int Value { get; set; }
        }

        public class OtherEvent
        {
            public string Data { get; set; }
        }

        [Fact]
        public void Publish_WithSubscriber_CallsHandler()
        {
            // Arrange
            TestEvent receivedEvent = null;
            _sut.Subscribe<TestEvent>(e => receivedEvent = e);

            var testEvent = new TestEvent { Message = "Hello", Value = 42 };

            // Act
            _sut.Publish(testEvent);

            // Assert
            receivedEvent.Should().NotBeNull();
            receivedEvent.Message.Should().Be("Hello");
            receivedEvent.Value.Should().Be(42);
        }

        [Fact]
        public void Publish_WithMultipleSubscribers_CallsAllHandlers()
        {
            // Arrange
            var receivedEvents = new List<TestEvent>();
            _sut.Subscribe<TestEvent>(e => receivedEvents.Add(e));
            _sut.Subscribe<TestEvent>(e => receivedEvents.Add(e));
            _sut.Subscribe<TestEvent>(e => receivedEvents.Add(e));

            var testEvent = new TestEvent { Message = "Test", Value = 1 };

            // Act
            _sut.Publish(testEvent);

            // Assert
            receivedEvents.Should().HaveCount(3);
            receivedEvents.Should().AllSatisfy(e => e.Should().Be(testEvent));
        }

        [Fact]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            // Arrange
            var testEvent = new TestEvent { Message = "Test", Value = 1 };

            // Act
            Action act = () => _sut.Publish(testEvent);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Publish_WithNullEvent_DoesNotCallHandler()
        {
            // Arrange
            var handlerCalled = false;
            _sut.Subscribe<TestEvent>(e => handlerCalled = true);

            // Act
            _sut.Publish<TestEvent>(null);

            // Assert
            handlerCalled.Should().BeFalse();
        }

        [Fact]
        public void Publish_DifferentEventTypes_OnlyCallsMatchingHandlers()
        {
            // Arrange
            TestEvent receivedTestEvent = null;
            OtherEvent receivedOtherEvent = null;

            _sut.Subscribe<TestEvent>(e => receivedTestEvent = e);
            _sut.Subscribe<OtherEvent>(e => receivedOtherEvent = e);

            var testEvent = new TestEvent { Message = "Test", Value = 1 };

            // Act
            _sut.Publish(testEvent);

            // Assert
            receivedTestEvent.Should().NotBeNull();
            receivedOtherEvent.Should().BeNull();
        }

        [Fact]
        public void Subscribe_WithNullHandler_ThrowsArgumentNullException()
        {
            // Act
            Action act = () => _sut.Subscribe<TestEvent>(null);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Subscribe_ReturnsDisposable_ThatUnsubscribes()
        {
            // Arrange
            var callCount = 0;
            var subscription = _sut.Subscribe<TestEvent>(e => callCount++);
            var testEvent = new TestEvent { Message = "Test", Value = 1 };

            // Act
            _sut.Publish(testEvent);
            subscription.Dispose();
            _sut.Publish(testEvent);

            // Assert
            callCount.Should().Be(1);
        }

        [Fact]
        public async Task PublishAsync_WithSubscriber_CallsHandler()
        {
            // Arrange
            TestEvent receivedEvent = null;
            _sut.Subscribe<TestEvent>(e => receivedEvent = e);

            var testEvent = new TestEvent { Message = "Async", Value = 99 };

            // Act
            await _sut.PublishAsync(testEvent);

            // Assert
            receivedEvent.Should().NotBeNull();
            receivedEvent.Message.Should().Be("Async");
            receivedEvent.Value.Should().Be(99);
        }

        [Fact]
        public async Task PublishAsync_WithAsyncSubscriber_AwaitsHandler()
        {
            // Arrange
            var wasAwaited = false;
            _sut.Subscribe<TestEvent>(async e =>
            {
                await Task.Delay(10);
                wasAwaited = true;
            });

            var testEvent = new TestEvent { Message = "Test", Value = 1 };

            // Act
            await _sut.PublishAsync(testEvent);

            // Assert
            wasAwaited.Should().BeTrue();
        }

        [Fact]
        public async Task PublishAsync_WithMultipleAsyncSubscribers_ExecutesAllConcurrently()
        {
            // Arrange
            var executionOrder = new List<int>();
            var lockObj = new object();

            _sut.Subscribe<TestEvent>(async e =>
            {
                await Task.Delay(30);
                lock (lockObj)
                    executionOrder.Add(1);
            });

            _sut.Subscribe<TestEvent>(async e =>
            {
                await Task.Delay(10);
                lock (lockObj)
                    executionOrder.Add(2);
            });

            _sut.Subscribe<TestEvent>(async e =>
            {
                await Task.Delay(20);
                lock (lockObj)
                    executionOrder.Add(3);
            });

            var testEvent = new TestEvent { Message = "Test", Value = 1 };

            // Act
            await _sut.PublishAsync(testEvent);

            // Assert
            // All three handlers should have executed
            executionOrder.Should().HaveCount(3);
            // All handlers should be represented (order may vary due to timing)
            executionOrder.Should().Contain(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Publish_WhenHandlerThrows_ContinuesWithOtherHandlers()
        {
            // Arrange
            var secondHandlerCalled = false;
            _sut.Subscribe<TestEvent>(e => throw new InvalidOperationException("Test exception"));
            _sut.Subscribe<TestEvent>(e => secondHandlerCalled = true);

            var testEvent = new TestEvent { Message = "Test", Value = 1 };

            // Act
            _sut.Publish(testEvent);

            // Assert
            secondHandlerCalled.Should().BeTrue();
        }

        [Fact]
        public async Task PublishAsync_WhenHandlerThrows_ContinuesWithOtherHandlers()
        {
            // Arrange
            var secondHandlerCalled = false;
            _sut.Subscribe<TestEvent>(async e =>
            {
                await Task.Yield();
                throw new InvalidOperationException("Test exception");
            });
            _sut.Subscribe<TestEvent>(async e =>
            {
                await Task.Yield();
                secondHandlerCalled = true;
            });

            var testEvent = new TestEvent { Message = "Test", Value = 1 };

            // Act
            await _sut.PublishAsync(testEvent);

            // Assert
            secondHandlerCalled.Should().BeTrue();
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            var subscription = _sut.Subscribe<TestEvent>(e => { });

            // Act
            Action act = () =>
            {
                subscription.Dispose();
                subscription.Dispose();
                subscription.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }
    }
}
