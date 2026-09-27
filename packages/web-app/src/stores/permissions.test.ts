import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { Capability } from '../api/permissions'
import { QueueMode } from '../api/types'

import { capabilityFor, usePermissionsStore } from './permissions'

const { on } = vi.hoisted(() => ({ on: vi.fn<(event: string, listener: unknown) => () => void>() }))

vi.mock(import('../api/client'), () => ({
  client: { call: vi.fn<() => Promise<unknown>>(), on } as unknown as typeof V6Client,
}))

const GUEST = {
  party_mode: true,
  role: 'guest',
  allowed: [Capability.QueueAdd],
  max_tracks_per_add: 1,
}

beforeEach(() => {
  setActivePinia(createPinia())
  on.mockReset()
  vi.unstubAllGlobals()
})

describe('before the server has said anything', () => {
  it('draws everything, as a server without Party Mode would allow', () => {
    const permissions = usePermissionsStore()
    for (const capability of Object.values(Capability)) {
      expect(permissions.can(capability)).toBe(true)
    }
    expect(permissions.canQueue(QueueMode.Now, null)).toBe(true)
    expect(permissions.mayPair).toBe(false)
  })
})

describe('a guest', () => {
  it('may append one track and nothing wider', () => {
    const permissions = usePermissionsStore()
    permissions.apply(GUEST)
    expect(permissions.canQueue(QueueMode.Last, 1)).toBe(true)
    expect(permissions.canQueue(QueueMode.Last, 2)).toBe(false)
    expect(permissions.canQueue(QueueMode.Last, null)).toBe(false)
    expect(permissions.canQueue(QueueMode.Next, 1)).toBe(false)
    expect(permissions.can(Capability.Playback)).toBe(false)
  })

  it('is offered pairing, and a host is not', () => {
    const permissions = usePermissionsStore()
    permissions.apply(GUEST)
    expect(permissions.mayPair).toBe(true)
    permissions.apply({ party_mode: true, role: 'host', allowed: Object.values(Capability) })
    expect(permissions.mayPair).toBe(false)
  })
})

describe('where permissions come from', () => {
  it('reads the capabilities route and then follows the event', async () => {
    const answer = Response.json({ ops: [], permissions: GUEST })
    vi.stubGlobal('fetch', vi.fn<() => Promise<Response>>().mockResolvedValue(answer))
    const permissions = usePermissionsStore()
    permissions.bind()
    await vi.waitFor(() => expect(permissions.role).toBe('guest'))

    const [[event, listener]] = on.mock.calls
    expect(event).toBe('permissions_changed')
    ;(listener as (value: unknown) => void)({ party_mode: false, role: 'host', allowed: [] })
    expect(permissions.partyMode).toBe(false)
  })

  it('keeps what it had when the route answers something unreadable', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<() => Promise<Response>>().mockResolvedValue(new Response('{"permissions":{"role":7}}')),
    )
    const permissions = usePermissionsStore()
    await permissions.refresh()
    expect(permissions.role).toBe('host')
  })
})

describe('the capability a queue mode needs', () => {
  it('follows the server: last appends, next inserts, the rest replace', () => {
    expect(capabilityFor(QueueMode.Last)).toBe(Capability.QueueAdd)
    expect(capabilityFor(QueueMode.Next)).toBe(Capability.QueueInsert)
    expect(capabilityFor(QueueMode.Now)).toBe(Capability.QueueReplace)
    expect(capabilityFor(QueueMode.AddAll)).toBe(Capability.QueueReplace)
  })
})
