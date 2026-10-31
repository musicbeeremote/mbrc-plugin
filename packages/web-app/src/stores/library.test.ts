import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { call, lastCall } from '../api/fake-client'

import { OpError } from '../api/parse'
import { ErrorCode } from '../api/types'

import { libraryTab, parentPosition } from '../composables/libraryLevels'

import { LibraryLevel, useLibraryStore } from './library'
import { drill, openTab, search } from './library.fixtures'

vi.mock(import('../api/client'), () => import('../api/fake-client'))

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
  call.mockResolvedValue({ total: 0, offset: 0, items: [] })
})

describe('browsing', () => {
  it('opens a tab at its own level with nothing in scope', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Genres)

    const [op, data] = lastCall()
    expect(op).toBe('library_genres')
    expect(data).toMatchObject({ offset: 0 })
  })

  // Drilling accumulates rather than replaces: an album reached through an
  // artist has to keep the artist, or going back lands somewhere else.
  it('carries the scope down and hands it back on the way out', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Genres)
    await drill(library, LibraryLevel.Artists, { genre: 'Jazz' })

    expect(lastCall()[0]).toBe('library_artists')
    expect(lastCall()[1]).toMatchObject({ genre: 'Jazz' })

    await drill(library, LibraryLevel.Albums, { artist: 'Miles Davis' })
    expect(lastCall()[1]).toMatchObject({ artist: 'Miles Davis' })
    expect(library.scope).toStrictEqual({ genre: 'Jazz', artist: 'Miles Davis' })

    expect(parentPosition({ level: library.level, scope: library.scope })).toStrictEqual({
      level: LibraryLevel.Artists,
      scope: { genre: 'Jazz' },
    })
  })

  it('keeps naming the tab it started from however deep it goes', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Genres)
    await drill(library, LibraryLevel.Artists, { genre: 'Jazz' })
    await drill(library, LibraryLevel.Albums, { artist: 'Miles Davis' })

    expect(libraryTab({ level: library.level, scope: library.scope })).toBe(LibraryLevel.Genres)
    expect(library.level).toBe(LibraryLevel.Albums)
  })

  it('has nowhere further out to go at a tab root', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Artists)

    expect(parentPosition({ level: library.level, scope: library.scope })).toBeNull()
  })

  // Paging asks for what is not on screen yet. Restarting from 0 would append
  // the first page to itself.
  it('resumes paging from what is already shown', async () => {
    const library = useLibraryStore()
    call.mockResolvedValue({ total: 300, offset: 0, items: Array.from({ length: 100 }) })
    await openTab(library, LibraryLevel.Artists)
    expect(library.hasMore).toBe(true)

    await library.load(true)
    expect(lastCall()[1]).toMatchObject({ offset: 100 })
  })
})

describe('a request the server refuses', () => {
  // Left unhandled a refusal reads as a pane where nothing happened, which is
  // indistinguishable from a library with nothing in it.
  it('says why instead of leaving the pane looking empty', async () => {
    const library = useLibraryStore()
    call.mockRejectedValue(
      new OpError({ code: ErrorCode.InvalidField, message: 'unknown sort field: year', field: 'sort' }),
    )

    await openTab(library, LibraryLevel.Albums)

    expect(library.error).toBe('unknown sort field: year')
    expect(library.loading).toBe(false)
  })

  it('clears the last failure once a read succeeds', async () => {
    const library = useLibraryStore()
    call.mockRejectedValue(new OpError({ code: ErrorCode.Internal, message: 'boom' }))
    await openTab(library, LibraryLevel.Albums)
    expect(library.error).toBe('boom')

    call.mockResolvedValue({ total: 0, offset: 0, items: [] })
    await openTab(library, LibraryLevel.Albums)
    expect(library.error).toBeNull()
  })
})

describe('the order a level opens in', () => {
  // MusicBee's own order is the protocol default and is effectively arbitrary
  // for a browse list, so a name list has to ask for the order a reader expects.
  it.each([
    [LibraryLevel.Genres, 'library_genres', 'name'],
    [LibraryLevel.Artists, 'library_artists', 'name'],
    [LibraryLevel.Albums, 'library_albums', 'name'],
  ])('opens %s sorted by name', async (level, op, sort) => {
    const library = useLibraryStore()
    await openTab(library, level)
    expect(lastCall()[0]).toBe(op)
    expect(lastCall()[1]).toMatchObject({ sort })
  })

  // A flat list of thousands sorted by title is a phone book; grouping by album
  // puts each record together in its own running order.
  it('opens a flat track list grouped by album', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Tracks)
    expect(lastCall()[1]).toMatchObject({ sort: 'album' })
  })

  it('reads one album in its own running order', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Albums)
    await drill(library, LibraryLevel.Tracks, { album: 'Kind of Blue' })
    expect(lastCall()[1]).toMatchObject({ album: 'Kind of Blue', sort: 'track' })
  })
})

describe('opening an album', () => {
  // Three bands have an album called Live. Asking for the title alone returns
  // all three records as one album, so the artist has to go with it.
  it('names the artist as well as the title', async () => {
    const library = useLibraryStore()
    await library.show({
      level: LibraryLevel.Tracks,
      scope: { artist: 'AC/DC', album: 'Live' },
    })

    const [op, data] = lastCall()
    expect(op).toBe('library_tracks')
    expect(data).toMatchObject({ album: 'Live', artist: 'AC/DC' })
  })
})

describe('search', () => {
  it('narrows the level it is run on', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Albums)
    await search(library, 'blue')

    const [op, data] = lastCall()
    expect(op).toBe('library_albums')
    expect(data).toMatchObject({ query: 'blue' })
  })

  // A box typed into and cleared is not a filter that matches nothing, and
  // whitespace is not a search term.
  it('sends no query at all when the box is blank', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Albums)
    await search(library, '   ')

    expect(lastCall()[1]).not.toHaveProperty('query')
  })

  // A term that narrowed a list of artists will rarely match that artist's album
  // titles too, and a level that came back empty would read as an empty library.
  it('does not follow a drill-down into the next level', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Artists)
    await search(library, 'davis')
    await drill(library, LibraryLevel.Albums, { artist: 'Miles Davis' })

    expect(library.query).toBe('')
    expect(lastCall()[1]).not.toHaveProperty('query')
  })
})
