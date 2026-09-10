/**
 * How long to wait before chasing a dropped connection again, and when to stop.
 *
 * Apart from the client because it is policy rather than mechanism: the numbers
 * are the interesting part and they are easier to argue about in one place.
 */

const MAX_BACKOFF_MS = 30_000
const BASE_BACKOFF_MS = 500

/**
 * How many times a dropped connection is chased before the app stops and says
 * so.
 *
 * Eight attempts is about two minutes with the delays below, which covers a
 * restarted plugin and a phone changing network. Past that the server is not
 * coming back on its own, and a page retrying into an empty room forever drains
 * a battery to tell nobody anything: better to stop and offer the button.
 */
export const MAX_ATTEMPTS = 8

/** Doubling, capped, so a long outage is checked twice a minute and no more. */
export function delayFor(attempt: number): number {
  return Math.min(BASE_BACKOFF_MS * 2 ** attempt, MAX_BACKOFF_MS)
}
