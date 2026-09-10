import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { Op } from '../api/ops'
import { QueueMode } from '../api/types'
import type { RadioEntry } from '../api/types'

/** One page per batch. Station lists are short, so this is usually all of it. */
const PAGE_SIZE = 100

/**
 * The radio stations MusicBee knows about.
 *
 * Its own store rather than part of the library: a station is a URL the player
 * streams, with no artist, album or track behind it to browse, and none of the
 * library's levels apply to it.
 */
export const useRadioStore = defineStore('radio', () => {
  const stations = ref<RadioEntry[]>([])
  const total = ref(0)
  const loading = ref(false)

  const hasMore = computed(() => stations.value.length < total.value)

  async function load(append = false): Promise<void> {
    loading.value = true
    try {
      const offset = append ? stations.value.length : 0
      const page = await client.call(Op.LibraryRadio, { offset, limit: PAGE_SIZE })
      total.value = page.total
      stations.value = append ? [...stations.value, ...page.items] : page.items
    } finally {
      loading.value = false
    }
  }

  /**
   * Plays a station.
   *
   * A station is queued like a path because that is what it is to the player: a
   * URL it can be told to play now.
   */
  async function play(url: string): Promise<void> {
    await client.call(Op.NowPlayingQueue, { paths: [url], mode: QueueMode.Now })
  }

  return { stations, total, loading, hasMore, load, play }
})
