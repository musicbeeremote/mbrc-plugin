using System;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Utilities.Network;

namespace MusicBeePlugin.Networking
{
    public class NetworkingManager : INetworkingManager
    {
        private readonly IDisposable _broadcastSubscription;
        private readonly IPluginLogger _logger;
        private readonly IDisposable _messageSendSubscription;
        private readonly IServiceDiscovery _serviceDiscovery;
        private readonly ISocketServer _socketServer;

        public NetworkingManager(
            ISocketServer socketServer,
            IServiceDiscovery serviceDiscovery,
            IEventAggregator eventAggregator,
            IPluginLogger logger)
        {
            _socketServer = socketServer;
            _serviceDiscovery = serviceDiscovery;
            _logger = logger;

            // Subscribe to MessageSendEvent to handle message sending
            _messageSendSubscription = eventAggregator.Subscribe<MessageSendEvent>(HandleMessageSend);

            // Subscribe to BroadcastEvent to handle broadcasting
            _broadcastSubscription = eventAggregator.Subscribe<BroadcastEvent>(HandleBroadcast);
        }

        // Lifecycle Management
        public bool IsRunning => _socketServer.IsRunning;

        public void StartListening()
        {
            _serviceDiscovery.StartListening();
            _socketServer.StartListening();
        }

        public void StopListening()
        {
            _socketServer.StopListening();
            _serviceDiscovery.StopListening();
        }

        public void Restart()
        {
            _socketServer.RestartSocket();
        }

        public void Dispose()
        {
            _messageSendSubscription?.Dispose();
            _broadcastSubscription?.Dispose();
            _socketServer?.Dispose();
            _serviceDiscovery?.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Handle MessageSendEvent by sending messages through the SocketServer.
        ///     Replaces the old ReplayAvailable command pattern.
        /// </summary>
        /// <param name="messageSendEvent">The event containing message and client info</param>
        private void HandleMessageSend(MessageSendEvent messageSendEvent)
        {
            try
            {
                if (string.IsNullOrEmpty(messageSendEvent.Message))
                {
                    _logger.Debug("Skipping empty message send");
                    return;
                }

                // Send via SocketServer (terminator will be added by SendMessageToSocket)
                _socketServer.Send(messageSendEvent.Message, messageSendEvent.ConnectionId);

                _logger.Debug(
                    $"Sent message to connection {messageSendEvent.ConnectionId}: {messageSendEvent.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"Failed to send message to connection {messageSendEvent.ConnectionId}: {messageSendEvent.Message}");
            }
        }

        /// <summary>
        ///     Handle BroadcastEvent by broadcasting through the SocketServer.
        ///     Replaces the old BroadcastEventAvailable command pattern.
        /// </summary>
        /// <param name="broadcastEvent">The event containing broadcast data</param>
        private void HandleBroadcast(BroadcastEvent broadcastEvent)
        {
            try
            {
                _socketServer.Broadcast(broadcastEvent);
                _logger.Debug($"Broadcasted event: {broadcastEvent}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to broadcast event: {broadcastEvent}");
            }
        }
    }
}
