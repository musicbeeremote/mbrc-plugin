<script setup lang="ts">
import { storeToRefs } from "pinia";
import { usePlayerStore } from "../stores/player";
import { formatMs, shuffleActive } from "../lib/player";

const store = usePlayerStore();
const { np, connected, showLyrics, hasTrack } = storeToRefs(store);
const {
  playPause,
  next,
  previous,
  toggleShuffle,
  toggleRepeat,
  toggleMute,
  toggleLove,
  setVolume,
  seek,
  toggleLyrics,
  loadState,
} = store;

const val = (e: Event) => Number((e.target as HTMLInputElement).value);
</script>

<template>
  <div class="flex w-[320px] shrink-0 flex-col overflow-hidden rounded-lg border border-zinc-800 bg-zinc-900/60">
    <!-- header -->
    <div class="flex items-center gap-2 border-b border-zinc-800 px-3 py-2">
      <span class="text-xs font-medium text-zinc-300">Now playing</span>
      <span
        class="h-2 w-2 rounded-full"
        :class="connected ? 'bg-emerald-400' : 'bg-zinc-600'"
        :title="connected ? 'Primary connected' : 'Primary disconnected'"
      />
      <button
        class="ml-auto text-[11px] text-zinc-500 hover:text-zinc-300 disabled:opacity-40"
        :disabled="!connected"
        title="Re-fetch player state"
        @click="loadState"
      >
        Refresh
      </button>
    </div>

    <div class="flex flex-1 flex-col gap-3 p-3">
      <!-- cover (with lyrics overlay) -->
      <div class="relative aspect-square w-full overflow-hidden rounded-md bg-zinc-950">
        <img
          v-if="np.coverDataUrl"
          :src="np.coverDataUrl"
          alt="cover"
          class="h-full w-full object-cover"
        />
        <div v-else class="flex h-full w-full items-center justify-center text-zinc-700">
          <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
            <circle cx="12" cy="12" r="9" />
            <circle cx="12" cy="12" r="2.5" />
          </svg>
        </div>

        <!-- lyrics overlay -->
        <div
          v-if="showLyrics"
          class="absolute inset-0 flex flex-col bg-zinc-950/92 p-3"
        >
          <div class="mb-1 flex items-center justify-between text-[11px] text-zinc-400">
            <span>Lyrics</span>
            <span class="text-zinc-600">{{ np.lyricsStatus }}</span>
          </div>
          <pre
            v-if="np.lyrics"
            class="flex-1 overflow-auto whitespace-pre-wrap break-words font-sans text-xs leading-relaxed text-zinc-200"
          >{{ np.lyrics }}</pre>
          <div v-else class="flex flex-1 items-center justify-center text-xs text-zinc-600">
            No lyrics.
          </div>
        </div>
      </div>

      <!-- title / artist / actions -->
      <div>
        <div class="flex items-start gap-2">
          <div class="min-w-0 flex-1">
            <div class="truncate text-sm font-semibold text-zinc-100" :title="np.title">
              {{ np.title || "-" }}
            </div>
            <div class="truncate text-xs text-zinc-400" :title="np.artist">{{ np.artist || "-" }}</div>
          </div>
          <!-- lyrics toggle -->
          <button
            class="rounded p-1 hover:bg-zinc-800"
            :class="showLyrics ? 'text-sky-400' : 'text-zinc-500'"
            :disabled="!connected"
            title="Lyrics"
            @click="toggleLyrics"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
              <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
              <path d="M9 10h6M9 13h4" />
            </svg>
          </button>
          <!-- love -->
          <button
            class="rounded p-1 hover:bg-zinc-800"
            :class="np.lfm === 'Love' ? 'text-rose-400' : 'text-zinc-500'"
            :disabled="!connected"
            title="Love (Last.fm)"
            @click="toggleLove"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" :fill="np.lfm === 'Love' ? 'currentColor' : 'none'" stroke="currentColor" stroke-width="1.8">
              <path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l1.1 1L12 21l7.7-7.6 1.1-1a5.5 5.5 0 0 0 0-7.8z" />
            </svg>
          </button>
        </div>
        <div class="mt-0.5 truncate text-[11px] text-zinc-500">
          {{ np.album || "-" }}<span v-if="np.year"> · {{ np.year }}</span>
        </div>
      </div>

      <!-- seek bar -->
      <div>
        <input
          type="range"
          class="w-full accent-sky-500"
          min="0"
          :max="np.durationMs || 0"
          :value="np.positionMs"
          :disabled="!connected || !np.durationMs"
          @change="seek(val($event))"
        />
        <div class="flex justify-between text-[10px] tabular-nums text-zinc-500">
          <span>{{ formatMs(np.positionMs) }}</span>
          <span>{{ formatMs(np.durationMs) }}</span>
        </div>
      </div>

      <!-- transport -->
      <div class="flex items-center justify-between">
        <button
          class="rounded p-1.5 hover:bg-zinc-800 disabled:opacity-40"
          :class="shuffleActive(np.shuffle) ? 'text-amber-400' : 'text-zinc-400'"
          :disabled="!connected"
          :title="`Shuffle: ${np.shuffle}`"
          @click="toggleShuffle"
        >
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
            <path d="M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5" />
          </svg>
        </button>
        <button
          class="rounded p-1.5 text-zinc-300 hover:bg-zinc-800 disabled:opacity-40"
          :disabled="!connected"
          title="Previous"
          @click="previous"
        >
          <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor">
            <path d="M6 5h2v14H6zM20 5v14l-11-7z" />
          </svg>
        </button>
        <button
          class="rounded-full bg-zinc-100 p-2.5 text-zinc-900 hover:bg-white disabled:opacity-40"
          :disabled="!connected"
          :title="np.state === 'Playing' ? 'Pause' : 'Play'"
          @click="playPause"
        >
          <svg v-if="np.state === 'Playing'" width="22" height="22" viewBox="0 0 24 24" fill="currentColor">
            <path d="M7 5h4v14H7zM13 5h4v14h-4z" />
          </svg>
          <svg v-else width="22" height="22" viewBox="0 0 24 24" fill="currentColor">
            <path d="M7 4v16l13-8z" />
          </svg>
        </button>
        <button
          class="rounded p-1.5 text-zinc-300 hover:bg-zinc-800 disabled:opacity-40"
          :disabled="!connected"
          title="Next"
          @click="next"
        >
          <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor">
            <path d="M16 5h2v14h-2zM4 5l11 7-11 7z" />
          </svg>
        </button>
        <button
          class="rounded p-1.5 hover:bg-zinc-800 disabled:opacity-40"
          :class="np.repeat !== 'None' && np.repeat !== 'Undefined' ? 'text-amber-400' : 'text-zinc-400'"
          :disabled="!connected"
          :title="`Repeat: ${np.repeat}`"
          @click="toggleRepeat"
        >
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
            <path d="M17 2l4 4-4 4" />
            <path d="M3 11v-1a4 4 0 0 1 4-4h14M7 22l-4-4 4-4" />
            <path d="M21 13v1a4 4 0 0 1-4 4H3" />
            <text v-if="np.repeat === 'One'" x="12" y="15" text-anchor="middle" font-size="7" fill="currentColor" stroke="none">1</text>
          </svg>
        </button>
      </div>

      <!-- volume -->
      <div class="flex items-center gap-2">
        <button
          class="rounded p-1 text-zinc-400 hover:bg-zinc-800 disabled:opacity-40"
          :disabled="!connected"
          :title="np.muted ? 'Unmute' : 'Mute'"
          @click="toggleMute"
        >
          <svg v-if="np.muted" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
            <path d="M11 5 6 9H2v6h4l5 4zM23 9l-6 6M17 9l6 6" />
          </svg>
          <svg v-else width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
            <path d="M11 5 6 9H2v6h4l5 4zM15.5 8.5a5 5 0 0 1 0 7M19 5a9 9 0 0 1 0 14" />
          </svg>
        </button>
        <input
          type="range"
          class="flex-1 accent-sky-500"
          min="0"
          max="100"
          :value="np.volume"
          :disabled="!connected"
          @change="setVolume(val($event))"
        />
        <span class="w-8 text-right text-[10px] tabular-nums text-zinc-500">{{ np.volume }}</span>
      </div>

      <div v-if="connected && !hasTrack" class="text-center text-[11px] text-zinc-600">
        Waiting for player state…
      </div>
      <div v-else-if="!connected" class="text-center text-[11px] text-zinc-600">
        Connect the primary socket to control playback.
      </div>
    </div>
  </div>
</template>
