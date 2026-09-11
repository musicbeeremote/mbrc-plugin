/**
 * What each op answers, as schemas.
 *
 * Split from the request half because it is the untrusted half: these are the
 * shapes the network hands back, and `parse` turns a payload into one of them or
 * refuses it. `OpResponses` is projected from the map below, so the thing that
 * validates a payload and the thing that types it are one definition.
 */

import { z } from 'zod'

import type { EventName, OpRequests } from './ops'
import {
  AlbumEntrySchema,
  ArtistEntrySchema,
  GenreEntrySchema,
  LastfmStatusSchema,
  LyricsSchema,
  pageSchema,
  PlaylistEntrySchema,
  PlaylistPageSchema,
  PlayStateSchema,
  QueuePageSchema,
  RadioEntrySchema,
  RepeatModeSchema,
  ShuffleModeSchema,
  EmptySchema,
  TrackSchema,
} from './types'

export const PlayerStatusSchema = z.object({
  play_state: PlayStateSchema,
  volume: z.number(),
  muted: z.boolean(),
  shuffle: ShuffleModeSchema,
  repeat: RepeatModeSchema,
  scrobbling: z.boolean(),
  stop_after_current: z.boolean(),
})
export type PlayerStatus = z.infer<typeof PlayerStatusSchema>

export const ScrobblingSchema = z.object({ enabled: z.boolean() })
export const LfmStatusSchema = z.object({ lfm_status: LastfmStatusSchema })
export type Scrobbling = z.infer<typeof ScrobblingSchema>

export const NowPlayingStateSchema = z.object({
  track: TrackSchema.nullable(),
  list_order: z.number().nullable(),
  position_ms: z.number(),
  duration_ms: z.number(),
  lfm_status: LastfmStatusSchema,
})
export type NowPlayingState = z.infer<typeof NowPlayingStateSchema>

export const PositionSchema = z.object({
  position_ms: z.number(),
  duration_ms: z.number(),
})
export type Position = z.infer<typeof PositionSchema>

/** Everything the player knows about the track beyond its tags. */
export const TrackDetailsSchema = z.object({
  track_count: z.number().nullable(),
  disc_count: z.number().nullable(),
  play_count: z.number().nullable(),
  skip_count: z.number().nullable(),
  channels: z.number().nullable(),
  sample_rate: z.number().nullable(),
  bitrate: z.number().nullable(),
  publisher: z.string(),
  composer: z.string(),
  comment: z.string(),
  grouping: z.string(),
  rating_album: z.string(),
  encoder: z.string(),
  kind: z.string(),
  format: z.string(),
  size: z.string(),
  date_modified: z.string(),
  last_played: z.string(),
})
export type TrackDetails = z.infer<typeof TrackDetailsSchema>

/** The output devices MusicBee can play through, and the one it is using. */
export const OutputDevicesSchema = z.object({
  active: z.string(),
  devices: z.array(z.string()),
})
export type OutputDevices = z.infer<typeof OutputDevicesSchema>

export const SystemInfoSchema = z.object({
  plugin_version: z.string(),
  protocol_version: z.number(),
})
export type SystemInfo = z.infer<typeof SystemInfoSchema>

/**
 * The schema that parses each op's answer.
 *
 * `satisfies` ties every key to an op name, so an op added to `OpRequests`
 * without a schema here fails the build rather than reaching the client
 * unvalidated.
 */
export const OpResponseSchemas = {
  system_info: SystemInfoSchema,

  player_play: EmptySchema,
  player_pause: EmptySchema,
  player_play_pause: EmptySchema,
  player_stop: EmptySchema,
  player_next: EmptySchema,
  player_previous: EmptySchema,
  player_set_scrobbling: ScrobblingSchema,
  player_set_stop_after_current: ScrobblingSchema,
  player_status: PlayerStatusSchema,
  player_set_volume: z.object({ volume: z.number() }),
  player_set_mute: z.object({ muted: z.boolean() }),
  player_set_shuffle: z.object({ mode: ShuffleModeSchema }),
  player_set_repeat: z.object({ mode: RepeatModeSchema }),
  player_output: OutputDevicesSchema,
  player_set_output: OutputDevicesSchema,

  track_get: TrackSchema,

  now_playing_state: NowPlayingStateSchema,
  now_playing_details: TrackDetailsSchema,
  now_playing_position: PositionSchema,
  now_playing_lyrics: LyricsSchema,
  now_playing_seek: PositionSchema,
  now_playing_set_rating: z.object({ rating: z.number().nullable() }),
  now_playing_set_lfm: z.object({ lfm_status: LastfmStatusSchema }),

  now_playing_list: QueuePageSchema,
  now_playing_list_play: EmptySchema,
  now_playing_list_remove: EmptySchema,
  now_playing_list_move: EmptySchema,
  now_playing_list_clear: EmptySchema,
  now_playing_queue: EmptySchema,

  library_genres: pageSchema(GenreEntrySchema),
  library_artists: pageSchema(ArtistEntrySchema),
  library_albums: pageSchema(AlbumEntrySchema),
  library_tracks: pageSchema(TrackSchema),
  library_radio: pageSchema(RadioEntrySchema),
  library_play_all: EmptySchema,
  /** How many tracks the scope selected and queued. */
  library_queue: z.object({ count: z.number() }),

  playlist_list: pageSchema(PlaylistEntrySchema),
  playlist_play: EmptySchema,
  playlist_tracks: PlaylistPageSchema,
} satisfies Record<keyof OpRequests, z.ZodType>

/** The `data` each op answers with, projected from the schemas above. */
export type OpResponses = {
  [K in keyof typeof OpResponseSchemas]: z.infer<(typeof OpResponseSchemas)[K]>
}

/**
 * Event payloads.
 *
 * Most are markers meaning "re-query" and carry `{}`; the typed ones are the
 * events that genuinely carry their new state, which is why patching from them
 * rather than refetching is correct in exactly those cases.
 *
 * `connection_changed` is this client's own, not the server's.
 */
export const EventPayloadSchemas = {
  connection_changed: z.object({ connected: z.boolean(), retrying: z.boolean() }),
  /** The server refused this browser's token. Client-side, like `connection_changed`. */
  auth_required: EmptySchema,
  play_state_changed: z.object({ play_state: PlayStateSchema }),
  volume_changed: z.object({ volume: z.number() }),
  mute_changed: z.object({ muted: z.boolean() }),
  /**
   * The three MusicBee announces to nobody.
   *
   * They reach us from the core's poll rather than from an event, which is what
   * makes a change made in MusicBee's own window visible here at all.
   */
  shuffle_changed: z.object({ shuffle: ShuffleModeSchema }),
  repeat_changed: z.object({ repeat: RepeatModeSchema }),
  scrobbling_changed: z.object({ scrobbling: z.boolean() }),
  stop_after_current_changed: z.object({ stop_after_current: z.boolean() }),
  now_playing_changed: z.object({
    artist: z.string(),
    title: z.string(),
    album: z.string(),
    path: z.string(),
  }),
  now_playing_lyrics_changed: EmptySchema,
  now_playing_list_changed: EmptySchema,
  cover_cache_changed: z.object({ building: z.boolean() }),
  library_changed: EmptySchema,
  server_shutdown: EmptySchema,
}

/** The payload each event carries, projected from the schemas above. */
export type EventPayloads = {
  [K in keyof typeof EventPayloadSchemas]: z.infer<(typeof EventPayloadSchemas)[K]>
}

/**
 * Fails the build if the two halves of the event catalog drift apart: an event
 * with a payload but no name in `WireEvent`, or a name with no payload.
 *
 * Both live here rather than beside `WireEvent` because this is the module that
 * can see both without importing itself back.
 */
type AllEventsNamed = Exclude<keyof EventPayloads, EventName> extends never ? true : never
type AllNamesTyped = Exclude<EventName, keyof EventPayloads> extends never ? true : never
const eventCatalogAgrees: AllEventsNamed & AllNamesTyped = true
void eventCatalogAgrees
