import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { Op } from '../api/ops'
import { QueueMode } from '../api/types'
import type { PodcastEpisode, PodcastSubscription } from '../api/types'

const PAGE_SIZE = 100

/**
 * Podcast subscriptions and the episodes of whichever one is open.
 *
 * Nothing here is announced: MusicBee says nothing when a feed refreshes or an
 * episode finishes downloading, so the only way to be current is to ask again.
 * Opening a subscription re-reads its episodes for that reason, rather than
 * trusting what was read the last time it was open.
 *
 * An episode is addressed by its place in the subscription, which is the only
 * key MusicBee takes. A list read before a feed refresh can name a neighbour,
 * and that is the whole cost: there is nothing to destroy at the wrong index.
 */
export const usePodcastStore = defineStore('podcast', () => {
  const subscriptions = ref<PodcastSubscription[]>([])
  const total = ref(0)
  const loading = ref(false)

  const episodes = ref<PodcastEpisode[]>([])
  const episodeTotal = ref(0)
  const openId = ref('')
  const loadingEpisodes = ref(false)

  const hasMore = computed(() => subscriptions.value.length < total.value)
  const hasMoreEpisodes = computed(() => episodes.value.length < episodeTotal.value)
  const open = computed(() => subscriptions.value.find((s) => s.id === openId.value))

  async function load(append = false): Promise<void> {
    loading.value = true
    try {
      const offset = append ? subscriptions.value.length : 0
      const page = await client.call(Op.PodcastSubscriptions, { offset, limit: PAGE_SIZE })
      total.value = page.total
      subscriptions.value = append ? [...subscriptions.value, ...page.items] : page.items
    } finally {
      loading.value = false
    }
  }

  /**
   * Reads a subscription's episodes, or continues the one already open.
   *
   * Opening a different subscription drops what is held first, so a slow feed
   * never shows the previous one's episodes under the new one's name.
   */
  async function openSubscription(id: string, append = false): Promise<void> {
    if (!append && openId.value !== id) {
      episodes.value = []
      episodeTotal.value = 0
    }
    openId.value = id
    loadingEpisodes.value = true
    try {
      const offset = append ? episodes.value.length : 0
      const page = await client.call(Op.PodcastEpisodes, { id, offset, limit: PAGE_SIZE })
      episodeTotal.value = page.total
      episodes.value = append ? [...episodes.value, ...page.items] : page.items
    } finally {
      loadingEpisodes.value = false
    }
  }

  /** Drops the open subscription, so the next one opened shows none of it. */
  function close(): void {
    openId.value = ''
    episodes.value = []
    episodeTotal.value = 0
  }

  /**
   * Plays an episode, or queues it.
   *
   * Downloaded or not makes no difference: MusicBee streams a feed URL the same
   * way it does from its own window.
   */
  async function play(index: number, mode: QueueMode = QueueMode.Now): Promise<void> {
    if (openId.value === '') return
    await client.call(Op.PodcastEpisodePlay, { id: openId.value, index, mode })
  }

  return {
    subscriptions,
    total,
    loading,
    hasMore,
    episodes,
    episodeTotal,
    hasMoreEpisodes,
    openId,
    open,
    loadingEpisodes,
    load,
    openSubscription,
    close,
    play,
  }
})
