/**
 * Addresses, as values.
 *
 * Kept apart from the router itself so the mapping between a URL and a place in
 * the library can be tested without pulling in every view the routes name.
 */

import type { RouteLocationRaw } from 'vue-router'

import type { LibraryScope } from '../api/ops'
import type { LibraryLevel, Position } from '../composables/libraryLevels'
import { SORT_FIELDS, isLibraryLevel } from '../composables/libraryLevels'

export const RouteName = {
  Playing: 'playing',
  Queue: 'queue',
  Library: 'library',
  Playlists: 'playlists',
  Radio: 'radio',
} as const
export type RouteName = (typeof RouteName)[keyof typeof RouteName]

/** A query value, which the router types as possibly repeated or absent. */
function one(value: unknown): string | undefined {
  if (typeof value === 'string' && value !== '') return value
  return undefined
}

/**
 * A scope value, where the empty string is an answer rather than the absence of
 * one: it names the records filed under no genre, artist or album at all.
 *
 * Read like the rest, `?artist=` and no artist at all say the same thing, and
 * opening Unknown artist asks for every album in the library.
 */
function scoped(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined
}

/**
 * The library position a URL names.
 *
 * An unreadable level is Artists rather than an error: a URL is typed by hand
 * and edited in the bar, and the library's front page is a better answer to a
 * typo than a blank pane.
 */
export function positionFromRoute(
  level: unknown,
  query: Record<string, unknown>,
): LibraryView {
  const scope: LibraryScope = {}
  const genre = scoped(query.genre)
  const artist = scoped(query.artist)
  const album = scoped(query.album)
  if (genre !== undefined) scope.genre = genre
  if (artist !== undefined) scope.artist = artist
  if (album !== undefined) scope.album = album

  const at = { level: isLibraryLevel(level) ? level : ('artists' as LibraryLevel), scope }
  const sort = one(query.sort)
  return {
    ...at,
    query: one(query.q) ?? '',
    // An order this level cannot be asked for is dropped rather than sent on:
    // the server refuses an unknown field, and the pane would show an error
    // where a hand-edited URL only meant to name a column.
    sort: sort !== undefined && SORT_FIELDS[at.level].includes(sort) ? sort : undefined,
    descending: one(query.order) === 'desc',
    albumArtists: one(query.aa) === '1',
  }
}

/** What a library URL says: where to look, what to look for, in what order. */
export interface LibraryView extends Position {
  query: string
  sort: string | undefined
  descending: boolean
  /** Browse the tag albums are filed under rather than every credit. */
  albumArtists: boolean
}

/** The address of a library position, with anything unset left out of the URL. */
export function libraryRoute(
  { level, scope }: Position,
  {
    query: search = '',
    sort,
    descending = false,
    albumArtists = false,
  }: Partial<Omit<LibraryView, keyof Position>> = {},
): RouteLocationRaw {
  const query: Record<string, string> = {}
  if (scope.genre !== undefined) query.genre = scope.genre
  if (scope.artist !== undefined) query.artist = scope.artist
  if (scope.album !== undefined) query.album = scope.album
  if (search.trim() !== '') query.q = search.trim()
  if (sort !== undefined) query.sort = sort
  if (descending) query.order = 'desc'
  if (albumArtists) query.aa = '1'
  return { name: RouteName.Library, params: { level }, query }
}

/** The address of a playlist folder, as its segments. */
export function playlistsRoute(path: string[]): RouteLocationRaw {
  return {
    name: RouteName.Playlists,
    query: path.length > 0 ? { path: path.join('/') } : {},
  }
}

/**
 * Where a wide window should go instead, or null to stay.
 *
 * Now playing owns the right rail on a wide screen, so it is not a destination
 * there: landing on it would draw the same pane twice. The route name is
 * undefined until the first navigation resolves, which is why this takes it
 * rather than reading it once at setup.
 */
export function wideRedirect(wide: boolean, name: unknown): string | null {
  return wide && name === RouteName.Playing ? '/library' : null
}

/** The folder segments a playlists URL names. */
export function playlistPathFromRoute(query: Record<string, unknown>): string[] {
  return (one(query.path) ?? '').split('/').filter((segment) => segment !== '')
}
