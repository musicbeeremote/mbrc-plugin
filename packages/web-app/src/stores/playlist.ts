/**
 * One playlist's contents, and the edits made to playlists.
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
import type { OpRequests, PlaylistSource } from '../api/ops'
import { OpError } from '../api/parse'
import type { PlaylistTrack, QueryField } from '../api/types'
import { ErrorCode, QueryField as Field } from '../api/types'

import { useLibraryStore } from './library'

/** Rows per request, matching the library's own page size. */
const PAGE_SIZE = 200

function reasonOf(error: unknown): string {
  return error instanceof OpError ? error.message : String(error)
}

/** The edits that name slots of the open playlist, and so carry its `version`. */
type GuardedOp = typeof Op.PlaylistRemoveTracks | typeof Op.PlaylistMoveTracks

export const usePlaylistStore = defineStore('playlist', () => {
  /** The playlist open, which every edit of "this playlist" is made to. */
  const url = ref('')
  const tracks = ref<PlaylistTrack[]>([])
  const name = ref('')
  /** Echoed back on an edit, so a playlist that moved since is refused. */
  const version = ref('')
  /** False for an auto playlist, which offers no edits at all. */
  const editable = ref(false)
  const total = ref(0)
  const totalDurationMs = ref(0)
  const query = ref('')
  /** Which column the search reads; `any` takes them all. */
  const queryField = ref<QueryField>(Field.Any)
  const loading = ref(false)
  /** The playlist changed under an edit, so the list shown was read again. */
  const stale = ref(false)
  /** Why the last edit failed, until a later read or edit succeeds. */
  const failure = ref('')

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
  async function load(from: string, append = false): Promise<void> {
    loading.value = true
    try {
      const offset = append ? tracks.value.length : 0
      const page = await client.call(Op.PlaylistTracks, {
        url: from,
        offset,
        limit: PAGE_SIZE,
        ...searchArg(),
        ...(append ? {} : { totals: true }),
      })
      url.value = from
      tracks.value = append ? [...tracks.value, ...page.items] : page.items
      name.value = page.name
      version.value = page.version
      editable.value = page.editable
      total.value = page.total
      if (!append) {
        totalDurationMs.value = page.total_duration_ms ?? 0
        stale.value = false
        failure.value = ''
      }
    } finally {
      loading.value = false
    }
  }

  /** Reads the rest of the open playlist, for an action over every row. */
  async function loadAll(): Promise<void> {
    if (!hasMore.value || url.value === '') return
    const held = tracks.value.length
    await load(url.value, true)
    // A page that added nothing would spin here: the list is shorter than its
    // own total, which an edit racing the read can leave behind.
    if (tracks.value.length <= held) return
    await loadAll()
  }

  /** Narrows the open playlist, reading it again from the start. */
  async function search(from: string, term: string, field: QueryField = Field.Any): Promise<void> {
    query.value = term
    queryField.value = field
    await load(from)
  }

  /** Reads the open playlist again, keeping a failure to read on screen rather than thrown. */
  async function reload(): Promise<void> {
    if (url.value === '') return
    try {
      await load(url.value)
    } catch (error) {
      failure.value = reasonOf(error)
    }
  }

  /**
   * Sends an edit of the open playlist, then reads it again.
   *
   * Every row after a removed or moved one has a new `order`, so the rows held
   * are read again rather than patched. A stale version raises the reloaded
   * notice; any other refusal is kept in `failure`.
   */
  async function edit<K extends GuardedOp>(op: K, data: Omit<OpRequests[K], 'url' | 'version'>) {
    try {
      await client.call(op, { ...data, url: url.value, version: version.value } as OpRequests[K])
    } catch (error) {
      await reload()
      // Set after the re-read, which clears both.
      if (error instanceof OpError && error.code === ErrorCode.StaleList) stale.value = true
      else failure.value = reasonOf(error)
      return
    }
    await reload()
  }

  async function remove(orders: number[]) {
    await edit(Op.PlaylistRemoveTracks, { orders })
  }

  /** Moves one track so it lands at `to`, both as `order` values. */
  async function move(from: number, to: number) {
    await edit(Op.PlaylistMoveTracks, { from_orders: [from], to_order: to })
  }

  /** Drops what is held, so the next playlist opened cannot show these rows. */
  function close() {
    url.value = ''
    tracks.value = []
    name.value = ''
    version.value = ''
    editable.value = false
    total.value = 0
    totalDurationMs.value = 0
    query.value = ''
    queryField.value = Field.Any
    stale.value = false
    failure.value = ''
  }

  /**
   * Appends tracks to any playlist, and answers how many went in.
   *
   * Unguarded: appending names no slot, so a playlist that changed since it was
   * read is still appended to correctly. The open one is read again when it is
   * the one that grew.
   */
  async function addTo(target: string, source: PlaylistSource): Promise<number> {
    const { added } = await client.call(Op.PlaylistAddTracks, { url: target, ...source })
    if (target === url.value) await reload()
    return added
  }

  /** Creates a playlist, empty or holding `source`, and answers its url. */
  async function create(
    title: string,
    { folder, source }: { folder?: string; source?: PlaylistSource } = {},
  ): Promise<string> {
    const created = await client.call(Op.PlaylistCreate, {
      name: title,
      ...(folder ? { folder } : {}),
      ...source,
    })
    await useLibraryStore().loadPlaylists()
    return created.url
  }

  /** Deletes a playlist, and closes it when it is the one open. */
  async function deletePlaylist(target: string) {
    await client.call(Op.PlaylistDelete, { url: target })
    if (target === url.value) close()
    await useLibraryStore().loadPlaylists()
  }

  return {
    url,
    tracks,
    name,
    version,
    editable,
    total,
    totalDurationMs,
    query,
    queryField,
    loading,
    stale,
    failure,
    hasMore,
    load,
    loadAll,
    search,
    remove,
    move,
    addTo,
    create,
    deletePlaylist,
    close,
  }
})
