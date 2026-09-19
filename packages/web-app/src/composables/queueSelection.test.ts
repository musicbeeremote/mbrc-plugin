import { nextTick, ref } from 'vue'
import { describe, expect, it } from 'vitest'

import { useQueueSelection } from './queueSelection'

function rowsOf(...orders: number[]) {
  return ref(orders.map((order) => ({ order })))
}

describe('picking rows', () => {
  it('toggles a row on and off', () => {
    const selection = useQueueSelection(rowsOf(0, 1, 2), ref(1), ref(''))
    selection.toggle(1)
    expect(selection.orders.value).toStrictEqual([1])
    selection.toggle(1)
    expect(selection.count.value).toBe(0)
  })

  it('extends from the last row toggled, in either direction', () => {
    const selection = useQueueSelection(rowsOf(0, 1, 2, 3, 4), ref(1), ref(''))
    selection.toggle(3)
    selection.toggle(1, true)
    expect(new Set(selection.orders.value)).toStrictEqual(new Set([1, 2, 3]))
  })

  it('treats an extend with nothing toggled yet as a plain toggle', () => {
    const selection = useQueueSelection(rowsOf(0, 1, 2), ref(1), ref(''))
    selection.toggle(2, true)
    expect(selection.orders.value).toStrictEqual([2])
  })

  it('selects every row shown', () => {
    const selection = useQueueSelection(rowsOf(4, 5), ref(1), ref(''))
    selection.selectAll()
    expect(selection.allPicked.value).toBe(true)
    expect(selection.count.value).toBe(2)
  })
})

describe('a queue that moves', () => {
  // Every held order may name a different track once the version moves, so a
  // remove sent with them would take out tracks nobody picked.
  it('drops the selection when the version changes', async () => {
    const version = ref(1)
    const selection = useQueueSelection(rowsOf(0, 1, 2), version, ref(''))
    selection.start()
    selection.toggle(0)
    selection.toggle(2)

    version.value = 2
    await nextTick()

    expect(selection.count.value).toBe(0)
    expect(selection.active.value).toBe(true)
  })

  it('forgets the range anchor along with the rows', async () => {
    const version = ref(1)
    const selection = useQueueSelection(rowsOf(0, 1, 2, 3), version, ref(''))
    selection.toggle(0)
    version.value = 2
    await nextTick()

    selection.toggle(3, true)
    expect(selection.orders.value).toStrictEqual([3])
  })
})

describe('a search that changes', () => {
  it('drops what was picked, so no hidden row is removed', async () => {
    const search = ref('')
    const selection = useQueueSelection(rowsOf(0, 1, 2), ref(1), search)
    selection.start()
    selection.toggle(0)

    search.value = 'abc'
    await nextTick()

    expect(selection.count.value).toBe(0)
    expect(selection.active.value).toBe(true)
  })
})

describe('leaving select mode', () => {
  it('clears what was picked', () => {
    const selection = useQueueSelection(rowsOf(0, 1), ref(1), ref(''))
    selection.start()
    selection.toggle(1)
    selection.stop()
    expect(selection.active.value).toBe(false)
    expect(selection.count.value).toBe(0)
  })
})
