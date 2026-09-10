import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { OpError } from '../api/parse'
import { Op, WireEvent } from '../api/ops'
import type { OpRequests } from '../api/ops'
import { ErrorCode } from '../api/types'
import type { QueueItem } from '../api/types'

import { usePlayerStore } from './player'

const PAGE_SIZE = 200

/** The queue ops that take an `order` and a `version`. */
type MutationOp =
  | typeof Op.NowPlayingListPlay
  | typeof Op.NowPlayingListRemove
  | typeof Op.NowPlayingListMove

/**
 * The now-playing queue.
 *
 * Mutations take `order` (the absolute storage index), never `position`, and
 * carry the `version` the page was read at. MusicBee addresses the queue by
 * index alone, so an `order` only means what it meant while the list it came
 * from still held; without the version a mutation racing a change would hit the
 * wrong slot instead of being refused.
 */
export const useQueueStore = defineStore('queue', () => {
  const items = ref<QueueItem[]>([])
  const total = ref(0)
  const version = ref(0)
  const upNext = ref(false)
  const loading = ref(false)
  const stale = ref(false)

  /**
   * Reads the queue, or continues it.
   *
   * `append` fetches what is past the rows already held, so a queue of
   * thousands is read a screenful at a time rather than truncated at the first
   * page. A page that comes back at a different version describes a list that
   * moved while it was being read, so it is started over rather than stitched
   * onto rows whose `order` no longer means the same thing.
   */
  async function load(append = false): Promise<void> {
    loading.value = true
    const offset = append ? items.value.length : 0
    try {
      const page = await client.call(Op.NowPlayingList, {
        offset,
        limit: PAGE_SIZE,
        up_next: upNext.value,
      })
      if (append && page.version !== version.value) {
        loading.value = false
        await load()
        // Set after the re-read, which clears it: the reason the list is being
        // shown again is what the notice is for.
        stale.value = true
        return
      }
      items.value = append ? [...items.value, ...page.items] : page.items
      total.value = page.total
      version.value = page.version
      if (!append) stale.value = false
    } finally {
      loading.value = false
    }
  }

  /** Whether the server holds more of the queue than is on screen. */
  const hasMore = computed(() => items.value.length < total.value)

  /**
   * Reads the rest of the queue, a page at a time.
   *
   * What a search needs: filtering the rows that happen to be paged in answers
   * with a list missing the track being looked for, and no way to say why. The
   * pages are read in turn rather than at once so the rows already held stay
   * usable while the rest arrives.
   */
  async function loadAll(): Promise<void> {
    if (!hasMore.value || loading.value) return

    const held = items.value.length
    await load(true)
    // A page that added nothing would spin here: the list is shorter than its
    // own total, which a mutation racing the read can leave behind.
    if (items.value.length <= held) return
    await loadAll()
  }

  async function setView(next: boolean) {
    upNext.value = next
    await load()
  }

  /**
   * Re-reads and retries once when the queue moved under a mutation.
   *
   * The player is re-read too: which track is current is answered from the same
   * list, so a mutation that shifts indices leaves the playing track's own
   * position out of date until something asks again.
   */
  async function mutate<K extends MutationOp>(op: K, data: OpRequests[K]) {
    try {
      await client.call(op, { ...data, version: version.value })
    } catch (error) {
      if (!(error instanceof OpError) || error.code !== ErrorCode.StaleList) throw error
      stale.value = true
      await load()
      return
    }
    await Promise.all([load(), usePlayerStore().refreshNowPlaying()])
  }

  async function play(order: number) {
    await mutate(Op.NowPlayingListPlay, { order })
  }
  async function remove(order: number) {
    await mutate(Op.NowPlayingListRemove, { order })
  }
  async function move(from: number, to: number) {
    await mutate(Op.NowPlayingListMove, { from, to })
  }

  function bind() {
    client.on(WireEvent.NowPlayingListChanged, () => {
      void load()
    })
  }

  return {
    items,
    total,
    version,
    upNext,
    loading,
    stale,
    hasMore,
    load,
    loadAll,
    setView,
    play,
    remove,
    move,
    bind,
  }
})
