<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

import IconAsc from '~icons/lucide/arrow-up'
import IconDesc from '~icons/lucide/arrow-down'
import IconSort from '~icons/lucide/arrow-up-down'

/**
 * The order a list is read in.
 *
 * Picking the order already showing reverses it rather than doing nothing,
 * which is the one gesture that makes a direction control unnecessary: the
 * arrow beside the checked field says which way it currently runs.
 */
defineProps<{
  label: string
  fields: string[]
  active: string
  descending: boolean
  name: (field: string) => string
}>()
const emit = defineEmits<{ select: [field: string] }>()

const open = ref(false)
const root = ref<HTMLElement | null>(null)

function choose(field: string) {
  open.value = false
  emit('select', field)
}

function onDocumentPointerDown(event: PointerEvent) {
  if (!open.value) return
  if (!root.value?.contains(event.target as Node)) open.value = false
}

function onEscape(event: KeyboardEvent) {
  if (event.key === 'Escape') open.value = false
}

onMounted(() => {
  document.addEventListener('pointerdown', onDocumentPointerDown, true)
  document.addEventListener('keydown', onEscape)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown, true)
  document.removeEventListener('keydown', onEscape)
})
</script>

<template>
  <div ref="root" class="relative shrink-0">
    <button
      class="tap-target rounded-control p-2 transition-colors"
      :class="descending ? 'text-accent' : 'text-outline hover:text-ink'"
      :aria-label="label"
      :title="label"
      :aria-expanded="open"
      aria-haspopup="menu"
      @click.stop="open = !open"
    >
      <IconSort class="size-5" />
    </button>

    <div
      v-if="open"
      role="menu"
      class="absolute right-0 z-10 mt-1 w-48 overflow-hidden rounded-control border border-surface-2 bg-surface/85 py-1 backdrop-blur-md shadow-lg"
    >
      <button
        v-for="field in fields"
        :key="field"
        role="menuitemradio"
        :aria-checked="field === active"
        class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm transition-colors hover:bg-surface-2/60"
        :class="field === active ? 'text-accent' : 'text-ink-soft'"
        @click.stop="choose(field)"
      >
        <span class="min-w-0 flex-1 truncate">{{ name(field) }}</span>
        <component
          :is="descending ? IconDesc : IconAsc"
          v-if="field === active"
          class="size-4 shrink-0"
        />
      </button>
    </div>
  </div>
</template>
