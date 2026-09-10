/**
 * The boundary where untrusted data becomes typed data.
 *
 * Everything above this line is typed by the op catalog; everything the network
 * hands us is `unknown` until it has been through a schema here. Keeping the
 * whole boundary in one module is what lets every call site and store stay
 * cast-free.
 */

import type { Op } from './ops'
import { OpResponseSchemas } from './responses'
import type { OpResponses } from './responses'
import { ErrorCode, V6ErrorSchema } from './types'
import type { V6Error } from './types'

/** A V6 op failed. `code` is the protocol's own error code, not an HTTP status. */
export class OpError extends Error {
  /** Widened past `ErrorCode`: a server may report a code this build predates. */
  readonly code: V6Error['code']
  readonly field?: string

  constructor(error: V6Error) {
    super(error.message)
    this.name = 'OpError'
    this.code = error.code
    this.field = error.field
  }
}

/**
 * A frame that is not JSON is dropped rather than thrown, so one malformed frame
 * cannot take the socket down with it.
 */
export function decodeJson(line: string): Record<string, unknown> | null {
  try {
    return JSON.parse(line) as Record<string, unknown>
  } catch {
    return null
  }
}

/**
 * The error half of a failed response, or null when it is not one.
 *
 * A malformed error is still a failure; it just cannot say which. Refusing to
 * parse it would turn a reported problem into silence.
 */
export function parseError(value: unknown): V6Error | null {
  const parsed = V6ErrorSchema.safeParse(value)
  return parsed.success ? parsed.data : null
}

/**
 * Parses one op's answer, or throws the protocol error for a payload that is not
 * the shape the catalog promises.
 *
 * A field that is missing or the wrong type surfaces at some unrelated call site
 * later, as a render of `undefined` rather than as the protocol mismatch it is.
 */
export function parseResponse<K extends Op>(op: K, data: unknown): OpResponses[K] {
  const parsed = OpResponseSchemas[op].safeParse(data ?? {})
  if (!parsed.success) {
    throw new OpError({
      code: ErrorCode.Internal,
      message: `${op} answered a payload this client cannot read`,
      field: parsed.error.issues[0]?.path.join('.') || undefined,
    })
  }
  return parsed.data as OpResponses[K]
}
