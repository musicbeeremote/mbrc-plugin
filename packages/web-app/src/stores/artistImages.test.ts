import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { call, handler, lastCall, on } from '../api/fake-client'

import { useArtistImagesStore } from './artistImages'
import { LibraryLevel, useLibraryStore } from './library'

vi.mock(import('../api/client'), () => import('../api/fake-client'))

beforeEach(() => {
  vi.useFakeTimers()
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

async function settle() {
  await vi.runAllTimersAsync()
}

describe('asking for pictures', () => {
  it('asks once for the names on screen, gathered into one request', async () => {
    answer({ Gadiel: 'g1' })
    const images = useArtistImagesStore()

    images.want(['Gadiel', 'Tico'])
    images.want(['tico', 'Yandel'])
    await settle()

    expect(call).toHaveBeenCalledTimes(1)
    expect(lastCall()).toStrictEqual([
      'library_artist_images',
      { artists: ['Gadiel', 'Tico', 'Yandel'] },
    ])
    expect(images.hashFor(' gadiel ')).toBe('g1')
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

  it('stops asking once the server says nothing will arrive', async () => {
    answer({}, false)
    const images = useArtistImagesStore()
    images.want(['Tico'])
    await settle()

    images.want(['Gadiel'])
    await settle()
    expect(call).toHaveBeenCalledTimes(1)
    expect(images.showsPictures).toBe(false)
  })

  it('takes a server without the op as one that sends no pictures', async () => {
    call.mockRejectedValue(new Error('unknown op'))
    const images = useArtistImagesStore()
    images.want(['Tico'])
    await settle()

    expect(images.enabled).toBe(false)
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
    images.want(['Tico'])
    await settle()

    expect(call).toHaveBeenCalledTimes(2)
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
