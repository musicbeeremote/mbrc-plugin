/**
 * Queueing from a track row, and how many tracks each placement takes, which
 * is what Party Mode judges a placement by.
 */

import { QueueMode } from '../api/types'
import { useLibraryStore } from '../stores/library'
import type { QueueOptions } from '../stores/library'
import { usePermissionsStore } from '../stores/permissions'

/** An album, genre or artist row queues a scope, whose size the server alone knows. */
export const wholeScope = (): number | null => null

/** A track row queues that one track, except add-all, which takes the whole level. */
export const trackOrLevel = (mode: QueueMode): number | null =>
  mode === QueueMode.AddAll ? null : 1

/** Queues the scope a track row belongs to, which is what add-all starts from. */
export type ScopeQueuer = (options: QueueOptions) => Promise<unknown>

/** The library level on screen, which is the scope of a library track row. */
const libraryLevel: ScopeQueuer = (options) => useLibraryStore().queueScope({}, options)

/**
 * A track's own queue actions, when this browser's role allows the placement.
 *
 * Three of them name this one track. The fourth means "start here and take the
 * rest with you", which is the whole scope rather than the page of it on screen,
 * so the server resolves it through `queueScope`.
 */
export async function queueTrack(
  src: string,
  mode: QueueMode,
  queueScope: ScopeQueuer = libraryLevel,
): Promise<void> {
  if (!usePermissionsStore().canQueue(mode, trackOrLevel(mode))) return
  await (mode === QueueMode.AddAll
    ? queueScope({ mode, play: src })
    : useLibraryStore().queue([src], mode))
}
