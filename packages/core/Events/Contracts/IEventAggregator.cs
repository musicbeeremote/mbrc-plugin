using System;
using System.Threading.Tasks;

namespace MusicBeePlugin.Events.Contracts
{
    /// <summary>
    ///     Defines the contract for an event aggregator that handles event publishing and subscription.
    /// </summary>
    public interface IEventAggregator
    {
        /// <summary>
        ///     Publishes an event to all registered handlers.
        /// </summary>
        /// <typeparam name="T">The type of event to publish.</typeparam>
        /// <param name="eventObj">The event object to publish.</param>
        void Publish<T>(T eventObj) where T : class;

        /// <summary>
        ///     Publishes an event asynchronously to all registered handlers.
        /// </summary>
        /// <typeparam name="T">The type of event to publish.</typeparam>
        /// <param name="eventObj">The event object to publish.</param>
        /// <returns>A task representing the async operation.</returns>
        Task PublishAsync<T>(T eventObj) where T : class;

        /// <summary>
        ///     Subscribes a handler to events of a specific type.
        /// </summary>
        /// <typeparam name="T">The type of event to subscribe to.</typeparam>
        /// <param name="handler">The handler function to execute when an event is published.</param>
        /// <returns>A subscription object that can be disposed to unsubscribe.</returns>
        IDisposable Subscribe<T>(Action<T> handler) where T : class;

        /// <summary>
        ///     Subscribes an async handler to events of a specific type.
        /// </summary>
        /// <typeparam name="T">The type of event to subscribe to.</typeparam>
        /// <param name="handler">The async handler function to execute when an event is published.</param>
        /// <returns>A subscription object that can be disposed to unsubscribe.</returns>
        IDisposable Subscribe<T>(Func<T, Task> handler) where T : class;
    }
}
