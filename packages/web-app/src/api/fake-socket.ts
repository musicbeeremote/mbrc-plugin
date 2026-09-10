/**
 * A WebSocket stand-in for the client's tests.
 *
 * Only test files import it, so it never reaches the bundle. It exists because
 * the client's behaviour worth testing - correlation, reconnect, identity - is
 * all about what it sends and how it reacts to what comes back.
 */

import { V6Client } from './client'

/** A WebSocket stand-in that records what was sent and lets a test push frames. */
export class FakeSocket {
  static last: FakeSocket | null = null
  static readonly CONNECTING = 0
  static readonly OPEN = 1

  readyState = 1
  sent: string[] = []

  private readonly handlers = new Map<string, Set<(event: unknown) => void>>()

  readonly url: string

  constructor(url: string) {
    this.url = url
    FakeSocket.last = this
  }

  addEventListener(type: string, handler: (event: unknown) => void): void {
    let set = this.handlers.get(type)
    if (!set) {
      set = new Set()
      this.handlers.set(type, set)
    }
    set.add(handler)
  }

  send(data: string): void {
    this.sent.push(data)
  }

  close(): void {
    this.readyState = 3
    this.fire('close', {})
  }

  /** Drives one of the client's own listeners, as the browser would. */
  fire(type: string, event: unknown): void {
    const handlers = this.handlers.get(type)
    if (!handlers) return
    for (const handler of handlers) handler(event)
  }

  open(): void {
    this.fire('open', {})
  }

  receive(frame: unknown): void {
    this.fire('message', { data: JSON.stringify(frame) })
  }
}

/** A client with its handshake already answered, which most tests start from. */
export function connectHandshaked(): { client: V6Client; socket: FakeSocket } {
  const client = new V6Client()
  client.connect()
  const socket = FakeSocket.last as FakeSocket
  socket.open()
  socket.receive({ id: 0, kind: 'response', data: {} })
  return { client, socket }
}
