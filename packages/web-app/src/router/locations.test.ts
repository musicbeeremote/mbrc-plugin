import { describe, expect, it } from 'vitest'

import { LibraryLevel, carriedSort, libraryTab, parentPosition } from '../composables/libraryLevels'

import {
  RouteName,
  libraryRoute,
  openPlaylistFromRoute,
  playlistPathFromRoute,
  playlistQueryFieldFromRoute,
  playlistQueryFromRoute,
  playlistsRoute,
  playlistTracksRoute,
  positionFromRoute,
  wideRedirect,
} from './locations'

describe('a library address', () => {
  it('reads a level and its filters out of the URL', () => {
    const at = positionFromRoute('albums', { artist: 'Miles Davis', q: 'blue' })

    expect(at.level).toBe(LibraryLevel.Albums)
    expect(at.scope).toStrictEqual({ artist: 'Miles Davis' })
    expect(at.query).toBe('blue')
  })

  // A URL is typed by hand and edited in the bar. The library's front page is a
  // better answer to a typo than a blank pane.
  it('falls back to a real level rather than showing nothing', () => {
    expect(positionFromRoute('sideways', {}).level).toBe(LibraryLevel.Artists)
    expect(positionFromRoute(undefined, {}).level).toBe(LibraryLevel.Artists)
  })

  // Records filed under no artist are a group you can open, and `?artist=` is
  // what naming it looks like. Read as absent, it asked for the whole library.
  it('keeps an empty scope value apart from a missing one', () => {
    expect(positionFromRoute('albums', { artist: '' }).scope).toStrictEqual({ artist: '' })
    expect(positionFromRoute('albums', {}).scope).toStrictEqual({})
  })

  it('keeps an empty album apart from a missing one', () => {
    expect(positionFromRoute('tracks', { artist: '', album: '' }).scope).toStrictEqual({
      artist: '',
      album: '',
    })
  })

  it('leaves an empty scope out of the URL entirely', () => {
    const route = libraryRoute({ level: LibraryLevel.Genres, scope: {} }) as {
      query: Record<string, string>
    }
    expect(route.query).toStrictEqual({})
  })

  it('round-trips a position through its own address', () => {
    const position = { level: LibraryLevel.Tracks, scope: { genre: 'Jazz', album: 'Kind of Blue' } }
    const route = libraryRoute(position, { query: ' blue ' }) as {
      params: { level: string }
      query: Record<string, string>
    }

    const back = positionFromRoute(route.params.level, route.query)
    expect(back.level).toBe(position.level)
    expect(back.scope).toStrictEqual(position.scope)
    expect(back.query).toBe('blue')
  })
})

describe('where a library position sits', () => {
  it('names the tab by the shallowest filter, however deep it went', () => {
    expect(libraryTab({ level: LibraryLevel.Albums, scope: { genre: 'Jazz', artist: 'Miles' } })).toBe(
      LibraryLevel.Genres,
    )
    expect(libraryTab({ level: LibraryLevel.Tracks, scope: { album: 'Kind of Blue' } })).toBe(
      LibraryLevel.Albums,
    )
    expect(libraryTab({ level: LibraryLevel.Tracks, scope: {} })).toBe(LibraryLevel.Tracks)
  })

  // Back is computed, not popped: a link opened straight into an album has no
  // stack behind it and still has somewhere to go up to.
  it('walks one filter back out, without a trail to pop', () => {
    expect(
      parentPosition({ level: LibraryLevel.Tracks, scope: { artist: 'Miles', album: 'Kind of Blue' } }),
    ).toStrictEqual({ level: LibraryLevel.Albums, scope: { artist: 'Miles' } })

    expect(parentPosition({ level: LibraryLevel.Albums, scope: { artist: 'Miles' } })).toStrictEqual({
      level: LibraryLevel.Artists,
      scope: {},
    })

    expect(parentPosition({ level: LibraryLevel.Artists, scope: {} })).toBeNull()
  })
})

describe('the order carried between levels', () => {
  // An order is how the reader wants lists read, not a property of the list in
  // front of them, so going up a level must not silently reset it.
  it('keeps an order the next level can also be asked for', () => {
    expect(carriedSort(LibraryLevel.Tracks, 'year', true)).toStrictEqual({
      sort: 'year',
      descending: true,
    })
    expect(carriedSort(LibraryLevel.Albums, 'year', false)).toStrictEqual({
      sort: 'year',
      descending: false,
    })
  })

  it('drops one the next level has no column for', () => {
    expect(carriedSort(LibraryLevel.Albums, 'track', true)).toStrictEqual({})
    expect(carriedSort(LibraryLevel.Genres, 'rating', false)).toStrictEqual({})
    expect(carriedSort(LibraryLevel.Tracks, undefined, true)).toStrictEqual({})
  })

  it('survives a round trip out of the URL', () => {
    const route = libraryRoute(
      { level: LibraryLevel.Albums, scope: { artist: 'Bob Mould' } },
      carriedSort(LibraryLevel.Albums, 'year', true),
    ) as { params: { level: string }; query: Record<string, string> }

    const back = positionFromRoute(route.params.level, route.query)
    expect(back.sort).toBe('year')
    expect(back.descending).toBe(true)
    expect(back.scope).toStrictEqual({ artist: 'Bob Mould' })
  })
})

describe('now playing on a wide window', () => {
  // It owns the right rail there, so landing on it drew the same pane twice.
  it('sends a wide window somewhere it is not already showing', () => {
    expect(wideRedirect(true, RouteName.Playing)).toBe('/library')
  })

  it('leaves a narrow window on it, where it is the only pane', () => {
    expect(wideRedirect(false, RouteName.Playing)).toBeNull()
  })

  // The name is undefined until the first navigation resolves, and reading it
  // once at setup is what let the duplicate through.
  it('stays put for a route that has not resolved yet', () => {
    expect(wideRedirect(true, undefined)).toBeNull()
    expect(wideRedirect(true, RouteName.Library)).toBeNull()
  })
})

describe('a playlist folder address', () => {
  it('round-trips a folder path', () => {
    const route = playlistsRoute(['tracks', 'subplaylst']) as { query: Record<string, string> }
    expect(route.query).toStrictEqual({ path: 'tracks/subplaylst' })
    expect(playlistPathFromRoute(route.query)).toStrictEqual(['tracks', 'subplaylst'])
  })

  it('says the root is the root, not a folder named nothing', () => {
    expect((playlistsRoute([]) as { query: Record<string, string> }).query).toStrictEqual({})
    expect(playlistPathFromRoute({})).toStrictEqual([])
    expect(playlistPathFromRoute({ path: '' })).toStrictEqual([])
  })
})

describe('an open playlist address', () => {
  const url = String.raw`C:\Music\Playlists\Caraven.mbp`

  it('carries the playlist and the folder it was opened from', () => {
    const route = playlistTracksRoute(url, ['tracks']) as { query: Record<string, string> }
    expect(route.query).toStrictEqual({ playlist: url, path: 'tracks' })
    expect(openPlaylistFromRoute(route.query)).toBe(url)
    expect(playlistPathFromRoute(route.query)).toStrictEqual(['tracks'])
  })

  it('round-trips a search, so a reload and a shared link keep it', () => {
    const route = playlistTracksRoute(url, [], { search: 'phone' }) as { query: Record<string, string> }
    expect(route.query.q).toBe('phone')
    expect(playlistQueryFromRoute(route.query)).toBe('phone')
  })

  /** An empty box is no search, and a URL should not carry one that says nothing. */
  it('leaves a blank search out of the address', () => {
    const blank = playlistTracksRoute(url, [], { search: '   ' }) as { query: Record<string, string> }
    expect(blank.query.q).toBeUndefined()
    expect(playlistQueryFromRoute(blank.query)).toBe('')
    expect(playlistQueryFromRoute({})).toBe('')
  })

  it('trims what it carries, so two spellings of one search are one address', () => {
    const route = playlistTracksRoute(url, [], { search: '  phone  ' }) as { query: Record<string, string> }
    expect(route.query.q).toBe('phone')
  })

  it('is a folder address when no playlist is open', () => {
    expect(openPlaylistFromRoute({ path: 'tracks' })).toBeUndefined()
  })
})

describe('a playlist search field', () => {
  const url = String.raw`C:\Music\Caraven.mbp`

  it('carries the column a search was pointed at', () => {
    const route = playlistTracksRoute(url, [], {
      search: 'caravan',
      field: 'artist',
    }) as { query: Record<string, string> }
    expect(route.query.qf).toBe('artist')
    expect(playlistQueryFieldFromRoute(route.query)).toBe('artist')
  })

  /** Every column is the default, so the common case leaves no trace. */
  it('leaves the address alone when the search reads them all', () => {
    const route = playlistTracksRoute(url, [], { search: 'caravan', field: 'any' }) as {
      query: Record<string, string>
    }
    expect(route.query.qf).toBeUndefined()
    expect(playlistQueryFieldFromRoute({})).toBe('any')
  })

  /** A column with nothing to find in it is not a search, so it is not an address. */
  it('drops the column when there is no term beside it', () => {
    const route = playlistTracksRoute(url, [], { field: 'title' }) as {
      query: Record<string, string>
    }
    expect(route.query.q).toBeUndefined()
    expect(route.query.qf).toBeUndefined()
  })

  it('reads an unknown column as every column', () => {
    expect(playlistQueryFieldFromRoute({ qf: 'genre' })).toBe('any')
    expect(playlistQueryFieldFromRoute({ qf: '' })).toBe('any')
  })
})
