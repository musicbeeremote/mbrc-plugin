import { beforeEach, describe, expect, it, vi } from 'vitest'

import { V6Client } from './client'
import { WireEvent } from './ops'
import { OpError } from './parse'

import { FakeSocket, connectHandshaked } from './fake-socket'

/** A player_status payload of the shape the catalog promises. */
const STATUS = {
  play_state: 'playing',
  volume: 42,
  muted: false,
  shuffle: 'off',
  repeat: 'none',
  scrobbling: true,
}

beforeEach(() => {
  // Spies are per-test: without this, a timer spy from one test counts calls
  // made by the next one.
  vi.restoreAllMocks()
  localStorage.clear()
  FakeSocket.last = null
  vi.stubGlobal('WebSocket', FakeSocket)
  vi.stubGlobal('crypto', { randomUUID: () => 'test-install-id' })
})

describe('the handshake', () => {
  it('opens with a V6 handshake naming this client as web', () => {
    const { socket } = connectHandshaked()
    const frame = JSON.parse(socket.sent[0]) as Record<string, never>
    expect(frame).toMatchObject({ id: 0, kind: 'request', op: 'handshake' })
    expect(frame.data).toMatchObject({ protocol_version: 6, client_type: 'web' })
  })

  // The token is in a cookie the page cannot read, which the browser sends on
  // the upgrade like it does on every other request to this origin. Putting one
  // in the URL was how it used to reach a socket that has no headers.
  it('puts no token in the URL or the handshake', () => {
    localStorage.setItem('mbrc.token', 'secret')
    const { socket } = connectHandshaked()

    expect(socket.url).not.toContain('token')
    expect(JSON.parse(socket.sent[0]).data).not.toHaveProperty('token')
  })

  it('reports connected only once the handshake is answered', () => {
    const client = new V6Client()
    const seen: boolean[] = []
    client.on('connection_changed', (data) => seen.push(Boolean(data.connected)))
    client.connect()
    const socket = FakeSocket.last as FakeSocket
    socket.open()
    expect(client.connected).toBe(false)
    socket.receive({ id: 0, kind: 'response', data: {} })
    expect(client.connected).toBe(true)
    expect(seen).toStrictEqual([true])
  })

  it('stays disconnected when the handshake is refused', () => {
    const client = new V6Client()
    client.connect()
    const socket = FakeSocket.last as FakeSocket
    socket.open()
    socket.receive({ id: 0, kind: 'response', error: { code: 'unauthorized', message: 'no' } })
    expect(client.connected).toBe(false)
  })
})

describe('op correlation', () => {
  it('resolves a call with the response carrying its id', async () => {
    const { client, socket } = connectHandshaked()
    const pending = client.call('player_status')
    const frame = JSON.parse(socket.sent[1]) as { id: number; op: string }
    expect(frame.op).toBe('player_status')
    socket.receive({ id: frame.id, kind: 'response', data: STATUS })
    await expect(pending).resolves.toStrictEqual(STATUS)
  })

  // Responses may arrive out of order, so a reply must be matched by id rather
  // than by the order calls were made in.
  it('matches replies by id, not by order sent', async () => {
    const { client, socket } = connectHandshaked()
    const first = client.call('library_artists')
    const second = client.call('player_status')
    const firstId = (JSON.parse(socket.sent[1]) as { id: number }).id
    const secondId = (JSON.parse(socket.sent[2]) as { id: number }).id

    const artists = { total: 1, offset: 0, items: [{ artist: 'Miles Davis', count: 5 }] }
    socket.receive({ id: secondId, kind: 'response', data: STATUS })
    socket.receive({ id: firstId, kind: 'response', data: artists })

    await expect(first).resolves.toStrictEqual(artists)
    await expect(second).resolves.toStrictEqual(STATUS)
  })

  it('rejects with the protocol error code', async () => {
    const { client, socket } = connectHandshaked()
    const pending = client.call('now_playing_list_play', { order: 3 })
    const {id} = (JSON.parse(socket.sent[1]) as { id: number })
    socket.receive({
      id,
      kind: 'response',
      error: { code: 'stale_list', message: 'the queue moved' },
    })
    await expect(pending).rejects.toBeInstanceOf(OpError)
    await expect(pending).rejects.toMatchObject({ code: 'stale_list' })
  })

  it('fails everything in flight when the connection drops', async () => {
    const { client, socket } = connectHandshaked()
    const pending = client.call('player_status')
    socket.close()
    await expect(pending).rejects.toThrow('connection closed')
  })
})

describe('events', () => {
  it('delivers an event to its listeners', () => {
    const { client, socket } = connectHandshaked()
    const seen: unknown[] = []
    client.on('volume_changed', (data) => seen.push(data))
    socket.receive({ kind: 'event', event: 'volume_changed', data: { volume: 10 } })
    expect(seen).toStrictEqual([{ volume: 10 }])
  })

  // The catalog grows additively, so a client that treated an unknown event as
  // an error could never have one added to it.
  it('ignores an unrecognised event instead of failing', () => {
    const { client, socket } = connectHandshaked()
    expect(() =>
      socket.receive({ kind: 'event', event: 'something_added_later', data: {} }),
    ).not.toThrow()
    expect(client.connected).toBe(true)
  })

  it('ignores a frame that is not JSON', () => {
    const { client, socket } = connectHandshaked()
    expect(() => socket.fire('message', { data: 'not json' })).not.toThrow()
    expect(client.connected).toBe(true)
  })
})

describe('a refused token', () => {
  // Reconnecting with a token the server has already refused is an infinite
  // loop that only pairing can break, so the client has to stop and say so.
  // Nothing is dropped here any more: the cookie is not the page's to clear,
  // and pairing again replaces it. What matters is that the app is told to ask,
  // and that the client stops reconnecting into the same refusal.
  it('stops the retry loop and asks for a new pairing', () => {
    const client = new V6Client()
    const asked: unknown[] = []
    client.on('auth_required', (data) => asked.push(data))

    client.connect()
    const socket = FakeSocket.last as FakeSocket
    socket.open()
    socket.receive({
      id: 0,
      kind: 'response',
      error: { code: 'unauthorized', message: 'pair this browser first' },
    })

    expect(asked).toHaveLength(1)
    expect(client.connected).toBe(false)

    // Spied only now, so the assertion is about the reconnect and nothing else.
    const timeout = vi.spyOn(window, 'setTimeout')
    socket.fire('close', {})
    expect(timeout).not.toHaveBeenCalled()
  })
})

describe('reconnect backoff', () => {
  // A proxy in front of a server that is down accepts the socket and drops it
  // at once. Resetting the backoff on `open` would read that as success and
  // hammer the server at the first step forever.
  /** Drives the retries the way the client does: through its own timer. */
  function failing(cycles: number) {
    const client = new V6Client()
    const timer: { fire?: () => void } = {}
    const timeout = vi.spyOn(window, 'setTimeout').mockImplementation(((fn: () => void) => {
      timer.fire = fn
      return 0
    }) as never)

    client.connect()
    for (let cycle = 0; cycle < cycles; cycle += 1) {
      const socket = FakeSocket.last as FakeSocket
      socket.open()
      socket.fire('close', {})
      timer.fire?.()
    }
    return { client, delays: timeout.mock.calls.map((call) => call[1]) }
  }

  it('does not reset on a socket that opens but never handshakes', () => {
    expect(failing(3).delays).toStrictEqual([500, 1000, 2000])
  })

  // A page retrying into an empty room forever tells nobody anything and costs
  // a phone its battery. It stops and says so instead, and the saying so is
  // what lets the app offer a button.
  it('gives up after a run of failures and reports that it has', () => {
    const stopped: boolean[] = []
    const client = new V6Client()
    client.on(WireEvent.ConnectionChanged, (data) => stopped.push(data.retrying))

    const timer: { fire?: () => void } = {}
    vi.spyOn(window, 'setTimeout').mockImplementation(((fn: () => void) => {
      timer.fire = fn
      return 0
    }) as never)

    client.connect()
    for (let cycle = 0; cycle < 12; cycle += 1) {
      const socket = FakeSocket.last as FakeSocket
      socket.open()
      socket.fire('close', {})
      timer.fire?.()
    }

    expect(stopped.at(-1)).toBe(false)
    expect(stopped.filter(Boolean)).toHaveLength(8)
  })

  // Asking again is an answer to having given up, so it starts the count over
  // rather than resuming a spent one and stopping on the first failure.
  it('starts the attempts over when asked to connect', () => {
    const { client } = failing(9)
    // Re-spying returns the same mock, so the run above is still in its calls.
    const timeout = vi.spyOn(window, 'setTimeout').mockReturnValue(0 as never)
    timeout.mockClear()

    client.connect()
    const socket = FakeSocket.last as FakeSocket
    socket.open()
    socket.fire('close', {})

    expect(timeout.mock.calls.map((call) => call[1])).toStrictEqual([500])
  })
})

describe('disconnect', () => {
  it('stops reconnecting once disconnected deliberately', () => {
    const { client, socket } = connectHandshaked()
    const timeout = vi.spyOn(window, 'setTimeout')
    client.disconnect()
    socket.fire('close', {})
    expect(timeout).not.toHaveBeenCalled()
  })
})
