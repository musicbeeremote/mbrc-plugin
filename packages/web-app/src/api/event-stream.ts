/**
 * The broadcast half of the protocol over plain HTTP, for a browser whose
 * WebSocket will not open.
 *
 * Commands already survive that - they fall back to `POST /api/v6/{op}` - but
 * events had no second road, so such a session read the state once and then
 * watched it go stale behind a reconnect notice. `EventSource` carries the same
 * envelopes the socket does, so the lines go to the same reader.
 *
 * One-way, so it is a backup and not a replacement: the socket is still chased
 * in the background, and the moment it handshakes this is stopped rather than
 * left to deliver every event twice.
 */

export interface StreamHandlers {
  /** One broadcast envelope, exactly as the socket would have delivered it. */
  line: (line: string) => void
  open: () => void
  failed: () => void
}

export class EventStream {
  private source: EventSource | null = null

  /** Whether the stream is carrying events right now. */
  get open(): boolean {
    return this.source !== null && this.source.readyState === EventSource.OPEN
  }

  /** Whether one has been started, open yet or not. */
  get started(): boolean {
    return this.source !== null
  }

  /** The pairing cookie goes with it, as it does with every same-origin fetch. */
  start(handlers: StreamHandlers): void {
    if (this.source !== null) return
    // Not every environment has one, and a client that throws on the way to its
    // own fallback is worse off than one that simply has no fallback.
    if (typeof EventSource === 'undefined') return

    const source = new EventSource('/api/events')
    this.source = source

    source.addEventListener('open', () => handlers.open())
    source.addEventListener('message', (event: MessageEvent) => handlers.line(String(event.data)))
    source.addEventListener('error', () => {
      // `EventSource` retries by itself, so an error is only final once it has
      // given up and closed; anything else is a reconnection in progress.
      if (source.readyState === EventSource.CLOSED) {
        this.stop()
        handlers.failed()
      }
    })
  }

  stop(): void {
    this.source?.close()
    this.source = null
  }
}
