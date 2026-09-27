import { z } from 'zod'

/**
 * What Party Mode may let a client do. Reads are never listed: every role may read.
 */
export const Capability = {
  Playback: 'playback',
  QueueAdd: 'queue_add',
  QueueInsert: 'queue_insert',
  QueueReplace: 'queue_replace',
  QueueEdit: 'queue_edit',
  Volume: 'volume',
  Modes: 'modes',
  LibraryEdit: 'library_edit',
  PlaylistEdit: 'playlist_edit',
  Output: 'output',
} as const
export type Capability = (typeof Capability)[keyof typeof Capability]

/**
 * What this client may do, from the handshake, the capabilities route and
 * `permissions_changed` alike.
 *
 * `allowed` stays a list of strings: a capability this build has not heard of
 * gates nothing here, and refusing the whole object over one would hide every
 * control the client was in fact allowed.
 */
export const PermissionsSchema = z.object({
  party_mode: z.boolean(),
  role: z.string(),
  allowed: z.array(z.string()),
  max_tracks_per_add: z.number().optional(),
})
export type Permissions = z.infer<typeof PermissionsSchema>

/**
 * The permissions a handshake reply carries, or `undefined`, which the event's
 * schema then refuses, so a server without Party Mode changes nothing here.
 */
export function permissionsIn(data: unknown): unknown {
  return (data as { permissions?: unknown } | undefined)?.permissions
}
