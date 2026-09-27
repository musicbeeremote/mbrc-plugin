import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { WireEvent } from '../api/ops'
import { Capability, PermissionsSchema } from '../api/permissions'
import { QueueMode } from '../api/types'
import type { Permissions } from '../api/permissions'

const EVERY_CAPABILITY: readonly string[] = Object.values(Capability)

const QUEUE_CAPABILITY: Record<QueueMode, Capability> = {
  [QueueMode.Last]: Capability.QueueAdd,
  [QueueMode.Next]: Capability.QueueInsert,
  [QueueMode.Now]: Capability.QueueReplace,
  [QueueMode.AddAll]: Capability.QueueReplace,
}

/** The capability queueing at `mode` needs, as the server judges it. */
export function capabilityFor(mode: QueueMode): Capability {
  return QUEUE_CAPABILITY[mode]
}

/**
 * What Party Mode lets this browser do.
 *
 * Starts as everything, which is what a server without Party Mode on answers,
 * so nothing blinks out while the first answer is on its way. The server still
 * refuses what this gets wrong; this only decides what is worth drawing.
 */
export const usePermissionsStore = defineStore('permissions', () => {
  const partyMode = ref(false)
  const role = ref('host')
  const allowed = ref<ReadonlySet<string>>(new Set(EVERY_CAPABILITY))
  const maxTracksPerAdd = ref<number | null>(null)

  function apply(permissions: Permissions) {
    partyMode.value = permissions.party_mode
    role.value = permissions.role
    allowed.value = new Set(permissions.allowed)
    maxTracksPerAdd.value = permissions.max_tracks_per_add ?? null
  }

  function can(capability: Capability): boolean {
    return allowed.value.has(capability)
  }

  /**
   * Whether queueing `count` tracks at `mode` would be let through.
   *
   * `count` is `null` for a scope (an album, an artist), whose size only the
   * server knows, so a role with a per-request limit may not queue one at all.
   */
  function canQueue(mode: QueueMode, count: number | null): boolean {
    if (!can(capabilityFor(mode))) return false
    const max = maxTracksPerAdd.value
    return max === null || (count !== null && count <= max)
  }

  /** Whether pairing could give this browser more than it has. */
  const mayPair = computed(() => partyMode.value && role.value !== 'host')

  /**
   * Asks the capabilities route, for a browser whose socket will not open.
   *
   * The handshake tells a socket client the same thing, and the event stream
   * carries every later change either way.
   */
  async function refresh() {
    const response = await fetch('/api/v6/capabilities').catch(() => null)
    const body = (await response?.json().catch(() => null)) as { permissions?: unknown } | null
    const parsed = PermissionsSchema.safeParse(body?.permissions)
    if (parsed.success) apply(parsed.data)
  }

  function bind() {
    client.on(WireEvent.PermissionsChanged, apply)
    void refresh()
  }

  return { partyMode, role, maxTracksPerAdd, can, canQueue, mayPair, apply, refresh, bind }
})
