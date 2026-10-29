<script setup lang="ts">
/**
 * The app's settings, behind one button beside the theme toggle.
 *
 * Drawn on the body and placed against the button, as `QueueMenu` is, so the
 * bar or rail it sits in never clips it.
 */
import { onBeforeUnmount, onMounted, ref } from 'vue'

import IconSettings from '~icons/lucide/settings'

import { usePreferences } from '../composables/usePreferences'

const { variant } = defineProps<{ variant: 'rail' | 'bar' }>()

const preferences = usePreferences()

const open = ref(false)
const trigger = ref<HTMLElement | null>(null)
const panel = ref<HTMLElement | null>(null)
const at = ref<{ left: number; bottom: number }>({ left: 0, bottom: 0 })

/** Matches `w-64`, and the gap the panel keeps from the window's edges. */
const PANEL_WIDTH = 256
const MARGIN = 8

/** Above the button: both places it sits are at the foot of the window. */
function place() {
  const box = trigger.value?.getBoundingClientRect()
  if (!box) return
  at.value = {
    left: Math.max(MARGIN, Math.min(box.left, window.innerWidth - PANEL_WIDTH - MARGIN)),
    bottom: window.innerHeight - box.top + 4,
  }
}

function toggle() {
  open.value = !open.value
  if (open.value) place()
}

function onDocumentPointerDown(event: PointerEvent) {
  if (!open.value) return
  const target = event.target as Node
  if (trigger.value?.contains(target) || panel.value?.contains(target)) return
  open.value = false
}

function onKey(event: KeyboardEvent) {
  if (event.key === 'Escape') open.value = false
}

function onResize() {
  open.value = false
}

onMounted(() => {
  document.addEventListener('pointerdown', onDocumentPointerDown, true)
  document.addEventListener('keydown', onKey)
  window.addEventListener('resize', onResize)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown, true)
  document.removeEventListener('keydown', onKey)
  window.removeEventListener('resize', onResize)
})
</script>

<template>
  <button
    ref="trigger"
    class="text-outline transition-colors hover:text-ink"
    :class="
      variant === 'rail'
        ? 'flex items-center gap-3 rounded-control px-3 py-2 text-left text-sm'
        : 'flex w-16 shrink-0 flex-col items-center gap-1 py-2 text-2xs'
    "
    :aria-label="$t('common.settings.label')"
    :aria-expanded="open"
    aria-haspopup="dialog"
    @click.stop="toggle"
  >
    <IconSettings class="size-5 shrink-0" />
    {{ $t('common.settings.label') }}
  </button>

  <Teleport to="body">
    <div
      v-if="open"
      ref="panel"
      role="dialog"
      :aria-label="$t('common.settings.label')"
      class="fixed z-50 w-64 overflow-hidden rounded-control border border-surface-2 bg-surface/85 p-1 backdrop-blur-md shadow-lg"
      :style="{ left: `${at.left}px`, bottom: `${at.bottom}px` }"
    >
      <button
        role="switch"
        :aria-checked="preferences.showMedia"
        class="flex w-full items-center gap-3 rounded-control px-3 py-2 text-left text-sm transition-colors hover:bg-surface-2/60"
        @click="preferences.setShowMedia(!preferences.showMedia)"
      >
        <span class="min-w-0 flex-1">{{ $t('common.settings.showMedia') }}</span>
        <span
          class="relative h-5 w-9 shrink-0 rounded-full transition-colors"
          :class="preferences.showMedia ? 'bg-accent' : 'bg-surface-2'"
        >
          <span
            class="absolute top-0.5 left-0.5 size-4 rounded-full bg-surface shadow transition-transform"
            :class="{ 'translate-x-4': preferences.showMedia }"
          />
        </span>
      </button>
    </div>
  </Teleport>
</template>
