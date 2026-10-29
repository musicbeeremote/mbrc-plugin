import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { MediaSource } from '../api/browse'
import { QueueMode } from '../api/types'
import { queueTrack } from '../composables/queueTracks'

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
function held() {
  const hands: { resolve: (value: unknown) => void } = { resolve: () => undefined }
  const promise = new Promise((resolve) => {
    hands.resolve = resolve
  })
  return { promise, ...hands }
}

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  usePreferences().setShowMedia(true)
  call.mockReset()
  on.mockReset()
})

describe('media sources', () => {
  it('keeps only the categories this build knows', async () => {
    call.mockResolvedValueOnce({
      items: [
        { source: 'audiobooks', count: 42 },
        { source: 'holograms', count: 3 },
      ],
    })
    const media = useMediaStore()
    await media.loadSources()

    expect(media.sources).toStrictEqual([{ source: 'audiobooks', count: 42 }])
  })

  /** A plugin without media ops refuses the read; the pane then has nothing to offer. */
  it('reads a refusal as no categories', async () => {
    call.mockResolvedValueOnce({ items: [{ source: 'videos', count: 1 }] })
    const media = useMediaStore()
    await media.loadSources()

    call.mockRejectedValueOnce(new Error('unknown_op'))
    await media.loadSources()
    expect(media.sources).toStrictEqual([])
  })
})

describe('books', () => {
  it('reads a category one page at a time', async () => {
    call.mockResolvedValueOnce(albums(2, 5))
    const media = useMediaStore()
    await media.showSource(MediaSource.Audiobooks)

    expect(lastCall()).toStrictEqual([
      'media_albums',
      { source: 'audiobooks', offset: 0, limit: 100 },
    ])
    expect(media.hasMoreAlbums).toBe(true)

    call.mockResolvedValueOnce(albums(3, 5))
    await media.loadAlbums(true)
    expect(lastCall()[1]).toMatchObject({ offset: 2 })
    expect(media.albums).toHaveLength(5)
  })

  /** Back from a book to the grid is not a reason to read the category again. */
  it('keeps the category it already shows', async () => {
    call.mockResolvedValueOnce(albums(2))
    const media = useMediaStore()
    await media.showSource(MediaSource.Audiobooks)
    await media.showSource(MediaSource.Audiobooks)

    expect(call).toHaveBeenCalledTimes(1)
  })

  it('a failed album read is retried when the source is opened again', async () => {
    call.mockRejectedValueOnce(new Error('timed out'))
    const media = useMediaStore()
    await media.showSource(MediaSource.Audiobooks)
    expect(media.error).toBe('Error: timed out')

    call.mockResolvedValueOnce(albums(2))
    await media.showSource(MediaSource.Audiobooks)

    expect(call).toHaveBeenCalledTimes(2)
    expect(media.albums).toHaveLength(2)
    expect(media.error).toBeNull()
  })

  it('drops an answer for a category the reader has left', async () => {
    const slow = held()
    call.mockReturnValueOnce(slow.promise)
    const media = useMediaStore()
    const first = media.showSource(MediaSource.Audiobooks)

    call.mockResolvedValueOnce(albums(1))
    await media.showSource(MediaSource.Videos)
    slow.resolve(albums(4))
    await first

    expect(media.source).toBe(MediaSource.Videos)
    expect(media.albums).toHaveLength(1)
  })

  it('reads a book by its title and author, in the order the server gives', async () => {
    call.mockResolvedValueOnce(albums(1))
    const media = useMediaStore()
    await media.showSource(MediaSource.Audiobooks)

    call.mockResolvedValueOnce(chapters(3))
    await media.openBook({ album: 'Dune', artist: 'Herbert' })

    const [op, data] = lastCall()
    expect(op).toBe('media_tracks')
    expect(data).toStrictEqual({
      source: 'audiobooks',
      album: 'Dune',
      artist: 'Herbert',
      offset: 0,
      limit: 100,
    })
    expect(data).not.toHaveProperty('sort')
    expect(media.tracks.map((t) => t.track_no)).toStrictEqual([1, 2, 3])
  })

  it('shows the failure of a read rather than an empty book', async () => {
    call.mockResolvedValueOnce(albums(1))
    const media = useMediaStore()
    await media.showSource(MediaSource.Audiobooks)

    call.mockRejectedValueOnce(new Error('timed out'))
    await media.openBook({ album: 'Dune' })
    expect(media.error).toBe('Error: timed out')
  })
})

describe('queueing', () => {
  async function inBook() {
    call.mockResolvedValueOnce(albums(1))
    const media = useMediaStore()
    await media.showSource(MediaSource.Audiobooks)
    call.mockResolvedValueOnce(chapters(3))
    await media.openBook({ album: 'Dune', artist: 'Herbert' })
    return media
  }

  it('plays a book by naming it, and reports how many files it queued', async () => {
    const media = await inBook()
    call.mockResolvedValueOnce({ count: 3 })

    const queued = await media.queue({ album: 'Dune', artist: 'Herbert' }, { mode: QueueMode.Now })

    expect(lastCall()).toStrictEqual([
      'media_queue',
      { source: 'audiobooks', album: 'Dune', artist: 'Herbert', mode: QueueMode.Now },
    ])
    expect(queued).toBe(3)
  })

  /** Starting at a chapter takes the rest of the book with it. */
  it('plays the open book from a chapter', async () => {
    const media = await inBook()
    call.mockResolvedValueOnce({ count: 3 })

    await queueTrack('C:/books/dune/2.mp3', QueueMode.AddAll, media.queueOpen)

    expect(lastCall()).toStrictEqual([
      'media_queue',
      {
        source: 'audiobooks',
        album: 'Dune',
        artist: 'Herbert',
        mode: QueueMode.AddAll,
        play: 'C:/books/dune/2.mp3',
      },
    ])
  })

  it('queues a single chapter by its path', async () => {
    const media = await inBook()
    call.mockResolvedValueOnce({})

    await queueTrack('C:/books/dune/2.mp3', QueueMode.Next, media.queueOpen)

    expect(lastCall()).toStrictEqual([
      'now_playing_queue',
      { paths: ['C:/books/dune/2.mp3'], mode: QueueMode.Next },
    ])
  })

  it('queues nothing before a category is shown', async () => {
    const media = useMediaStore()
    await expect(media.queue({ album: 'Dune' })).resolves.toBe(0)
    expect(call).not.toHaveBeenCalled()
  })
})
