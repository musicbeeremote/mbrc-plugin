<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import IconBack from '~icons/lucide/chevron-left'
import IconFolder from '~icons/lucide/folder'
import IconPlaylists from '~icons/lucide/list'
import IconMusic from '~icons/lucide/music'
import IconPlay from '~icons/lucide/play'
import IconSearch from '~icons/lucide/search'

import { coverUrl, formatDuration, QueryField, QueueMode, trackLabel } from '../api/types'
import EmptyState from '../components/EmptyState.vue'
import PlayingIndicator from '../components/PlayingIndicator.vue'
import QueueMenu from '../components/QueueMenu.vue'
import { browsePlaylists, playlistLabel } from '../composables/playlistFolders'
import { useLazyRows } from '../composables/useLazyRows'
import {
  openPlaylistFromRoute,
  playlistPathFromRoute,
  playlistQueryFieldFromRoute,
  playlistQueryFromRoute,
  playlistsRoute,
  playlistTracksRoute,
} from '../router/locations'
import { useLibraryStore } from '../stores/library'
import { usePlayerStore } from '../stores/player'
import { usePlaylistStore } from '../stores/playlist'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const library = useLibraryStore()
const player = usePlayerStore()
const playlist = usePlaylistStore()

/** The folder being looked at, as segments. Empty is the playlists root. */
const path = computed(() => playlistPathFromRoute(route.query))

/** The playlist open, if any. The URL says which, so a reload keeps it open. */
const openUrl = computed(() => openPlaylistFromRoute(route.query))

/** What it is narrowed to, which the address carries for the same reason. */
const openQuery = computed(() => playlistQueryFromRoute(route.query))

/** And which column that reads, so a shared link searches what it was told to. */
const openField = computed(() => playlistQueryFieldFromRoute(route.query))

/** The columns a search can be pointed at, in the order the row reads them. */
const FIELDS = [QueryField.Any, QueryField.Title, QueryField.Artist, QueryField.Album] as const

const here = computed(() => browsePlaylists(library.playlists, path.value))

const heading = computed(() =>
  openUrl.value ? playlist.name : (path.value.at(-1) ?? ''),
)

/** Matches the row height below, which the virtual list needs stated. */
const ROW_HEIGHT = ref(56)

const tracks = computed(() => playlist.tracks)

const { list, containerProps, wrapperProps } = useLazyRows(tracks, {
  rowHeight: ROW_HEIGHT,
  hasMore: computed(() => playlist.hasMore),
  loading: computed(() => playlist.loading),
  loadMore: async () => {
    if (openUrl.value) await playlist.load(openUrl.value, true)
  },
})

onMounted(() => {
  void library.loadPlaylists()
})

/** Open whenever the address carries a term, so a shared link shows its search. */
const searching = ref(openQuery.value !== '')
const searchInput = ref<HTMLInputElement | null>(null)
const term = ref(openQuery.value)
const field = ref<QueryField>(openField.value)

const SEARCH_DEBOUNCE_MS = 250
let debounce = 0

/** A box you have to click into after asking for it is a box that was not asked for. */
async function toggleSearch() {
  searching.value = !searching.value
  if (searching.value) {
    await nextTick()
    searchInput.value?.focus()
  } else if (term.value !== '') {
    term.value = ''
    field.value = QueryField.Any
  }
}

// The search is part of the address, so it survives a reload and can be shared,
// exactly as the library's is.
watch(term, (value) => {
  window.clearTimeout(debounce)
  debounce = window.setTimeout(() => {
    if (!openUrl.value || value.trim() === openQuery.value) return
    void router.replace(
      playlistTracksRoute(openUrl.value, path.value, { search: value, field: field.value }),
    )
  }, SEARCH_DEBOUNCE_MS)
})

// Changing the column is not typing, so it takes effect at once rather than
// after the pause that keeps a keystroke from being a request of its own.
watch(field, (value) => {
  if (!openUrl.value || term.value.trim() === '') return
  void router.replace(
    playlistTracksRoute(openUrl.value, path.value, { search: term.value, field: value }),
  )
})

// The address is what says which playlist is open, so opening, closing and a
// pasted link all arrive here rather than through three separate paths.
watch(
  [openUrl, openQuery, openField],
  ([url, q, f], previous) => {
    const [wasUrl, wasQuery, wasField] = previous ?? [undefined, undefined, undefined]
    if (url === wasUrl && q === wasQuery && f === wasField && playlist.tracks.length > 0) return
    if (url !== wasUrl) {
      playlist.close()
      term.value = q
      field.value = f
      searching.value = q !== ''
    }
    if (url) void playlist.search(url, q, f)
  },
  { immediate: true },
)

/** Back leaves the playlist first, and only then the folder. */
function back() {
  if (openUrl.value) router.push(playlistsRoute(path.value))
  else router.push(playlistsRoute(path.value.slice(0, -1)))
}

function queueTrack(src: string, mode: QueueMode) {
  if (mode === QueueMode.Now) void library.playNow([src], src)
  else void library.queue([src], mode)
}

/** Hours and minutes, because a playlist's length is not read to the second. */
function formatLength(ms: number): string {
  if (ms <= 0) return ''
  const minutes = Math.round(ms / 60000)
  if (minutes < 60) return t('playlists.minutes', minutes)
  return t('playlists.hoursMinutes', { h: Math.floor(minutes / 60), m: minutes % 60 })
}

/** What the open playlist holds: how many tracks, and how long they run. */
const summary = computed(() => {
  const count = t('playlists.trackCount', playlist.total)
  const length = formatLength(playlist.totalDurationMs)
  return length === '' ? count : `${count} · ${length}`
})

</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center gap-2 border-b border-surface-2 p-3">
      <button
        v-if="path.length > 0 || openUrl"
        class="-ml-1 rounded-control p-1 text-accent transition-colors"
        :aria-label="$t('common.action.back')"
        @click="back()"
      >
        <IconBack class="size-5" />
      </button>
      <div class="min-w-0 flex-1">
        <span class="block truncate text-sm font-medium">
          {{ path.length === 0 ? $t('playlists.title') : heading }}
        </span>
        <!-- What is in it, which is the question opening one asks. -->
        <span v-if="openUrl" class="block truncate text-2xs text-outline">
          {{ summary }}
        </span>
      </div>

      <!-- The trail, so a folder three deep still says where it is. -->
      <span v-if="!openUrl && path.length > 1" class="ml-auto truncate text-2xs text-outline">
        {{ path.slice(0, -1).join(' / ') }}
      </span>

      <template v-if="openUrl">
        <button
          class="rounded-control p-2 text-ink-soft transition-colors hover:bg-surface-2/60"
          :aria-label="$t('playlists.action.search')"
          @click="toggleSearch()"
        >
          <IconSearch class="size-4" />
        </button>
        <button
          class="rounded-control p-2 text-accent transition-colors hover:bg-surface-2/60"
          :aria-label="$t('playlists.action.play', { name: playlist.name })"
          @click="library.playPlaylist(openUrl)"
        >
          <IconPlay class="size-4" />
        </button>
      </template>
    </div>

    <!-- Narrowing is server-side: filtering the rows that happen to be paged in
         would answer with the looked-for track missing and no way to say why. -->
    <div v-if="openUrl && searching" class="flex items-center gap-2 border-b border-surface-2 px-3 py-2">
      <input
        ref="searchInput"
        v-model="term"
        type="search"
        class="min-w-0 flex-1 rounded-control bg-surface-2 px-3 py-2 text-sm outline-none placeholder:text-outline"
        :placeholder="$t('playlists.searchPlaceholder')"
      />
      <!-- Which column the term reads. Beside the box rather than in a menu,
           because it changes what the box means. -->
      <select
        v-model="field"
        class="rounded-control bg-surface-2 px-2 py-2 text-xs text-ink-soft outline-none"
        :aria-label="$t('playlists.searchField')"
      >
        <option v-for="name in FIELDS" :key="name" :value="name">
          {{ $t(`playlists.field.${name}`) }}
        </option>
      </select>
    </div>

    <ul
      v-show="!openUrl && (here.folders.length > 0 || here.playlists.length > 0)"
      class="flex-1 overflow-y-auto"
    >
      <li v-for="folder in here.folders" :key="folder">
        <button
          class="flex w-full items-center gap-3 border-b border-surface-2 px-3 py-3 text-left transition-colors hover:bg-surface-2/40 active:bg-surface-2/70"
          @click="router.push(playlistsRoute([...path, folder]))"
        >
          <IconFolder class="size-5 shrink-0 text-outline" />
          <span class="min-w-0 flex-1 truncate">{{ folder }}</span>
        </button>
      </li>

      <li v-for="entry in here.playlists" :key="entry.url" class="flex items-center border-b border-surface-2">
        <!-- Opening a playlist rather than playing it: what is in one is a
             question with an answer now, and playing it is one tap further. -->
        <button
          class="flex min-w-0 flex-1 items-center gap-3 px-3 py-3 text-left transition-colors hover:bg-surface-2/40 active:bg-surface-2/70"
          @click="router.push(playlistTracksRoute(entry.url, path))"
        >
          <IconPlaylists class="size-5 shrink-0 text-outline" />
          <span class="min-w-0 flex-1 truncate">{{ playlistLabel(entry) }}</span>
        </button>
        <button
          class="mr-2 rounded-control p-2 text-accent transition-colors hover:bg-surface-2/60"
          :aria-label="$t('playlists.action.play', { name: playlistLabel(entry) })"
          @click="library.playPlaylist(entry.url)"
        >
          <IconPlay class="size-4" />
        </button>
      </li>
    </ul>

    <!-- One playlist's tracks. Virtualised and paged like the library's own
         list, because a playlist is as long as someone made it.

         It stands down when it holds nothing: it stretches, and so does the
         notice that replaces it, so two of them would split the pane and leave
         the message stranded in the bottom half. -->
    <div
      v-if="openUrl && playlist.tracks.length > 0"
      v-bind="containerProps"
      class="flex-1 overflow-y-auto"
    >
      <div v-bind="wrapperProps">
        <div
          v-for="row in list"
          :key="row.data.order"
          class="flex items-center border-b border-surface-2"
          :style="{ height: `${ROW_HEIGHT}px` }"
        >
          <div class="relative ml-3 size-10 shrink-0 overflow-hidden rounded-control bg-surface-2">
            <img
              v-if="coverUrl(row.data.cover_hash)"
              :src="coverUrl(row.data.cover_hash) ?? undefined"
              alt=""
              loading="lazy"
              class="h-full w-full object-cover"
            />
            <IconMusic v-else class="absolute inset-0 m-auto size-1/2 text-outline opacity-40" />
            <div
              v-if="row.data.src === player.track?.src"
              class="absolute inset-0 grid place-items-center bg-surface/70"
            >
              <PlayingIndicator class="text-accent" />
            </div>
          </div>
          <button
            class="min-w-0 flex-1 px-3 text-left"
            @click="queueTrack(row.data.src, QueueMode.Now)"
          >
            <span
              class="block min-w-0 truncate text-sm/tight"
              :class="{ 'text-accent': row.data.src === player.track?.src }"
            >
              {{ trackLabel(row.data.title, row.data.src) }}
            </span>
            <span class="mt-0.5 block truncate text-2xs/tight text-outline">
              {{ row.data.artist || $t('common.unknown.artist') }} &middot;
              {{ row.data.album || $t('common.unknown.album') }}
            </span>
          </button>
          <span class="text-2xs tabular-nums text-outline">
            {{ formatDuration(row.data.duration_ms) }}
          </span>
          <QueueMenu
            :label="$t('library.action.more', { title: row.data.title })"
            @select="(mode) => queueTrack(row.data.src, mode)"
          />
        </div>
      </div>
    </div>

    <EmptyState
      v-if="!playlist.loading && openUrl && playlist.total === 0"
      :icon="IconPlaylists"
      :title="playlist.query ? $t('playlists.noMatches') : $t('playlists.emptyPlaylist')"
    />

    <EmptyState
      v-if="
        !library.loading &&
        !openUrl &&
        here.folders.length === 0 &&
        here.playlists.length === 0
      "
      :icon="IconPlaylists"
      :title="$t('playlists.empty')"
    />
  </div>
</template>
