using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;

namespace MusicBeePlugin.Utilities.Network
{
    /// <summary>
    ///     Implementation of protocol capability checks.
    ///     Uses capability flags stored on the client during protocol handshake.
    ///     Supports legacy float versions (2.1, 2.2) with their specific feature sets.
    /// </summary>
    public class ProtocolCapabilities : IProtocolCapabilities
    {
        private readonly IAuthenticator _authenticator;

        public ProtocolCapabilities(IAuthenticator authenticator)
        {
            _authenticator = authenticator;
        }

        /// <inheritdoc />
        public ClientOS GetClientPlatform(string connectionId)
        {
            return _authenticator.Client(connectionId)?.ClientPlatform ?? ClientOS.Unknown;
        }

        /// <summary>
        /// Gets the capabilities for a client, or default capabilities if not found.
        /// </summary>
        private ClientCapabilities GetCapabilities(string connectionId)
        {
            return _authenticator.Client(connectionId)?.Capabilities ?? new ClientCapabilities();
        }

        /// <inheritdoc />
        public bool SupportsPayloadObjects(string connectionId)
        {
            return GetCapabilities(connectionId).SupportsPayloadObjects;
        }

        /// <inheritdoc />
        public bool SupportsPagination(string connectionId)
        {
            return GetCapabilities(connectionId).SupportsPagination;
        }

        /// <inheritdoc />
        public bool SupportsAutoDjShuffle(string connectionId)
        {
            return GetCapabilities(connectionId).SupportsAutoDjShuffle;
        }

        /// <inheritdoc />
        public bool SupportsFullPlayerStatus(string connectionId)
        {
            return GetCapabilities(connectionId).SupportsFullPlayerStatus;
        }
    }
}
