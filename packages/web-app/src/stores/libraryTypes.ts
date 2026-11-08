import type { Op } from '../api/ops'
import type { QueueMode } from '../api/types'

/** What a level is showing beyond its scope: a search, and an order. */
export interface ViewOptions {
  query?: string
  sort?: string
  descending?: boolean
  albumArtists?: boolean
}

/** How a scope goes into the queue: where, from which track, in what order. */
export interface QueueOptions {
  mode?: QueueMode
  /** The track to start from, for "play all from here". */
  play?: string
  shuffle?: boolean
}

/** One page per scroll batch. Large enough that a phone rarely pages twice. */
export const PAGE_SIZE = 100

/** The `query` parameter, or nothing at all when the box is empty. */
export function searchArgOf(query: string): { query?: string } {
  const trimmed = query.trim()
  return trimmed === '' ? {} : { query: trimmed }
}

/** The first `count` rows of a list, read a page at a time. */
export async function readRows<T>(
  count: number,
  read: (offset: number) => Promise<T[]>,
): Promise<T[]> {
  const offsets = Array.from({ length: Math.ceil(count / PAGE_SIZE) }, (_, i) => i * PAGE_SIZE)
  const pages = await Promise.all(offsets.map((offset) => read(offset)))
  return pages.flat()
}

/** The ops that answer a `{total, offset, items}` page. */
export type ListOp =
  | typeof Op.LibraryGenres
  | typeof Op.LibraryArtists
  | typeof Op.LibraryAlbums
  | typeof Op.LibraryTracks
  | typeof Op.LibraryRadio
  | typeof Op.PlaylistList
