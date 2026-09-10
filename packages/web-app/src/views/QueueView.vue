<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'

import IconGrip from '~icons/lucide/grip-vertical'
import IconMusic from '~icons/lucide/music'
import IconQueue from '~icons/lucide/list-music'
import IconSearch from '~icons/lucide/search'
import IconX from '~icons/lucide/x'

import { ShuffleMode, coverUrl, formatDuration, trackLabel } from '../api/types'
import EmptyState from '../components/EmptyState.vue'
import PlayingIndicator from '../components/PlayingIndicator.vue'
import { heardRows } from '../composables/queueHeard'
import { useDragSort } from '../composables/useDragSort'
import { useLazyRows } from '../composables/useLazyRows'
import { usePlayerStore } from '../stores/player'
import { useQueueStore } from '../stores/queue'

const queue = useQueueStore()
const player = usePlayerStore()

onMounted(() => {
  void queue.load()
})

/**
 * Dragging reorders the queue itself, so it is offered only on the full list.
 *
 * Up Next is play order, which for a shuffled queue is not storage order: a row
 * moved two places down there names no storage slot to move it to.
 */
/**
 * The filter over the queue.
 *
 * A search reads the rest of the queue first: filtering the rows that happen to
 * be paged in answers with the looked-for track missing and no way to say why.
 */
const term = ref('')

watch(term, (value) => {
  if (value !== '') void queue.loadAll()
})

const canReorder = computed(() => !queue.upNext && term.value === '')

const rows = computed(() => {
  const needle = term.value.trim().toLowerCase()
  if (needle === '') return queue.items
  return queue.items.filter((item) =>
    [item.title, item.artist, item.album].some((field) =>
      field.toLowerCase().includes(needle),
    ),
  )
})

/**
 * Which row is playing, by path rather than by index.
 *
 * MusicBee's current index follows a moved file rather than the playing track,
 * so for a moment after a reorder it names the wrong row. The path is what is
 * actually coming out of the speakers.
 */
function isPlaying(src: string): boolean {
  return src === player.track?.src
}

/**
 * Rows already heard, dimmed. Worked out here rather than refetched: the ranks
 * the server sent go stale on every track change, and rebuilding them costs a
 * walk of the whole queue.
 */
const heard = computed(() =>
  heardRows(rows.value, player.track?.src, player.shuffle !== ShuffleMode.Off),
)

/** Uniform, because both the virtual list and the drag maths need it to be. */
const ROW_HEIGHT = 56
const rowHeight = computed(() => ROW_HEIGHT)

const { list, containerProps, wrapperProps } = useLazyRows(
  rows,
  {
    rowHeight,
    hasMore: computed(() => queue.hasMore),
    loading: computed(() => queue.loading),
    loadMore: () => queue.load(true),
  },
)

const drag = useDragSort(
  () => rows.value.length,
  (from, to) => {
    const moved = rows.value[from]
    const target = rows.value[to]
    if (moved && target) void queue.move(moved.order, target.order)
  },
)
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center gap-2 border-b border-surface-2 p-3">
      <button
        class="rounded-full px-3 py-1 text-sm"
        :class="
          queue.upNext
            ? 'bg-surface-2'
            : 'bg-accent text-white'
"
        @click="queue.setView(false)"
      >
        {{ $t('queue.view.full') }}
      </button>
      <button
        class="rounded-full px-3 py-1 text-sm"
        :class="
          queue.upNext
            ? 'bg-accent text-white'
            : 'bg-surface-2'
"
        @click="queue.setView(true)"
      >
        {{ $t('queue.view.upNext') }}
      </button>
      <span class="ml-auto text-xs text-outline">
        {{ term === '' ? $t('queue.count', queue.total) : $t('queue.found', { shown: rows.length, total: queue.total }) }}
      </span>
    </div>

    <!-- Searching only narrows what is shown. Nothing here changes what is
         playing: the queue is where you were, and typing must not lose it. -->
    <div class="relative border-b border-surface-2 px-3 py-2">
      <IconSearch class="pointer-events-none absolute top-1/2 left-5 size-4 -translate-y-1/2 text-outline" />
      <input
        id="queue-search"
        v-model="term"
        name="queue-search"
        type="search"
        class="w-full rounded-control border border-surface-2 bg-transparent py-1.5 pr-8 pl-8 text-sm"
        :placeholder="$t('queue.search.placeholder')"
      />
      <button
        v-if="term !== ''"
        class="absolute top-1/2 right-4 -translate-y-1/2 rounded-control p-1 text-outline transition-colors hover:text-ink"
        :aria-label="$t('queue.search.clear')"
        @click="term = ''"
      >
        <IconX class="size-4" />
      </button>
    </div>

    <p v-if="queue.stale" class="bg-accent-soft p-2 text-center text-xs text-accent">
      {{ $t('queue.reloaded') }}
    </p>

    <div v-show="rows.length > 0" v-bind="containerProps" class="flex-1">
      <div v-bind="wrapperProps">
        <div
        v-for="{ index, data: item } in list"
        :key="item.order"
        data-drag-row
        class="flex cursor-pointer items-center gap-3 border-b border-surface-2 px-3 transition-colors hover:bg-surface-2/40 active:bg-surface-2/70"
        :class="[
          { 'opacity-40': !queue.upNext && heard(index) },
          index === drag.from.value ? 'relative z-10 bg-surface shadow-lg' : 'transition-transform',
        ]"
        :style="{
          height: `${ROW_HEIGHT}px`,
          transform: `translateY(${index === drag.from.value ? drag.offset.value : drag.shift(index)}px)`,
        }"
      >
        <button
          v-if="canReorder"
          class="tap-target -ml-1 cursor-grab touch-none p-1 text-outline transition-colors hover:text-ink"
          :aria-label="$t('queue.action.reorder', { title: item.title })"
          @pointerdown="drag.start(index, $event)"
          @pointermove="drag.move"
          @pointerup="drag.drop"
          @pointercancel="drag.drop"
        >
          <IconGrip class="size-4" />
        </button>
        <!-- The library is cached, so the queue knows each row's cover without
             asking for anything; the playing mark rides on it rather than
             taking a column that is blank on every row but one. -->
        <div class="relative size-10 shrink-0 overflow-hidden rounded-control bg-surface-2">
          <img
            v-if="coverUrl(item.cover_hash)"
            :src="coverUrl(item.cover_hash) ?? undefined"
            alt=""
            loading="lazy"
            class="h-full w-full object-cover"
          />
          <IconMusic v-else class="absolute inset-0 m-auto size-1/2 text-outline opacity-40" />
          <div
            v-if="isPlaying(item.src)"
            class="absolute inset-0 grid place-items-center bg-surface/70"
          >
            <PlayingIndicator class="text-accent" />
          </div>
        </div>
        <button class="min-w-0 flex-1 text-left" @click="queue.play(item.order)">
          <p
            class="truncate text-sm font-medium"
            :class="{ 'text-accent': isPlaying(item.src) }"
          >
            {{ trackLabel(item.title, item.src) }}
          </p>
          <p class="truncate text-xs text-outline">
            {{ item.artist || $t('common.unknown.artist') }}
          </p>
        </button>
        <span class="text-xs tabular-nums text-outline">
          {{ formatDuration(item.duration_ms) }}
        </span>
        <button
          class="tap-target p-2 text-outline transition-colors hover:text-rose-500"
          :aria-label="$t('queue.action.remove', { title: item.title })"
          @click="queue.remove(item.order)"
        >
          <IconX class="size-4" />
        </button>
        </div>
      </div>
    </div>

    <!-- A search that matches nothing is not an empty queue, and saying so
         would send someone looking for tracks that are there. -->
    <EmptyState
      v-if="!queue.loading && rows.length === 0"
      :icon="IconQueue"
      :title="term === '' ? $t('queue.empty') : $t('queue.search.none', { term })"
      :hint="term === '' ? $t('queue.emptyHint') : undefined"
    />
  </div>
</template>
