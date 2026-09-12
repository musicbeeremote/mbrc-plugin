<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import IconBack from '~icons/lucide/chevron-left'
import IconDownloaded from '~icons/lucide/circle-check'
import IconPodcast from '~icons/lucide/podcast'

import { coverUrl, formatDuration } from '../api/display'
import { QueueMode } from '../api/types'
import type { PodcastEpisode } from '../api/types'
import EmptyState from '../components/EmptyState.vue'
import QueueMenu from '../components/QueueMenu.vue'
import { openPodcastFromRoute, podcastsRoute } from '../router/locations'
import { usePodcastStore } from '../stores/podcast'

const route = useRoute()
const router = useRouter()
const podcast = usePodcastStore()

/** The placements an episode has. One episode is not a list to add all of. */
const EPISODE_MODES = [QueueMode.Now, QueueMode.Next, QueueMode.Last]

const openId = computed(() => openPodcastFromRoute(route.query))

onMounted(() => {
  void podcast.load()
})

/**
 * The URL says which subscription is open, so a reload or a shared link lands
 * where it left off. Re-read on every open rather than kept: MusicBee announces
 * nothing about podcasts, so what was read last time may be a refresh behind.
 */
watch(
  openId,
  (id) => {
    if (id === undefined) podcast.close()
    else void podcast.openSubscription(id)
  },
  { immediate: true },
)

function episodeLength(episode: PodcastEpisode): string {
  const ms = episode.duration_ms
  return ms === null || ms === undefined ? '' : formatDuration(ms)
}

/** The day it was published. The clock time is noise for a weekly show. */
function published(episode: PodcastEpisode): string {
  const raw = episode.date
  if (raw === null || raw === undefined || raw === '') return ''
  const at = new Date(raw)
  return Number.isNaN(at.getTime()) ? '' : at.toLocaleDateString()
}
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center gap-2 border-b border-surface-2 p-3">
      <button
        v-if="openId !== undefined"
        class="-ml-1 rounded-control p-1 text-accent transition-colors"
        :aria-label="$t('common.action.back')"
        @click="router.push(podcastsRoute())"
      >
        <IconBack class="size-5" />
      </button>

      <div class="min-w-0 flex-1">
        <span class="block truncate text-sm font-medium">
          {{ podcast.open?.title ?? $t('podcasts.title') }}
        </span>
        <span v-if="openId !== undefined" class="block truncate text-2xs text-outline">
          {{ $t('podcasts.episodeCount', podcast.episodeTotal) }}
        </span>
      </div>

      <span v-if="openId === undefined" class="text-2xs tabular-nums text-outline">
        {{ podcast.total }}
      </span>
    </div>

    <!-- The subscriptions, as the art the reader recognises them by. -->
    <div v-if="openId === undefined" class="flex-1 overflow-y-auto">
      <ul class="grid grid-cols-2 gap-3 p-3 sm:grid-cols-3 lg:grid-cols-4">
        <li v-for="subscription in podcast.subscriptions" :key="subscription.id">
          <button
            class="w-full text-left transition-opacity hover:opacity-80"
            @click="router.push(podcastsRoute(subscription.id))"
          >
            <div class="aspect-square w-full overflow-hidden rounded-control bg-surface-2">
              <img
                v-if="coverUrl(subscription.image_hash ?? undefined)"
                :src="coverUrl(subscription.image_hash ?? undefined) ?? ''"
                :alt="subscription.title"
                class="size-full object-cover"
                loading="lazy"
              />
              <div v-else class="grid size-full place-items-center text-outline">
                <IconPodcast class="size-8" />
              </div>
            </div>
            <p class="mt-2 truncate text-sm font-medium">{{ subscription.title }}</p>
            <p class="truncate text-2xs text-outline">
              {{
                subscription.downloaded_count > 0
                  ? $t('podcasts.downloadedCount', subscription.downloaded_count)
                  : subscription.genre
              }}
            </p>
          </button>
        </li>
      </ul>

      <EmptyState
        v-if="!podcast.loading && podcast.subscriptions.length === 0"
        :icon="IconPodcast"
        :title="$t('podcasts.empty.title')"
        :hint="$t('podcasts.empty.hint')"
      />

      <button
        v-if="podcast.hasMore"
        class="w-full p-4 text-sm text-accent"
        :disabled="podcast.loading"
        @click="podcast.load(true)"
      >
        {{ podcast.loading ? $t('common.state.loading') : $t('common.state.loadMore') }}
      </button>
    </div>

    <!-- One subscription's episodes, newest first as MusicBee orders them. -->
    <div v-else class="flex-1 overflow-y-auto">
      <ul>
        <li v-for="episode in podcast.episodes" :key="episode.index">
          <div
            class="flex items-center gap-3 border-b border-surface-2 px-3 py-3 transition-colors hover:bg-surface-2/40"
            :class="{ 'opacity-50': episode.has_been_played }"
          >
            <button class="min-w-0 flex-1 text-left" @click="podcast.play(episode.index)">
              <p class="truncate text-sm font-medium">{{ episode.title }}</p>
              <p class="flex items-center gap-1.5 truncate text-2xs text-outline">
                <!-- Downloaded is worth a mark rather than a word: it is the
                     difference between playing now and playing over the network. -->
                <IconDownloaded
                  v-if="episode.is_downloaded"
                  class="size-3 shrink-0 text-accent"
                  :aria-label="$t('podcasts.downloaded')"
                />
                <span class="truncate">{{
                  [published(episode), episodeLength(episode)].filter(Boolean).join(' · ')
                }}</span>
              </p>
            </button>
            <QueueMenu
              :label="$t('podcasts.action.queue', { title: episode.title })"
              :modes="EPISODE_MODES"
              @select="(mode) => podcast.play(episode.index, mode)"
            />
          </div>
        </li>
      </ul>

      <EmptyState
        v-if="!podcast.loadingEpisodes && podcast.episodes.length === 0"
        :icon="IconPodcast"
        :title="$t('podcasts.empty.episodes')"
        :hint="$t('podcasts.empty.episodesHint')"
      />

      <button
        v-if="podcast.hasMoreEpisodes"
        class="w-full p-4 text-sm text-accent"
        :disabled="podcast.loadingEpisodes"
        @click="podcast.openSubscription(openId, true)"
      >
        {{ podcast.loadingEpisodes ? $t('common.state.loading') : $t('common.state.loadMore') }}
      </button>
    </div>
  </div>
</template>
