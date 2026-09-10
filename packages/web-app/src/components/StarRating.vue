<script setup lang="ts">
import { computed, ref } from 'vue'

import IconStar from '~icons/lucide/star'

/**
 * The track rating, in half stars.
 *
 * MusicBee stores half values and the server accepts them, so a control that
 * could only set whole ones was quietly rounding the user's library.
 *
 * The whole row is one target rather than ten: half a star is a few pixels
 * wide, and a control you have to aim at is one you set wrong. Pointing
 * anywhere along the row previews the value under the finger and releasing
 * commits it, so precision comes from seeing the answer before you let go.
 * The per-star buttons stay for the keyboard, and take no pointer events.
 *
 * Choosing the value already set clears the rating, which is the only way back
 * to unrated once something has been rated.
 */
const props = defineProps<{ rating: number | null; label: (stars: number) => string }>()
const emit = defineEmits<{ set: [rating: number | null] }>()

const STARS = [1, 2, 3, 4, 5]

const row = ref<HTMLElement | null>(null)
/** The value under the pointer, which is what the stars show while pointing. */
const preview = ref<number | null>(null)

const current = computed(() => props.rating ?? 0)
const shown = computed(() => preview.value ?? current.value)

/** How much of one star is filled, as a percentage of its width. */
function fill(star: number): number {
  return Math.min(Math.max(shown.value - (star - 1), 0), 1) * 100
}

/** The half-star value at a point along the row. */
function valueAt(clientX: number): number {
  const box = row.value?.getBoundingClientRect()
  if (!box || box.width === 0) return 0
  const share = (clientX - box.left) / box.width
  const halves = Math.ceil(Math.min(Math.max(share, 0), 1) * 10)
  return Math.max(halves, 1) / 2
}

function choose(value: number) {
  emit('set', current.value === value ? null : value)
}

function onMove(event: PointerEvent) {
  preview.value = valueAt(event.clientX)
}

function onDown(event: PointerEvent) {
  onMove(event)
  try {
    ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
  } catch {
    // The pointer is gone; the row still sees moves while it is over it.
  }
}

function onUp(event: PointerEvent) {
  const value = valueAt(event.clientX)
  preview.value = null
  choose(value)
}
</script>

<template>
  <div
    ref="row"
    class="flex touch-none items-center select-none"
    @pointerdown="onDown"
    @pointermove="onMove"
    @pointerup="onUp"
    @pointercancel="preview = null"
    @pointerleave="preview = null"
  >
    <div v-for="star in STARS" :key="star" class="relative h-11 w-8">
      <!-- The outline, and over it the same star in the same place, clipped to
           the filled share. Clipping rather than sizing keeps the two exactly
           aligned however wide the container is. -->
      <IconStar class="absolute inset-0 m-auto size-5 text-outline" />
      <IconStar
        class="absolute inset-0 m-auto size-5 fill-current text-accent transition-[clip-path] duration-100"
        :style="{ clipPath: `inset(0 ${100 - fill(star)}% 0 0)` }"
      />

      <button
        class="pointer-events-none absolute inset-y-0 left-0 w-1/2"
        :aria-label="label(star - 0.5)"
        @click="choose(star - 0.5)"
      />
      <button
        class="pointer-events-none absolute inset-y-0 right-0 w-1/2"
        :aria-label="label(star)"
        @click="choose(star)"
      />
    </div>
  </div>
</template>
