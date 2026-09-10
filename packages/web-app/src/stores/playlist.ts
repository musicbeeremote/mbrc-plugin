/**
 * One playlist's contents.
 *
 * Apart from the library store on purpose: a playlist is its own list with its
 * own count and its own paging, and sharing the browse level's would have
 * closing a playlist leave the level underneath reporting a total that was
 * never its own.
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { Op } from '../api/ops'
import type { PlaylistTrack, QueryField } from '../api/types'
import { QueryField as Field } from '../api/types'

/** Rows per request, matching the library's own page size. */
const PAGE_SIZE = 200

export const usePlaylistStore = defineStore('playlist', () => {
  const tracks = ref<PlaylistTrack[]>([])
  const name = ref('')
  /** Echoed back by a future playlist mutation (#115); opaque here. */
  const version = ref('')
  const total = ref(0)
  const totalDurationMs = ref(0)
  const query = ref('')
  /** Which column the search reads; `any` takes them all. */
  const queryField = ref<QueryField>(Field.Any)
  const loading = ref(false)

  const hasMore = computed(() => tracks.value.length < total.value)

  /**
   * The search parameters, or nothing at all when the box is empty.
   *
   * The field rides with the term rather than on its own: naming a column to
   * search and searching for nothing is not a narrower list, it is the list.
   */
  function searchArg(): { query?: string; query_field?: QueryField } {
    const trimmed = query.value.trim()
    if (trimmed === '') return {}
    return queryField.value === Field.Any
      ? { query: trimmed }
      : { query: trimmed, query_field: queryField.value }
  }

  /**
   * Reads a playlist, or continues the one already open when appending.
   *
   * The totals are asked for only when the list is read from the start: they
   * describe the whole list rather than a page, so a second page would pay the
   * server to count what the first page already reported.
   */
  async function load(url: string, append = false): Promise<void> {
    loading.value = true
    try {
      const offset = append ? tracks.value.length : 0
      const page = await client.call(Op.PlaylistTracks, {
        url,
        offset,
        limit: PAGE_SIZE,
        ...searchArg(),
        ...(append ? {} : { totals: true }),
      })
      tracks.value = append ? [...tracks.value, ...page.items] : page.items
      name.value = page.name
      version.value = page.version
      total.value = page.total
      if (!append) totalDurationMs.value = page.total_duration_ms ?? 0
    } finally {
      loading.value = false
    }
  }

  /** Narrows the open playlist, reading it again from the start. */
  async function search(url: string, term: string, field: QueryField = Field.Any): Promise<void> {
    query.value = term
    queryField.value = field
    await load(url)
  }

  /** Drops what is held, so the next playlist opened cannot show these rows. */
  function close() {
    tracks.value = []
    name.value = ''
    version.value = ''
    total.value = 0
    totalDurationMs.value = 0
    query.value = ''
    queryField.value = Field.Any
  }

  return {
    tracks,
    name,
    version,
    total,
    totalDurationMs,
    query,
    queryField,
    loading,
    hasMore,
    load,
    search,
    close,
  }
})
