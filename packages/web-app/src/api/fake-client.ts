/**
 * A `client` stand-in for the store tests.
 *
 * Only test files import it, so it never reaches the bundle. A test swaps it in
 * with `vi.mock(import('../api/client'), () => import('../api/fake-client'))`
 * and then drives `call` and `on` to script the server.
 */

import { vi } from 'vitest'

import type { client as V6Client } from './client'

export const call = vi.fn<(op: string, data?: Record<string, unknown>) => Promise<unknown>>()
export const on = vi.fn<(event: string, listener: (data?: unknown) => void) => () => void>()

export const client = { call, on } as unknown as typeof V6Client

/** The op and payload of the most recent request. */
export function lastCall(): [string, Record<string, unknown>] {
  return call.mock.calls.at(-1) as [string, Record<string, unknown>]
}

/** The listener the store registered for `event`. */
export function handler(event: string): (data?: unknown) => void {
  const found = on.mock.calls.find(([name]) => name === event)?.[1]
  if (!found) throw new Error(`bind() registered no ${event} handler`)
  return found
}
