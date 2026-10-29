<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import IconBack from '~icons/lucide/chevron-left'
import IconFiles from '~icons/lucide/list-video'
import IconMedia from '~icons/lucide/book-headphones'
import IconPlay from '~icons/lucide/play'
import IconQueue from '~icons/lucide/list-plus'

import type { AlbumEntry } from '../api/browse'
import type { MediaScope } from '../api/ops'
import { coverUrl, formatDuration, trackLabel } from '../api/display'
import { QueueMode } from '../api/types'
import type { Track } from '../api/types'
import AlbumTile from '../components/AlbumTile.vue'
import CoverPlaceholder from '../components/CoverPlaceholder.vue'
import EmptyState from '../components/EmptyState.vue'
import PlayingIndicator from '../components/PlayingIndicator.vue'
import QueueMenu from '../components/QueueMenu.vue'
import { queueTrack, trackOrLevel } from '../composables/queueTracks'
import { useGridPaging } from '../composables/useGridPaging'
import { mediaFromRoute, mediaRoute } from '../router/locations'
import { useMediaStore } from '../stores/media'
import { usePermissionsStore } from '../stores/permissions'
import { usePlayerStore } from '../stores/player'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const media = useMediaStore()
const permissions = usePermissionsStore()
const player = usePlayerStore()

/** The placements a book has. Add-all is a chapter's, where it means "from here". */
const BOOK_MODES = [QueueMode.Now, QueueMode.Next, QueueMode.Last]

const place = computed(() => mediaFromRoute(route.query))

/** The category the URL names, else the first one MusicBee has anything in. */
const source = computed(() => place.value.source ?? media.sources[0]?.source)

/** What the URL has open: a book, every file, or nothing. */
const scope = computed<MediaScope | null>(() => {
  const { album, artist, all } = place.value
  if (album !== undefined) return artist === undefined ? { album } : { album, artist }
  return all === true ? {} : null
})

onMounted(() => {
  void media.loadSources()
})

watch(
  [source, scope],
  ([next, open]) => {
    if (next === undefined) return
    void media.showSource(next)
    if (open === null) media.close()
    else void media.openBook(open)
  },
  { immediate: true },
)

/** A book is named by its author as well as its title: two authors share titles. */
function bookScope(entry: AlbumEntry): MediaScope {
  return { album: entry.album, artist: entry.artist }
}

function openBook(entry: AlbumEntry) {
  void router.push(mediaRoute({ source: source.value, ...bookScope(entry) }))
}

const heading = computed(() => {
  if (scope.value === null) return t(`media.source.${source.value ?? 'audiobooks'}`)
  return scope.value.album ?? t('media.allFiles')
})

const subheading = computed(() => {
  if (scope.value === null) return t('media.bookCount', media.albumTotal)
  const author = scope.value.artist ?? ''
  return [author, t('media.fileCount', media.trackTotal)].filter(Boolean).join(' · ')
})

/** A chapter's queue actions; its add-all plays the rest of the open book from it. */
function queueChapter(track: Track, mode: QueueMode) {
  void queueTrack(track.src, mode, media.queueOpen)
}

function queueOpen(mode: QueueMode) {
  if (scope.value !== null) void media.queue(scope.value, { mode })
}

const onGridScroll = useGridPaging({
  hasMore: () => media.hasMoreAlbums,
  loading: () => media.loadingAlbums,
  loadMore: () => media.loadAlbums(true),
})
</script>

<template>
  <div class="flex h-full flex-col">
    <!-- Only the categories that hold anything: an empty tab is a dead end. -->
    <nav
      v-if="media.sources.length > 1"
      class="flex border-b border-surface-2"
      role="tablist"
    >
      <button
        v-for="entry in media.sources"
        :key="entry.source"
        role="tab"
        class="flex-1 border-b-2 px-2 py-2.5 text-xs font-medium transition-colors"
        :class="
          source === entry.source
            ? 'border-accent text-accent'
            : 'border-transparent text-outline hover:text-ink'
        "
        :aria-selected="source === entry.source"
        @click="router.push(mediaRoute({ source: entry.source }))"
      >
        {{ $t(`media.source.${entry.source}`) }}
      </button>
    </nav>

    <div v-if="source !== undefined" class="flex items-center gap-2 border-b border-surface-2 p-3">
      <button
        v-if="scope !== null"
        class="-ml-1 rounded-control p-1 text-accent transition-colors"
        :aria-label="$t('common.action.back')"
        @click="router.push(mediaRoute({ source }))"
      >
        <IconBack class="size-5" />
      </button>

      <div class="min-w-0 flex-1">
        <span class="block truncate text-sm font-medium">{{ heading }}</span>
        <span class="block truncate text-2xs text-outline">{{ subheading }}</span>
      </div>

      <!-- Files with no album have no tile, so this is the way to them. -->
      <button
        v-if="scope === null"
        class="flex items-center gap-1.5 rounded-full bg-surface-2 px-3 py-1.5 text-xs transition-colors hover:bg-surface-2/60"
        @click="router.push(mediaRoute({ source, all: true }))"
      >
        <IconFiles class="size-3.5 shrink-0 text-outline" />
        {{ $t('media.allFiles') }}
      </button>

      <template v-else>
        <button
          v-if="permissions.canQueue(QueueMode.Now, null)"
          class="tap-target rounded-control p-2 text-outline transition-colors hover:text-ink"
          :aria-label="$t('media.action.play', { title: heading })"
          :title="$t('media.action.play', { title: heading })"
          @click="queueOpen(QueueMode.Now)"
        >
          <IconPlay class="size-4 fill-current" />
        </button>
        <button
          v-if="permissions.canQueue(QueueMode.Last, null)"
          class="tap-target rounded-control p-2 text-outline transition-colors hover:text-ink"
          :aria-label="$t('media.action.queue', { title: heading })"
          :title="$t('media.action.queue', { title: heading })"
          @click="queueOpen(QueueMode.Last)"
        >
          <IconQueue class="size-4" />
        </button>
      </template>
    </div>

    <!-- The books of a category, as the covers they are recognised by. -->
    <div
      v-if="source !== undefined && scope === null"
      class="flex-1 overflow-y-auto"
      @scroll="onGridScroll"
    >
      <ul class="grid grid-cols-2 gap-x-4 gap-y-5 p-4 sm:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-6">
        <li v-for="entry in media.albums" :key="`${entry.artist}/${entry.album}`">
          <AlbumTile
            :entry="entry"
            :label="$t('library.action.more', { title: entry.album })"
            :modes="BOOK_MODES"
            :detail="$t('media.fileCount', entry.count)"
            @open="openBook(entry)"
            @select="(mode) => media.queue(bookScope(entry), { mode })"
          />
        </li>
      </ul>

      <EmptyState
        v-if="!media.loadingAlbums && media.albums.length === 0 && !media.error"
        :icon="IconMedia"
        :title="$t('media.empty.books')"
        :hint="$t('media.empty.booksHint')"
      />
    </div>

    <!-- One book's chapters, in reading order, or every file in the category. -->
    <div v-else-if="source !== undefined" class="flex-1 overflow-y-auto">
      <ul>
        <li
          v-for="track in media.tracks"
          :key="track.src"
          class="flex items-center border-b border-surface-2 transition-colors hover:bg-surface-2/40"
        >
          <div class="relative ml-3 size-10 shrink-0 overflow-hidden rounded-control bg-surface-2">
            <img
              v-if="coverUrl(track.cover_hash)"
              :src="coverUrl(track.cover_hash) ?? undefined"
              alt=""
              loading="lazy"
              class="h-full w-full object-cover"
            />
            <CoverPlaceholder v-else :name="track.album || track.artist_display" />
            <div
              v-if="track.src === player.track?.src"
              class="absolute inset-0 grid place-items-center bg-surface/70"
            >
              <PlayingIndicator class="text-accent" />
            </div>
          </div>
          <button class="min-w-0 flex-1 px-3 py-2.5 text-left" @click="queueChapter(track, QueueMode.AddAll)">
            <span
              class="block truncate text-sm/tight"
              :class="{ 'text-accent': track.src === player.track?.src }"
            >
              <span v-if="track.track_no" class="mr-1.5 text-2xs tabular-nums text-outline">
                {{ track.track_no }}
              </span>{{ trackLabel(track.title, track.src) }}
            </span>
            <!-- Inside a book every chapter has the same author; the file list
                 mixes them, so there it is worth a line. -->
            <span
              v-if="scope?.album === undefined"
              class="mt-0.5 block truncate text-2xs/tight text-outline"
            >
              {{ [track.artist_display, track.album].filter(Boolean).join(' · ') }}
            </span>
          </button>
          <span class="text-2xs tabular-nums text-outline">
            {{ formatDuration(track.duration_ms) }}
          </span>
          <QueueMenu
            :label="$t('library.action.more', { title: track.title })"
            :tracks="trackOrLevel"
            @select="(mode) => queueChapter(track, mode)"
          />
        </li>
      </ul>

      <EmptyState
        v-if="!media.loadingTracks && media.tracks.length === 0 && !media.error"
        :icon="IconMedia"
        :title="$t('media.empty.files')"
      />

      <button
        v-if="media.hasMoreTracks"
        class="w-full p-4 text-sm text-accent"
        :disabled="media.loadingTracks"
        @click="media.loadTracks(true)"
      >
        {{ media.loadingTracks ? $t('common.state.loading') : $t('common.state.loadMore') }}
      </button>
    </div>

    <EmptyState
      v-else
      :icon="IconMedia"
      :title="$t('media.empty.title')"
      :hint="$t('media.empty.hint')"
    />

    <p
      v-if="media.error"
      class="border-t border-surface-2 bg-rose-500/10 p-3 text-center text-xs text-rose-500"
      role="alert"
    >
      {{ $t('media.failed', { reason: media.error }) }}
    </p>

    <p
      v-if="media.loadingAlbums || media.loadingTracks"
      class="shrink-0 p-2 text-center text-2xs text-outline"
      role="status"
    >
      {{ $t('common.state.loading') }}
    </p>
  </div>
</template>
