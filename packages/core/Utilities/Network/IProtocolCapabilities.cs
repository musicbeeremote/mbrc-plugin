using MusicBeePlugin.Networking;

namespace MusicBeePlugin.Utilities.Network
{
    /// <summary>
    ///     Provides semantic capability checks for client protocol versions.
    ///     Centralizes all version-dependent behavior decisions.
    ///
    ///     V3 is the first integer protocol version and consolidates all features
    ///     from the earlier float-based versions (2.1, 2.2). All V3+ capability
    ///     checks share the same threshold.
    /// </summary>
    public interface IProtocolCapabilities
    {
        /// <summary>
        ///     Gets the client platform (Android/iOS/Unknown).
        /// </summary>
        ClientOS GetClientPlatform(string connectionId);

        /// <summary>
        ///     Checks if the client supports payload wrapper objects (CoverPayload, LyricsPayload).
        ///     Available in V3+.
        /// </summary>
        bool SupportsPayloadObjects(string connectionId);

        /// <summary>
        ///     Checks if the client supports paginated list responses.
        ///     Available in V3+ (originally introduced in Protocol 2.2).
        /// </summary>
        bool SupportsPagination(string connectionId);

        /// <summary>
        ///     Checks if the client supports AutoDJ shuffle states (Off, Shuffle, AutoDJ).
        ///     Available in V3+ (originally introduced in Protocol 2.1).
        /// </summary>
        bool SupportsAutoDjShuffle(string connectionId);

        /// <summary>
        ///     Checks if the client supports full player status with ShuffleState format.
        ///     Available in V3+. Legacy clients (V2) receive boolean shuffle instead.
        /// </summary>
        bool SupportsFullPlayerStatus(string connectionId);
    }
}
