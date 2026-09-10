/**
 * A long list that renders only what is on screen and fetches the rest as it is
 * reached.
 *
 * Two problems, one answer. A library of tens of thousands of rows cannot all be
 * in the DOM, and it cannot all be pulled over the wire either; both are solved
 * by knowing which window the viewport is looking at. The virtual list gives
 * that window, and the same number that decides what to render decides when the
 * next page is due.
 */

import { useVirtualList } from '@vueuse/core'
import type { Ref } from 'vue'
import { computed, watch } from 'vue'

/**
 * How close to the end of what is loaded the viewport gets before the next page
 * is asked for. Roughly a screenful, so the fetch is already in flight by the
 * time the rows would have run out.
 */
const PREFETCH_ROWS = 20

/** Rows kept rendered above and below the viewport, so a fast scroll stays filled. */
const OVERSCAN = 10

interface LazyRowsOptions {
  /** Uniform row height in pixels. Rows must all be this tall for the maths to hold. */
  rowHeight: Ref<number>
  /** Whether the server has more beyond what is loaded. */
  hasMore: Ref<boolean>
  /** True while a page is in flight, so the end is not asked for twice. */
  loading: Ref<boolean>
  loadMore: () => Promise<void>
}

export function useLazyRows<T>(
  items: Ref<T[]>,
  options: LazyRowsOptions,
): ReturnType<typeof useVirtualList<T>> {
  const { list, containerProps, wrapperProps, scrollTo } = useVirtualList(items, {
    itemHeight: () => options.rowHeight.value,
    overscan: OVERSCAN,
  })

  /** The furthest row the viewport has reached, which is what pages the list. */
  const lastVisible = computed(() => list.value.at(-1)?.index ?? 0)

  watch(lastVisible, (index) => {
    if (!options.hasMore.value || options.loading.value) return
    if (index >= items.value.length - PREFETCH_ROWS) void options.loadMore()
  })

  return { list, containerProps, wrapperProps, scrollTo }
}
