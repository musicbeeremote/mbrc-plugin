import { describe, expect, it } from 'vitest'

import type { Credit } from '../api/credits'
import type { Track } from '../api/types'

import { artistParts } from './artistParts'

function track(display: string, credits: Credit[]): Track {
  return {
    src: '/a.mp3',
    artist: credits.filter((c) => c.role !== 'performer').map((c) => c.name),
    artist_display: display,
    artist_credits: credits,
    composer: [],
    title: 'T',
    album: 'Album',
    album_artist: '',
    track_no: 1,
    disc_no: 1,
    genre: [],
    year: null,
    duration_ms: null,
    rating: null,
    date_added: null,
  }
}

const JOINERS = { none: '', comma: ', ', feat: ' feat. ' } as const

const show = (t: Track) =>
  artistParts(t)
    .map((p) => `${JOINERS[p.joiner]}[${p.label}]`)
    .join('')

describe('the parts of an artist line', () => {
  it('links each artist of a split track, guests after feat.', () => {
    const t = track('Tico feat. Wisin & Yandel, Gadiel', [
      { name: 'Tico', role: 'artist' },
      { name: 'Wisin & Yandel', role: 'guest' },
      { name: 'Gadiel', role: 'guest' },
    ])
    expect(show(t)).toBe('[Tico] feat. [Wisin & Yandel], [Gadiel]')
  })

  it('keeps two lead artists apart without a feat.', () => {
    const t = track('A & B', [
      { name: 'A', role: 'artist' },
      { name: 'B', role: 'artist' },
    ])
    expect(show(t)).toBe('[A], [B]')
  })

  // The text MusicBee shows is the one the user knows the track by.
  it('shows a track nobody split as its display string, one link', () => {
    const t = track('ProleteR & Mister Colfer', [
      { name: 'ProleteR & Mister Colfer', role: 'artist' },
      { name: 'Engineer', role: 'performer', detail: 'mix' },
    ])
    expect(artistParts(t)).toStrictEqual([
      { name: 'ProleteR & Mister Colfer', label: 'ProleteR & Mister Colfer', joiner: 'none' },
    ])
  })

  it('has nothing to link with no track or no artist', () => {
    expect(artistParts(null)).toStrictEqual([])
    expect(artistParts(track('', []))).toStrictEqual([])
  })
})
