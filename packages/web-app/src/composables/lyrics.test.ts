import { describe, expect, it } from 'vitest'

import { activeLyricLine } from './lyrics'
import { LyricsType } from '../api/types'

const SYNCED = {
  type: LyricsType.Synced,
  lines: [
    { text: 'first', at_ms: 0 },
    { text: 'second', at_ms: 12_340 },
    { text: 'third', at_ms: 15_900 },
  ],
}

describe('the line being sung', () => {
  // The highlight advancing is the whole feature: it has to land on the line
  // whose time has come and stay there until the next one does.
  it.each([
    [0, 0],
    [12_339, 0],
    [12_340, 1],
    [15_899, 1],
    [15_900, 2],
    [900_000, 2],
  ])('at %ims is line %i', (positionMs, expected) => {
    expect(activeLyricLine(SYNCED, positionMs)).toBe(expected)
  })

  // Plain lyrics carry no clock, so there is no line to point at. Guessing one
  // would highlight words the singer is not on.
  it('points at nothing for lyrics that carry no timings', () => {
    const plain = { type: LyricsType.Plain, lines: [{ text: 'a' }, { text: 'b' }] }
    expect(activeLyricLine(plain, 5000)).toBe(-1)
  })

  it('points at nothing when there are no lyrics at all', () => {
    expect(activeLyricLine({ type: LyricsType.None, lines: [] }, 5000)).toBe(-1)
  })

  // A track whose first line starts after the intro has no line before it.
  it('points at nothing before the first line is due', () => {
    const late = { type: LyricsType.Synced, lines: [{ text: 'late', at_ms: 8000 }] }
    expect(activeLyricLine(late, 7999)).toBe(-1)
    expect(activeLyricLine(late, 8000)).toBe(0)
  })
})
