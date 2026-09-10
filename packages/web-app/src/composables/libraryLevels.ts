/**
 * The library's browse levels, and the order each opens in.
 */

import type { LibraryScope } from '../api/ops'

/**
 * The four browse levels, which are also the four tabs.
 *
 * A tab is an entry point rather than a separate mode: picking Genres and
 * drilling in lands on the same Artists level the Artists tab opens on, with a
 * genre in scope. That is how the Android app's `LibraryTab` works, and it means
 * one set of levels serves both.
 */
export const LibraryLevel = {
  Genres: 'genres',
  Artists: 'artists',
  Albums: 'albums',
  Tracks: 'tracks',
} as const
export type LibraryLevel = (typeof LibraryLevel)[keyof typeof LibraryLevel]

/** Whether a string names a level, for reading one out of a URL. */
export function isLibraryLevel(value: unknown): value is LibraryLevel {
  return Object.values(LibraryLevel).includes(value as LibraryLevel)
}

/**
 * The order each level opens in.
 *
 * MusicBee's own order is the protocol's default, and for a browse list it is
 * effectively arbitrary - genres come back in the order the library met them.
 * A reader expects a name list alphabetical, so the client asks for that rather
 * than the server deciding for every client.
 *
 * Tracks are the exception: a flat list of thousands sorted by title is a phone
 * book. Grouping by album puts each record together, in its own running order,
 * which is how a library is read.
 */
export const DEFAULT_SORT: Record<LibraryLevel, string> = {
  [LibraryLevel.Genres]: 'name',
  [LibraryLevel.Artists]: 'name',
  [LibraryLevel.Albums]: 'name',
  [LibraryLevel.Tracks]: 'album',
}

/** Inside one album there is only one order worth having. */
export const ALBUM_TRACK_SORT = 'track'

/**
 * The orders each level can be asked for, as the server spells them.
 *
 * A name list has one, so its control is a direction and nothing more. The two
 * lists with real tags behind them have the fields those tags carry, in the
 * order a reader is likely to want them rather than the order they were added.
 */
export const SORT_FIELDS: Record<LibraryLevel, string[]> = {
  [LibraryLevel.Genres]: ['name'],
  [LibraryLevel.Artists]: ['name'],
  [LibraryLevel.Albums]: ['name', 'artist', 'year'],
  [LibraryLevel.Tracks]: [
    'title',
    'artist',
    'album',
    'album_artist',
    'track',
    'year',
    'rating',
    'date_added',
  ],
}

/** Where in the library a view is: a level, and the filters that got it there. */
export interface Position {
  level: LibraryLevel
  scope: LibraryScope
}

/**
 * The order to keep when moving to another level.
 *
 * An order is a reading preference, not a property of the list in front of you,
 * so it travels with you rather than resetting at every step. It is dropped
 * only where the level cannot be asked for it: albums have no track number.
 */
export function carriedSort(
  level: LibraryLevel,
  sort: string | undefined,
  descending: boolean,
): { sort?: string; descending?: boolean } {
  if (sort === undefined || !SORT_FIELDS[level].includes(sort)) return {}
  return { sort, descending }
}

/** The order a position opens in when the address does not name one. */
export function defaultSort({ level, scope }: Position): string {
  return scope.album === undefined ? DEFAULT_SORT[level] : ALBUM_TRACK_SORT
}

/**
 * Which tab a position belongs under.
 *
 * Derived from the scope rather than remembered, because the URL is the whole
 * state: the tab is the shallowest thing that had to be picked to get here, so
 * a genre in scope means Genres however deep the drill-down went.
 */
export function libraryTab({ level, scope }: Position): LibraryLevel {
  if (scope.genre !== undefined) return LibraryLevel.Genres
  if (scope.artist !== undefined) return LibraryLevel.Artists
  if (scope.album !== undefined) return LibraryLevel.Albums
  return level
}

/** Drops the scope keys that are not set, so a position compares by value. */
function prune({ level, scope }: Position): Position {
  const kept: LibraryScope = {}
  if (scope.genre !== undefined) kept.genre = scope.genre
  if (scope.artist !== undefined) kept.artist = scope.artist
  if (scope.album !== undefined) kept.album = scope.album
  return { level, scope: kept }
}

/**
 * The position one level out, or null at a tab's root.
 *
 * Back is computed, not popped off a stack: a link opened straight into an
 * album has no stack behind it and still has somewhere to go up to.
 */
export function parentPosition({ scope }: Position): Position | null {
  const { genre, artist, album } = scope
  if (album !== undefined) return prune({ level: LibraryLevel.Albums, scope: { genre, artist } })
  if (artist !== undefined) return prune({ level: LibraryLevel.Artists, scope: { genre } })
  if (genre !== undefined) return prune({ level: LibraryLevel.Genres, scope: {} })
  return null
}
