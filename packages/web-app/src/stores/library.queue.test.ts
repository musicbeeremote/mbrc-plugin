import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { QueueMode } from '../api/types'

import type { LibraryScope } from '../api/ops'

import { LibraryLevel, useLibraryStore } from './library'

type Store = ReturnType<typeof useLibraryStore>

/** Opening a tab, as the router does it: a level with nothing in scope. */
function openTab(library: Store, level: LibraryLevel) {
  return library.show({ level, scope: {} })
}

/** Drilling in, as the router does it: the scope in hand plus one more filter. */
function drill(library: Store, level: LibraryLevel, add: LibraryScope) {
  return library.show({ level, scope: { ...library.scope, ...add } })
}

/** Searching, as the router does it: the same position, a different `q`. */
function search(library: Store, term: string) {
  return library.show({ level: library.level, scope: library.scope }, { query: term })
}

// Hoisted with the mock factory, which vitest lifts above every other statement
// in the file: a plain `const` here is not yet initialised when it runs.
const { call } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data: Record<string, unknown>) => Promise<unknown>>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call } as unknown as typeof V6Client,
}))

/** The last call as `[op, data]`, which is what every assertion here is about. */
function lastCall(): [string, Record<string, unknown>] {
  return call.mock.calls.at(-1) as [string, Record<string, unknown>]
}

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
  call.mockResolvedValue({ total: 0, offset: 0, items: [] })
})


describe('queueing', () => {
  // The browser holds one page of the library, so it cannot name an artist's
  // tracks. It names the artist and the server resolves them.
  it('queues a scope by naming it rather than its tracks', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Artists)
    await drill(library, LibraryLevel.Albums, { artist: 'Miles Davis' })
    call.mockResolvedValue({ count: 42 })

    const queued = await library.queueScope({ album: 'Kind of Blue' }, { mode: QueueMode.Next })

    const [op, data] = lastCall()
    expect(op).toBe('library_queue')
    expect(data).toStrictEqual({
      artist: 'Miles Davis',
      album: 'Kind of Blue',
      mode: QueueMode.Next,
    })
    expect(queued).toBe(42)
  })

  it('carries the active search into what it queues', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Tracks)
    await search(library, 'blue')
    call.mockResolvedValue({ count: 3 })

    await library.queueScope({}, { mode: QueueMode.Last })
    expect(lastCall()[1]).toMatchObject({ query: 'blue' })
  })

  it('names a track to start from when one is given', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Tracks)
    call.mockResolvedValue({ count: 9 })

    await library.queueScope({}, { mode: QueueMode.AddAll, play: 'c:/music/track.mp3' })
    expect(lastCall()[1]).toMatchObject({
      mode: QueueMode.AddAll,
      play: 'c:/music/track.mp3',
    })
  })

  // MusicBee's play-all command is the whole library and cannot be narrowed, so
  // over a filtered list it would play something other than what is on screen.
  it('plays the whole library only when nothing narrows the list', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Artists)
    await library.playAll(false)
    expect(lastCall()).toStrictEqual(['library_play_all', { shuffle: false }])
  })

  it('plays what the pane shows once a scope or a search narrows it', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Genres)
    await drill(library, LibraryLevel.Artists, { genre: 'Power Metal' })
    call.mockResolvedValue({ count: 12 })

    await library.playAll(true)

    const [op, data] = lastCall()
    expect(op).toBe('library_queue')
    expect(data).toStrictEqual({
      genre: 'Power Metal',
      mode: QueueMode.Now,
      shuffle: true,
    })
  })

  // A search matches the names on screen; a queue matches track titles and
  // artists. Carrying a term from one into the other queues nothing at all:
  // "punk" names a genre but appears in none of its tracks.
  it('does not carry the search into a row that was named outright', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Genres)
    await search(library, 'punk')

    await library.queueScope({ genre: 'Punk' }, { mode: QueueMode.Last })

    expect(lastCall()[0]).toBe('library_queue')
    expect(lastCall()[1]).toStrictEqual({ genre: 'Punk', mode: 'last' })
  })

  it('keeps the search when queueing the pane itself', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Tracks)
    await search(library, 'blue')

    await library.queueScope({}, { mode: QueueMode.Now })

    expect(lastCall()[1]).toMatchObject({ query: 'blue', mode: 'now' })
  })

  it('treats an active search as narrowing, with no drill-down needed', async () => {
    const library = useLibraryStore()
    await openTab(library, LibraryLevel.Tracks)
    await search(library, 'emerald')
    call.mockResolvedValue({ count: 2 })

    await library.playAll(false)

    const [op, data] = lastCall()
    expect(op).toBe('library_queue')
    expect(data).toMatchObject({ query: 'emerald', mode: QueueMode.Now })
  })

  it('queues named paths through the ordinary queue op', async () => {
    const library = useLibraryStore()
    await library.queue(['a.mp3'], QueueMode.Now)

    expect(lastCall()).toStrictEqual([
      'now_playing_queue',
      { paths: ['a.mp3'], mode: QueueMode.Now },
    ])
  })
})
