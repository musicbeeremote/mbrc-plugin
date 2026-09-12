/**
 * The V6 payload shapes this app reads, as schemas.
 *
 * Mirrors `docs/protocol-v6.md`. Every shape is a zod schema named `<Name>Schema`
 * with its type inferred from it as `<Name>`, so the validator and the type can
 * never drift: there is only one definition and the type is a projection of it.
 *
 * Objects deliberately strip unknown keys rather than reject them. V6 grows
 * additively and a client that refused a field it had not heard of could never
 * be added to, so an unrecognised field is dropped, not an error.
 *
 * Enumerations stay `as const` objects with the schema derived from them, so the
 * wire spellings live in one place and a call site names a member instead of
 * retyping a literal. TypeScript's own `enum` is not an option here:
 * `erasableSyntaxOnly` rejects it.
 */

import { z } from 'zod'

export const PlayState = {
  Playing: 'playing',
  Paused: 'paused',
  Stopped: 'stopped',
  Undefined: 'undefined',
} as const
export const PlayStateSchema = z.enum(PlayState)
export type PlayState = z.infer<typeof PlayStateSchema>

export const ShuffleMode = {
  Off: 'off',
  Shuffle: 'shuffle',
  AutoDj: 'autodj',
} as const
export const ShuffleModeSchema = z.enum(ShuffleMode)
export type ShuffleMode = z.infer<typeof ShuffleModeSchema>

export const RepeatMode = {
  None: 'none',
  All: 'all',
  One: 'one',
} as const
export const RepeatModeSchema = z.enum(RepeatMode)
export type RepeatMode = z.infer<typeof RepeatModeSchema>

export const LastfmStatus = {
  Normal: 'normal',
  Love: 'love',
  Ban: 'ban',
} as const
export const LastfmStatusSchema = z.enum(LastfmStatus)
export type LastfmStatus = z.infer<typeof LastfmStatusSchema>

/** Note `AddAll` is `add_all`: V6 is snake_case where V4 spelled it `add-all`. */
export const QueueMode = {
  Next: 'next',
  Last: 'last',
  Now: 'now',
  AddAll: 'add_all',
} as const
export const QueueModeSchema = z.enum(QueueMode)
export type QueueMode = z.infer<typeof QueueModeSchema>

export const LyricsType = {
  Synced: 'synced',
  Plain: 'plain',
  None: 'none',
} as const
export const LyricsTypeSchema = z.enum(LyricsType)
export type LyricsType = z.infer<typeof LyricsTypeSchema>

/**
 * The protocol's error codes.
 *
 * `StaleList` is the one the queue acts on rather than merely reports: it means
 * the list moved under a mutation and the page has to be re-read.
 */
export const ErrorCode = {
  MalformedFrame: 'malformed_frame',
  UnsupportedVersion: 'unsupported_version',
  MissingField: 'missing_field',
  InvalidField: 'invalid_field',
  UnknownOp: 'unknown_op',
  Unauthorized: 'unauthorized',
  NotAllowed: 'not_allowed',
  InvalidToken: 'invalid_token',
  StaleList: 'stale_list',
  Internal: 'internal',
  NotFound: 'not_found',
  Unavailable: 'unavailable',
} as const
export const ErrorCodeSchema = z.enum(ErrorCode)
export type ErrorCode = z.infer<typeof ErrorCodeSchema>

/**
 * The error half of a failed response.
 *
 * The code is left as a plain string when it is one this build has not heard of.
 * Refusing to parse an error would turn a server that reported a problem into a
 * client that reports nothing, which is the worse of the two failures.
 */
export const V6ErrorSchema = z.object({
  code: z.union([ErrorCodeSchema, z.string()]),
  message: z.string(),
  field: z.string().optional(),
})
export type V6Error = z.infer<typeof V6ErrorSchema>

/**
 * Ops that answer `{}`.
 *
 * Loose rather than strict: the reply to a command is an empty object today and
 * may carry something later, and a setter is not the place to start failing.
 */
export const EmptySchema = z.looseObject({})
export type Empty = z.infer<typeof EmptySchema>

/** The canonical track, uniform across every domain that returns one. */
export const TrackSchema = z.object({
  src: z.string(),
  artist: z.string(),
  title: z.string(),
  album: z.string(),
  album_artist: z.string(),
  track_no: z.number(),
  disc_no: z.number(),
  genre: z.string(),
  year: z.number().nullable(),
  duration_ms: z.number().nullable(),
  rating: z.number().nullable(),
  date_added: z.string().nullable(),
  /** Album-level content hash; absent when no cached cover exists. */
  cover_hash: z.string().optional(),
})
export type Track = z.infer<typeof TrackSchema>

/** A queue entry: a track plus the three indices the queue ops speak in. */
export const QueueItemSchema = TrackSchema.extend({
  /** The absolute storage index. This is the key every mutation takes. */
  order: z.number(),
  /** Display rank within the returned window. */
  position: z.number(),
  /** Rank in shuffle play order; -1 once the track has been played. */
  play_position: z.number(),
})
export type QueueItem = z.infer<typeof QueueItemSchema>

/**
 * The shared pagination envelope.
 *
 * A function rather than a schema constant, because the envelope is generic over
 * its item: `pageSchema(TrackSchema)` is the schema, `Page<Track>` the type.
 */
export function pageSchema<T extends z.ZodType>(
  item: T,
): z.ZodObject<{ total: z.ZodNumber; offset: z.ZodNumber; items: z.ZodArray<T> }> {
  return z.object({
    total: z.number(),
    offset: z.number(),
    items: z.array(item),
  })
}

export interface Page<T> {
  total: number
  offset: number
  items: T[]
}

/** A queue page also carries the version its `order` values are valid against. */
export const QueuePageSchema = pageSchema(QueueItemSchema).extend({
  version: z.number(),
  /** The run time; absent unless `totals` was asked for. */
  total_duration_ms: z.number().nullish(),
})
export type QueuePage = z.infer<typeof QueuePageSchema>

/** Which column a playlist search reads. `Any` takes all three. */
export const QueryField = {
  Any: 'any',
  Title: 'title',
  Artist: 'artist',
  Album: 'album',
} as const
export type QueryField = (typeof QueryField)[keyof typeof QueryField]

/** A playlist entry: a track plus where it sits and where it is shown. */
export const PlaylistTrackSchema = TrackSchema.extend({
  /** Place in the playlist. Absolute, and the key a mutation takes. */
  order: z.number(),
  /** Rank among the rows returned, which a search makes differ from `order`. */
  position: z.number(),
})
export type PlaylistTrack = z.infer<typeof PlaylistTrackSchema>

/**
 * A playlist page carries the playlist's own name and a version.
 *
 * The version is a string where the queue's is a number: it is a 64-bit hash of
 * the ordered paths, which no JavaScript number can hold exactly. Opaque to a
 * client either way - it is echoed back on a mutation, never interpreted.
 */
export const PlaylistPageSchema = pageSchema(PlaylistTrackSchema).extend({
  name: z.string(),
  version: z.string(),
  /** Summed over what `total` counts; absent unless `totals` was asked for. */
  total_duration_ms: z.number().nullish(),
})
export type PlaylistPage = z.infer<typeof PlaylistPageSchema>

export const GenreEntrySchema = z.object({
  genre: z.string(),
  count: z.number(),
})
export type GenreEntry = z.infer<typeof GenreEntrySchema>

export const ArtistEntrySchema = z.object({
  artist: z.string(),
  count: z.number(),
})
export type ArtistEntry = z.infer<typeof ArtistEntrySchema>

export const AlbumEntrySchema = z.object({
  album: z.string(),
  artist: z.string(),
  count: z.number(),
  cover_hash: z.string().optional(),
  /** Derived from the album's tracks; absent when none of them carry a year. */
  year: z.number().optional(),
})
export type AlbumEntry = z.infer<typeof AlbumEntrySchema>

export const RadioEntrySchema = z.object({
  name: z.string(),
  url: z.string(),
})
export type RadioEntry = z.infer<typeof RadioEntrySchema>

export const PlaylistEntrySchema = z.object({
  url: z.string(),
  name: z.string(),
})
export type PlaylistEntry = z.infer<typeof PlaylistEntrySchema>

export const LyricLineSchema = z.object({
  text: z.string(),
  at_ms: z.number().optional(),
})
export type LyricLine = z.infer<typeof LyricLineSchema>

export const LyricsSchema = z.object({
  type: LyricsTypeSchema,
  // Absent, not empty, when there is nothing to show: the server omits the key
  // entirely for `type: "none"`. Defaulting here keeps every reader able to
  // treat it as a list rather than checking twice.
  lines: z.array(LyricLineSchema).default([]),
})
export type Lyrics = z.infer<typeof LyricsSchema>
