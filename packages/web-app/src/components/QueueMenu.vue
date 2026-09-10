<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

import IconMore from '~icons/lucide/ellipsis-vertical'

import { QueueMode } from '../api/types'

/**
 * The four ways to queue something, on any row that can be queued.
 *
 * The player takes four placements and the app has only ever used two of them,
 * so a row that plays on tap has no way to say "after this one". A menu is what
 * makes the other two reachable without a second tap target per row.
 *
 * It is rendered on the body rather than in the row. A virtualised row carries
 * a transform, which makes it a stacking context, and inside one no z-index can
 * lift the menu over the navigation bar - so it is positioned against the
 * button from outside instead.
 */
defineProps<{ label: string }>()
const emit = defineEmits<{ select: [mode: QueueMode] }>()

const open = ref(false)
const trigger = ref<HTMLElement | null>(null)
/** Where the menu was opened from, when that was a point rather than the button. */
const from = ref<{ top: number; left: number } | null>(null)
const menu = ref<HTMLElement | null>(null)
const at = ref({ top: 0, left: 0 })

/** Matches `w-44`, and the gap the menu keeps from the window's edges. */
const MENU_WIDTH = 176
const MENU_HEIGHT = 176
const MARGIN = 8

const MODES: { mode: QueueMode; label: string }[] = [
  { mode: QueueMode.Now, label: 'library.action.now' },
  { mode: QueueMode.Next, label: 'library.action.next' },
  { mode: QueueMode.Last, label: 'library.action.last' },
  { mode: QueueMode.AddAll, label: 'library.action.addAll' },
]

/** Under whatever opened it, or above when the window has no room below. */
function place() {
  const box = trigger.value?.getBoundingClientRect()
  const anchor = from.value ?? (box && { top: box.bottom, left: box.right })
  if (!anchor) return
  const below = anchor.top + MENU_HEIGHT + MARGIN < window.innerHeight
  at.value = {
    top: below ? anchor.top + 4 : anchor.top - MENU_HEIGHT - 4,
    left: Math.max(
      MARGIN,
      Math.min(anchor.left - MENU_WIDTH, window.innerWidth - MENU_WIDTH - MARGIN),
    ),
  }
}

function toggle() {
  from.value = null
  open.value = !open.value
  if (open.value) place()
}

/**
 * Opens the menu at a point, for a right-click on the row it belongs to.
 *
 * A desktop reader expects the row's own menu there, and the row has no way to
 * draw one; the button it already carries is the menu, so it is opened rather
 * than duplicated.
 */
function openAt(event: MouseEvent) {
  from.value = { top: event.clientY, left: event.clientX + MENU_WIDTH }
  open.value = true
  place()
}

defineExpose({ openAt })

function choose(mode: QueueMode) {
  open.value = false
  emit('select', mode)
}

// A menu that survives a tap elsewhere is a menu that has to be dismissed
// twice. Capture, so a click on a row underneath closes it before that row acts.
function onDocumentPointerDown(event: PointerEvent) {
  if (!open.value) return
  const target = event.target as Node
  if (trigger.value?.contains(target) || menu.value?.contains(target)) return
  open.value = false
}

function onEscape(event: KeyboardEvent) {
  if (event.key === 'Escape') open.value = false
}

// Anchored to a button that scrolls, so the menu closes rather than drifting
// away from the row it belongs to.
function onScroll() {
  open.value = false
}

onMounted(() => {
  document.addEventListener('pointerdown', onDocumentPointerDown, true)
  document.addEventListener('keydown', onEscape)
  window.addEventListener('scroll', onScroll, true)
  window.addEventListener('resize', onScroll)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown, true)
  document.removeEventListener('keydown', onEscape)
  window.removeEventListener('scroll', onScroll, true)
  window.removeEventListener('resize', onScroll)
})
</script>

<template>
  <!-- Padded to two ends at once. The right is the room a 44px target needs to
       fall on screen at all, and 8px is the least that does it; the left is what
       keeps the count beside it from reading as part of the button. Both are
       carried here so a row only has to place the menu. -->
  <div class="shrink-0 pr-2 pl-1">
    <button
      ref="trigger"
      class="tap-target rounded-control p-2 text-outline transition-colors hover:text-ink"
      :aria-label="label"
      :aria-expanded="open"
      aria-haspopup="menu"
      @click.stop="toggle"
    >
      <IconMore class="size-4" />
    </button>

    <Teleport to="body">
      <div
        v-if="open"
        ref="menu"
        role="menu"
        class="fixed z-50 w-44 overflow-hidden rounded-control border border-surface-2 bg-surface/85 py-1 backdrop-blur-md shadow-lg"
        :style="{ top: `${at.top}px`, left: `${at.left}px` }"
      >
        <button
          v-for="entry in MODES"
          :key="entry.mode"
          role="menuitem"
          class="block w-full px-3 py-2 text-left text-sm transition-colors hover:bg-surface-2/60"
          @click.stop="choose(entry.mode)"
        >
          {{ $t(entry.label) }}
        </button>
      </div>
    </Teleport>
  </div>
</template>
