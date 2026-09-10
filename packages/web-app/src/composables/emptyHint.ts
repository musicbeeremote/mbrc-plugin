/**
 * The line under an empty pane, which has to say which nothing this is.
 *
 * A search that found nothing is explained by the search itself, so it takes no
 * line at all. An empty level inside an artist is not the library being empty,
 * and saying so sends someone looking for a fault that is not there.
 */

import type { LibraryScope } from '../api/ops'

export function emptyHintKey(query: string, scope: LibraryScope): string | undefined {
  if (query !== '') return undefined
  return Object.keys(scope).length > 0 ? 'library.emptyScopedHint' : 'library.emptyHint'
}
