/**
 * How a track's artist line breaks into names that can each be opened.
 *
 * Kept apart from the component so the rule can be tested without mounting it.
 */

import type { Track } from '../api/types'

/** One name in the line, and what joins it to the name before. */
export interface ArtistPart {
  /** The name the library files the artist under. */
  name: string
  /** What the line shows for it. */
  label: string
  joiner: 'none' | 'comma' | 'feat'
}

/**
 * The parts of a track's artist line.
 *
 * Only a split track breaks up: more than one artist, or a guest. Its artists
 * come first, then the guests after "feat.", which is how MusicBee's splitter
 * reads them back. Anything else is its display string, the text MusicBee
 * shows, as one part named by the one artist the server files it under.
 */
export function artistParts(track: Track | null | undefined): ArtistPart[] {
  if (!track) return []
  const leads = track.artist_credits.filter((c) => c.role === 'artist' || c.role === 'guest')
  if (leads.length < 2) {
    const [name] = track.artist
    if (name === undefined || track.artist_display === '') return []
    return [{ name, label: track.artist_display, joiner: 'none' }]
  }
  const ordered = [
    ...leads.filter((c) => c.role === 'artist'),
    ...leads.filter((c) => c.role === 'guest'),
  ]
  const parts: ArtistPart[] = []
  for (const [i, credit] of ordered.entries()) {
    let joiner: ArtistPart['joiner'] = 'comma'
    if (i === 0) joiner = 'none'
    else if (credit.role === 'guest' && ordered[i - 1]?.role === 'artist') joiner = 'feat'
    parts.push({ name: credit.name, label: credit.name, joiner })
  }
  return parts
}
