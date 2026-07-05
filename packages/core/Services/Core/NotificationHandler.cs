using System;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Configuration;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Media;

namespace MusicBeePlugin.Services.Core
{
    /// <summary>
    ///     Service implementation for handling MusicBee notifications.
    ///     Contains the actual notification handling logic extracted from Plugin.cs.
    /// </summary>
    public class NotificationHandler : INotificationHandler
    {
        private readonly ICoverService _coverService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPluginLogger _logger;
        private readonly LyricCoverModel _lyricCoverModel;
        private readonly IPlayerDataProvider _playerDataProvider;
        private readonly ITrackDataProvider _trackDataProvider;

        public NotificationHandler(
            ICoverService coverService,
            IPlayerDataProvider playerDataProvider,
            ITrackDataProvider trackDataProvider,
            LyricCoverModel lyricCoverModel,
            IEventAggregator eventAggregator,
            IPluginLogger logger)
        {
            _coverService = coverService;
            _playerDataProvider = playerDataProvider;
            _trackDataProvider = trackDataProvider;
            _lyricCoverModel = lyricCoverModel;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        /// <summary>
        ///     Handles track change notifications.
        /// </summary>
        public void HandleTrackChanged(string sourceFileUrl)
        {
            try
            {
                _logger.Debug("Processing track changed notification");

                BroadcastNowPlayingTrackCover();
                BroadcastTrackRating();
                BroadcastLoveStatus();
                BroadcastNowPlayingTrackLyrics();
                BroadcastPlayPosition();

                // Broadcast track change event
                var broadcastEvent = new BroadcastEvent(ProtocolConstants.NowPlayingTrack);
                broadcastEvent.AddPayload(ProtocolConstants.V2, GetTrackInfo());
                broadcastEvent.AddPayload(ProtocolConstants.V3, GetTrackInfoV2());
                _eventAggregator.Publish(broadcastEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle track changed notification");
            }
        }

        /// <summary>
        ///     Handles volume level change notifications.
        /// </summary>
        public void HandleVolumeLevelChanged() =>
            HandleNotificationSafely("volume level changed",
                () => BroadcastValue(ProtocolConstants.PlayerVolume, _playerDataProvider.GetVolume));

        /// <summary>
        ///     Handles volume mute change notifications.
        /// </summary>
        public void HandleVolumeMuteChanged() =>
            HandleNotificationSafely("volume mute changed",
                () => BroadcastValue(ProtocolConstants.PlayerMute, _playerDataProvider.GetMute));

        /// <summary>
        ///     Handles play state change notifications.
        /// </summary>
        public void HandlePlayStateChanged() =>
            HandleNotificationSafely("play state changed",
                () => BroadcastValue(ProtocolConstants.PlayerState, _playerDataProvider.GetPlayState));

        /// <summary>
        ///     Handles notification when now playing lyrics are ready.
        /// </summary>
        public void HandleNowPlayingLyricsReady() =>
            HandleNotificationSafely("now playing lyrics ready",
                () => _lyricCoverModel.Lyrics = _trackDataProvider.GetNowPlayingLyrics());

        /// <summary>
        ///     Handles notification when now playing artwork is ready.
        /// </summary>
        public void HandleNowPlayingArtworkReady() =>
            HandleNotificationSafely("now playing artwork ready",
                () => _lyricCoverModel.SetCover(_trackDataProvider.GetNowPlayingDownloadedArtwork()));

        /// <summary>
        ///     Handles notification when now playing list changes.
        /// </summary>
        public void HandleNowPlayingListChanged() =>
            HandleNotificationSafely("now playing list changed",
                () => BroadcastValue(ProtocolConstants.NowPlayingListChanged, () => true));

        /// <summary>
        ///     Handles notification when a file is added to the library.
        /// </summary>
        public void HandleFileAddedToLibrary(string sourceFileUrl)
        {
            try
            {
                _logger.Debug("Processing file added to library notification for {FileUrl}", sourceFileUrl);
                _coverService.CacheTrackCover(sourceFileUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle file added to library notification for {FileUrl}", sourceFileUrl);
            }
        }

        /// <summary>
        ///     Broadcasts the initial now playing state including cover and lyrics.
        /// </summary>
        public void BroadcastInitialNowPlayingState() =>
            HandleNotificationSafely("broadcast initial now playing state", () =>
            {
                var currentTrackUrl = _trackDataProvider.GetNowPlayingFileUrl();
                if (!string.IsNullOrEmpty(currentTrackUrl))
                {
                    BroadcastNowPlayingTrackCover();
                    BroadcastNowPlayingTrackLyrics();
                }
            });

        #region Private Helper Methods

        /// <summary>
        ///     Wraps notification handling with consistent logging and error handling.
        /// </summary>
        private void HandleNotificationSafely(string operationName, Action operation)
        {
            try
            {
                _logger.Debug("Processing {OperationName} notification", operationName);
                operation();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle {OperationName} notification", operationName);
            }
        }

        /// <summary>
        ///     Broadcasts a simple value to all clients using the specified protocol constant.
        /// </summary>
        private void BroadcastValue<T>(string protocolConstant, Func<T> getValue)
        {
            var value = getValue();
            var message = MessageSendEvent.Create(protocolConstant, value);
            _eventAggregator.Publish(message);
        }

        private void BroadcastNowPlayingTrackCover()
        {
            var artwork = _trackDataProvider.GetNowPlayingArtwork();
            if (string.IsNullOrEmpty(artwork))
                artwork = _trackDataProvider.GetNowPlayingDownloadedArtwork();
            _lyricCoverModel.SetCover(artwork);
        }

        private void BroadcastTrackRating() =>
            BroadcastValue(ProtocolConstants.NowPlayingRating, _trackDataProvider.GetNowPlayingRating);

        private void BroadcastLoveStatus() =>
            BroadcastValue(ProtocolConstants.NowPlayingLfmRating, _trackDataProvider.GetNowPlayingLastfmStatus);

        private void BroadcastNowPlayingTrackLyrics() =>
            _lyricCoverModel.Lyrics = _trackDataProvider.GetNowPlayingLyrics();

        private void BroadcastPlayPosition() =>
            BroadcastValue(ProtocolConstants.NowPlayingPosition, _trackDataProvider.GetPlaybackPosition);

        private NowPlayingTrack GetTrackInfo()
        {
            var trackV2 = _trackDataProvider.GetNowPlayingTrackInfo();
            return new NowPlayingTrack
            {
                Artist = trackV2.Artist,
                Title = trackV2.Title,
                Album = trackV2.Album,
                Year = trackV2.Year
            };
        }

        private NowPlayingTrackV2 GetTrackInfoV2() => _trackDataProvider.GetNowPlayingTrackInfo();

        #endregion
    }
}
