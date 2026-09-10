/**
 * Telling apart the two ways the server can refuse this browser.
 *
 * They look alike and are fixed by opposite things: a token it will not accept
 * is what pairing is for, while an id another installation already holds is
 * fixed by taking a new one and not by pairing at all.
 */

import { ErrorCode } from './types'
import type { V6Error } from './types'

/** Whether an error means this browser's token will never be accepted as-is. */
export function isAuthFailure(error: unknown): boolean {
  const code = (error as V6Error | undefined)?.code
  return code === ErrorCode.Unauthorized || code === ErrorCode.InvalidToken
}

/** Whether the stored `client_id` is spoken for, which a new id fixes and pairing does not. */
export function isIdentityRefusal(error: unknown): boolean {
  const detail = error as V6Error | undefined
  return detail?.code === ErrorCode.InvalidToken && detail.field === 'client_token'
}
