/**
 * Reordering a list by dragging a row.
 *
 * Pointer events rather than HTML5 drag and drop, which does not exist on
 * touch: a phone is the main way this list is reordered. A row is picked up by
 * its handle, not by the row itself, so a finger dragged anywhere else still
 * scrolls the list.
 *
 * The maths is index arithmetic on a uniform row height, measured from the row
 * being dragged rather than assumed, so a change of type size does not silently
 * offset every drop by a few pixels.
 *
 * Picking a row up and putting it down are answered by a tick of haptics where
 * the device has them, so a gesture is known to have taken before the eye
 * confirms it. Silently ignored everywhere else, which includes iOS.
 */

import { ref } from 'vue'
import type { Ref } from 'vue'

export interface DragSort {
  /** The index being dragged, or -1 when nothing is. */
  from: Ref<number>
  /** Where it would land if released now. */
  to: Ref<number>
  /** How far the dragged row has travelled, for drawing it under the finger. */
  offset: Ref<number>
  /** How far a row at `index` has to shift to make room. */
  shift: (index: number) => number
  start: (index: number, event: PointerEvent) => void
  move: (event: PointerEvent) => void
  drop: () => void
}

/** A row lifted this far is being dragged, not tapped. */
const SLOP_PX = 4

/**
 * What the handle looks upward for to find the row it belongs to.
 *
 * An attribute rather than a tag: the list this reorders is virtual, so its
 * rows are whatever element the windowing renders, and a selector naming a tag
 * finds nothing and quietly refuses every drag.
 */
const ROW_SELECTOR = '[data-drag-row]'

export function useDragSort(count: () => number, onDrop: (from: number, to: number) => void): DragSort {
  const from = ref(-1)
  const to = ref(-1)
  const offset = ref(0)

  let rowHeight = 0
  let startY = 0

  function shift(index: number): number {
    if (from.value < 0 || index === from.value) return 0
    const height = rowHeight
    if (from.value < to.value && index > from.value && index <= to.value) return -height
    if (from.value > to.value && index < from.value && index >= to.value) return height
    return 0
  }

  function start(index: number, event: PointerEvent) {
    const row = (event.currentTarget as HTMLElement).closest(ROW_SELECTOR)
    if (!row) return
    rowHeight = row.getBoundingClientRect().height
    startY = event.clientY
    from.value = index
    to.value = index
    offset.value = 0
    navigator.vibrate?.(8)
    ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
    event.preventDefault()
  }

  function move(event: PointerEvent) {
    if (from.value < 0) return
    const travelled = event.clientY - startY
    if (Math.abs(travelled) < SLOP_PX && to.value === from.value) return
    offset.value = travelled
    const steps = rowHeight > 0 ? Math.round(travelled / rowHeight) : 0
    to.value = Math.min(Math.max(from.value + steps, 0), Math.max(count() - 1, 0))
  }

  function drop() {
    if (from.value >= 0 && to.value !== from.value) {
      navigator.vibrate?.(12)
      onDrop(from.value, to.value)
    }
    from.value = -1
    to.value = -1
    offset.value = 0
  }

  return { from, to, offset, shift, start, move, drop }
}
