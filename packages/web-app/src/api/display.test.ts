import { describe, expect, it } from 'vitest'

import { coverUrl, formatDuration, nowPlayingCoverUrl, trackLabel } from './display'

describe('formatDuration', () => {
  it('renders under an hour as m:ss', () => {
    expect(formatDuration(0)).toBe('0:00')
    expect(formatDuration(9_000)).toBe('0:09')
    expect(formatDuration(240_000)).toBe('4:00')
    expect(formatDuration(3_599_000)).toBe('59:59')
  })

  it('renders an hour and over as h:mm:ss', () => {
    expect(formatDuration(3_600_000)).toBe('1:00:00')
    expect(formatDuration(3_661_000)).toBe('1:01:01')
  })

  // The protocol returns null when a duration tag did not parse. Rendering that
  // as 0:00 would claim a zero-length track instead of an unknown one.
  it('renders an unknown duration as a placeholder, not zero', () => {
    expect(formatDuration(null)).toBe('--:--')
    expect(formatDuration(undefined)).toBe('--:--')
    expect(formatDuration(Number.NaN)).toBe('--:--')
    expect(formatDuration(-1)).toBe('--:--')
  })
})

describe('nowPlayingCoverUrl', () => {
  // The album cache is built at a grid cell's size, so the pane must not be fed
  // from it: this route renders the source artwork at the size the pane wants.
  it('asks the now-playing route rather than the album cache', () => {
    const url = nowPlayingCoverUrl('da39a3ee5e6b4b0d3255bfef95601890afd80709')
    expect(url).toBe(
      '/api/cover/now-playing?v=da39a3ee5e6b4b0d3255bfef95601890afd80709',
    )
  })

  // The server ignores `v`; it is there so the browser can tell one track's art
  // from the next and still cache each immutably.
  it('changes with the track so a cached image is never the wrong one', () => {
    expect(nowPlayingCoverUrl('aaa')).not.toBe(nowPlayingCoverUrl('bbb'))
    expect(nowPlayingCoverUrl(undefined)).toBeNull()
    expect(nowPlayingCoverUrl('')).toBeNull()
  })
})

describe('coverUrl', () => {
  it('addresses a cover by its content hash', () => {
    expect(coverUrl('da39a3ee5e6b4b0d3255bfef95601890afd80709')).toBe(
      '/api/cover/da39a3ee5e6b4b0d3255bfef95601890afd80709',
    )
  })

  // cover_hash is omitted when the album has no cached cover, and an <img> with
  // an empty src re-requests the page itself.
  it('yields nothing when the album has no cached cover', () => {
    expect(coverUrl(undefined)).toBeNull()
    expect(coverUrl('')).toBeNull()
  })
})

describe('trackLabel', () => {
  it('uses the title when there is one', () => {
    expect(trackLabel('Emerald Sword', 'Z:/m/02 - Emerald Sword.mp3')).toBe('Emerald Sword')
  })

  // "Untitled" three times in a row names nothing; the file name is the one
  // thing an untitled track always has that tells it from the next one.
  it('falls back to the file name, without its extension', () => {
    expect(trackLabel('', String.raw`Z:\music\Emerald Sword.mp3`)).toBe('Emerald Sword')
    expect(trackLabel('   ', 'Z:/m/track.flac')).toBe('track')
  })

  it('keeps a name that has no extension to strip', () => {
    expect(trackLabel('', 'Z:/m/rawfile')).toBe('rawfile')
  })
})
