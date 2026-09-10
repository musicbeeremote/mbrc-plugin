/**
 * One op over HTTP, which is how the app talks when a socket will not open.
 *
 * Apart from the client because it holds no connection state: this path has to
 * work before a socket has ever been opened, and still works after the client
 * has stopped trying to open one. Events do not come this way - the socket is
 * the only thing that carries them - so a session on this path answers what it
 * is asked and hears nothing unprompted.
 */

import { OpError, decodeJson, parseError, parseResponse } from './parse'
import type { Op, OpRequests } from './ops'
import type { OpResponses } from './responses'
import { ErrorCode } from './types'

export async function callOverHttp<K extends Op>(
  op: K,
  data: OpRequests[K],
): Promise<OpResponses[K]> {
  const response = await fetch(`/api/v6/${op}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  })
  const body = decodeJson(await response.text())
  if (!response.ok) {
    throw new OpError(
      parseError(body?.error) ?? { code: ErrorCode.Internal, message: response.statusText },
    )
  }
  return parseResponse(op, body)
}
