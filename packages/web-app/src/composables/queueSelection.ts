/**
 * Which queue or playlist rows are picked for a batch action, keyed by `order`.
 *
 * An `order` is a storage slot, and it only names a track in the list it was
 * read from. So the selection belongs to one version of the queue: when the
 * version moves, every held order may now point at a different track, and the
 * selection is dropped rather than carried across to rows nobody picked.
 *
 * A change of search drops it too. Picks hidden by a search would still be
 * removed, and "select all" over the matches would silently discard them.
 *
 * A range runs between the last row toggled and the one shift-clicked, over the
 * rows as shown, so under a search it spans the matches rather than the hidden
 * rows between them.
 */

import { computed, ref, watch } from 'vue'
import type { ComputedRef, Ref } from 'vue'

interface Ordered {
  order: number
}

export interface QueueSelection {
  active: Ref<boolean>
  count: ComputedRef<number>
  orders: ComputedRef<number[]>
  allPicked: ComputedRef<boolean>
  start: () => void
  stop: () => void
  has: (order: number) => boolean
  toggle: (order: number, extend?: boolean) => void
  selectAll: () => void
  clear: () => void
}

export function useQueueSelection(
  rows: Ref<readonly Ordered[]>,
  version: Ref<number | string>,
  search: Ref<string>,
): QueueSelection {
  const active = ref(false)
  const picked = ref(new Set<number>())
  let anchor: number | undefined = undefined

  function clear() {
    picked.value = new Set()
    anchor = undefined
  }

  watch([version, search], clear)

  function start() {
    active.value = true
  }

  function stop() {
    active.value = false
    clear()
  }

  function has(order: number): boolean {
    return picked.value.has(order)
  }

  /** Toggles one row, or with `extend` selects every row from the anchor to it. */
  function toggle(order: number, extend = false) {
    const next = new Set(picked.value)
    const at = rows.value.findIndex((row) => row.order === order)
    const from = anchor === undefined ? -1 : rows.value.findIndex((row) => row.order === anchor)
    if (extend && from !== -1 && at !== -1) {
      const [low, high] = from < at ? [from, at] : [at, from]
      for (const row of rows.value.slice(low, high + 1)) next.add(row.order)
    } else if (next.has(order)) {
      next.delete(order)
    } else {
      next.add(order)
    }
    anchor = order
    picked.value = next
  }

  function selectAll() {
    picked.value = new Set(rows.value.map((row) => row.order))
  }

  const count = computed(() => picked.value.size)
  const orders = computed(() => [...picked.value])
  const allPicked = computed(
    () => rows.value.length > 0 && rows.value.every((row) => picked.value.has(row.order)),
  )

  return { active, count, orders, allPicked, start, stop, has, toggle, selectAll, clear }
}
