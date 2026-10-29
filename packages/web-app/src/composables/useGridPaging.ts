/**
 * A cover grid's own paging.
 *
 * It is not virtualized - its rows hold several items each, and a screenful of
 * album art is a few dozen elements rather than a few thousand - so it pages on
 * the scrollbar reaching the end rather than on a row index.
 */

interface GridPagingOptions {
  /** Whether the server has more beyond what is loaded. */
  hasMore: () => boolean
  loading: () => boolean
  loadMore: () => unknown
}

/** The grid's scroll handler: asks for the next page within a screenful of the end. */
export function useGridPaging({ hasMore, loading, loadMore }: GridPagingOptions) {
  return function onGridScroll(event: Event): void {
    const el = event.target as HTMLElement
    const remaining = el.scrollHeight - el.scrollTop - el.clientHeight
    if (remaining < el.clientHeight && hasMore() && !loading()) void loadMore()
  }
}
