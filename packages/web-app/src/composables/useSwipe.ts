/**
 * A one-direction swipe, for the gestures a music app is expected to have.
 *
 * Dragging the mini player up to open it is the standard way into a full player
 * on a phone, and tapping it is not a replacement: a thumb already resting on
 * the bar reaches for the gesture first.
 *
 * Only vertical travel counts, and only past a distance a scroll would not
 * produce, so a finger that was really scrolling the list underneath is not
 * mistaken for a swipe.
 */

import { ref } from 'vue'
import type { Ref } from 'vue'

/** How far the finger travels before this is a swipe rather than a tap. */
const THRESHOLD_PX = 40

/** Beyond this much sideways travel it was not a vertical swipe at all. */
const DRIFT_PX = 60

export interface Swipe {
  /** How far the finger has travelled, for drawing the gesture as it happens. */
  offset: Ref<number>
  start: (event: PointerEvent) => void
  move: (event: PointerEvent) => void
  end: () => void
}

export function useSwipe(onUp: () => void, onDown?: () => void): Swipe {
  const offset = ref(0)
  let from: { x: number; y: number } | null = null

  function start(event: PointerEvent) {
    from = { x: event.clientX, y: event.clientY }
    offset.value = 0
  }

  function move(event: PointerEvent) {
    if (!from) return
    if (Math.abs(event.clientX - from.x) > DRIFT_PX) {
      from = null
      offset.value = 0
      return
    }
    offset.value = event.clientY - from.y
  }

  function end() {
    const travelled = offset.value
    from = null
    offset.value = 0
    if (travelled <= -THRESHOLD_PX) onUp()
    else if (travelled >= THRESHOLD_PX) onDown?.()
  }

  return { offset, start, move, end }
}
