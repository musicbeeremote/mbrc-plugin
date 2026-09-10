import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'

import { usePlaylistStore } from './playlist'

const { call } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data: Record<string, unknown>) => Promise<unknown>>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on: vi.fn<() => () => void>() } as unknown as typeof V6Client,
}))

function lastCall(): [string, Record<string, unknown>] {
  return call.mock.calls.at(-1) as [string, Record<string, unknown>]
}

function page(count: number, total: number, from = 0) {
  return {
    total,
    offset: from,
    name: 'My Playlist',
    version: 'abc123',
    items: Array.from({ length: count }, (_, i) => ({
      src: `/${from + i}.mp3`,
      artist: 'A',
      title: `T${from + i}`,
      album: '',
      album_artist: '',
      track_no: 0,
      disc_no: 0,
      genre: '',
      year: null,
      duration_ms: null,
      rating: null,
      date_added: null,
      order: from + i,
    })),
  }
}

describe('playlist store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    call.mockReset()
  })

  it('reads a playlist and keeps its name and version', async () => {
    call.mockResolvedValueOnce(page(2, 2))
    const store = usePlaylistStore()
    await store.load('playlist://x')

    expect(lastCall()[0]).toBe('playlist_tracks')
    expect(lastCall()[1]).toMatchObject({ url: 'playlist://x', offset: 0 })
    expect(store.name).toBe('My Playlist')
    expect(store.version).toBe('abc123')
    expect(store.tracks).toHaveLength(2)
  })

  it('appends the next page from where the loaded rows end', async () => {
    call.mockResolvedValueOnce(page(2, 5))
    const store = usePlaylistStore()
    await store.load('p')

    call.mockResolvedValueOnce(page(2, 5, 2))
    await store.load('p', true)

    expect(lastCall()[1]).toMatchObject({ offset: 2 })
    expect(store.tracks).toHaveLength(4)
    expect(store.tracks.at(-1)?.order).toBe(3)
  })

  /** The row's `order` is what a mutation keys on, so paging must not renumber. */
  it('keeps each row at its own place in the playlist', async () => {
    call.mockResolvedValueOnce(page(2, 4, 2))
    const store = usePlaylistStore()
    await store.load('p')

    expect(store.tracks.map((t) => t.order)).toStrictEqual([2, 3])
  })

  it('knows when there is more to read', async () => {
    call.mockResolvedValueOnce(page(2, 5))
    const store = usePlaylistStore()
    await store.load('p')
    expect(store.hasMore).toBe(true)

    call.mockResolvedValueOnce(page(3, 5, 2))
    await store.load('p', true)
    expect(store.hasMore).toBe(false)
  })

  /** Closing has to empty it, or the next playlist opens showing the last one. */
  it('drops what it held when closed', async () => {
    call.mockResolvedValueOnce(page(2, 2))
    const store = usePlaylistStore()
    await store.load('p')
    store.close()

    expect(store.tracks).toStrictEqual([])
    expect(store.name).toBe('')
    expect(store.total).toBe(0)
    expect(store.hasMore).toBe(false)
  })

  it('stops loading when a read fails', async () => {
    call.mockRejectedValueOnce(new Error('gone'))
    const store = usePlaylistStore()
    await expect(store.load('p')).rejects.toThrow('gone')
    expect(store.loading).toBe(false)
  })
})

describe('playlist search and totals', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    call.mockReset()
  })

  /** The totals describe the whole list, so a second page must not re-ask. */
  it('asks for totals when reading from the start, not when appending', async () => {
    call.mockResolvedValueOnce({ ...page(2, 5), total_duration_ms: 300_000 })
    const store = usePlaylistStore()
    await store.load('p')
    expect(lastCall()[1]).toMatchObject({ totals: true })
    expect(store.totalDurationMs).toBe(300_000)

    call.mockResolvedValueOnce(page(2, 5, 2))
    await store.load('p', true)
    expect(lastCall()[1].totals).toBeUndefined()
    expect(store.totalDurationMs).toBe(300_000)
  })

  it('sends a search term and reads the narrowed list from the start', async () => {
    call.mockResolvedValueOnce({ ...page(5, 5), total_duration_ms: 500_000 })
    const store = usePlaylistStore()
    await store.load('p')

    call.mockResolvedValueOnce({ ...page(1, 1), total_duration_ms: 100_000 })
    await store.search('p', 'queen')

    expect(lastCall()[1]).toMatchObject({ query: 'queen', offset: 0, totals: true })
    expect(store.total).toBe(1)
    expect(store.totalDurationMs).toBe(100_000)
  })

  /** An empty box is no search at all, not a search for nothing. */
  it('drops the query parameter when the box is cleared', async () => {
    call.mockResolvedValueOnce(page(2, 2))
    const store = usePlaylistStore()
    await store.search('p', '   ')
    expect(lastCall()[1].query).toBeUndefined()
  })

  it('forgets the search when the playlist is closed', async () => {
    call.mockResolvedValueOnce({ ...page(1, 1), total_duration_ms: 100 })
    const store = usePlaylistStore()
    await store.search('p', 'x')
    store.close()
    expect(store.query).toBe('')
    expect(store.totalDurationMs).toBe(0)
  })
})
