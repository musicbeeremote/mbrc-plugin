<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import type { Component } from 'vue'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'

import IconLibrary from '~icons/lucide/library'
import IconMusic from '~icons/lucide/music'
import IconPlaylists from '~icons/lucide/list'
import IconQueue from '~icons/lucide/list-music'
import IconRadio from '~icons/lucide/radio'

import { client } from './api/client'
import { WireEvent } from './api/ops'
import NowPlayingBar from './components/NowPlayingBar.vue'
import ThemeToggle from './components/ThemeToggle.vue'
import { useShortcuts } from './composables/useShortcuts'
import { useUpdateWatch } from './composables/useUpdateWatch'
import { useLibraryStore } from './stores/library'
import { usePlayerStore } from './stores/player'
import { useQueueStore } from './stores/queue'
import { RouteName, wideRedirect } from './router/locations'
import NowPlayingView from './views/NowPlayingView.vue'
import PairingView from './views/PairingView.vue'

const TABS: { id: RouteName; to: string; label: string; icon: Component }[] = [
  { id: RouteName.Playing, to: '/playing', label: 'common.nav.playing', icon: IconMusic },
  { id: RouteName.Queue, to: '/queue', label: 'common.nav.queue', icon: IconQueue },
  { id: RouteName.Library, to: '/library', label: 'common.nav.library', icon: IconLibrary },
  { id: RouteName.Playlists, to: '/lists', label: 'common.nav.playlists', icon: IconPlaylists },
  { id: RouteName.Radio, to: '/radio', label: 'common.nav.radio', icon: IconRadio },
]

/**
 * Two layouts, not one that stretches.
 *
 * Narrow is a phone: one pane at a time behind a bottom tab bar. Wide is a
 * tablet or desktop, where a single centred column would waste most of the
 * screen: a nav rail, the browse pane, and now playing all stay on screen at
 * once, so changing what is playing never hides what you were browsing.
 */
const isWide = useMediaQuery('(min-width: 768px)')

const route = useRoute()
const router = useRouter()
const tab = computed(() => route.name as RouteName)

const needsPairing = ref(false)
const ready = ref(false)

const player = usePlayerStore()
const update = useUpdateWatch()
const queue = useQueueStore()
const library = useLibraryStore()

useShortcuts(() => ({
  playPause: () => void player.playPause(),
  next: () => void player.next(),
  previous: () => void player.previous(),
  volume: player.volume,
  setVolume: (value) => void player.setVolume(value),
  toggleMute: () => void player.setMuted(!player.muted),
}))

// Now playing owns the right rail on a wide screen, so it is not a destination
// there. A phone rotated into a tablet layout on the Playing tab would
// otherwise land on a pane that no longer exists.
const tabs = computed(() =>
  isWide.value ? TABS.filter((entry) => entry.id !== RouteName.Playing) : TABS,
)

// Immediate, because the redirect has to happen on the first evaluation too: a
// window that is already wide when the app loads never fires a change, and
// Playing is not a destination here, so `main` would render nothing at all.
watch(
  [isWide, tab],
  ([wide, name]) => {
    const target = wideRedirect(wide, name)
    if (target) void router.replace(target)
  },
  { immediate: true },
)

/**
 * Whether the server is asking browsers to pair at all.
 *
 * Read from the server rather than remembered, because the setting is toggled in
 * MusicBee while this page is open.
 */
/**
 * Whether this browser has to pair before it can do anything.
 *
 * Asked rather than worked out: the pairing token lives in a cookie the page
 * cannot read, which is the point of it being there, so the server is the only
 * side that can say whether this browser already holds one.
 */
async function mustPair(): Promise<boolean> {
  const response = await fetch('/api/pair/status').catch(() => null)
  const status = (await response?.json().catch(() => null)) as {
    auth_required?: boolean
    paired?: boolean
  } | null
  if (status === null) return false
  return status.auth_required === true && status.paired !== true
}

function start() {
  player.bind()
  queue.bind()
  library.bind()
  update.bind()
  client.on(WireEvent.AuthRequired, async () => {
    // A refusal is only the user's to fix while pairing is enforced. With it off
    // there is no code to enter, so reconnect rather than show a screen that
    // cannot help; the client has already stopped retrying on its own.
    if (!(await mustPair())) {
      client.connect()
      return
    }
    needsPairing.value = true
    ready.value = false
  })
  client.on(WireEvent.ServerShutdown, () => {
    // A deliberate stop, not a dropped cable: retrying into a MusicBee that is
    // closing achieves nothing, so stop until the user reloads.
    client.disconnect()
    player.connected = false
  })
  client.connect()
  ready.value = true
}

onMounted(async () => {
  needsPairing.value = await mustPair()
  if (!needsPairing.value) start()
})

onUnmounted(() => client.disconnect())

/** Reloading is what picks up the bundle the new plugin serves. */
function reload() {
  globalThis.location.reload()
}

function onPaired() {
  needsPairing.value = false
  start()
}
</script>

<template>
  <PairingView v-if="needsPairing" @paired="onPaired" />

  <!-- Installed, the app draws under the status bar (iOS asks for that, so the
       artwork reaches the top edge), so the shell keeps the inset clear. -->
  <div v-else-if="ready" class="flex h-full flex-col pt-[env(safe-area-inset-top)]">
    <p
      v-if="!player.connected && player.retrying"
      class="bg-surface-2 p-1.5 text-center text-2xs tracking-wide text-ink-soft"
      role="status"
    >
      {{ $t('common.state.reconnecting') }}
    </p>

    <!-- Chased for about two minutes and not there. A button rather than a
         spinner: the reader knows whether MusicBee is running and the page
         does not, and retrying at them forever only costs battery. -->
    <button
      v-else-if="!player.connected"
      class="bg-surface-2 p-1.5 text-center text-2xs tracking-wide text-accent"
      @click="client.connect()"
    >
      {{ $t('common.state.disconnected') }}
    </button>

    <!-- The plugin was replaced under this page, so what it is running is a
         build the server no longer serves. Reloading is the whole fix. -->
    <button
      v-if="update.available.value"
      class="bg-accent-soft p-1.5 text-center text-2xs tracking-wide text-accent"
      @click="reload()"
    >
      {{ $t('common.state.updated', { version: update.available.value }) }}
    </button>

    <div class="flex min-h-0 flex-1">
      <!-- Wide: a persistent nav rail instead of the bottom bar. -->
      <nav
        v-if="isWide"
        class="flex w-44 shrink-0 flex-col gap-1 border-r border-surface-2 p-3"
      >
        <RouterLink
          v-for="entry in tabs"
          :key="entry.id"
          :to="entry.to"
          class="flex items-center gap-3 rounded-control px-3 py-2 text-left text-sm transition-colors"
          :class="
            tab === entry.id
              ? 'bg-accent-soft text-accent font-medium'
              : 'text-ink-soft hover:bg-surface-2/50'
          "
          :aria-current="tab === entry.id ? 'page' : undefined"
        >
          <component :is="entry.icon" class="size-5 shrink-0" />
          {{ $t(entry.label) }}
        </RouterLink>

        <ThemeToggle variant="rail" />
      </nav>

      <main class="min-w-0 flex-1">
        <RouterView />
      </main>

      <!-- Wide: now playing is always on screen, never a place you navigate to. -->
      <aside
        v-if="isWide"
        class="w-[clamp(20rem,30vw,32rem)] shrink-0 overflow-y-auto border-l border-surface-2"
      >
        <NowPlayingView />
      </aside>
    </div>

    <!-- Narrow: a compact bar so transport is reachable from every tab. -->
    <NowPlayingBar
      v-if="!isWide && tab !== RouteName.Playing"
      @open="router.push('/playing')"
    />

    <!-- The theme sits at the end of the bar but outside the nav: it is a
         setting, not a fifth place to go. -->
    <div
      v-if="!isWide"
      class="flex border-t border-surface-2 pb-[env(safe-area-inset-bottom)]"
    >
      <nav class="flex min-w-0 flex-1">
        <RouterLink
          v-for="entry in tabs"
          :key="entry.id"
          :to="entry.to"
          class="flex min-w-0 flex-1 flex-col items-center gap-1 py-2 text-2xs transition-colors"
          :class="tab === entry.id ? 'text-accent' : 'text-outline'"
          :aria-current="tab === entry.id ? 'page' : undefined"
        >
          <component :is="entry.icon" class="size-5" />
          <span class="max-w-full truncate">{{ $t(entry.label) }}</span>
        </RouterLink>
      </nav>

      <ThemeToggle variant="bar" />
    </div>
  </div>
</template>
