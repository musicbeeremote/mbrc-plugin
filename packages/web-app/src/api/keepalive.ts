/** The interval a server that names none expects, in seconds. */
export const DEFAULT_PING_INTERVAL_SECS = 15

/**
 * Pings a socket at the interval its handshake reply names.
 *
 * The server closes a V6 socket that stays silent for three intervals, and an
 * idle page sends nothing else.
 */
export class Keepalive {
  private timer: number | null = null

  start(handshakeReply: unknown, ping: () => void): void {
    this.stop()
    const named = (handshakeReply as { ping_interval_secs?: unknown } | null)?.ping_interval_secs
    const secs = typeof named === 'number' && named > 0 ? named : DEFAULT_PING_INTERVAL_SECS
    this.timer = window.setInterval(ping, secs * 1000)
  }

  stop(): void {
    if (this.timer === null) return
    clearInterval(this.timer)
    this.timer = null
  }
}
