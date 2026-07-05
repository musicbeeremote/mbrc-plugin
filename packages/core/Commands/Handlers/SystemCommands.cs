using System;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Events.Extensions;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Network;

namespace MusicBeePlugin.Commands.Handlers
{
    /// <summary>
    ///     System and connection commands using delegate pattern with dependency injection
    /// </summary>
    public class SystemCommands
    {
        // Client platform string constants
        private const string PlatformAndroid = "Android";
        private const string PlatformIos = "iOS";

        private readonly IAuthenticator _authenticator;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPluginLogger _logger;
        private readonly IUserSettings _userSettings;

        public SystemCommands(
            IPluginLogger logger,
            IEventAggregator eventAggregator,
            IAuthenticator authenticator,
            IUserSettings userSettings)
        {
            _logger = logger;
            _eventAggregator = eventAggregator;
            _authenticator = authenticator;
            _userSettings = userSettings;
        }

        /// <summary>
        ///     Handle protocol request - replaces RequestProtocol
        ///     Supports both legacy string format ("4") and new object format ({"protocol_version": 4, "no_broadcast": false})
        /// </summary>
        public bool HandleProtocol(ITypedCommandContext<ProtocolHandshakeRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "protocol", context.ShortId);

                var client = _authenticator.Client(context.ConnectionId);
                if (client == null)
                {
                    _logger.LogError("Client {ConnectionId} not found in authenticator", context.ConnectionId);
                    return false;
                }

                // Handle different data formats - match original behavior with extensions
                _logger.Debug(
                    "Protocol message received - Data type: {DataType}, Content: {Data}",
                    context.Data?.GetType()?.Name ?? "null", context.Data);

                // Try typed object format first (new complex format with DTO)
                var request = context.TypedData;
                double rawVersion = ProtocolVersionMapper.DefaultVersion;

                if (context.IsValid && request.ProtocolVersion > 0)
                {
                    client.BroadcastsEnabled = !request.NoBroadcast;
                    client.ClientProtocolVersion = ProtocolVersionMapper.MapVersion(request.ProtocolVersion);
                    rawVersion = request.ProtocolVersion;

                    _logger.Debug(
                        "Set client {ConnectionId} protocol version to {ProtocolVersion} (object format)",
                        context.ConnectionId, client.ClientProtocolVersion);

                    if (!string.IsNullOrEmpty(request.ClientId))
                    {
                        client.ClientId = request.ClientId;
                        _logger.Debug("Set client {ClientId} client ID to {NewClientId}", context.ShortId, client.ClientId);
                    }
                }
                else if (ProtocolVersionMapper.TryParse(context.Data, out var clientProtocolVersion, out rawVersion))
                {
                    // Handle simple formats - version number as int, string, or legacy float (2.1, 2.2)
                    client.BroadcastsEnabled = true;
                    client.ClientProtocolVersion = clientProtocolVersion;

                    _logger.Debug(
                        "Set client {ConnectionId} protocol version to {ProtocolVersion} (simple format, raw: {RawVersion})",
                        context.ConnectionId, clientProtocolVersion, rawVersion);
                }
                else
                {
                    // Default to broadcasts enabled if no valid data
                    client.BroadcastsEnabled = true;
                    _logger.Debug(
                        "No valid protocol version found for client {ConnectionId}, keeping default",
                        context.ConnectionId);
                }

                // Set capabilities based on raw version (preserves 2.1, 2.2 feature support)
                client.SetCapabilitiesFromVersion(rawVersion);
                _logger.Debug("Set client {ClientId} capabilities: {Capabilities}", context.ShortId, client.Capabilities);

                _logger.Debug("Updated client: {Client}", client);

                // Send back server protocol version
                _eventAggregator.PublishMessage(
                    ProtocolConstants.Protocol,
                    ProtocolConstants.ProtocolVersion,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute protocol request");
                return false;
            }
        }

        /// <summary>
        ///     Handle player request - replaces RequestPlayer
        /// </summary>
        public bool HandlePlayer(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "player", context.ShortId);

                // Parse and set client platform if provided
                var clientOs = context.GetDataOrDefault<string>() ?? string.Empty;
                var client = _authenticator.Client(context.ConnectionId);
                if (client != null)
                {
                    if (clientOs.Equals(PlatformAndroid, StringComparison.OrdinalIgnoreCase))
                        client.ClientPlatform = ClientOS.Android;
                    else if (clientOs.Equals(PlatformIos, StringComparison.OrdinalIgnoreCase))
                        client.ClientPlatform = ClientOS.iOS;
                    else
                        client.ClientPlatform = ClientOS.Unknown;

                    _logger.Debug("Set client {ClientId} platform to {ClientPlatform}", context.ShortId, client.ClientPlatform);
                }

                // Send back player name
                _eventAggregator.PublishMessage(
                    ProtocolConstants.Player,
                    ProtocolConstants.PlayerName,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute player request");
                return false;
            }
        }

        /// <summary>
        ///     Handle plugin version request - replaces RequestPluginVersion
        /// </summary>
        public bool HandlePluginVersion(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "plugin version", context.ShortId);

                // Send back plugin version
                _eventAggregator.PublishMessage(
                    ProtocolConstants.PluginVersion,
                    _userSettings.CurrentVersion,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute plugin version request");
                return false;
            }
        }

        /// <summary>
        ///     Handle ping request - replaces PingReply
        /// </summary>
        public bool HandlePing(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "ping", context.ShortId);

                // Send back pong response
                _eventAggregator.PublishMessage(
                    ProtocolConstants.Pong,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute ping request");
                return false;
            }
        }

        /// <summary>
        ///     Handle pong response - replaces HandlePong
        /// </summary>
        public bool HandlePong(ICommandContext context)
        {
            try
            {
                _logger.Debug("Pong received from client {ClientId}: {Timestamp}", context.ShortId, DateTime.UtcNow);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle pong");
                return false;
            }
        }
    }
}
