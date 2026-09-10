<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import IconClose from '~icons/lucide/x'

import { useSwipe } from '../composables/useSwipe'

/**
 * A panel that gets real room: a sheet up from the bottom on a phone, a centred
 * dialog on a desktop.
 *
 * Built on the native `<dialog>` rather than a div, which is what makes Escape,
 * focus trapping, inertness of the page behind, and the top layer come for free.
 * Everything below the UA reset is layout.
 */
const props = defineProps<{ open: boolean; title: string }>()
const emit = defineEmits<{ close: [] }>()

const dialog = ref<HTMLDialogElement | null>(null)

/**
 * Dragging the header down closes it, which is how a sheet is dismissed on a
 * phone; the X stays for a pointer. It follows the finger while it happens, so
 * the gesture is answered before it completes.
 */
const swipe = useSwipe(
  () => undefined,
  () => emit('close'),
)
const drag = computed(() => Math.max(swipe.offset.value, 0))

watch(
  () => props.open,
  (open) => {
    const element = dialog.value
    if (!element) return
    // `showModal` throws if it is already open, and `close` fires the event that
    // brings us back here, so both are guarded on the element's own state.
    if (open && !element.open) element.showModal()
    else if (!open && element.open) element.close()
  },
)

/**
 * Closes on a tap outside the sheet.
 *
 * A modal `<dialog>` gives Escape and the backdrop for free but not this: the
 * backdrop is painted by the dialog itself, so a click on it lands on the
 * dialog element and means nothing to it. The target check keeps a click inside
 * the content out of this, and the box check keeps a keyboard-driven click - one
 * that reports no coordinates at all - from reading as a tap in the corner.
 */
function onBackdropClick(event: MouseEvent) {
  const element = dialog.value
  if (!element || event.target !== element) return

  const box = element.getBoundingClientRect()
  const outside =
    event.clientX < box.left ||
    event.clientX > box.right ||
    event.clientY < box.top ||
    event.clientY > box.bottom
  if (outside) emit('close')
}

// A dialog left open in the top layer outlives its component, so it has to be
// closed on the way out rather than merely unmounted.
onBeforeUnmount(() => {
  if (dialog.value?.open) dialog.value.close()
})
</script>

<template>
  <dialog
    ref="dialog"
    class="m-0 mt-auto max-h-[85dvh] w-full max-w-none rounded-t-panel border-0 bg-surface p-0 text-ink backdrop:bg-black/40 sm:m-auto sm:max-h-[80dvh] sm:w-[34rem] sm:rounded-panel"
    @close="emit('close')"
    @click="onBackdropClick"
  >
    <div
      class="flex max-h-[inherit] flex-col transition-transform"
      :style="{ transform: `translateY(${drag}px)` }"
    >
      <header
        class="flex shrink-0 touch-none items-center justify-between border-b border-surface-2 px-4 py-3"
        @pointerdown="swipe.start"
        @pointermove="swipe.move"
        @pointerup="swipe.end"
        @pointercancel="swipe.end"
      >
        <h2 class="truncate text-sm font-semibold">{{ title }}</h2>
        <button
          class="tap-target rounded-control p-1 text-outline transition-colors hover:text-ink"
          :aria-label="$t('common.action.close')"
          @click="emit('close')"
        >
          <IconClose class="size-4" />
        </button>
      </header>

      <!-- Hidden horizontally, not auto: a scaled-up line paints a little wider
           than its layout box, which is enough to raise a scrollbar for a few
           pixels of overhang that nobody needs to scroll to. -->
      <div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto p-4">
        <slot />
      </div>
    </div>
  </dialog>
</template>
