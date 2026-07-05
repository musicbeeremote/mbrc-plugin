using System;
using System.Linq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Events.Extensions;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Utilities.Data;
using MusicBeePlugin.Utilities.Network;

namespace MusicBeePlugin.Commands.Handlers
{
    /// <summary>
    ///     Playlist commands using delegate pattern with dependency injection
    /// </summary>
    public class PlaylistCommands
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IPluginLogger _logger;
        private readonly IPlaylistDataProvider _playlistDataProvider;
        private readonly IProtocolCapabilities _protocolCapabilities;

        public PlaylistCommands(IPlaylistDataProvider playlistDataProvider, IPluginLogger logger,
            IEventAggregator eventAggregator, IProtocolCapabilities protocolCapabilities)
        {
            _playlistDataProvider = playlistDataProvider;
            _logger = logger;
            _eventAggregator = eventAggregator;
            _protocolCapabilities = protocolCapabilities;
        }

        /// <summary>
        ///     Handle playlist play request - replaces RequestPlaylistPlay
        /// </summary>
        public bool HandlePlaylistPlay(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "playlist play", context.ShortId);

                var playlistUrl = context.GetDataOrDefault<string>();
                if (string.IsNullOrWhiteSpace(playlistUrl))
                {
                    _logger.Warn("Playlist URL is empty");
                    return false;
                }

                // Play the playlist
                var success = _playlistDataProvider.PlayPlaylist(playlistUrl);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlaylistPlay,
                    success,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute playlist play request");
                return false;
            }
        }

        /// <summary>
        ///     Handle playlist list request - replaces RequestPlaylistList
        /// </summary>
        public bool HandlePlaylistList(ITypedCommandContext<PaginationRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "playlist list", context.ShortId);

                // Get the playlist list from adapter
                var playlists = _playlistDataProvider.GetPlaylists().ToList();

                // Use typed pagination request
                var paginationRequest = context.TypedData;
                var supportsPagination = _protocolCapabilities.SupportsPagination(context.ConnectionId);

                if (!supportsPagination || paginationRequest == null)
                {
                    // Legacy non-paged response
                    _eventAggregator.PublishMessage(
                        ProtocolConstants.PlaylistList,
                        playlists,
                        context.ConnectionId);
                }
                else
                {
                    // Paged response for newer clients
                    var offset = paginationRequest.Offset;
                    var limit = paginationRequest.Limit > 0 ? paginationRequest.Limit : PaginationRequest.DefaultLimit;

                    var message =
                        PagedResponseHelper.CreatePagedMessage(ProtocolConstants.PlaylistList, playlists, offset,
                            limit);
                    var response = MessageSendEvent.FromSocketMessage(message, context.ConnectionId);
                    _eventAggregator.Publish(response);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute playlist list request");
                return false;
            }
        }
    }
}
