/**
 * The op catalog: the op names, and what each one takes.
 *
 * This is the mechanical half of `docs/protocol-v6.md`. Its job is to make the
 * client generic over the op name, so a call site names an op and gets its
 * response type back with no cast. Adding an op here is what makes it callable.
 *
 * Requests stay plain types - they are ours to construct, and a schema for them
 * would only re-check what the compiler already proved. The answers are schemas
 * instead, and live in `responses`.
 */

import type {
  Empty,
  LastfmStatus,
  QueryField,
  QueueMode,
  RepeatMode,
  ShuffleMode,
} from './types'

interface PageArgs {
  offset?: number
  limit?: number
}

/**
 * The filters that narrow a library level.
 *
 * The same set names what `library_queue` queues, which is what makes "queue
 * what I am looking at" a matter of passing the scope along rather than
 * enumerating the tracks it selects.
 */
/** How a level is ordered: a field it shows, ascending unless told otherwise. */
export interface LibraryOrder {
  sort?: string
  order?: 'asc' | 'desc'
}

export interface LibraryScope {
  genre?: string
  artist?: string
  album?: string
  /** Case-insensitive substring over the names the level shows. */
  query?: string
}

/** The `data` each op takes. */
export interface OpRequests {
  system_info: Empty

  player_play: Empty
  player_pause: Empty
  player_play_pause: Empty
  player_stop: Empty
  player_next: Empty
  player_previous: Empty
  // Refused with `unavailable` when no last.fm account is configured, which
  // is a state the server knows and the client cannot.
  player_set_scrobbling: { enabled: boolean }
  player_status: Empty
  player_set_volume: { volume: number }
  player_set_mute: { muted: boolean }
  player_set_shuffle: { mode: ShuffleMode }
  player_set_repeat: { mode: RepeatMode }
  player_output: Empty
  player_set_output: { device: string }

  track_get: { src: string }

  now_playing_state: { include_list_order?: boolean }
  now_playing_details: Empty
  now_playing_position: Empty
  now_playing_lyrics: Empty
  now_playing_seek: { position_ms: number }
  now_playing_set_rating: { rating: number | null }
  now_playing_set_lfm: { status: LastfmStatus }

  now_playing_list: PageArgs & { up_next?: boolean }
  now_playing_list_play: { order: number; version?: number }
  now_playing_list_remove: { order: number; version?: number }
  now_playing_list_move: { from: number; to: number; version?: number }
  now_playing_queue: { paths: string[]; mode?: QueueMode; play?: string }

  library_genres: PageArgs & LibraryOrder & Pick<LibraryScope, 'query'>
  // `album_artists` browses the tag albums are filed under rather than every
  // credit, which is a shorter and different list, not a filter of the same one.
  library_artists: PageArgs &
    LibraryOrder &
    Pick<LibraryScope, 'genre' | 'query'> & { album_artists?: boolean }
  library_albums: PageArgs & LibraryOrder & Pick<LibraryScope, 'artist' | 'query'>
  // The artist is not a second filter here: it says which record is meant when
  // several share a title, and is ignored without an album.
  library_tracks: PageArgs & LibraryOrder & Pick<LibraryScope, 'album' | 'artist' | 'query'>
  library_radio: PageArgs
  library_play_all: { shuffle?: boolean }
  library_queue: LibraryScope & { mode?: QueueMode; play?: string; shuffle?: boolean }

  playlist_list: PageArgs
  playlist_play: { url: string }
  // `query` and `totals` are what a window cannot answer, so each is asked for
  // rather than assumed: both read the whole playlist's tags server-side.
  playlist_tracks: PageArgs & {
    url: string
    query?: string
    query_field?: QueryField
    totals?: boolean
  }
}

/**
 * The op names, so a call site names a member instead of retyping a literal.
 *
 * `satisfies` ties every value to a key of `OpRequests`, and `AllOpsNamed`
 * below fails the build if an op is added to the catalog without being named
 * here, which is what keeps the two from drifting.
 */
export const Op = {
  SystemInfo: 'system_info',

  PlayerPlay: 'player_play',
  PlayerPause: 'player_pause',
  PlayerPlayPause: 'player_play_pause',
  PlayerStop: 'player_stop',
  PlayerNext: 'player_next',
  PlayerPrevious: 'player_previous',
  PlayerSetScrobbling: 'player_set_scrobbling',
  PlayerStatus: 'player_status',
  PlayerSetVolume: 'player_set_volume',
  PlayerSetMute: 'player_set_mute',
  PlayerSetShuffle: 'player_set_shuffle',
  PlayerSetRepeat: 'player_set_repeat',
  PlayerOutput: 'player_output',
  PlayerSetOutput: 'player_set_output',

  TrackGet: 'track_get',

  NowPlayingState: 'now_playing_state',
  NowPlayingDetails: 'now_playing_details',
  NowPlayingPosition: 'now_playing_position',
  NowPlayingLyrics: 'now_playing_lyrics',
  NowPlayingSeek: 'now_playing_seek',
  NowPlayingSetRating: 'now_playing_set_rating',
  NowPlayingSetLfm: 'now_playing_set_lfm',

  NowPlayingList: 'now_playing_list',
  NowPlayingListPlay: 'now_playing_list_play',
  NowPlayingListRemove: 'now_playing_list_remove',
  NowPlayingListMove: 'now_playing_list_move',
  NowPlayingQueue: 'now_playing_queue',

  LibraryGenres: 'library_genres',
  LibraryArtists: 'library_artists',
  LibraryAlbums: 'library_albums',
  LibraryTracks: 'library_tracks',
  LibraryRadio: 'library_radio',
  LibraryQueue: 'library_queue',
  LibraryPlayAll: 'library_play_all',

  PlaylistList: 'playlist_list',
  PlaylistPlay: 'playlist_play',
  PlaylistTracks: 'playlist_tracks',
} as const satisfies Record<string, keyof OpRequests>

export type Op = (typeof Op)[keyof typeof Op]

/** Fails the build if an op joins the catalog without being named in `Op`. */
type AllOpsNamed = Exclude<keyof OpRequests, Op> extends never ? true : never
const allOpsNamed: AllOpsNamed = true
void allOpsNamed

/** Event names, on the same footing as `Op`. */
export const WireEvent = {
  ConnectionChanged: 'connection_changed',
  AuthRequired: 'auth_required',
  PlayStateChanged: 'play_state_changed',
  VolumeChanged: 'volume_changed',
  MuteChanged: 'mute_changed',
  ShuffleChanged: 'shuffle_changed',
  RepeatChanged: 'repeat_changed',
  ScrobblingChanged: 'scrobbling_changed',
  NowPlayingChanged: 'now_playing_changed',
  NowPlayingLyricsChanged: 'now_playing_lyrics_changed',
  NowPlayingListChanged: 'now_playing_list_changed',
  CoverCacheChanged: 'cover_cache_changed',
  LibraryChanged: 'library_changed',
  ServerShutdown: 'server_shutdown',
} as const

export type EventName = (typeof WireEvent)[keyof typeof WireEvent]
