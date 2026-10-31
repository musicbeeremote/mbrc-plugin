import type { LibraryScope } from '../api/ops'

import type { LibraryLevel, useLibraryStore } from './library'

type Store = ReturnType<typeof useLibraryStore>

/** Opening a tab, as the router does it: a level with nothing in scope. */
export function openTab(library: Store, level: LibraryLevel): Promise<void> {
  return library.show({ level, scope: {} })
}

/** Drilling in, as the router does it: the scope in hand plus one more filter. */
export function drill(library: Store, level: LibraryLevel, add: LibraryScope): Promise<void> {
  return library.show({ level, scope: { ...library.scope, ...add } })
}

/** Searching, as the router does it: the same position, a different `q`. */
export function search(library: Store, term: string): Promise<void> {
  return library.show({ level: library.level, scope: library.scope }, { query: term })
}
