import { describe, expect, it } from 'vitest'

import type { Track } from '../api/types'

import { albumSummary } from './albumSummary'

function track(over: Partial<Track>): Track {
  return {
    src: '/a.mp3',
    artist: 'Artist',
    title: 'T',
    album: 'Album',
    album_artist: 'Album Artist',
    track_no: 1,
    disc_no: 1,
    genre: '',
    year: null,
    duration_ms: null,
    rating: null,
    date_added: null,
    ...over,
  }
}

describe('what an album says beyond its name', () => {
  it('names the artist it is filed under, and the year it came out', () => {
    expect(albumSummary([track({ year: 2012 })])).toBe('Album Artist · 2012')
  })

  // The same rule the album list dates by: a reissued track stamped with the
  // reissue year must not redate the record.
  it('takes the earliest year its tracks carry', () => {
    const tracks = [track({ year: 2020 }), track({ year: 1998 }), track({ year: null })]
    expect(albumSummary(tracks)).toBe('Album Artist · 1998')
  })

  it('says only the artist when nothing is dated', () => {
    expect(albumSummary([track({ year: null })])).toBe('Album Artist')
  })

  it('falls back to the track artist, and says nothing about nothing', () => {
    expect(albumSummary([track({ album_artist: '', year: 1984 })])).toBe('Artist · 1984')
    expect(albumSummary([])).toBe('')
  })
})
