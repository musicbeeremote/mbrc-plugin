/**
 * What an album is beyond its name: who made it, and when.
 *
 * Read off the tracks already on screen rather than asked for. Dated by the
 * earliest year they carry, which is how the album list dates them too: a
 * reissued track stamped with the reissue year must not redate the record.
 */

import type { Track } from '../api/types'

export function albumSummary(tracks: Track[]): string {
  const [first] = tracks
  if (!first) return ''
  const artist = first.album_artist || first.artist || ''
  const years = tracks
    .map((track) => track.year)
    .filter((year): year is number => typeof year === 'number' && year > 0)
  const year = years.length > 0 ? Math.min(...years) : null
  return [artist, year].filter(Boolean).join(' · ')
}
