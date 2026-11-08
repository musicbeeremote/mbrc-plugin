import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { call, handler, lastCall, on } from '../api/fake-client'
import { OpError } from '../api/parse'

import { useArtistImagesStore } from './artistImages'
import { LibraryLevel, useLibraryStore } from './library'

vi.mock(import('../api/client'), () => import('../api/fake-client'))

beforeEach(() => {
  vi.useFakeTimers()
  localStorage.clear()
  setActivePinia(createPinia())
  call.mockReset()
  on.mockReset()
})

afterEach(() => {
  vi.useRealTimers()
})

/** Answers `library_artist_images` with `stored` and `enabled`. */
function answer(stored: Record<string, string>, enabled = true) {
  call.mockImplementation(async (_op, data) => ({
    artists: (data as { artists: string[] }).artists.map((artist) =>
      stored[artist] ? { artist, image_hash: stored[artist] } : { artist },
    ),
    enabled,
  }))
}

/** Lets the gathered names go out, and their answers come back. */
async function settle() {
  await vi.advanceTimersByTimeAsync(200)
}

describe('asking for pictures', () => {
  it('asks once scrolling pauses, for what is on screen by then', async () => {
    answer({ Tico: 't1' })
    const images = useArtistImagesStore()

    images.want(['Gadiel', 'Passed'])
    await vi.advanceTimersByTimeAsync(100)
    images.want(['Tico', 'tico', 'Yandel'])
    await settle()

    expect(call).toHaveBeenCalledTimes(1)
    expect(lastCall()).toStrictEqual([
      'library_artist_images',
      { artists: ['Tico', 'Yandel'] },
    ])
    expect(images.hashFor(' tico ')).toBe('t1')
    expect(images.enabled).toBe(true)
  })

  it('does not ask again for a name it has, or asked for a moment ago', async () => {
    answer({ Gadiel: 'g1' })
    const images = useArtistImagesStore()
    images.want(['Gadiel', 'Tico'])
    await settle()

    images.want(['Gadiel', 'Tico'])
    await settle()
    expect(call).toHaveBeenCalledTimes(1)

    vi.advanceTimersByTime(10 * 60 * 1000)
    images.want(['Tico'])
    await settle()
    expect(lastCall()[1]).toStrictEqual({ artists: ['Tico'] })
  })

  it('splits more names than one request takes', async () => {
    answer({})
    const images = useArtistImagesStore()
    const names = Array.from({ length: 501 }, (_, i) => `Artist ${i}`)
    images.want(names)
    await settle()

    expect(call.mock.calls).toStrictEqual([
      ['library_artist_images', { artists: names.slice(0, 500) }],
      ['library_artist_images', { artists: ['Artist 500'] }],
    ])
  })

  it('checks one name a minute while the server sends none, then asks for the rest', async () => {
    answer({}, false)
    const images = useArtistImagesStore()
    images.want(['Tico', 'Gadiel'])
    await settle()

    images.want(['Tico', 'Gadiel', 'Yandel'])
    await settle()
    expect(call).toHaveBeenCalledTimes(1)
    expect(images.showsPictures).toBe(false)

    await vi.advanceTimersByTimeAsync(60 * 1000)
    expect(lastCall()[1]).toStrictEqual({ artists: ['Tico'] })

    answer({ Tico: 't1' })
    await vi.advanceTimersByTimeAsync(60 * 1000)
    expect(call.mock.calls.slice(-2).map(([, data]) => data)).toStrictEqual([
      { artists: ['Tico'] },
      { artists: ['Gadiel', 'Yandel'] },
    ])
    expect(images.showsPictures).toBe(true)
  })

  it('sends no request when nothing on screen is due', async () => {
    answer({ Tico: 't1' })
    const images = useArtistImagesStore()
    images.want(['Tico'])
    await settle()

    images.want(['Tico'])
    images.want([])
    await settle()

    expect(call).toHaveBeenCalledTimes(1)
  })

  it('takes a server without the op as one that sends no pictures', async () => {
    call.mockRejectedValue(new OpError({ code: 'unknown_op', message: 'unknown op' }))
    const images = useArtistImagesStore()
    images.want(['Tico'])
    await settle()
    await vi.advanceTimersByTimeAsync(60 * 1000)

    expect(images.enabled).toBe(false)
    expect(call).toHaveBeenCalledTimes(1)
  })

  it('recovers from a failed request by itself', async () => {
    call.mockRejectedValue(new Error('timed out'))
    const images = useArtistImagesStore()
    images.want(['Tico', 'Gadiel'])
    await settle()

    answer({ Tico: 't1', Gadiel: 'g1' })
    await vi.advanceTimersByTimeAsync(60 * 1000)

    expect(images.hashFor('Tico')).toBe('t1')
    expect(images.hashFor('Gadiel')).toBe('g1')
  })
})

describe('events and connections', () => {
  it('shows a picture announced after the request', () => {
    const images = useArtistImagesStore()
    images.bind()

    handler('library_artist_image_updated')({ artist: 'Gadiel', image_hash: 'g2' })

    expect(images.hashFor('GADIEL')).toBe('g2')
    expect(images.showsPictures).toBe(true)
  })

  it('asks again after reconnecting, since the setting may have changed', async () => {
    answer({}, false)
    const images = useArtistImagesStore()
    images.bind()
    images.want(['Tico'])
    await settle()

    answer({ Tico: 't1' })
    handler('connection_changed')({ connected: true, retrying: false })
    await settle()

    expect(call).toHaveBeenCalledTimes(2)
    expect(images.hashFor('Tico')).toBe('t1')
  })

  it('asks again after reconnecting for what is on screen, with no scroll', async () => {
    answer({})
    const images = useArtistImagesStore()
    images.bind()
    images.want(['Tico'])
    await settle()

    answer({ Tico: 't1' })
    handler('connection_changed')({ connected: true, retrying: false })
    await settle()

    expect(call).toHaveBeenCalledTimes(2)
    expect(images.hashFor('Tico')).toBe('t1')
  })

  it('keeps the column through a reconnect, and for the next visit', async () => {
    answer({})
    const images = useArtistImagesStore()
    images.bind()
    images.want(['Tico'])
    await settle()

    handler('connection_changed')({ connected: true, retrying: false })
    expect(images.showsPictures).toBe(true)

    setActivePinia(createPinia())
    expect(useArtistImagesStore().showsPictures).toBe(true)
  })
})

describe('a picture that is gone', () => {
  it('is forgotten when the server stops listing it, and asked for again', async () => {
    answer({})
    const images = useArtistImagesStore()
    images.remember('Tico', 't1')

    images.remember('Tico', undefined)
    images.want(['Tico'])
    await settle()

    expect(images.hashFor('Tico')).toBeUndefined()
    expect(call).toHaveBeenCalledTimes(1)
  })

  it('stays gone under every name once its file failed, until reconnecting', async () => {
    answer({ Tico: 't1' })
    const images = useArtistImagesStore()
    images.bind()
    images.remember('Tico', 't1')
    images.remember('Tico feat. Gadiel', 't1')

    images.markBroken('t1')
    images.want(['Tico'])
    await settle()

    expect(images.hashFor('Tico')).toBeUndefined()
    expect(images.hashFor('Tico feat. Gadiel')).toBeUndefined()
    expect(images.isBroken('t1')).toBe(true)

    handler('connection_changed')({ connected: true, retrying: false })
    await settle()
    expect(images.isBroken('t1')).toBe(false)
    expect(images.hashFor('Tico')).toBe('t1')
  })
})

describe('library pages', () => {
  it('keeps the pictures an artist page carries', async () => {
    call.mockResolvedValue({
      total: 2,
      offset: 0,
      items: [
        { artist: 'Gadiel', count: 3, image_hash: 'g1' },
        { artist: 'Tico', count: 1 },
      ],
    })
    const library = useLibraryStore()
    await library.show({ level: LibraryLevel.Artists, scope: {} })

    const images = useArtistImagesStore()
    expect(images.hashFor('gadiel')).toBe('g1')
    expect(images.hashFor('Tico')).toBeUndefined()
  })
})
