import { describe, expect, it } from 'vitest'

import { LibraryLevel, albumYear, carriedSort } from './libraryLevels'

describe('ordering by original year', () => {
  it('is offered on both lists with tags behind them', () => {
    expect(carriedSort(LibraryLevel.Albums, 'original_year', true)).toStrictEqual({
      sort: 'original_year',
      descending: true,
    })
    expect(carriedSort(LibraryLevel.Tracks, 'original_year', false)).toStrictEqual({
      sort: 'original_year',
      descending: false,
    })
  })

  // A remaster sits among the year it first came out, so its tile must say that
  // year or the grid reads as out of order.
  it('dates an album tile by the year the list is ordered by', () => {
    const remaster = { album: 'Paranoid', artist: 'Black Sabbath', count: 8, year: 2009, original_year: 1970 }
    const yearOnly = { album: 'Fireball', artist: 'Deep Purple', count: 7, year: 1971 }

    expect(albumYear(remaster, 'original_year')).toBe(1970)
    expect(albumYear(yearOnly, 'original_year')).toBe(1971)
    expect(albumYear(remaster, 'year')).toBe(2009)
    expect(albumYear(remaster, 'name')).toBe(2009)
    expect(albumYear({ album: 'Undated', artist: 'X', count: 1 }, 'original_year')).toBeUndefined()
  })
})
