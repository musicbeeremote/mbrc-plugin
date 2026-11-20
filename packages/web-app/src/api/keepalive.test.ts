import { beforeEach, describe, expect, it, vi } from 'vitest'

import { V6Client } from './client'

import { FakeSocket } from './fake-socket'

beforeEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
  vi.stubGlobal('WebSocket', FakeSocket)
  vi.stubGlobal('crypto', { randomUUID: () => 'test-install-id' })
})

describe('the keepalive', () => {
  // The server closes a socket silent for three intervals, and an idle page sends nothing else.
  it('pings at the interval the handshake names, and stops once closed', () => {
    vi.useFakeTimers()
    const client = new V6Client()
    client.connect()
    const socket = FakeSocket.last as FakeSocket
    socket.open()
    socket.receive({ id: 0, kind: 'response', data: { ping_interval_secs: 2 } })
    const pings = (): number => socket.sent.filter((f) => JSON.parse(f).op === 'ping').length

    vi.advanceTimersByTime(4000)
    expect(pings()).toBe(2)
    expect(JSON.parse(socket.sent.at(-1) as string).id).toBeGreaterThan(0)

    client.disconnect()
    vi.advanceTimersByTime(10_000)
    expect(pings()).toBe(2)
    vi.useRealTimers()
  })
})
