<script setup lang="ts">
import type { Component } from 'vue'

/**
 * What a pane shows when it has nothing to show.
 *
 * A bare line of text in a list reads as something that failed to load. An icon
 * with it says the pane worked and the answer was nothing, which is a different
 * thing and the one that is usually true.
 */
defineProps<{
  icon: Component
  title: string
  /** The line under it, for saying why it is empty when that is not obvious. */
  hint?: string
}>()
</script>

<template>
  <div class="flex flex-1 flex-col items-center justify-center gap-3 px-6 text-center">
    <component :is="icon" class="size-10 text-outline opacity-40" />
    <p class="text-sm text-ink-soft">{{ title }}</p>
    <p v-if="hint" class="max-w-xs text-2xs text-outline">{{ hint }}</p>
    <slot />
  </div>
</template>
