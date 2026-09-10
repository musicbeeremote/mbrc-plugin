import { describe, expect, it, vi } from 'vitest'

import { useSwipe } from './useSwipe'

function at(x: number, y: number): PointerEvent {
  return { clientX: x, clientY: y } as PointerEvent
}

describe('a swipe on the mini player', () => {
  it('opens the player when the finger travels far enough up', () => {
    const up = vi.fn<() => void>()
    const swipe = useSwipe(up)

    swipe.start(at(100, 500))
    swipe.move(at(100, 440))
    expect(swipe.offset.value).toBe(-60)
    swipe.end()

    expect(up).toHaveBeenCalledTimes(1)
    expect(swipe.offset.value).toBe(0)
  })

  // A bar that opened on any upward pixel would open whenever a thumb rested
  // on it before scrolling.
  it('ignores travel too small to be meant', () => {
    const up = vi.fn<() => void>()
    const swipe = useSwipe(up)

    swipe.start(at(100, 500))
    swipe.move(at(100, 480))
    swipe.end()

    expect(up).not.toHaveBeenCalled()
  })

  it('calls the other way only when there is somewhere to go', () => {
    const up = vi.fn<() => void>()
    const down = vi.fn<() => void>()
    const swipe = useSwipe(up, down)

    swipe.start(at(100, 100))
    swipe.move(at(100, 200))
    swipe.end()

    expect(down).toHaveBeenCalledTimes(1)
    expect(up).not.toHaveBeenCalled()
  })

  // A finger going sideways is on its way somewhere else.
  it('gives up when the finger drifts across', () => {
    const up = vi.fn<() => void>()
    const swipe = useSwipe(up)

    swipe.start(at(100, 500))
    swipe.move(at(200, 430))
    swipe.end()

    expect(up).not.toHaveBeenCalled()
  })
})
