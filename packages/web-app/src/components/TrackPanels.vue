<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import IconCheck from '~icons/lucide/check'
import IconDetails from '~icons/lucide/info'
import IconLyrics from '~icons/lucide/mic-vocal'
import IconOutput from '~icons/lucide/speaker'

import { activeLyricLine } from '../composables/lyrics'
import { usePlayerStore } from '../stores/player'

import EmptyState from './EmptyState.vue'
import PanelSheet from './PanelSheet.vue'

/**
 * The three things about the playing track worth more than a row of their own.
 *
 * Lyrics and the tag list are content and open a sheet, which is the only way
 * either gets the room it needs: a rail is narrow, and words in a letterbox are
 * the worst version of that feature. Output is a control, not content, so it
 * stays a popover anchored to its own button.
 *
 * Details and outputs fetch only while open: they are per-track, and would
 * otherwise be round trips on every track change for panels nobody has looked
 * at. Lyrics are the exception, fetched with the track, because whether a track
 * has any is on the button before the panel is ever opened.
 */
const { t } = useI18n()
const player = usePlayerStore()

const showLyrics = ref(false)
const showDetails = ref(false)
const showOutput = ref(false)

const outputRoot = ref<HTMLElement | null>(null)
const lyricsBox = ref<HTMLElement | null>(null)

/** Whether the track has any, which the button shows without being opened. */
const hasLyrics = computed(() => player.lyrics.lines.length > 0)

function openLyrics() {
  showLyrics.value = true
}

function openDetails() {
  showDetails.value = true
  void player.refreshDetails()
}

function toggleOutput() {
  showOutput.value = !showOutput.value
  if (showOutput.value) void player.refreshOutputs()
}

/** The synced line the playhead is in, so the words follow the music. */
const activeLine = computed(() => activeLyricLine(player.lyrics, player.positionMs))

// Synced lyrics that do not scroll are lyrics you have to chase by hand.
watch(activeLine, async (index) => {
  if (!showLyrics.value || index < 0) return
  await nextTick()
  lyricsBox.value?.children[index]?.scrollIntoView({ block: 'center', behavior: 'smooth' })
})

/** The tag rows worth showing: the ones this track actually has. */
const detailRows = computed(() => {
  const { details } = player
  if (!details) return []
  return (
    [
      ['format', details.format],
      ['bitrate', details.bitrate === null ? '' : `${details.bitrate} kbps`],
      ['sampleRate', details.sample_rate === null ? '' : `${details.sample_rate} Hz`],
      ['channels', details.channels === null ? '' : String(details.channels)],
      ['size', details.size],
      ['kind', details.kind],
      ['publisher', details.publisher],
      ['composer', details.composer],
      ['grouping', details.grouping],
      ['comment', details.comment],
      ['encoder', details.encoder],
      ['playCount', details.play_count === null ? '' : String(details.play_count)],
      ['skipCount', details.skip_count === null ? '' : String(details.skip_count)],
      ['lastPlayed', details.last_played],
      ['dateModified', details.date_modified],
    ] as const
  )
    .filter(([, value]) => value !== '' && value !== undefined)
    .map(([key, value]) => ({ label: t(`player.detail.${key}`), value }))
})

/** The track a sheet is about, for its title. */
const trackTitle = computed(() => player.track?.title ?? t('player.nothingPlaying'))

// A popover that survives a tap elsewhere has to be dismissed twice.
function onDocumentPointerDown(event: PointerEvent) {
  if (!showOutput.value) return
  if (!outputRoot.value?.contains(event.target as Node)) showOutput.value = false
}

onMounted(() => document.addEventListener('pointerdown', onDocumentPointerDown, true))
onBeforeUnmount(() => document.removeEventListener('pointerdown', onDocumentPointerDown, true))
</script>

<template>
  <div class="flex justify-center gap-1">
    <button
      class="flex items-center gap-1.5 rounded-control px-3 py-1.5 text-2xs transition-colors"
      :class="hasLyrics ? 'text-accent' : 'text-outline hover:text-ink'"
      @click="openLyrics"
    >
      <IconLyrics class="size-4" />
      {{ $t('player.panel.lyrics') }}
    </button>

    <button
      class="flex items-center gap-1.5 rounded-control px-3 py-1.5 text-2xs text-outline transition-colors hover:text-ink"
      @click="openDetails"
    >
      <IconDetails class="size-4" />
      {{ $t('player.panel.details') }}
    </button>

    <div ref="outputRoot" class="relative">
      <button
        class="flex items-center gap-1.5 rounded-control px-3 py-1.5 text-2xs transition-colors"
        :class="showOutput ? 'bg-accent-soft text-accent' : 'text-outline hover:text-ink'"
        :aria-expanded="showOutput"
        aria-haspopup="menu"
        @click="toggleOutput"
      >
        <IconOutput class="size-4" />
        {{ $t('player.panel.output') }}
      </button>

      <div
        v-if="showOutput"
        role="menu"
        class="absolute right-0 bottom-full z-10 mb-1 w-60 overflow-hidden rounded-control border border-surface-2 bg-surface/85 py-1 backdrop-blur-md shadow-lg"
      >
        <p v-if="player.outputs.devices.length === 0" class="px-3 py-2 text-2xs text-outline">
          {{ $t('player.empty.outputs') }}
        </p>
        <button
          v-for="device in player.outputs.devices"
          v-else
          :key="device"
          role="menuitem"
          class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm transition-colors hover:bg-surface-2/60"
          :class="device === player.outputs.active ? 'text-accent' : 'text-ink-soft'"
          @click="player.setOutput(device)"
        >
          <IconCheck v-if="device === player.outputs.active" class="size-4 shrink-0" />
          <span v-else class="size-4 shrink-0" />
          <span class="truncate">{{ device }}</span>
        </button>
      </div>
    </div>
  </div>

  <PanelSheet :open="showLyrics" :title="trackTitle" @close="showLyrics = false">
    <EmptyState
      v-if="player.lyrics.lines.length === 0"
      :icon="IconLyrics"
      :title="$t('player.empty.lyrics')"
    />
    <div v-else ref="lyricsBox" class="space-y-2 py-2">
      <!-- The line being sung is larger as well as brighter, so it is findable
           without reading. It scales rather than changing font size: a size
           change reflows every line below it, and the list is scrolling itself
           at the same time. -->
      <p
        v-for="(line, index) in player.lyrics.lines"
        :key="index"
        class="origin-center text-center text-base transition-all duration-200"
        :class="
          index === activeLine
            ? 'scale-110 font-semibold text-accent'
            : 'text-ink-soft opacity-60'
        "
      >
        {{ line.text || ' ' }}
      </p>
    </div>
  </PanelSheet>

  <PanelSheet :open="showDetails" :title="trackTitle" @close="showDetails = false">
    <EmptyState
      v-if="detailRows.length === 0"
      :icon="IconDetails"
      :title="$t('player.empty.details')"
    />
    <dl v-else class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
      <template v-for="row in detailRows" :key="row.label">
        <dt class="text-outline">{{ row.label }}</dt>
        <dd class="break-words text-ink-soft">{{ row.value }}</dd>
      </template>
    </dl>
  </PanelSheet>
</template>
