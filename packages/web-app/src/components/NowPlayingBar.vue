<script setup lang="ts">
import { computed } from 'vue'

import IconMusic from '~icons/lucide/music'
import IconPause from '~icons/lucide/pause'
import IconPlay from '~icons/lucide/play'
import IconSkipBack from '~icons/lucide/skip-back'
import IconSkipForward from '~icons/lucide/skip-forward'

import { coverUrl } from '../api/display'
import ArtistLinks from './ArtistLinks.vue'
import { Capability } from '../api/permissions'
import { PlayState } from '../api/types'
import { useSwipe } from '../composables/useSwipe'
import { usePlayerStore } from '../stores/player'
import { usePermissionsStore } from '../stores/permissions'

/**
 * The compact transport strip shown on a phone while another tab is open.
 *
 * Without it, pausing from the library means leaving the library. Tapping the
 * strip itself opens the full Playing pane.
 */
const emit = defineEmits<{ open: [] }>()

/**
 * Dragging the bar up opens the player, which is how every music app on a phone
 * does it. The bar follows the finger while it is happening, so the gesture is
 * visible before it completes rather than only after.
 */
const swipe = useSwipe(() => emit('open'))
const lift = computed(() => Math.max(Math.min(swipe.offset.value, 0), -24))

const player = usePlayerStore()
const permissions = usePermissionsStore()
const cover = computed(() => coverUrl(player.track?.cover_hash))
const progress = computed(() =>
  player.durationMs > 0 ? (player.positionMs / player.durationMs) * 100 : 0,
)
</script>

<template>
  <div
    class="touch-pan-y border-t border-surface-2 transition-transform"
    :style="{ transform: `translateY(${lift}px)` }"
    @pointerdown="swipe.start"
    @pointermove="swipe.move"
    @pointerup="swipe.end"
    @pointercancel="swipe.end"
  >
    <div class="h-0.5 bg-surface-2">
      <div class="h-full bg-accent transition-[width] duration-200" :style="{ width: `${progress}%` }" />
    </div>
    <div class="flex items-center gap-3 p-2">
      <!-- The artist line is links of its own, which cannot sit inside the
           button that opens the player, so the cover and title open it. -->
      <div class="flex min-w-0 flex-1 items-center gap-3">
        <button
          class="size-10 shrink-0 overflow-hidden rounded-control bg-surface-2 text-outline"
          :aria-label="$t('player.open')"
          @click="$emit('open')"
        >
          <img v-if="cover" :src="cover" alt="" class="h-full w-full object-cover" />
          <IconMusic v-else class="h-full w-full p-2.5" />
        </button>
        <div class="min-w-0">
          <button class="block w-full truncate text-left text-sm font-medium" @click="$emit('open')">
            {{ player.track?.title || $t('player.nothingPlaying') }}
          </button>
          <p class="truncate text-xs text-outline"><ArtistLinks :track="player.track" /></p>
        </div>
      </div>
      <button
        v-if="permissions.can(Capability.Playback)"
        class="p-2 transition-transform active:scale-90"
        :aria-label="$t('player.action.previous')"
        @click="player.previous()"
      >
        <IconSkipBack class="size-5 fill-current" />
      </button>
      <button
        v-if="permissions.can(Capability.Playback)"
        class="rounded-full bg-accent p-2 text-white transition-transform active:scale-90"
        :aria-label="$t('player.action.playPause')"
        @click="player.playPause()"
      >
        <IconPause v-if="player.playState === PlayState.Playing" class="size-5 fill-current" />
        <IconPlay v-else class="size-5 fill-current" />
      </button>
      <button
        v-if="permissions.can(Capability.Playback)"
        class="p-2 transition-transform active:scale-90"
        :aria-label="$t('player.action.next')"
        @click="player.next()"
      >
        <IconSkipForward class="size-5 fill-current" />
      </button>
    </div>
  </div>
</template>
