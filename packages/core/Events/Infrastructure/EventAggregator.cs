using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MusicBeePlugin.Events.Contracts;

namespace MusicBeePlugin.Events.Infrastructure
{
    /// <summary>
    ///     Thread-safe event aggregator implementation for publishing and subscribing to events.
    /// </summary>
    public class EventAggregator : IEventAggregator
    {
        private readonly ConcurrentDictionary<Type, ConcurrentBag<IEventSubscription>> _subscriptions
            = new ConcurrentDictionary<Type, ConcurrentBag<IEventSubscription>>();
        private readonly ConcurrentDictionary<Type, int> _disposedCounts
            = new ConcurrentDictionary<Type, int>();
        private const int CleanupThreshold = 10;

        /// <summary>
        ///     Publishes an event to all registered handlers synchronously.
        /// </summary>
        /// <typeparam name="T">The type of event to publish.</typeparam>
        /// <param name="eventObj">The event object to publish.</param>
        public void Publish<T>(T eventObj) where T : class
        {
            if (eventObj == null)
                return;

            var eventType = typeof(T);
            if (!_subscriptions.TryGetValue(eventType, out var subscriptions))
                return;

            var disposedCount = 0;
            foreach (var subscription in subscriptions)
            {
                if (subscription.IsDisposed)
                {
                    disposedCount++;
                    continue;
                }

                try
                {
                    subscription.Handle(eventObj);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling event: {ex}");
                }
            }

            // Only clean up when disposed count exceeds threshold
            if (disposedCount > 0)
            {
                var totalDisposed = _disposedCounts.AddOrUpdate(eventType, disposedCount, (k, v) => v + disposedCount);
                if (totalDisposed >= CleanupThreshold)
                {
                    CleanupDisposedSubscriptions(eventType);
                    _disposedCounts.TryUpdate(eventType, 0, totalDisposed);
                }
            }
        }

        /// <summary>
        ///     Publishes an event to all registered handlers asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of event to publish.</typeparam>
        /// <param name="eventObj">The event object to publish.</param>
        /// <returns>A task representing the async operation.</returns>
        public async Task PublishAsync<T>(T eventObj) where T : class
        {
            if (eventObj == null)
                return;

            var eventType = typeof(T);
            if (!_subscriptions.TryGetValue(eventType, out var subscriptions))
                return;

            // Count disposed and create tasks for active subscriptions
            var disposedCount = 0;
            var tasks = new System.Collections.Generic.List<Task>();

            foreach (var subscription in subscriptions)
            {
                if (subscription.IsDisposed)
                {
                    disposedCount++;
                }
                else
                {
                    tasks.Add(SafeHandleAsync(subscription, eventObj));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }

            // Only clean up when disposed count exceeds threshold
            if (disposedCount > 0)
            {
                var totalDisposed = _disposedCounts.AddOrUpdate(eventType, disposedCount, (k, v) => v + disposedCount);
                if (totalDisposed >= CleanupThreshold)
                {
                    CleanupDisposedSubscriptions(eventType);
                    _disposedCounts.TryUpdate(eventType, 0, totalDisposed);
                }
            }
        }

        private static async Task SafeHandleAsync(IEventSubscription subscription, object eventObj)
        {
            try
            {
                await subscription.HandleAsync(eventObj);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling event async: {ex}");
            }
        }

        /// <summary>
        ///     Subscribes a synchronous handler to events of a specific type.
        /// </summary>
        /// <typeparam name="T">The type of event to subscribe to.</typeparam>
        /// <param name="handler">The handler function to execute when an event is published.</param>
        /// <returns>A subscription object that can be disposed to unsubscribe.</returns>
        public IDisposable Subscribe<T>(Action<T> handler) where T : class
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var eventType = typeof(T);
            var subscription = new EventSubscription<T>(handler);

            _subscriptions.AddOrUpdate(
                eventType,
                new ConcurrentBag<IEventSubscription> { subscription },
                (key, existing) =>
                {
                    existing.Add(subscription);
                    return existing;
                });

            return subscription;
        }

        /// <summary>
        ///     Subscribes an asynchronous handler to events of a specific type.
        /// </summary>
        /// <typeparam name="T">The type of event to subscribe to.</typeparam>
        /// <param name="handler">The async handler function to execute when an event is published.</param>
        /// <returns>A subscription object that can be disposed to unsubscribe.</returns>
        public IDisposable Subscribe<T>(Func<T, Task> handler) where T : class
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var eventType = typeof(T);
            var subscription = new AsyncEventSubscription<T>(handler);

            _subscriptions.AddOrUpdate(
                eventType,
                new ConcurrentBag<IEventSubscription> { subscription },
                (key, existing) =>
                {
                    existing.Add(subscription);
                    return existing;
                });

            return subscription;
        }

        private void CleanupDisposedSubscriptions(Type eventType)
        {
            if (!_subscriptions.TryGetValue(eventType, out var subscriptions))
                return;

            var activeSubscriptions = subscriptions.Where(s => !s.IsDisposed).ToList();
            if (activeSubscriptions.Count != subscriptions.Count)
                _subscriptions.TryUpdate(eventType, new ConcurrentBag<IEventSubscription>(activeSubscriptions),
                    subscriptions);
        }
    }

    /// <summary>
    ///     Internal interface for event subscriptions.
    /// </summary>
    internal interface IEventSubscription : IDisposable
    {
        bool IsDisposed { get; }
        void Handle(object eventObj);
        Task HandleAsync(object eventObj);
    }

    /// <summary>
    ///     Synchronous event subscription implementation.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    internal sealed class EventSubscription<T> : IEventSubscription where T : class
    {
        private readonly Action<T> _handler;
        private volatile bool _isDisposed;

        public EventSubscription(Action<T> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool IsDisposed => _isDisposed;

        public void Handle(object eventObj)
        {
            if (_isDisposed || !(eventObj is T typedEvent))
                return;
            _handler(typedEvent);
        }

        public Task HandleAsync(object eventObj)
        {
            Handle(eventObj);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    ///     Asynchronous event subscription implementation.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    internal sealed class AsyncEventSubscription<T> : IEventSubscription where T : class
    {
        private readonly Func<T, Task> _handler;
        private volatile bool _isDisposed;

        public AsyncEventSubscription(Func<T, Task> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool IsDisposed => _isDisposed;

        public void Handle(object eventObj)
        {
            if (_isDisposed || !(eventObj is T typedEvent))
                return;
            _handler(typedEvent).GetAwaiter().GetResult();
        }

        public async Task HandleAsync(object eventObj)
        {
            if (_isDisposed || !(eventObj is T typedEvent))
                return;
            await _handler(typedEvent);
        }

        public void Dispose()
        {
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
