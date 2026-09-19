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

function reasonOf(error: unknown): string {
  return error instanceof OpError ? error.message : String(error)
}

/** The queue ops that carry the `version` the page was read at. */
type MutationOp =
  | typeof Op.NowPlayingListPlay
  | typeof Op.NowPlayingListRemove
  | typeof Op.NowPlayingListMove
  | typeof Op.NowPlayingListClear

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
  const totalDurationMs = ref(0)
  const version = ref(0)
  const upNext = ref(false)
  const loading = ref(false)
  const stale = ref(false)
  /** Why the last change to the queue failed, until a later read or change succeeds. */
  const failure = ref('')

  /**
   * Reads the queue, or continues it.
   *
   * `append` fetches what is past the rows already held, so a queue of
   * thousands is read a screenful at a time rather than truncated at the first
   * page. A page that comes back at a different version describes a list that
   * moved while it was being read, so it is started over rather than stitched
   * onto rows whose `order` no longer means the same thing.
   *
   * The run time is asked for only when the queue is read from the start: it
   * describes the whole queue rather than a page, so a second page would pay
   * the server to sum again what the first page already reported.
   */
  async function readPage(append: boolean): Promise<void> {
    loading.value = true
    const offset = append ? items.value.length : 0
    try {
      const page = await client.call(Op.NowPlayingList, {
        offset,
        limit: PAGE_SIZE,
        up_next: upNext.value,
        ...(append ? {} : { totals: true }),
      })
      if (append && page.version !== version.value) {
        loading.value = false
        await readPage(false)
        // Set after the re-read, which clears it: the reason the list is being
        // shown again is what the notice is for.
        stale.value = true
        return
      }
      items.value = append ? [...items.value, ...page.items] : page.items
      total.value = page.total
      version.value = page.version
      if (!append) {
        totalDurationMs.value = page.total_duration_ms ?? 0
        stale.value = false
        failure.value = ''
      }
    } finally {
      loading.value = false
    }
  }

  let current: Promise<void> | undefined = undefined

  /** Runs `readPage`, keeping hold of it so a caller can wait it out. */
  function read(append: boolean): Promise<void> {
    const running = readPage(append)
    current = running
    return running.finally(() => {
      if (current === running) current = undefined
    })
  }

  let reading: Promise<void> | undefined = undefined
  let readAgain = false

  /** A read that fails is dropped when a newer one was asked for meanwhile. */
  async function readUntilSettled(): Promise<void> {
    readAgain = false
    try {
      await read(false)
    } catch (error) {
      if (!readAgain) throw error
    }
    if (readAgain) await readUntilSettled()
  }

  async function settle(): Promise<void> {
    try {
      await readUntilSettled()
    } finally {
      reading = undefined
    }
  }

  /**
   * Reads the queue from the start, collapsing a burst of asks into one read
   * plus one trailing read.
   *
   * MusicBee announces a change once per host call, so removing twenty tracks
   * arrives as twenty events inside a few milliseconds. A request made while a
   * read is running shares it and asks for one more once it lands, since the
   * running read may have started before the change it was asked about.
   */
  function reload(): Promise<void> {
    if (reading) {
      readAgain = true
      return reading
    }
    reading = settle()
    return reading
  }

  /** A re-read nobody waits on, whose failure is shown rather than thrown. */
  async function refresh(): Promise<void> {
    try {
      await reload()
    } catch (error) {
      failure.value = reasonOf(error)
    }
  }

  async function load(append = false): Promise<void> {
    await (append ? read(true) : reload())
  }

  /** Whether the server holds more of the queue than is on screen. */
  const hasMore = computed(() => items.value.length < total.value)

  /**
   * Reads the rest of the queue, a page at a time.
   *
   * What a search needs: filtering the rows that happen to be paged in answers
   * with a list missing the track being looked for, and no way to say why. The
   * pages are read in turn rather than at once so the rows already held stay
   * usable while the rest arrives. A read already running is waited out rather
   * than taken as the rest, which it may not be.
   */
  async function loadAll(): Promise<void> {
    if (current) {
      await current.catch(() => undefined)
      return loadAll()
    }
    if (!hasMore.value) return

    const held = items.value.length
    await load(true)
    // A page that added nothing would spin here: the list is shorter than its
    // own total, which a mutation racing the read can leave behind.
    if (items.value.length <= held) return
    await loadAll()
  }

  async function setView(next: boolean) {
    upNext.value = next
    await reload()
  }

  /**
   * Sends a mutation, and re-reads the queue when the server refuses it.
   *
   * A stale version raises the reloaded notice; any other refusal is kept in
   * `failure`, since a batch the host refused part way may have removed some
   * tracks. The player is re-read too: which track is current is answered from
   * the same list, so a mutation that shifts indices leaves the playing track's
   * own position out of date until something asks again.
   */
  async function mutate<K extends MutationOp>(op: K, data: OpRequests[K]) {
    try {
      await client.call(op, { ...data, version: version.value })
    } catch (error) {
      await reload().catch(() => undefined)
      // Set after the re-read, which clears both.
      if (error instanceof OpError && error.code === ErrorCode.StaleList) stale.value = true
      else failure.value = reasonOf(error)
      return
    }
    await Promise.all([refresh(), usePlayerStore().refreshNowPlaying()])
  }

  async function play(order: number) {
    await mutate(Op.NowPlayingListPlay, { order })
  }
  /** Removes the given slots in one request; the server works out the order. */
  async function remove(orders: number[]) {
    if (orders.length === 0) return
    await mutate(Op.NowPlayingListRemove, { orders })
  }
  async function move(from: number, to: number) {
    await mutate(Op.NowPlayingListMove, { from, to })
  }
  async function clear() {
    await mutate(Op.NowPlayingListClear, {})
  }

  function bind() {
    client.on(WireEvent.NowPlayingListChanged, () => {
      void refresh()
    })
  }

  return {
    items,
    total,
    totalDurationMs,
    version,
    upNext,
    loading,
    stale,
    failure,
    hasMore,
    load,
    loadAll,
    setView,
    play,
    remove,
    move,
    clear,
    bind,
  }
})
