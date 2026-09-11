import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { OpError } from '../api/parse'
import { ErrorCode } from '../api/types'

import { useQueueStore } from './queue'

const { call } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data: Record<string, unknown>) => Promise<unknown>>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on: vi.fn<() => () => void>() } as unknown as typeof V6Client,
}))

function lastCall(): [string, Record<string, unknown>] {
  return call.mock.calls.at(-1) as [string, Record<string, unknown>]
}

function page(count: number, total: number, version = 1) {
  return {
    total,
    offset: 0,
    version,
    items: Array.from({ length: count }, (_, i) => ({
      src: `/${i}.mp3`,
      artist: 'A',
      title: `T${i}`,
      album: '',
      album_artist: '',
      track_no: 0,
      disc_no: 0,
      genre: '',
      year: null,
      duration_ms: null,
      rating: null,
      date_added: null,
      order: i,
      position: i,
      play_position: i,
    })),
  }
}

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
})

describe('reading a long queue', () => {
  // The view held one page and called that the queue: a count saying 800 over a
  // list that stopped at 200.
  it('continues past the first page instead of stopping at it', async () => {
    const queue = useQueueStore()
    call.mockResolvedValue(page(200, 500))
    await queue.load()

    expect(queue.items).toHaveLength(200)
    expect(queue.hasMore).toBe(true)

    await queue.load(true)
    expect(lastCall()[1]).toMatchObject({ offset: 200 })
    expect(queue.items).toHaveLength(400)
  })

  it('knows when it holds the whole of it', async () => {
    const queue = useQueueStore()
    call.mockResolvedValue(page(12, 12))
    await queue.load()

    expect(queue.hasMore).toBe(false)
  })

  // `order` only means what it meant while the list it came from still held, so
  // a page from a moved list cannot be stitched onto the rows already shown.
  it('starts over when the list moved between pages', async () => {
    const queue = useQueueStore()
    call.mockResolvedValue(page(200, 500, 1))
    await queue.load()

    call.mockResolvedValue(page(3, 3, 2))
    await queue.load(true)

    expect(queue.items).toHaveLength(3)
    expect(queue.stale).toBe(true)
    expect(queue.version).toBe(2)
  })
})

describe('a mutation the server refuses', () => {
  // The notice was set before the re-read that clears it, so the one thing it
  // exists to explain - the list being redrawn from under the user - never said
  // anything.
  it('leaves the notice up after the re-read', async () => {
    const queue = useQueueStore()
    call.mockResolvedValue(page(3, 3, 1))
    await queue.load()

    call.mockRejectedValueOnce(new OpError({ code: ErrorCode.StaleList, message: 'stale' }))
    call.mockResolvedValue(page(4, 4, 2))
    await queue.remove(0)

    expect(queue.stale).toBe(true)
  })
})
