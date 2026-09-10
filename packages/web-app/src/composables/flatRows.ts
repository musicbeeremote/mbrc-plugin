/**
 * The rows of whichever flat library level is showing.
 *
 * One list serves all three rather than one each: only a single level is on
 * screen at a time, and a list per level would keep three scroll positions that
 * all have to be reset anyway. Albums are not here - they are a cover grid,
 * whose rows hold several items each.
 *
 * The three shapes are told apart by what they carry rather than by a tag: a
 * track has a path, and only a genre row is named after a genre.
 */

import type { ArtistEntry, GenreEntry, Track } from '../api/types'

export type FlatRow = GenreEntry | ArtistEntry | Track

export function isGenre(row: FlatRow): row is GenreEntry {
  return 'genre' in row && !('src' in row)
}

export function isArtist(row: FlatRow): row is ArtistEntry {
  return 'artist' in row && !('src' in row)
}

export function isTrack(row: FlatRow): row is Track {
  return 'src' in row
}
