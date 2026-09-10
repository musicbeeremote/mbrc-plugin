/**
 * The V6 client.
 *
 * The browser is an ordinary V6 client that happens to speak over a WebSocket:
 * the same handshake, the same ops, the same events. `call` correlates replies
 * by `id`; REST is the fallback for a browser where the socket will not open,
 * and the choice is invisible to the caller.
 */

import { MAX_ATTEMPTS, delayFor } from './backoff'
import { isAuthFailure, isIdentityRefusal } from './refusals'
import { EventStream } from './event-stream'
import { callOverHttp } from './http'
import type { EventName, Op, OpRequests } from './ops'
import { EventPayloadSchemas } from './responses'
import type { EventPayloads, OpResponses } from './responses'
import { OpError, decodeJson, parseError, parseResponse } from './parse'
import { installId, keepIssuedToken, renewIdentity, storedClientToken } from './session'
import { ErrorCode } from './types'

/**
 * Listeners and pending calls are stored untyped and handed back typed by
 * `on` and `call`.
 *
 * The alternative is a map keyed by event name with a differently-typed value
 * per key, which TypeScript cannot express for a heterogeneous Map without the
 * same assertion happening somewhere less visible.
 */
type Listener = (data: never) => void

interface Pending {
  /** Kept so the reply can be parsed by the schema of the op that asked. */
  op: Op
  resolve: (value: never) => void
  reject: (reason: unknown) => void
}

export class V6Client {
  private socket: WebSocket | null = null
  private nextId = 1
  private readonly pending = new Map<number, Pending>()
  private readonly listeners = new Map<string, Set<Listener>>()
  private attempt = 0
  private reconnectTimer: number | null = null
  private closedDeliberately = false
  private handshaked = false
  private readonly events = new EventStream()

  /** Per-install id, so the server can group this browser connections. */
  private clientId = installId()

  /**
   * Opens the connection, and is the way back from having given up: an explicit
   * ask starts the attempts over rather than resuming a spent count. The
   * retries call `open` instead, which does not.
   */
  connect(): void {
    this.attempt = 0
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    this.open()
  }

  private open(): void {
    this.closedDeliberately = false
    if (this.socket && this.socket.readyState <= WebSocket.OPEN) return

    // No token in the URL: the browser sends the pairing cookie on the upgrade
    // like it does on everything else from this origin.
    const scheme = location.protocol === 'https:' ? 'wss' : 'ws'
    const socket = new WebSocket(`${scheme}://${location.host}/ws`)
    this.socket = socket

    socket.addEventListener('open', () => {
      this.handshake()
    })
    socket.addEventListener('message', (event: MessageEvent) => {
      this.receive(String(event.data))
    })
    socket.addEventListener('close', () => {
      this.handshaked = false
      this.failAllPending(new Error('connection closed'))
      if (this.closedDeliberately) {
        this.emitConnection(false)
        return
      }
      this.scheduleReconnect()
      this.startEventStream()
    })
    socket.addEventListener('error', () => {
      socket.close()
    })
  }

  /** Stops reconnecting. Used on `server_shutdown` and when the page goes away. */
  disconnect(): void {
    this.closedDeliberately = true
    this.events.stop()
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    this.socket?.close()
    this.socket = null
  }

  get connected(): boolean {
    return this.handshaked
  }

  on<E extends EventName>(event: E, listener: (data: EventPayloads[E]) => void): () => void {
    let set = this.listeners.get(event)
    if (!set) {
      set = new Set()
      this.listeners.set(event, set)
    }
    set.add(listener as Listener)
    return () => set.delete(listener as Listener)
  }

  /**
   * Runs one op. Uses the socket when it is up and HTTP otherwise, so a caller
   * never has to know which transport carried it.
   */
  async call<K extends Op>(op: K, data: OpRequests[K] = {} as OpRequests[K]): Promise<OpResponses[K]> {
    if (this.socket?.readyState === WebSocket.OPEN && this.handshaked) {
      return this.callOverSocket(op, data)
    }
    try {
      return await callOverHttp(op, data)
    } catch (error) {
      if (error instanceof OpError && isAuthFailure(error)) this.rejectAuth()
      throw error
    }
  }

  private callOverSocket<K extends Op>(op: K, data: OpRequests[K]): Promise<OpResponses[K]> {
    const id = this.nextId
    this.nextId += 1
    return new Promise<OpResponses[K]>((resolve, reject) => {
      this.pending.set(id, { op, resolve: resolve as Pending['resolve'], reject })
      this.socket?.send(JSON.stringify({ id, kind: 'request', op, data }))
    })
  }

  private handshake(): void {
    const clientToken = storedClientToken()
    const frame = {
      id: 0,
      kind: 'request',
      op: 'handshake',
      data: {
        protocol_version: 6,
        client_id: this.clientId,
        client_type: 'web',
        ...(clientToken ? { client_token: clientToken } : {}),
      },
    }
    this.socket?.send(JSON.stringify(frame))
  }

  private receive(line: string): void {
    const frame = decodeJson(line)
    if (!frame) return

    if (frame.kind === 'event') {
      // Unknown events are skipped, never treated as errors: the catalog grows
      // additively and a client that rejected one could never be added to.
      this.dispatchEvent(String(frame.event), frame.data)
      return
    }
    if (frame.kind !== 'response') return

    const id = Number(frame.id)
    if (id === 0) {
      this.handshaked = frame.error === undefined
      if (this.handshaked) {
        keepIssuedToken(frame.data)
        // Reset here rather than on `open`: a proxy in front of a server that is
        // down accepts the socket and drops it immediately, and treating that as
        // success would hold the backoff at its first step forever.
        this.attempt = 0
        // The socket carries events too, so leaving the stream open would
        // deliver every one of them twice.
        this.events.stop()
      } else if (isIdentityRefusal(frame.error)) {
        this.clientId = renewIdentity()
      } else if (isAuthFailure(frame.error)) {
        this.rejectAuth()
      }
      this.emitConnection(this.handshaked)
      return
    }

    const pending = this.pending.get(id)
    if (!pending) return
    this.pending.delete(id)
    if (frame.error) {
      pending.reject(
        new OpError(
          parseError(frame.error) ?? { code: ErrorCode.Internal, message: 'unreadable error' },
        ),
      )
      return
    }
    try {
      ;(pending.resolve as (value: unknown) => void)(parseResponse(pending.op, frame.data))
    } catch (error) {
      pending.reject(error)
    }
  }

  /**
   * Drops a token the server will not accept and stops reconnecting.
   *
   * Retrying with the same rejected token is an infinite loop that can only be
   * broken by pairing again, so the app is told to ask rather than left
   * reconnecting into the same refusal.
   */
  private rejectAuth(): void {
    this.disconnect()
    this.dispatchEvent('auth_required', {})
  }

  /** `retrying` says whether another attempt is already on a timer. */
  private emitConnection(connected: boolean, retrying = !connected): void {
    this.dispatchEvent('connection_changed', { connected, retrying })
  }

  /**
   * Fans one event out. `event` is a wire string rather than an `EventName`
   * because the catalog grows additively: an event this build has never heard
   * of has no listeners and is dropped, never treated as an error.
   */
  private dispatchEvent(event: string, data: unknown): void {
    const listeners = this.listeners.get(event)
    if (!listeners) return
    const schema = EventPayloadSchemas[event as EventName]
    // An event whose payload does not parse is dropped rather than delivered:
    // a listener patching state from it would write whatever came through.
    const parsed = schema ? schema.safeParse(data ?? {}) : null
    if (parsed && !parsed.success) return
    const payload = parsed ? parsed.data : (data ?? {})
    for (const listener of listeners) (listener as (value: unknown) => void)(payload)
  }

  private failAllPending(reason: unknown): void {
    for (const pending of this.pending.values()) pending.reject(reason)
    this.pending.clear()
  }

  /**
   * Backs off, and eventually stops.
   *
   * Giving up is reported rather than silent, so the app can offer to try again
   * instead of showing a spinner over a server that went home hours ago.
   */
  private scheduleReconnect(): void {
    // A stream that is carrying events is the app working, so the socket's own
    // progress is not news: reporting it would flap a notice over a live page.
    if (this.attempt >= MAX_ATTEMPTS) {
      if (!this.events.started) this.emitConnection(false, false)
      return
    }
    const delay = delayFor(this.attempt)
    this.attempt += 1
    if (!this.events.started) this.emitConnection(false)
    this.reconnectTimer = window.setTimeout(() => {
      this.open()
    }, delay)
  }

  /**
   * Opens the HTTP event stream, so a browser that cannot hold a socket still
   * sees what changes. The socket keeps being chased behind it.
   */
  private startEventStream(): void {
    this.events.start({
      line: (line) => this.receive(line),
      open: () => this.emitConnection(true),
      failed: () => this.emitConnection(false, this.attempt < MAX_ATTEMPTS),
    })
  }
}

export const client = new V6Client()
