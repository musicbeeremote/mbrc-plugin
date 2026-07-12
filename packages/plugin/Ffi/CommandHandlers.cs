using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Models;
using MusicBeePlugin.Logging;
using MusicBeePlugin.Settings;
using MusicBeePlugin.Ffi.Generated;
using MusicBeePlugin.Utilities;

namespace MusicBeePlugin.Ffi
{
    /// <summary>
    ///     The write side of the FFI RPC: maps a Rust <c>execute_command</c>
    ///     request (a <see cref="CommandType"/> id + MessagePack params) to a
    ///     provider mutation. One-way (returns success only). FFI-free, so it
    ///     unit-tests against mock providers. The caller (<see cref="NativeBridge"/>)
    ///     serializes access under the API lock.
    /// </summary>
    internal sealed class CommandHandlers
    {
        private readonly IPlayerDataProvider _player;
        private readonly ITrackDataProvider _track;
        private readonly IPlaylistDataProvider _playlist;
        private readonly IUserSettings _userSettings;
        private readonly ISystemOperations _system;
        private readonly IPluginLogger _logger;

        public CommandHandlers(
            IPlayerDataProvider player,
            ITrackDataProvider track,
            IPlaylistDataProvider playlist,
            IUserSettings userSettings,
            ISystemOperations system,
            IPluginLogger logger)
        {
            _player = player;
            _track = track;
            _playlist = playlist;
            _userSettings = userSettings;
            _system = system;
            _logger = logger;
        }

        /// <summary>Apply a command. Returns true on success.</summary>
        public bool Handle(int commandType, byte[] p)
        {
            switch ((CommandType)commandType)
            {
                case CommandType.Play: return _player.Play();
                case CommandType.Pause: return _player.Pause();
                case CommandType.PlayPause: return _player.PlayPause();
                case CommandType.Stop: return _player.StopPlayback();
                case CommandType.Next: return _player.PlayNext();
                case CommandType.Previous: return _player.PlayPrevious();
                case CommandType.SetVolume: return _player.SetVolume(Int(p));
                case CommandType.SetPosition: return _player.SetPosition(Int(p));
                case CommandType.SetMute: return _player.SetMute(Bool(p));
                case CommandType.SetShuffle: return _player.SetShuffle(Bool(p));
                case CommandType.SetScrobble: return _player.SetScrobble(Bool(p));
                case CommandType.SetAutoDj: return _player.SetAutoDj(Bool(p));
                case CommandType.SetRepeat: return ApplyRepeat(p);
                case CommandType.SetRating: return _track.SetNowPlayingRating(Str(p));
                case CommandType.SetLfmRating: return ApplyLfmRating(p);
                case CommandType.OutputSwitch: return _player.SetOutputDevice(Str(p));
                case CommandType.PlaylistPlay: return _playlist.PlayPlaylist(Str(p));
                case CommandType.LibraryPlayAll: return _playlist.PlayAllLibrary(Bool(p));
                case CommandType.NowPlayingListPlay: return _playlist.PlayNowPlayingByIndex(Index(p));
                case CommandType.NowPlayingListRemove: return _playlist.RemoveFromNowPlayingList(Index(p));
                case CommandType.NowPlayingListMove: return ApplyMove(p);
                case CommandType.NowPlayingListSearch: return ApplySearch(p);
                case CommandType.NowPlayingQueue: return ApplyQueue(p);
                case CommandType.NowPlayingTagChange: return ApplyTagChange(p);
                case CommandType.SetBackgroundTaskMessage: return ApplyBackgroundTaskMessage(p);
                default:
                    _logger.Warn("Unknown command type {0}", commandType);
                    return false;
            }
        }

        private bool ApplyRepeat(byte[] p)
        {
            var dto = Msgpack.Deserialize<SetRepeatParams>(p);
            if (!Enum.TryParse<RepeatMode>(dto.mode, out var mode))
            {
                _logger.Warn("Invalid repeat mode '{0}'", dto.mode ?? "<null>");
                return false;
            }
            return _player.SetRepeatMode(mode);
        }

        private bool ApplyLfmRating(byte[] p)
        {
            var dto = Msgpack.Deserialize<SetLfmRatingParams>(p);
            if (!Enum.TryParse<LastfmStatus>(dto.status, out var status))
            {
                _logger.Warn("Invalid lfm status '{0}'", dto.status ?? "<null>");
                return false;
            }
            return _track.SetNowPlayingLastfmStatus(status);
        }

        private bool ApplyMove(byte[] p)
        {
            var dto = Msgpack.Deserialize<MoveParams>(p);
            if (dto.@from < 0 || dto.to < 0) return false;
            return _playlist.MoveNowPlayingTrack(dto.@from, dto.to);
        }

        private bool ApplySearch(byte[] p)
        {
            var query = Msgpack.Deserialize<StringValueParams>(p).value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query)) return false;
            var match = _track.SearchNowPlayingList(query, SearchSourceHelper.GetSearchSource(_userSettings));
            if (string.IsNullOrEmpty(match)) return false;
            return _playlist.PlayNowPlayingTrack(match);
        }

        private bool ApplyQueue(byte[] p)
        {
            var dto = Msgpack.Deserialize<NowPlayingQueueParams>(p);
            var files = dto.files ?? new List<string>();
            if (files.Count == 0) return false;
            if (!Enum.TryParse<QueueType>(dto.queue_type, out var queueType))
                queueType = QueueType.Next;
            var play = string.IsNullOrEmpty(dto.play) ? null : dto.play;
            return _playlist.QueueFiles(queueType, files.ToArray(), play);
        }

        private bool ApplyTagChange(byte[] p)
        {
            var dto = Msgpack.Deserialize<TagChangeParams>(p);
            if (string.IsNullOrEmpty(dto.tag)) return false;
            var fileUrl = _track.GetNowPlayingFileUrl();
            if (string.IsNullOrEmpty(fileUrl)) return false;
            if (!_track.SetTrackTag(fileUrl, dto.tag, dto.value ?? string.Empty)) return false;
            return _track.CommitTrackTags(fileUrl);
        }

        private bool ApplyBackgroundTaskMessage(byte[] p)
        {
            // Host-only UI: the Rust core drives the cover-cache build now and
            // asks the host to show its progress in MusicBee's status bar.
            _system.SetBackgroundTaskMessage(Str(p));
            return true;
        }

        private static bool Bool(byte[] p) => Msgpack.Deserialize<SetBoolParams>(p).value;
        private static int Int(byte[] p) => Msgpack.Deserialize<SetIntParams>(p).value;
        private static string Str(byte[] p) => Msgpack.Deserialize<StringValueParams>(p).value ?? string.Empty;
        private static int Index(byte[] p) => Msgpack.Deserialize<IndexParams>(p).index;
    }
}
