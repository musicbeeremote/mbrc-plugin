import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { MediaSource } from '../api/browse'

import { usePreferences } from '../composables/usePreferences'

import { useMediaStore } from './media'

const { call, on } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data: Record<string, unknown>) => Promise<unknown>>(),
  on: vi.fn<(event: string, listener: (data: unknown) => void) => () => void>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on } as unknown as typeof V6Client,
}))

function lastCall(): [string, Record<string, unknown>] {
  return call.mock.calls.at(-1) as [string, Record<string, unknown>]
}

function albums(count: number, total = count) {
  return {
    total,
    offset: 0,
    items: Array.from({ length: count }, (_, i) => ({
      album: `Book ${i}`,
      artist: `Author ${i}`,
      count: 10,
    })),
  }
}

function chapters(count: number, total = count) {
  return {
    total,
    offset: 0,
    items: Array.from({ length: count }, (_, i) => ({
      src: `C:/books/dune/${i + 1}.mp3`,
      artist: ['Herbert'],
      artist_display: 'Herbert',
      title: `Chapter ${i + 1}`,
      album: 'Dune',
      album_artist: 'Herbert',
      track_no: i + 1,
      disc_no: 1,
      genre: [],
      year: null,
      duration_ms: 60_000,
      rating: null,
      date_added: null,
    })),
  }
}

/** A promise the test settles by hand, to land an answer after the reader moved on. */
function handler(event: string): (data: unknown) => void {
  const found = on.mock.calls.find(([name]) => name === event)?.[1]
  if (!found) throw new Error(`bind() registered no ${event} handler`)
  return found
}

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  usePreferences().setShowMedia(true)
  call.mockReset()
  on.mockReset()
})

describe('while media is turned off', () => {
  beforeEach(() => {
    usePreferences().setShowMedia(false)
  })

  it('asks the server nothing, on connect or on a change', async () => {
    const media = useMediaStore()
    media.bind()

    handler('connection_changed')({ connected: true, retrying: false })
    handler('media_changed')({ source: 'inbox' })
    await media.loadSources()
    await Promise.resolve()

    expect(call).not.toHaveBeenCalled()
    expect(media.sources).toStrictEqual([])
  })

  it('reads the categories as soon as it is turned on', async () => {
    call.mockResolvedValueOnce({ items: [{ source: 'audiobooks', count: 4 }] })
    const media = useMediaStore()
    media.bind()

    usePreferences().setShowMedia(true)

    await vi.waitFor(() => expect(media.sources).toHaveLength(1))
    expect(lastCall()[0]).toBe('media_sources')
  })

  it('drops the categories when turned off', async () => {
    usePreferences().setShowMedia(true)
    call.mockResolvedValueOnce({ items: [{ source: 'audiobooks', count: 4 }] })
    const media = useMediaStore()
    media.bind()
    await media.loadSources()

    usePreferences().setShowMedia(false)

    await vi.waitFor(() => expect(media.sources).toStrictEqual([]))
    expect(call).toHaveBeenCalledTimes(1)
  })
})

describe('media_changed', () => {
  it('re-reads the category on screen and the book open in it', async () => {
    call.mockResolvedValueOnce(albums(1))
    const media = useMediaStore()
    media.bind()
    await media.showSource(MediaSource.Inbox)
    call.mockResolvedValueOnce(chapters(1))
    await media.openBook({})

    call.mockReset()
    const answers: Record<string, unknown> = {
      media_sources: { items: [{ source: 'inbox', count: 3 }] },
      media_albums: albums(2),
      media_tracks: chapters(3),
    }
    call.mockImplementation(async (op: string) => answers[op])
    handler('media_changed')({ source: 'inbox' })

    await vi.waitFor(() => expect(media.tracks).toHaveLength(3))
    expect(media.albums).toHaveLength(2)
    expect(media.sources).toStrictEqual([{ source: 'inbox', count: 3 }])
  })

  /** The server sends one event per changed source; the categories are read once. */
  it('re-reads the categories once for a burst', async () => {
    const media = useMediaStore()
    media.bind()
    call.mockResolvedValue({ items: [] })

    const changed = handler('media_changed')
    changed({ source: 'audiobooks' })
    changed({ source: 'videos' })
    changed({ source: 'inbox' })

    await vi.waitFor(() => expect(call).toHaveBeenCalledTimes(1))
    await Promise.resolve()
    expect(call).toHaveBeenCalledTimes(1)
    expect(lastCall()[0]).toBe('media_sources')
  })

  it('leaves another category alone', async () => {
    call.mockResolvedValueOnce(albums(1))
    const media = useMediaStore()
    media.bind()
    await media.showSource(MediaSource.Audiobooks)

    call.mockReset()
    call.mockResolvedValue({ items: [] })
    handler('media_changed')({ source: 'videos' })

    await vi.waitFor(() => expect(call).toHaveBeenCalledTimes(1))
    expect(lastCall()[0]).toBe('media_sources')
    expect(media.albums).toHaveLength(1)
  })

  it('reads the categories once connected', async () => {
    call.mockResolvedValueOnce({ items: [{ source: 'videos', count: 2 }] })
    const media = useMediaStore()
    media.bind()

    handler('connection_changed')({ connected: true, retrying: false })
    await vi.waitFor(() => expect(media.sources).toHaveLength(1))
  })
})
