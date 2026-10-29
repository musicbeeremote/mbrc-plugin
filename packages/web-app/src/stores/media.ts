import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'

import { client } from '../api/client'
import { isMediaSource } from '../api/browse'
import type { AlbumEntry, MediaSource, MediaSourceEntry } from '../api/browse'
import { reasonOf } from '../api/parse'
import { Op, WireEvent } from '../api/ops'
import type { MediaScope } from '../api/ops'
import { QueueMode } from '../api/types'
import type { Track } from '../api/types'

import { usePreferences } from '../composables/usePreferences'

import type { QueueOptions } from './library'

const PAGE_SIZE = 100

/** A category with something in it, narrowed to one this build knows. */
type SourceEntry = Omit<MediaSourceEntry, 'source'> & { source: MediaSource }

function sameScope(a: MediaScope | null, b: MediaScope | null): boolean {
  return a?.album === b?.album && a?.artist === b?.artist
}

/**
 * Audiobooks, videos and the inbox: what MusicBee files apart from the music.
 *
 * A book is an album and its author the album's artist; a chapter is a track.
 * The category on screen, and the book open inside it, are the router's answer:
 * `showSource` and `openBook` are called with what the URL said.
 *
 * Answers that land after the reader has moved on are dropped rather than
 * shown, so a slow category never fills the grid of the one opened after it.
 *
 * Nothing is asked of the server while the reader has media turned off.
 */
export const useMediaStore = defineStore('media', () => {
  const preferences = usePreferences()

  const sources = ref<SourceEntry[]>([])

  const source = ref<MediaSource | undefined>(undefined)
  const albums = ref<AlbumEntry[]>([])
  const albumTotal = ref(0)
  const loadingAlbums = ref(false)
  /** The source whose first page of albums was read, so returning to it needs no read. */
  const loadedSource = ref<MediaSource | undefined>(undefined)

  /** The book open, `{}` for every file in the category, `null` for none. */
  const open = ref<MediaScope | null>(null)
  const tracks = ref<Track[]>([])
  const trackTotal = ref(0)
  const loadingTracks = ref(false)

  const failure = ref<string | null>(null)

  const hasMoreAlbums = computed(() => albums.value.length < albumTotal.value)
  const hasMoreTracks = computed(() => tracks.value.length < trackTotal.value)

  /**
   * Reads which categories hold anything.
   *
   * A plugin without media ops refuses the read, which leaves the list empty and
   * the pane out of the navigation rather than showing an error nobody can act on.
   */
  async function loadSources(): Promise<void> {
    if (!preferences.showMedia) {
      sources.value = []
      return
    }
    try {
      const answer = await client.call(Op.MediaSources, {})
      sources.value = answer.items.flatMap(({ source: name, count }) =>
        isMediaSource(name) ? [{ source: name, count }] : [],
      )
    } catch {
      sources.value = []
    }
  }

  let sourcesQueued = false

  /** Re-reads the categories once for a burst of `media_changed`, one per source. */
  function reloadSources(): void {
    if (sourcesQueued) return
    sourcesQueued = true
    queueMicrotask(() => {
      sourcesQueued = false
      void loadSources()
    })
  }

  async function loadAlbums(append = false): Promise<void> {
    const asked = source.value
    if (asked === undefined) return
    loadingAlbums.value = true
    failure.value = null
    try {
      const offset = append ? albums.value.length : 0
      const page = await client.call(Op.MediaAlbums, { source: asked, offset, limit: PAGE_SIZE })
      if (source.value !== asked) return
      albumTotal.value = page.total
      albums.value = append ? [...albums.value, ...page.items] : page.items
      loadedSource.value = asked
    } catch (error) {
      if (source.value === asked) failure.value = reasonOf(error)
    } finally {
      loadingAlbums.value = false
    }
  }

  async function loadTracks(append = false): Promise<void> {
    const asked = source.value
    const scope = open.value
    if (asked === undefined || scope === null) return
    loadingTracks.value = true
    failure.value = null
    try {
      const offset = append ? tracks.value.length : 0
      const page = await client.call(Op.MediaTracks, {
        source: asked,
        ...scope,
        offset,
        limit: PAGE_SIZE,
      })
      if (source.value !== asked || !sameScope(open.value, scope)) return
      trackTotal.value = page.total
      tracks.value = append ? [...tracks.value, ...page.items] : page.items
    } catch (error) {
      if (source.value === asked && sameScope(open.value, scope)) failure.value = reasonOf(error)
    } finally {
      loadingTracks.value = false
    }
  }

  function resetTracks(): void {
    tracks.value = []
    trackTotal.value = 0
  }

  /** Drops the open book, so the next one opened shows none of it. */
  function close(): void {
    open.value = null
    resetTracks()
  }

  /**
   * Shows a category's books, read afresh unless they were already read.
   *
   * Returning to the grid from a book keeps what was read: `media_changed` is
   * what says the category moved. A read that failed is tried again.
   */
  async function showSource(next: MediaSource): Promise<void> {
    if (next === source.value && loadedSource.value === next) return
    if (next === source.value) {
      await loadAlbums()
      return
    }
    source.value = next
    loadedSource.value = undefined
    albums.value = []
    albumTotal.value = 0
    close()
    await loadAlbums()
  }

  /** Reads a book's chapters in reading order, or every file for `{}`. */
  async function openBook(scope: MediaScope): Promise<void> {
    if (!sameScope(open.value, scope)) resetTracks()
    open.value = scope
    await loadTracks()
  }

  /** Queues what a scope of the current category selects, resolved by the server. */
  async function queue(
    scope: MediaScope,
    { mode = QueueMode.Last, play, shuffle = false }: QueueOptions = {},
  ): Promise<number> {
    if (source.value === undefined) return 0
    const result = await client.call(Op.MediaQueue, {
      source: source.value,
      ...scope,
      mode,
      ...(play ? { play } : {}),
      ...(shuffle ? { shuffle } : {}),
    })
    return result.count
  }

  /** Queues the open book, or every file when none is: what a chapter's add-all takes. */
  function queueOpen(options: QueueOptions = {}): Promise<number> {
    return queue(open.value ?? {}, options)
  }

  function bind() {
    client.on(WireEvent.ConnectionChanged, (data) => {
      if (data.connected) void loadSources()
    })
    client.on(WireEvent.MediaChanged, ({ source: changed }) => {
      if (!preferences.showMedia) return
      reloadSources()
      if (changed !== source.value) return
      void loadAlbums()
      if (open.value !== null) void loadTracks()
    })
    watch(
      () => preferences.showMedia,
      () => {
        void loadSources()
      },
    )
  }

  return {
    sources,
    source,
    albums,
    albumTotal,
    loadingAlbums,
    hasMoreAlbums,
    open,
    tracks,
    trackTotal,
    loadingTracks,
    hasMoreTracks,
    error: failure,
    loadSources,
    loadAlbums,
    loadTracks,
    showSource,
    openBook,
    close,
    queue,
    queueOpen,
    bind,
  }
})
