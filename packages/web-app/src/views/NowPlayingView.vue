<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import IconAutoDj from '~icons/lucide/audio-lines'
import IconBan from '~icons/lucide/ban'
import IconHeart from '~icons/lucide/heart'
import IconMusic from '~icons/lucide/music'
import IconPause from '~icons/lucide/pause'
import IconPlay from '~icons/lucide/play'
import IconRepeat from '~icons/lucide/repeat'
import IconScrobble from '~icons/lucide/radio-tower'
import IconRepeatOne from '~icons/lucide/repeat-1'
import IconShuffle from '~icons/lucide/shuffle'
import IconSkipBack from '~icons/lucide/skip-back'
import IconSkipForward from '~icons/lucide/skip-forward'
import IconVolume from '~icons/lucide/volume-2'
import IconVolumeOff from '~icons/lucide/volume-x'

import { formatDuration, nowPlayingCoverUrl } from '../api/display'
import { LastfmStatus, PlayState, RepeatMode, ShuffleMode } from '../api/types'
import StarRating from '../components/StarRating.vue'
import TrackPanels from '../components/TrackPanels.vue'
import { useCoverAccent } from '../composables/useCoverAccent'
import { usePlayerStore } from '../stores/player'

const { t } = useI18n()

/** The art at window size, which is the only place it is not boxed in. */
const expanded = ref(false)


const player = usePlayerStore()

// The server pushes no position events, so the bar is advanced locally and
// corrected against the server twice a minute. Seeking replaces both.
const TICK_MS = 250
const RESYNC_MS = 30_000

const seeking = ref(false)
const seekValue = ref(0)

const tick = window.setInterval(() => {
  if (!seeking.value) player.advance(TICK_MS)
}, TICK_MS)
const resync = window.setInterval(() => {
  if (player.connected && !seeking.value) void player.refreshPosition()
}, RESYNC_MS)

onUnmounted(() => {
  clearInterval(tick)
  clearInterval(resync)
})

const cover = computed(() => nowPlayingCoverUrl(player.track?.cover_hash))
const progress = computed(() =>
  player.durationMs > 0 ? (player.positionMs / player.durationMs) * 100 : 0,
)

/**
 * Something is playing but has no length: a radio stream.
 *
 * Reported as a duration of zero, which a seek bar renders as a track that is
 * both empty and unseekable while the elapsed clock climbs beside it.
 */
const isLive = computed(() => player.track !== null && player.durationMs <= 0)

useCoverAccent(cover)

watch(
  () => player.positionMs,
  (value) => {
    if (!seeking.value) seekValue.value = value
  },
  { immediate: true },
)

const volumeValue = ref(0)
watch(
  () => player.shownVolume,
  (value) => {
    volumeValue.value = value
  },
  { immediate: true },
)

function commitSeek() {
  seeking.value = false
  void player.seek(seekValue.value)
}

/** The label says which mode is on, since the icon alone cannot. */
const shuffleLabel = computed(() => {
  if (player.shuffle === ShuffleMode.AutoDj) return t('player.action.autoDj')
  return player.shuffle === ShuffleMode.Shuffle
    ? t('player.action.shuffleOn')
    : t('player.action.shuffleOff')
})

/** none -> all -> one -> none, the order MusicBee's own button cycles in. */
const REPEAT_CYCLE: Record<RepeatMode, RepeatMode> = {
  [RepeatMode.None]: RepeatMode.All,
  [RepeatMode.All]: RepeatMode.One,
  [RepeatMode.One]: RepeatMode.None,
}

function cycleRepeat() {
  void player.setRepeat(REPEAT_CYCLE[player.repeat])
}
</script>

<template>
  <div class="relative isolate flex h-full flex-col overflow-hidden">
    <!-- The art again, blown out and blurred: it tints the pane with the record
         being played rather than with a colour we chose in advance. -->
    <img
      v-if="cover"
      :src="cover"
      alt=""
      aria-hidden="true"
      class="absolute -z-10 h-full w-full scale-125 object-cover opacity-25 blur-3xl saturate-150"
    />

    <!-- Centred safely: a column taller than the pane spills equally both ways,
         and the half above the scroll origin is somewhere `overflow-y` can
         never reach. `safe` falls back to the start exactly then, so the art
         keeps its top rather than being centred out of the window. -->
    <div
      class="flex h-full flex-col items-center justify-center-safe gap-4 overflow-y-auto p-4 sm:gap-7 sm:p-6"
    >
      <!-- The art takes what the rest of the pane does not want, rather than a
           fixed size the pane may not have: everything below it is a row of
           text or controls that cannot usefully be smaller, so the art is the
           only thing that can give, and on a short phone something has to.
           Capped at each width so it fills the pane it is in without swelling
           past it: the pane itself is what widens on a large screen. -->
      <div
        class="flex min-h-0 w-full max-w-xs flex-1 items-center justify-center sm:max-w-sm lg:max-w-lg"
      >
        <button
          class="flex h-full max-h-80 min-h-40 w-full items-center justify-center transition-transform sm:max-h-96 lg:max-h-128 enabled:hover:scale-[1.02]"
          :disabled="!cover"
          :aria-label="$t('player.art.expand')"
          @click="expanded = true"
        >
          <!-- Sized by the sleeve itself, clamped on both axes: an explicit
               height would win over the width cap and stretch a square record
               into a rectangle on whichever pane is the narrower. -->
          <img
            v-if="cover"
            :src="cover"
            alt=""
            class="max-h-full max-w-full rounded-art object-contain shadow-2xl ring-1 ring-outline/20"
          />
          <div
            v-else
            class="flex aspect-square h-full max-h-full max-w-full items-center justify-center rounded-art bg-surface-2 shadow-2xl ring-1 ring-outline/20"
          >
            <IconMusic class="size-1/3 opacity-20" />
          </div>
        </button>
      </div>

      <div class="w-full max-w-sm space-y-1 text-center lg:max-w-lg">
        <p class="truncate text-xl leading-tight font-semibold tracking-tight">
          {{ player.track?.title || $t('player.nothingPlaying') }}
        </p>
        <p class="truncate text-sm text-ink-soft">{{ player.track?.artist }}</p>
        <p class="truncate text-xs text-outline">{{ player.track?.album }}</p>
      </div>

      <!-- A stream has no length, so there is no position to show or seek to.
           A bar pinned at zero under a rising clock is worse than no bar. -->
      <div v-if="isLive" class="flex w-full max-w-sm items-center justify-center gap-2 lg:max-w-lg">
        <span class="size-1.5 rounded-full bg-accent"></span>
        <span class="text-2xs font-medium tracking-wide text-accent uppercase">
          {{ $t('player.live') }}
        </span>
        <span class="text-2xs tabular-nums text-outline">{{ formatDuration(seekValue) }}</span>
      </div>

      <div v-else class="w-full max-w-sm lg:max-w-lg">
        <input
          type="range"
          class="w-full"
          min="0"
          :max="player.durationMs || 1"
          v-model.number="seekValue"
          :disabled="!player.durationMs"
          @pointerdown="seeking = true"
          @change="commitSeek"
        />
        <div class="flex justify-between text-2xs tabular-nums text-outline">
          <span>{{ formatDuration(seekValue) }}</span>
          <span>{{ formatDuration(player.durationMs) }}</span>
        </div>
        <div class="sr-only" role="progressbar" :aria-valuenow="Math.round(progress)">
          {{ Math.round(progress) }}%
        </div>
      </div>

      <div class="flex w-full max-w-sm items-center justify-center gap-5 sm:gap-6 lg:max-w-lg">
        <button
          class="p-2 transition-colors"
          :class="
            player.shuffle !== ShuffleMode.Off
              ? 'text-accent'
              : 'text-outline hover:text-ink'
"
          :aria-label="shuffleLabel"
          :title="shuffleLabel"
          :aria-pressed="player.shuffle !== ShuffleMode.Off"
          @click="player.cycleShuffle()"
        >
          <IconAutoDj v-if="player.shuffle === ShuffleMode.AutoDj" class="size-5" />
          <IconShuffle v-else class="size-5" />
        </button>
        <button
          class="p-2 transition-transform active:scale-90"
          :aria-label="$t('player.action.previous')"
          @click="player.previous()"
        >
          <IconSkipBack class="size-7 fill-current" />
        </button>
        <button
          class="rounded-full bg-accent p-4 text-white shadow-lg transition-transform active:scale-95"
          :aria-label="$t('player.action.playPause')"
          @click="player.playPause()"
        >
          <IconPause v-if="player.playState === PlayState.Playing" class="size-7 fill-current" />
          <IconPlay v-else class="size-7 fill-current" />
        </button>
        <button
          class="p-2 transition-transform active:scale-90"
          :aria-label="$t('player.action.next')"
          @click="player.next()"
        >
          <IconSkipForward class="size-7 fill-current" />
        </button>
        <button
          class="p-2 transition-colors"
          :class="
            player.repeat !== RepeatMode.None
              ? 'text-accent'
              : 'text-outline hover:text-ink'
"
          :aria-label="$t('player.action.repeat')"
          :aria-pressed="player.repeat !== RepeatMode.None"
          @click="cycleRepeat"
        >
          <IconRepeatOne v-if="player.repeat === RepeatMode.One" class="size-5" />
          <IconRepeat v-else class="size-5" />
        </button>
      </div>

      <div class="flex w-full max-w-sm items-center gap-3 lg:max-w-lg">
        <button
          class="text-outline transition-colors hover:text-ink"
          :aria-label="$t('player.action.mute')"
          @click="player.setMuted(!player.muted)"
        >
          <IconVolumeOff v-if="player.muted" class="size-5" />
          <IconVolume v-else class="size-5" />
        </button>
        <input
          type="range"
          class="flex-1"
          min="0"
          max="100"
          v-model.number="volumeValue"
          @change="player.setVolume(volumeValue)"
        />
        <span class="w-8 text-right text-2xs tabular-nums text-outline">
          {{ player.shownVolume }}
        </span>
      </div>

      <div class="flex items-center gap-1">
        <StarRating
          :rating="player.rating"
          :label="(stars) => $t('player.action.rate', { stars })"
          @set="player.setRating"
        />
        <span class="mx-2 h-5 w-px bg-outline/50"></span>
        <!-- Love and ban write MusicBee's own Love rating, so they work with
             or without a last.fm account, and a track already loved shows it
             rather than making you remember. -->
        <button
          class="p-1 transition-colors"
          :class="
            player.lastfm === LastfmStatus.Love
              ? 'text-rose-500'
              : 'text-outline hover:text-rose-500'
          "
          :aria-pressed="player.lastfm === LastfmStatus.Love"
          :aria-label="$t('player.action.love')"
          @click="player.setLastfm(LastfmStatus.Love)"
        >
          <IconHeart
            class="size-5"
            :class="{ 'fill-current': player.lastfm === LastfmStatus.Love }"
          />
        </button>
        <button
          class="p-1 transition-colors"
          :class="player.lastfm === LastfmStatus.Ban ? 'text-ink' : 'text-outline hover:text-ink'"
          :aria-pressed="player.lastfm === LastfmStatus.Ban"
          :aria-label="$t('player.action.ban')"
          @click="player.setLastfm(LastfmStatus.Ban)"
        >
          <IconBan class="size-5" />
        </button>

        <!-- Scrobbling is the odd one out: a player setting rather than a tag
             on the track, and the only one the server can refuse, since it is
             the only one that needs an account. -->
        <button
          class="p-1 transition-colors"
          :class="player.scrobbling ? 'text-accent' : 'text-outline hover:text-ink'"
          :aria-pressed="player.scrobbling"
          :aria-label="$t('player.action.scrobble')"
          :title="player.scrobblingRefusal ?? $t('player.action.scrobble')"
          @click="player.setScrobbling(!player.scrobbling)"
        >
          <IconScrobble class="size-5" />
        </button>
      </div>

      <p v-if="player.scrobblingRefusal" class="text-2xs text-outline" role="status">
        {{ player.scrobblingRefusal }}
      </p>

      <TrackPanels />
    </div>

    <!-- Nothing but the record: no controls to dodge, and a click anywhere
         closes it, so it needs no chrome of its own. -->
    <div
      v-if="expanded && cover"
      role="dialog"
      :aria-label="$t('player.art.expanded')"
      class="fixed inset-0 z-30 flex items-center justify-center bg-surface/95 p-6 backdrop-blur-xl"
      @click="expanded = false"
      @keydown.esc="expanded = false"
    >
      <img
        :src="cover"
        alt=""
        class="max-h-full max-w-full rounded-art object-contain shadow-2xl"
      />
    </div>
  </div>
</template>
