using System;
using System.Linq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Events.Extensions;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.Utilities.Data;
using MusicBeePlugin.Utilities.Mapping;

namespace MusicBeePlugin.Commands.Handlers
{
    /// <summary>
    ///     Command handlers for library-related operations (radio stations, search, browse, etc.)
    /// </summary>
    public class LibraryCommands
    {
        private readonly ICoverService _coverService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ILibraryDataProvider _libraryDataProvider;
        private readonly IPluginLogger _logger;
        private readonly IPlaylistDataProvider _playlistDataProvider;
        private readonly IUserSettings _userSettings;

        public LibraryCommands(ILibraryDataProvider libraryDataProvider, IPlaylistDataProvider playlistDataProvider,
            ICoverService coverService, IPluginLogger logger, IEventAggregator eventAggregator,
            IUserSettings userSettings)
        {
            _libraryDataProvider = libraryDataProvider;
            _playlistDataProvider = playlistDataProvider;
            _coverService = coverService;
            _logger = logger;
            _eventAggregator = eventAggregator;
            _userSettings = userSettings;
        }

        /// <summary>
        ///     Handle radio stations request - replaces RequestRadioStations
        /// </summary>
        public bool HandleRadioStations(ITypedCommandContext<PaginationRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "radio stations", context.ShortId);

                var paginationRequest = context.TypedData;
                var offset = paginationRequest?.Offset ?? 0;
                var limit = paginationRequest?.Limit > 0 ? paginationRequest.Limit : PaginationRequest.DefaultLimit;

                var stations = _libraryDataProvider.GetRadioStations(offset, limit);
                var message =
                    PagedResponseHelper.CreatePagedMessage(ProtocolConstants.RadioStations, stations, offset, limit);

                var response = MessageSendEvent.FromSocketMessage(message, context.ConnectionId);
                _eventAggregator.Publish(response);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute radio stations request");
                return false;
            }
        }

        /// <summary>
        ///     Generic search handler template to reduce code duplication.
        /// </summary>
        private bool HandleSearch<T>(
            ICommandContext context,
            string searchType,
            string protocolConstant,
            Func<string, SearchSource, T> searchFunc)
        {
            try
            {
                _logger.Debug("Processing library search {SearchType} request for client {ClientId}", searchType, context.ShortId);

                var query = context.GetDataOrDefault<string>();
                var searchSource = SearchSourceHelper.GetSearchSource(_userSettings);
                var results = searchFunc(query, searchSource);

                _eventAggregator.PublishMessage(protocolConstant, results, context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute library search {SearchType} request", searchType);
                return false;
            }
        }

        /// <summary>
        ///     Handle library search by title - replaces RequestLibSearchTitle
        /// </summary>
        public bool HandleSearchTitle(ICommandContext context) =>
            HandleSearch(context, "title", ProtocolConstants.LibrarySearchTitle, _libraryDataProvider.SearchTracks);

        /// <summary>
        ///     Handle library search by genre - replaces RequestLibSearchGenre
        /// </summary>
        public bool HandleSearchGenre(ICommandContext context) =>
            HandleSearch(context, "genre", ProtocolConstants.LibrarySearchGenre, _libraryDataProvider.SearchGenres);

        /// <summary>
        ///     Handle library search by artist - replaces RequestLibSearchArtist
        /// </summary>
        public bool HandleSearchArtist(ICommandContext context) =>
            HandleSearch(context, "artist", ProtocolConstants.LibrarySearchArtist, _libraryDataProvider.SearchArtists);

        /// <summary>
        ///     Handle library search by album - replaces RequestLibSearchAlbum
        /// </summary>
        public bool HandleSearchAlbum(ICommandContext context) =>
            HandleSearch(context, "album", ProtocolConstants.LibrarySearchAlbum, _libraryDataProvider.SearchAlbums);

        /// <summary>
        ///     Handle library queue request for any meta tag type
        /// </summary>
        private bool HandleQueueByMetaTag(ITypedCommandContext<SearchRequest> context, MetaTag metaTag, string protocolConstant)
        {
            try
            {
                _logger.Debug("Processing queue {MetaTag} request for client {ClientId}", metaTag, context.ShortId);

                if (!context.IsValid)
                {
                    _logger.Warn("Invalid data format for queue request");
                    return false;
                }

                var request = context.TypedData;
                var type = request.Type;
                var query = request.Query ?? string.Empty;

                var queueType = QueueTypeMapper.MapFromString(type);
                var searchSource = SearchSourceHelper.GetSearchSource(_userSettings);

                string[] trackList;
                if (metaTag == MetaTag.Title && queueType == QueueType.PlayNow)
                    trackList = new[] { query };
                else
                    trackList = _libraryDataProvider.GetFileUrlsByMetaTag(metaTag, query, searchSource);

                var success = _playlistDataProvider.QueueFiles(queueType, trackList,
                    trackList.Length > 0 ? trackList[0] : null);

                _eventAggregator.PublishMessage(protocolConstant, success, context.ConnectionId);

                _logger.Debug("Queue {MetaTag} operation completed with success: {Success}", metaTag, success);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute queue {MetaTag} request", metaTag);
                return false;
            }
        }

        /// <summary>
        ///     Handle library queue track request - replaces RequestLibQueueTrack
        /// </summary>
        public bool HandleQueueTrack(ITypedCommandContext<SearchRequest> context)
        {
            return HandleQueueByMetaTag(context, MetaTag.Title, ProtocolConstants.LibraryQueueTrack);
        }

        /// <summary>
        ///     Handle library queue genre request - replaces RequestLibQueueGenre
        /// </summary>
        public bool HandleQueueGenre(ITypedCommandContext<SearchRequest> context)
        {
            return HandleQueueByMetaTag(context, MetaTag.Genre, ProtocolConstants.LibraryQueueGenre);
        }

        /// <summary>
        ///     Handle library queue artist request - replaces RequestLibQueueArtist
        /// </summary>
        public bool HandleQueueArtist(ITypedCommandContext<SearchRequest> context)
        {
            return HandleQueueByMetaTag(context, MetaTag.Artist, ProtocolConstants.LibraryQueueArtist);
        }

        /// <summary>
        ///     Handle library queue album request - replaces RequestLibQueueAlbum
        /// </summary>
        public bool HandleQueueAlbum(ITypedCommandContext<SearchRequest> context)
        {
            return HandleQueueByMetaTag(context, MetaTag.Album, ProtocolConstants.LibraryQueueAlbum);
        }

        /// <summary>
        ///     Generic browse handler template to reduce code duplication.
        /// </summary>
        private bool HandleBrowse<T>(
            ITypedCommandContext<PaginationRequest> context,
            string browseType,
            string protocolConstant,
            Func<int, int, System.Collections.Generic.IEnumerable<T>> browseFunc)
        {
            try
            {
                _logger.Debug("Processing browse {BrowseType} request for client {ClientId}", browseType, context.ShortId);

                var paginationRequest = context.TypedData;
                var offset = paginationRequest?.Offset ?? 0;
                var limit = paginationRequest?.Limit > 0 ? paginationRequest.Limit : PaginationRequest.DefaultLimit;

                var data = browseFunc(offset, limit).ToList();
                var message = PagedResponseHelper.CreatePagedMessage(protocolConstant, data, offset, limit);

                var response = MessageSendEvent.FromSocketMessage(message, context.ConnectionId);
                _eventAggregator.Publish(response);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute browse {BrowseType} request", browseType);
                return false;
            }
        }

        /// <summary>
        ///     Handle library browse genres request
        /// </summary>
        public bool HandleBrowseGenres(ITypedCommandContext<PaginationRequest> context) =>
            HandleBrowse(context, "genres", ProtocolConstants.LibraryBrowseGenres, _libraryDataProvider.BrowseGenres);

        /// <summary>
        ///     Handle library browse artists request (special case with albumArtists parameter)
        /// </summary>
        public bool HandleBrowseArtists(ITypedCommandContext<BrowseArtistsRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "browse artists", context.ShortId);

                // Use typed request which includes album_artists and pagination
                var request = context.TypedData;
                var albumArtists = request?.AlbumArtists ?? false;
                var offset = request?.Offset ?? 0;
                var limit = request?.Limit > 0 ? request.Limit : PaginationRequest.DefaultLimit;

                var artists = _libraryDataProvider.BrowseArtists(offset, limit, albumArtists).ToList();
                var message = PagedResponseHelper.CreatePagedMessage(
                    ProtocolConstants.LibraryBrowseArtists, artists, offset, limit);

                var response = MessageSendEvent.FromSocketMessage(message, context.ConnectionId);
                _eventAggregator.Publish(response);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute browse artists request");
                return false;
            }
        }

        /// <summary>
        ///     Handle library browse albums request
        /// </summary>
        public bool HandleBrowseAlbums(ITypedCommandContext<PaginationRequest> context) =>
            HandleBrowse(context, "albums", ProtocolConstants.LibraryBrowseAlbums, _libraryDataProvider.BrowseAlbums);

        /// <summary>
        ///     Handle library browse tracks request
        /// </summary>
        public bool HandleBrowseTracks(ITypedCommandContext<PaginationRequest> context) =>
            HandleBrowse(context, "tracks", ProtocolConstants.LibraryBrowseTracks, _libraryDataProvider.BrowseTracks);

        /// <summary>
        ///     Handle library play all request - replaces RequestLibPlayAll
        /// </summary>
        public bool HandlePlayAll(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "library play all", context.ShortId);

                // Extract shuffle parameter from event data
                var shuffle = false;
                if (context.Data != null)
                {
                    if (context.Data is bool boolValue)
                        shuffle = boolValue;
                    else if (context.Data is string stringValue && bool.TryParse(stringValue, out var parsedShuffle))
                        shuffle = parsedShuffle;
                }

                var success = _playlistDataProvider.PlayAllLibrary(shuffle);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.LibraryPlayAll,
                    success,
                    context.ConnectionId);

                _logger.Debug("Library play all operation completed with success: {Success}", success);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute library play all request");
                return false;
            }
        }

        /// <summary>
        ///     Handle library album tracks request - replaces RequestLibAlbumTracks
        /// </summary>
        public bool HandleAlbumTracks(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "album tracks", context.ShortId);

                var album = context.GetDataOrDefault<string>();
                if (string.IsNullOrEmpty(album))
                {
                    _logger.Warn("Empty album name received");
                    return false;
                }

                var searchSource = SearchSourceHelper.GetSearchSource(_userSettings);
                var tracks = _libraryDataProvider.GetAlbumTracks(album, searchSource);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.LibraryAlbumTracks,
                    tracks,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute album tracks request");
                return false;
            }
        }

        /// <summary>
        ///     Handle library artist albums request - replaces RequestLibArtistAlbums
        /// </summary>
        public bool HandleArtistAlbums(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "artist albums", context.ShortId);

                var artist = context.GetDataOrDefault<string>();
                if (string.IsNullOrEmpty(artist))
                {
                    _logger.Warn("Empty artist name received");
                    return false;
                }

                var searchSource = SearchSourceHelper.GetSearchSource(_userSettings);
                var albums = _libraryDataProvider.GetArtistAlbums(artist, searchSource);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.LibraryArtistAlbums,
                    albums,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute artist albums request");
                return false;
            }
        }

        /// <summary>
        ///     Handle library genre artists request - replaces RequestLibGenreArtists
        /// </summary>
        public bool HandleGenreArtists(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "genre artists", context.ShortId);

                var genre = context.GetDataOrDefault<string>();
                if (string.IsNullOrEmpty(genre))
                {
                    _logger.Warn("Empty genre name received");
                    return false;
                }

                var searchSource = SearchSourceHelper.GetSearchSource(_userSettings);
                var artists = _libraryDataProvider.GetGenreArtists(genre, searchSource);

                _eventAggregator.PublishMessage(
                    ProtocolConstants.LibraryGenreArtists,
                    artists,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute genre artists request");
                return false;
            }
        }

        /// <summary>
        ///     Handle album cover request - replaces RequestLibraryAlbumCover
        /// </summary>
        public bool HandleAlbumCover(ITypedCommandContext<AlbumCoverRequest> context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "album cover", context.ShortId);

                if (!context.IsValid)
                {
                    _logger.Warn("Invalid data format for album cover request");
                    return false;
                }

                var request = context.TypedData;
                var album = request.Album ?? string.Empty;
                var artist = request.Artist ?? string.Empty;
                var hash = request.Hash ?? string.Empty;
                var size = request.Size;

                // Check if this is a paginated request
                if (request.IsPaginatedRequest)
                {
                    var pageResult = _coverService.GetCoverPage(request.Offset, request.Limit);

                    _eventAggregator.PublishMessage(
                        ProtocolConstants.LibraryAlbumCover,
                        pageResult,
                        context.ConnectionId);
                }
                else
                {
                    // Single cover request
                    var coverPayload = size != null
                        ? _coverService.GetCoverBySize(artist, album, size)
                        : _coverService.GetAlbumCover(artist, album, hash);

                    _eventAggregator.PublishMessage(
                        ProtocolConstants.LibraryAlbumCover,
                        coverPayload,
                        context.ConnectionId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute album cover request");
                return false;
            }
        }

        /// <summary>
        ///     Handle cover cache build status request - replaces RequestCoverCacheBuildStatus
        /// </summary>
        public bool HandleCoverCacheStatus(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Request} request for client {ClientId}", "cover cache status", context.ShortId);

                _coverService.BroadcastCacheStatus(context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute cover cache status request");
                return false;
            }
        }
    }
}
