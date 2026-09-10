/**
 * The handshake identity, driven through a real client over a fake socket.
 *
 * Separate from `session.test.ts`, which covers the storage helpers alone: what
 * matters here is what the client puts on the wire and what it does with the
 * answer.
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'

import { V6Client } from './client'

import { FakeSocket } from './fake-socket'

beforeEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
  FakeSocket.last = null
  vi.stubGlobal('WebSocket', FakeSocket)
  vi.stubGlobal('crypto', { randomUUID: () => 'test-install-id' })
})

describe('the identity token', () => {
  // The id is persisted the moment it is minted. Dropping the token the server
  // issues alongside it leaves a browser holding an id it can never handshake
  // with again, and every reload lands on the pairing screen that cannot fix it.
  it('is kept when issued and presented on the next handshake', () => {
    const first = new V6Client()
    first.connect()
    const opening = FakeSocket.last as FakeSocket
    opening.open()
    expect(JSON.parse(opening.sent[0]).data).not.toHaveProperty('client_token')
    opening.receive({ id: 0, kind: 'response', data: { client_token: 'issued-1' } })

    const second = new V6Client()
    second.connect()
    const again = FakeSocket.last as FakeSocket
    again.open()
    expect(JSON.parse(again.sent[0]).data.client_token).toBe('issued-1')
  })

  // A refused id belongs to an installation that can prove it and we cannot, so
  // it is unusable forever. Pairing does not touch it: the recovery is a new id.
  it('starts a new identity when the server refuses the stored id', () => {
    let minted = 0
    vi.stubGlobal('crypto', {
      randomUUID: () => {
        minted += 1
        return `install-${minted}`
      },
    })
    const client = new V6Client()
    const asked: unknown[] = []
    client.on('auth_required', (data) => asked.push(data))
    client.connect()
    const socket = FakeSocket.last as FakeSocket
    socket.open()
    socket.receive({
      id: 0,
      kind: 'response',
      error: { code: 'invalid_token', message: 'held elsewhere', field: 'client_token' },
    })

    expect(asked).toHaveLength(0)
    expect(localStorage.getItem('mbrc.client_id')).toBe('install-2')

    // The reconnect the closing socket schedules is what carries the new id.
    const timeout = vi.spyOn(window, 'setTimeout').mockReturnValue(0 as never)
    socket.fire('close', {})
    expect(timeout).toHaveBeenCalledWith(expect.any(Function), 500)
  })
})
