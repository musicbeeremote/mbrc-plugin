import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { Capability } from '../api/permissions'
import { QueueMode } from '../api/types'
import { usePermissionsStore } from '../stores/permissions'

import { queueTrack, trackOrLevel, wholeScope } from './queueTracks'

const { call } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data?: Record<string, unknown>) => Promise<unknown>>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on: vi.fn<() => () => void>() } as unknown as typeof V6Client,
}))

const GUEST = {
  party_mode: true,
  role: 'guest',
  allowed: [Capability.QueueAdd],
  max_tracks_per_add: 1,
}

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
  call.mockResolvedValue({ count: 0 })
})

describe('how many tracks a library row queues', () => {
  it('is one for a track, and the whole level for its add-all', () => {
    expect(trackOrLevel(QueueMode.Last)).toBe(1)
    expect(trackOrLevel(QueueMode.AddAll)).toBeNull()
    expect(wholeScope()).toBeNull()
  })
})

describe('queueing a library track', () => {
  it('sends what the role allows', async () => {
    usePermissionsStore().apply(GUEST)
    await queueTrack('a.mp3', QueueMode.Last)
    expect(call).toHaveBeenCalledWith('now_playing_queue', { paths: ['a.mp3'], mode: 'last' })
  })

  // A tap on a track plays it now, which a guest may not: the tap does nothing
  // rather than sending a request the server will only refuse.
  it('sends nothing the role may not do', async () => {
    usePermissionsStore().apply(GUEST)
    await queueTrack('a.mp3', QueueMode.Now)
    await queueTrack('a.mp3', QueueMode.AddAll)
    expect(call).not.toHaveBeenCalled()
  })
})
