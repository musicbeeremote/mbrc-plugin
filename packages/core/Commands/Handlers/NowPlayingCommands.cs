using System;
using System.Globalization;
using System.Linq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Events.Extensions;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Commands;
using MusicBeePlugin.Models.Configuration;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Utilities.Data;
using MusicBeePlugin.Utilities.Network;

namespace MusicBeePlugin.Commands.Handlers
{
    /// <summary>
    ///     Now playing commands using delegate pattern with dependency injection
    /// </summary>
    public class NowPlayingCommands
    {
        // Queue type string constants
        private const string QueueTypeNext = "next";
        private const string QueueTypeLast = "last";
        private const string QueueTypeNow = "now";
        private const string QueueTypePlayNow = "playnow";
        private const string QueueTypeAddAll = "add-all";
        private const string QueueTypeAddAndPlay = "addandplay";

        // Last.fm action string constants
        private const string LastfmActionToggle = "toggle";
        private const string LastfmActionLove = "love";
        private const string LastfmActionBan = "ban";

        private readonly ITrackDataProvider _trackDataProvider;
        private readonly IPlaylistDataProvider _playlistDataProvider;
        private readonly IPlayerDataProvider _playerDataProvider;
        private readonly IBroadcaster _broadcaster;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPluginLogger _logger;
        private readonly LyricCoverModel _lyricCoverModel;
        private readonly IProtocolCapabilities _protocolCapabilities;
        private readonly IUserSettings _userSettings;

        public NowPlayingCommands(
            ITrackDataProvider trackDataProvider,
            IPlaylistDataProvider playlistDataProvider,
            IPlayerDataProvider playerDataProvider,
            IPluginLogger logger,
            IEventAggregator eventAggregator,
            LyricCoverModel lyricCoverModel,
            IBroadcaster broadcaster,
            IUserSettings userSettings,
            IProtocolCapabilities protocolCapabilities)
        {
            _trackDataProvider = trackDataProvider;
            _playlistDataProvider = playlistDataProvider;
            _playerDataProvider = playerDataProvider;
            _logger = logger;
            _eventAggregator = eventAggregator;
            _lyricCoverModel = lyricCoverModel;
            _broadcaster = broadcaster;
            _userSettings = userSettings;
            _protocolCapabilities = protocolCapabilities;
        }

        /// <summary>
        ///     Handle now playing track info request - replaces RequestSongInfo
        /// </summary>
        public bool HandleTrackInfo(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "track info", context.ShortId);

                object trackData;
                if (_protocolCapabilities.SupportsPayloadObjects(context.ConnectionId))
                {
                    // Use NowPlayingTrackV2 for newer clients (V3+)
                    trackData = _trackDataProvider.GetNowPlayingTrackInfo();
                }
                else
                {
                    // For older clients, we need to convert to NowPlayingTrack
                    var trackV2 = _trackDataProvider.GetNowPlayingTrackInfo();
                    trackData = ConvertToLegacyTrack(trackV2);
                }

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingTrack,
                    trackData,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute track info request");
                return false;
            }
        }

        /// <summary>
        ///     Handle now playing track details request - replaces RequestSongDetails
        /// </summary>
        public bool HandleTrackDetails(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "track details", context.ShortId);

                var trackDetails = _trackDataProvider.GetNowPlayingTrackDetails();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingDetails,
                    trackDetails,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute track details request");
                return false;
            }
        }

        /// <summary>
        ///     Handle now playing position request - gets or sets playback position
        /// </summary>
        public bool HandlePlaybackPosition(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "playback position", context.ShortId);

                var positionChanged = false;

                // Try to parse position as int (handles both direct int and string representations)
                if (context.TryGetData<int>(out var position))
                {
                    _logger.Debug("Setting playback position to {Position}ms", position);
                    _playerDataProvider.SetPosition(position);
                    positionChanged = true;
                }

                var playbackPosition = _trackDataProvider.GetPlaybackPosition();

                if (positionChanged)
                {
                    // Broadcast to all clients when position changes (global state)
                    var message = MessageSendEvent.Create(ProtocolConstants.NowPlayingPosition, playbackPosition);
                    _eventAggregator.Publish(message);
                }
                else
                {
                    // Query only - respond to requesting client only
                    _eventAggregator.PublishMessage(
                        ProtocolConstants.NowPlayingPosition,
                        playbackPosition,
                        context.ConnectionId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute playback position request");
                return false;
            }
        }

        /// <summary>
        ///     Handle now playing cover request
        /// </summary>
        public bool HandleCover(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "cover", context.ShortId);

                object coverData;
                if (_protocolCapabilities.SupportsPayloadObjects(context.ConnectionId))
                {
                    // V3+ uses CoverPayload
                    coverData = new CoverPayload(_lyricCoverModel.Cover, true);
                }
                else
                {
                    // V2 uses raw cover data
                    coverData = _lyricCoverModel.Cover;
                }

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingCover,
                    coverData,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute cover request");
                return false;
            }
        }

        /// <summary>
        ///     Handle now playing lyrics request
        /// </summary>
        public bool HandleLyrics(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "lyrics", context.ShortId);

                var lyrics = _trackDataProvider.GetNowPlayingLyrics();

                // Update LyricCoverModel cache (this will also broadcast to all clients)
                _lyricCoverModel.Lyrics = lyrics;

                // Send protocol-specific response to requesting client
                object lyricsData;
                if (_protocolCapabilities.SupportsPayloadObjects(context.ConnectionId))
                {
                    // V3+ uses LyricsPayload
                    lyricsData = new LyricsPayload(lyrics);
                }
                else
                {
                    // V2 uses raw lyrics or "Lyrics Not Found" message
                    lyricsData = !string.IsNullOrEmpty(lyrics) ? lyrics : "Lyrics Not Found";
                }

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingLyrics,
                    lyricsData,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute lyrics request");
                return false;
            }
        }

        /// <summary>
        ///     Handle now playing list remove track request - replaces RequestNowPlayingTrackRemoval
        /// </summary>
        public bool HandleRemoveTrack(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "remove track", context.ShortId);

                // Parse the index from event data
                if (!context.TryGetData<int>(out var index) || index < 0)
                {
                    _logger.Warn("Invalid index parameter: {Data}", context.Data);
                    return false;
                }

                // Perform the remove operation
                var success = _playlistDataProvider.RemoveFromNowPlayingList(index);

                // Create response matching original format
                var reply = new RemoveTrackResponse(success, index);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingListRemove,
                    reply,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute remove track request");
                return false;
            }
        }

        /// <summary>
        ///     Handle now playing list move track request - replaces RequestNowPlayingMoveTrack
        /// </summary>
        public bool HandleMoveTrack(ITypedCommandContext<MoveTrackRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "move track", context.ShortId);

                // Validate typed request
                if (!context.IsValid)
                {
                    _logger.Warn("Invalid data format for move track request or missing parameters");
                    return false;
                }

                var request = context.TypedData;
                var from = request.From.Value;
                var to = request.To.Value;

                if (from < 0 || to < 0)
                {
                    _logger.Warn("Invalid position parameters: from={From}, to={To}", from, to);
                    return false;
                }

                // Perform the move operation
                var success = _playlistDataProvider.MoveNowPlayingTrack(from, to);

                // Create response matching original format
                var reply = new MoveTrackResponse(success, from, to);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingListMove,
                    reply,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute move track request");
                return false;
            }
        }

        /// <summary>
        ///     Handle now playing list search request - replaces RequestNowPlayingSearch
        /// </summary>
        public bool HandleSearch(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "search", context.ShortId);

                var query = context.GetDataOrDefault<string>();
                if (string.IsNullOrWhiteSpace(query))
                {
                    _logger.Warn("Search query is empty");
                    return false;
                }

                // Search for matching track in now playing list
                var searchSource = SearchSourceHelper.GetSearchSource(_userSettings);
                var matchingTrack = _trackDataProvider.SearchNowPlayingList(query, searchSource);

                var success = false;
                if (!string.IsNullOrEmpty(matchingTrack))
                    // Play the found track
                    success = _playlistDataProvider.PlayNowPlayingTrack(matchingTrack);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingListSearch,
                    success,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute search request");
                return false;
            }
        }

        /// <summary>
        ///     Handle now playing queue request - replaces RequestNowPlayingQueue
        /// </summary>
        public bool HandleQueue(ITypedCommandContext<QueueRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "queue", context.ShortId);

                if (!context.IsValid)
                {
                    _logger.Warn("Invalid queue request payload");
                    SendQueueResponse(context.ConnectionId, 400);
                    return false;
                }

                var request = context.TypedData;
                if (request.Data == null || request.Data.Count == 0)
                {
                    _logger.Warn("Queue request has no data");
                    SendQueueResponse(context.ConnectionId, 400);
                    return false;
                }

                var queueTypeString = request.Queue ?? QueueTypeNow;
                var play = request.Play;
                var fileUrls = request.Data.Where(s => !string.IsNullOrEmpty(s)).ToArray();

                if (fileUrls.Length == 0)
                {
                    _logger.Warn("Queue request has no valid file URLs");
                    SendQueueResponse(context.ConnectionId, 400);
                    return false;
                }

                var queueType = ParseQueueType(queueTypeString);
                var success = _playlistDataProvider.QueueFiles(queueType, fileUrls, play);
                SendQueueResponse(context.ConnectionId, success ? 200 : 500);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute queue request");
                SendQueueResponse(context.ConnectionId, 500);
                return false;
            }
        }

        private void SendQueueResponse(string clientId, int code)
        {
            var queueResponse = new QueueResponse { Code = code };
            _eventAggregator.PublishMessage(
                ProtocolConstants.NowPlayingQueue,
                queueResponse,
                clientId);
        }

        /// <summary>
        ///     Handle now playing list play request - replaces RequestNowPlayingPlay
        /// </summary>
        public bool HandleNowPlayingListPlay(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "now playing list play", context.ShortId);

                if (!context.TryGetData<int>(out var trackIndex))
                {
                    _logger.Warn("Invalid track index: {Data}", context.Data);
                    SendNowPlayingListPlayResponse(context.ConnectionId, false);
                    return false;
                }

                // Check if client is Android and adjust index accordingly
                var isAndroid = _protocolCapabilities.GetClientPlatform(context.ConnectionId) == ClientOS.Android;
                var adjustedIndex = isAndroid ? trackIndex - 1 : trackIndex;

                // Play the track at the specified index
                var success = _playlistDataProvider.PlayNowPlayingByIndex(adjustedIndex);
                SendNowPlayingListPlayResponse(context.ConnectionId, success);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute now playing list play request");
                SendNowPlayingListPlayResponse(context.ConnectionId, false);
                return false;
            }
        }

        private void SendNowPlayingListPlayResponse(string clientId, bool success)
        {
            _eventAggregator.PublishMessage(
                ProtocolConstants.NowPlayingListPlay,
                success,
                clientId);
        }

        private static QueueType ParseQueueType(string queueTypeString)
        {
            switch (queueTypeString?.ToLower(CultureInfo.InvariantCulture))
            {
                case QueueTypeNext:
                    return QueueType.Next;
                case QueueTypeLast:
                    return QueueType.Last;
                case QueueTypeNow:
                case QueueTypePlayNow:
                    return QueueType.PlayNow;
                case QueueTypeAddAll:
                case QueueTypeAddAndPlay:
                    return QueueType.AddAndPlay;
                default:
                    return QueueType.PlayNow;
            }
        }

        /// <summary>
        ///     Handle now playing list request - replaces RequestNowPlayingList
        /// </summary>
        public bool HandleNowPlayingList(ITypedCommandContext<PaginationRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "now playing list", context.ShortId);

                var paginationRequest = context.TypedData;
                var supportsPagination = _protocolCapabilities.SupportsPagination(context.ConnectionId);

                if (!supportsPagination || paginationRequest == null)
                {
                    // Legacy response for older clients
                    var trackList = _trackDataProvider.GetNowPlayingListLegacy();
                    _eventAggregator.PublishMessage(
                        ProtocolConstants.NowPlayingList,
                        trackList,
                        context.ConnectionId);
                }
                else
                {
                    // Paged response for newer clients
                    var offset = paginationRequest.Offset;
                    var limit = paginationRequest.Limit > 0 ? paginationRequest.Limit : 100;

                    var isAndroid = _protocolCapabilities.GetClientPlatform(context.ConnectionId) == ClientOS.Android;
                    var tracks = isAndroid
                        ? _trackDataProvider.GetNowPlayingListPage(offset, limit).ToList()
                        : _trackDataProvider.GetNowPlayingListOrdered(offset, limit).ToList();

                    var message =
                        PagedResponseHelper.CreatePagedMessage(ProtocolConstants.NowPlayingList, tracks, offset, limit);
                    var response = MessageSendEvent.FromSocketMessage(message, context.ConnectionId);
                    _eventAggregator.Publish(response);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute now playing list request");
                return false;
            }
        }

        /// <summary>
        ///     Handle tag change request - replaces RequestTagChange
        /// </summary>
        public bool HandleTagChange(ITypedCommandContext<TagChangeRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "tag change", context.ShortId);

                // Validate typed request
                if (!context.IsValid)
                {
                    _logger.Warn("Invalid tag change request or tag name is missing");
                    return false;
                }

                var request = context.TypedData;
                var tagName = request.Tag;
                var newValue = request.Value;

                // Get current track file URL
                var currentTrack = _trackDataProvider.GetNowPlayingFileUrl();
                if (string.IsNullOrEmpty(currentTrack))
                {
                    _logger.Warn("No current track playing");
                    return false;
                }

                // Set the tag using the adapter
                var success = _trackDataProvider.SetTrackTag(currentTrack, tagName, newValue ?? string.Empty);
                if (!success)
                {
                    _logger.Warn("Failed to set tag {TagName} to value {NewValue}", tagName, newValue);
                    return false;
                }

                // Commit the tag changes
                success = _trackDataProvider.CommitTrackTags(currentTrack);
                if (!success)
                {
                    _logger.Warn("Failed to commit tag changes for track {TrackUrl}", currentTrack);
                    return false;
                }

                // Get updated track details to send back to client
                var updatedDetails = _trackDataProvider.GetNowPlayingTrackDetails();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingDetails,
                    updatedDetails,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute tag change request");
                return false;
            }
        }

        /// <summary>
        ///     Handle rating request - replaces RequestRating
        /// </summary>
        public bool HandleRating(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "rating", context.ShortId);

                var rating = context.GetDataOrDefault<string>();

                // If rating is provided (including empty string to clear rating), try to set it
                if (rating != null)
                {
                    var success = _trackDataProvider.SetNowPlayingRating(rating);
                    if (!success)
                        _logger.Warn("Failed to set rating to: {Rating}", rating);
                }

                // Always return the current rating (whether we just set it or not)
                var currentRating = _trackDataProvider.GetNowPlayingRating();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingRating,
                    currentRating,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute rating request");
                return false;
            }
        }

        /// <summary>
        ///     Handle LastFM love rating request - replaces RequestLfmLoveRating
        /// </summary>
        public bool HandleLastfmLoveRating(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "LastFM love rating", context.ShortId);

                var action = context.GetDataOrDefault<string>();
                var currentStatus = _trackDataProvider.GetNowPlayingLastfmStatus();

                if (action != null)
                {
                    if (action.Equals(LastfmActionToggle, StringComparison.OrdinalIgnoreCase))
                    {
                        // Toggle between normal and love status
                        var newStatus = currentStatus == LastfmStatus.Love ||
                                        currentStatus == LastfmStatus.Ban
                            ? LastfmStatus.Normal
                            : LastfmStatus.Love;

                        var success = _trackDataProvider.SetNowPlayingLastfmStatus(newStatus);
                        if (success)
                            currentStatus = newStatus;
                    }
                    else if (action.Equals(LastfmActionLove, StringComparison.OrdinalIgnoreCase))
                    {
                        var success = _trackDataProvider.SetNowPlayingLastfmStatus(LastfmStatus.Love);
                        if (success)
                            currentStatus = LastfmStatus.Love;
                    }
                    else if (action.Equals(LastfmActionBan, StringComparison.OrdinalIgnoreCase))
                    {
                        var success = _trackDataProvider.SetNowPlayingLastfmStatus(LastfmStatus.Ban);
                        if (success)
                            currentStatus = LastfmStatus.Ban;
                    }
                }

                // Send current status back to client
                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingLfmRating,
                    currentStatus,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute LastFM love rating request");
                return false;
            }
        }

        /// <summary>
        ///     Handle initialization request - replaces ProcessInitRequest
        ///     Sends initial state information to the client
        /// </summary>
        public bool HandleInit(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "init", context.ShortId);

                // Send track info
                var trackInfo = _trackDataProvider.GetNowPlayingTrackInfo();
                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingTrack,
                    trackInfo,
                    context.ConnectionId);

                // Send track rating  
                var rating = _trackDataProvider.GetNowPlayingRating();
                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingRating,
                    rating,
                    context.ConnectionId);

                // Send LastFM status
                var lastfmStatus = _trackDataProvider.GetNowPlayingLastfmStatus();
                _eventAggregator.PublishMessage(
                    ProtocolConstants.NowPlayingLfmRating,
                    lastfmStatus,
                    context.ConnectionId);

                // Send player status
                var isLegacyClient = !_protocolCapabilities.SupportsFullPlayerStatus(context.ConnectionId);
                var playerStatus = _playerDataProvider.GetPlayerStatus(isLegacyClient);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerStatus,
                    playerStatus,
                    context.ConnectionId);

                // Broadcast cover to all clients
                var cover = _trackDataProvider.GetNowPlayingArtwork();
                if (!string.IsNullOrEmpty(cover))
                    _broadcaster.BroadcastCover(cover);

                // Broadcast lyrics to all clients
                var lyrics = _trackDataProvider.GetNowPlayingLyrics();
                if (!string.IsNullOrEmpty(lyrics))
                    _broadcaster.BroadcastLyrics(lyrics);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute init request");
                return false;
            }
        }

        /// <summary>
        ///     Convert NowPlayingTrackV2 to legacy NowPlayingTrack for older clients
        /// </summary>
        private static NowPlayingTrack ConvertToLegacyTrack(NowPlayingTrackV2 trackV2)
        {
            // Create a legacy track object for older protocol versions
            var legacyTrack = new NowPlayingTrack
            {
                Artist = trackV2.Artist,
                Album = trackV2.Album,
                Year = trackV2.Year,
                Title = trackV2.Title
            };

            return legacyTrack;
        }
    }
}
