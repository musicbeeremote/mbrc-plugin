/**
 * Per-browser identity and credentials.
 *
 * Separate from the transport because it outlives any one connection: the token
 * and the install id survive reloads, and the pairing screen reads them without
 * a socket existing at all.
 *
 * Every accessor tolerates storage throwing. A browser in private mode denies
 * access outright, and a session that works but is not remembered is much better
 * than one that will not start.
 */

const TOKEN_KEY = 'mbrc.token'
const CLIENT_ID_KEY = 'mbrc.client_id'
const CLIENT_TOKEN_KEY = 'mbrc.client_token'

/**
 * Drops the pairing token an older build kept here.
 *
 * The token lives in a cookie the page cannot read, and a browser paired before
 * that change is still holding a working credential in storage any script on
 * this origin can take. Nothing reads it any more, so the only thing left to do
 * with it is throw it away.
 */
export function forgetLegacyToken(): void {
  try {
    localStorage.removeItem(TOKEN_KEY)
  } catch {
    /* a browser that denies storage has nothing stored to forget */
  }
}

/**
 * A version 4 UUID, without asking for a secure context.
 *
 * `crypto.randomUUID` exists only in one, and a browser counts `http://localhost`
 * as secure while `http://192.168.1.20` is not - so on the address a phone
 * actually reaches this server by, the function is simply absent and calling it
 * threw before the app had drawn anything. `getRandomValues` carries no such
 * condition; the last resort keeps a weaker id working rather than throwing,
 * and the server asks only that an id be present and short.
 */
function uuid(): string {
  if (typeof crypto !== 'undefined') {
    if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()

    if (typeof crypto.getRandomValues === 'function') {
      // The v4 version and variant, as arithmetic rather than masks: byte 6
      // keeps its low nibble under a 4, byte 8 its low six bits under a 10.
      const hex = [...crypto.getRandomValues(new Uint8Array(16))]
        .map((byte, index) => {
          if (index === 6) return (byte % 16) + 0x40
          if (index === 8) return (byte % 64) + 0x80
          return byte
        })
        .map((byte) => byte.toString(16).padStart(2, '0'))
        .join('')
      return [
        hex.slice(0, 8),
        hex.slice(8, 12),
        hex.slice(12, 16),
        hex.slice(16, 20),
        hex.slice(20),
      ].join('-')
    }
  }

  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

/** Stable per-install id, minted once and kept. */
export function installId(): string {
  try {
    const existing = localStorage.getItem(CLIENT_ID_KEY)
    if (existing) return existing
    const minted = uuid()
    localStorage.setItem(CLIENT_ID_KEY, minted)
    return minted
  } catch {
    return uuid()
  }
}

/**
 * The identity token the server issued for this `client_id`.
 *
 * Distinct from the pairing token above: this one settles which installation
 * owns a `client_id`, and the server demands it on every handshake after the one
 * that issued it. Keeping the id without it is what makes a reload hand back an
 * id this browser can no longer prove, which the server refuses.
 */
export function storedClientToken(): string | null {
  try {
    return localStorage.getItem(CLIENT_TOKEN_KEY)
  } catch {
    return null
  }
}

/** Keeps the identity token out of a handshake reply, if it carried one. */
export function keepIssuedToken(data: unknown): void {
  const issued = (data as { client_token?: unknown } | undefined)?.client_token
  if (typeof issued !== 'string' || issued.length === 0) return
  try {
    localStorage.setItem(CLIENT_TOKEN_KEY, issued)
  } catch {
    /* the handshake still holds; the next reload starts a new identity */
  }
}

/**
 * Forgets this browser's identity and returns the id that replaces it.
 *
 * The recovery the server names when it refuses a `client_id`: an id whose token
 * we cannot present is unusable forever, and a fresh one is always accepted.
 */
export function renewIdentity(): string {
  try {
    localStorage.removeItem(CLIENT_ID_KEY)
    localStorage.removeItem(CLIENT_TOKEN_KEY)
  } catch {
    /* as above */
  }
  return installId()
}
