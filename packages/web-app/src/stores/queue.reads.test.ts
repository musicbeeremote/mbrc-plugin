import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'

import { useQueueStore } from './queue'

const { call, on } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data: Record<string, unknown>) => Promise<unknown>>(),
  on: vi.fn<(event: string, handler: () => void) => () => void>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on } as unknown as typeof V6Client,
}))

/** A page of `count` rows from a queue of `total`, rows ordered from `offset`. */
function page(count: number, total: number, { version = 1, offset = 0 } = {}) {
  return {
    total,
    offset,
    version,
    items: Array.from({ length: count }, (_, i) => ({
      src: `/${offset + i}.mp3`,
      artist: 'A',
      title: `T${offset + i}`,
      album: '',
      album_artist: '',
      track_no: 0,
      disc_no: 0,
      genre: '',
      year: null,
      duration_ms: null,
      rating: null,
      date_added: null,
      order: offset + i,
      position: offset + i,
      play_position: offset + i,
    })),
  }
}

/** A promise the test settles by hand, standing in for a read still on the wire. */
function held() {
  const hands: { resolve: (value: unknown) => void; reject: (error: unknown) => void } = {
    resolve: () => undefined,
    reject: () => undefined,
  }
  const promise = new Promise((resolve, reject) => {
    hands.resolve = resolve
    hands.reject = reject
  })
  return { promise, ...hands }
}

/** The handler `bind()` registered for the queue's change event. */
function changedHandler(): () => void {
  const handler = on.mock.calls.find(([event]) => event === 'now_playing_list_changed')?.[1]
  if (!handler) throw new Error('bind() registered no change handler')
  return handler
}

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
  on.mockReset()
})

describe('a burst of change events', () => {
  // A batch remove of four tracks arrives as four events within milliseconds,
  // and each would otherwise start its own read of the whole queue.
  it('collapses into the running read plus one trailing read', async () => {
    const queue = useQueueStore()
    queue.bind()
    const changed = changedHandler()

    const first = held()
    call.mockReturnValueOnce(first.promise)
    call.mockResolvedValue(page(3, 3, { version: 5 }))

    for (let i = 0; i < 4; i += 1) changed()
    first.resolve(page(3, 3, { version: 1 }))
    await vi.waitFor(() => expect(queue.version).toBe(5))

    expect(call).toHaveBeenCalledTimes(2)
  })

  // The trailing read was skipped when the first one threw, leaving the list at
  // a version the events had already moved past.
  it('still makes the trailing read when the running one fails', async () => {
    const queue = useQueueStore()
    queue.bind()
    const changed = changedHandler()

    const first = held()
    call.mockReturnValueOnce(first.promise)
    call.mockResolvedValue(page(3, 3, { version: 5 }))

    changed()
    changed()
    first.reject(new Error('timed out'))
    await vi.waitFor(() => expect(queue.version).toBe(5))
  })

  it('reads once when nothing arrives while it runs', async () => {
    const queue = useQueueStore()
    call.mockResolvedValue(page(3, 3, { version: 2 }))

    await queue.load()

    expect(call).toHaveBeenCalledTimes(1)
  })
})

describe('reading the whole queue', () => {
  // Asked while a page was loading, it read nothing more: a search matched only
  // the rows already held, and select all picked part of the queue as the whole.
  it('waits out a read already running instead of stopping at it', async () => {
    const queue = useQueueStore()
    call.mockResolvedValue(page(200, 400))
    await queue.load()

    const running = held()
    call.mockReturnValueOnce(running.promise)
    const scrolled = queue.load(true)
    const all = queue.loadAll()
    call.mockResolvedValue(page(100, 400, { offset: 300 }))
    running.resolve(page(100, 400, { offset: 200 }))
    await Promise.all([scrolled, all])

    expect(queue.items).toHaveLength(400)
  })
})
