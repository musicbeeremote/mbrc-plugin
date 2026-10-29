<script setup lang="ts">
import type { AlbumEntry } from '../api/browse'
import { coverUrl } from '../api/display'
import type { QueueMode } from '../api/types'
import { wholeScope } from '../composables/queueTracks'

import CoverPlaceholder from './CoverPlaceholder.vue'
import QueueMenu from './QueueMenu.vue'

/** One album in a cover grid: its art, its name and artist, and its queue menu. */
defineProps<{
  entry: AlbumEntry
  /** The queue menu's accessible name. */
  label: string
  modes?: QueueMode[]
  playlist?: boolean
  /** Pushed to the caption's edge: a year, a file count. Nothing is drawn when absent. */
  detail?: string | number
}>()
const emit = defineEmits<{ open: []; select: [mode: QueueMode]; playlist: [] }>()
</script>

<template>
  <div>
    <button
      class="block w-full transition-transform active:scale-[0.98]"
      @click="emit('open')"
    >
      <!-- A blank tile reads as a cover still loading. The placeholder says
           the album has none, which is a different and permanent thing. -->
      <div
        class="relative flex aspect-square items-center justify-center overflow-hidden rounded-control bg-surface-2 text-outline"
      >
        <img
          v-if="coverUrl(entry.cover_hash)"
          :src="coverUrl(entry.cover_hash) ?? undefined"
          alt=""
          loading="lazy"
          class="h-full w-full object-cover"
        />
        <CoverPlaceholder v-else :name="entry.album || entry.artist" large />
      </div>
    </button>

    <!-- The menu button is taller than the two lines of caption beside it,
         so its padding is pulled back out: left to itself it would set the
         caption's height and break the grid's vertical rhythm. -->
    <div class="mt-2 flex items-start gap-1">
      <button class="min-w-0 flex-1 text-left" @click="emit('open')">
        <span class="block truncate text-sm/tight">
          {{ entry.album || $t('common.unknown.album') }}
        </span>
        <span class="mt-0.5 flex items-baseline gap-1.5 text-2xs/tight text-outline">
          <!-- The artist takes the whole line so the detail is pushed to the
               tile's edge, where it reads as a column down the grid rather
               than as a word trailing each name at its own length. -->
          <span class="min-w-0 flex-1 truncate">
            {{ entry.artist || $t('common.unknown.artist') }}
          </span>
          <span v-if="detail" class="shrink-0 tabular-nums opacity-70">
            {{ detail }}
          </span>
        </span>
      </button>
      <QueueMenu
        class="-mt-1.5 -mr-2"
        :label="label"
        :modes="modes"
        :playlist="playlist"
        :tracks="wholeScope"
        @select="(mode) => emit('select', mode)"
        @playlist="emit('playlist')"
      />
    </div>
  </div>
</template>
