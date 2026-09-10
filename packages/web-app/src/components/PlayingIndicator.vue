<script setup lang="ts">
import { computed } from 'vue'

import { PlayState } from '../api/types'
import { usePlayerStore } from '../stores/player'

/**
 * The mark on the row that is playing.
 *
 * Bars that move, because the thing it marks is moving: a static glyph says
 * "this one" where the point is "this one, now". They hold still while paused,
 * which is the same information without the lie.
 *
 * The reduced-motion rule in `style.css` stops the animation globally, leaving
 * the bars as a legible static mark rather than nothing.
 */
const player = usePlayerStore()

const BARS = [0, 1, 2]

/** Each bar is offset so they never rise together, which reads as a meter. */
const DELAYS_MS = [0, 220, 440]

const animating = computed(() => player.playState === PlayState.Playing)
</script>

<template>
  <span class="flex h-4 w-4 shrink-0 items-end justify-center gap-0.5" role="img" :aria-label="$t('queue.playing')">
    <span
      v-for="bar in BARS"
      :key="bar"
      class="w-0.5 rounded-full bg-current"
      :class="animating ? 'animate-[equalize_900ms_ease-in-out_infinite]' : 'h-1.5'"
      :style="animating ? { animationDelay: `${DELAYS_MS[bar]}ms` } : undefined"
    />
  </span>
</template>
