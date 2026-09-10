import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useDragSort } from './useDragSort'

const ROW = 40

/**
 * A handle inside a real row, in the markup the queue actually renders.
 *
 * Built rather than faked: a stub that answers every `closest` hid a selector
 * naming a tag the virtual list does not render, so no drag ever started.
 */
function handleInARow(): HTMLElement {
  const row = document.createElement('div')
  row.dataset.dragRow = ''
  row.getBoundingClientRect = () => ({ height: ROW }) as DOMRect
  const handle = document.createElement('button')
  handle.setPointerCapture = () => undefined
  row.append(handle)
  document.body.append(row)
  return handle
}

let handle: HTMLElement = handleInARow()

beforeEach(() => {
  document.body.innerHTML = ''
  handle = handleInARow()
})

function pointer(clientY: number): PointerEvent {
  return {
    clientY,
    pointerId: 1,
    currentTarget: handle,
    preventDefault: () => undefined,
  } as unknown as PointerEvent
}

describe('dragging a row to a new place', () => {
  it('lands where the pointer travelled, in whole rows', () => {
    const onDrop = vi.fn<(from: number, to: number) => void>()
    const drag = useDragSort(() => 5, onDrop)

    drag.start(1, pointer(0))
    drag.move(pointer(ROW * 2))
    expect(drag.to.value).toBe(3)

    drag.drop()
    expect(onDrop).toHaveBeenCalledWith(1, 3)
  })

  // The handle finds its row by an attribute the row carries. A selector that
  // named a tag found nothing in the virtual list, so `start` returned before
  // it had picked anything up and every drag was silently refused.
  it('picks the row up when the handle sits in one', () => {
    const drag = useDragSort(() => 5, vi.fn<(from: number, to: number) => void>())

    drag.start(2, pointer(0))

    expect(drag.from.value).toBe(2)
  })

  // A tap on the handle must not reorder anything: without a threshold, a
  // pointer that wobbles by a pixel would still round to the same row and fire.
  it('does not report a move that never left the row', () => {
    const onDrop = vi.fn<(from: number, to: number) => void>()
    const drag = useDragSort(() => 5, onDrop)

    drag.start(2, pointer(0))
    drag.move(pointer(2))
    drag.drop()

    expect(onDrop).not.toHaveBeenCalled()
    expect(drag.from.value).toBe(-1)
  })

  it('cannot be dragged off either end of the list', () => {
    const onDrop = vi.fn<(from: number, to: number) => void>()
    const drag = useDragSort(() => 3, onDrop)

    drag.start(0, pointer(0))
    drag.move(pointer(-ROW * 4))
    expect(drag.to.value).toBe(0)

    drag.move(pointer(ROW * 9))
    expect(drag.to.value).toBe(2)
  })

  // The rows between the source and the target move out of the way by exactly
  // one row, in the direction opposite the drag.
  it('opens a gap by shifting the rows it passes', () => {
    const drag = useDragSort(() => 5, vi.fn<(from: number, to: number) => void>())

    drag.start(1, pointer(0))
    drag.move(pointer(ROW * 2))

    expect(drag.shift(0)).toBe(0)
    expect(drag.shift(2)).toBe(-ROW)
    expect(drag.shift(3)).toBe(-ROW)
    expect(drag.shift(4)).toBe(0)
    expect(drag.shift(1)).toBe(0)
  })
})
