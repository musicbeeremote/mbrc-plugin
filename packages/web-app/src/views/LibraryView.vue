<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import IconBack from '~icons/lucide/chevron-left'
import IconLibrary from '~icons/lucide/library'
import IconMusic from '~icons/lucide/music'
import IconNoResults from '~icons/lucide/search-x'
import IconPlay from '~icons/lucide/play'
import IconSearch from '~icons/lucide/search'
import IconShuffle from '~icons/lucide/shuffle'
import IconClear from '~icons/lucide/x'

import type { LibraryScope } from '../api/ops'
import type { AlbumEntry, ArtistEntry, GenreEntry, Track } from '../api/types'
import { coverUrl, formatDuration, QueueMode, trackLabel } from '../api/types'
import EmptyState from '../components/EmptyState.vue'
import PlayingIndicator from '../components/PlayingIndicator.vue'
import QueueMenu from '../components/QueueMenu.vue'
import SortMenu from '../components/SortMenu.vue'
import TrackColumns, { LENGTH_COLUMN, TRACK_COLUMNS } from '../components/TrackColumns.vue'
import { albumSummary } from '../composables/albumSummary'
import { emptyHintKey } from '../composables/emptyHint'
import { isArtist, isGenre, isTrack } from '../composables/flatRows'
import type { FlatRow } from '../composables/flatRows'
import { useLazyRows } from '../composables/useLazyRows'
import {
  SORT_FIELDS,
  carriedSort,
  defaultSort,
  libraryTab,
  parentPosition,
} from '../composables/libraryLevels'
import { libraryRoute, positionFromRoute } from '../router/locations'
import { useLibraryStore, LibraryLevel } from '../stores/library'
import { usePlayerStore } from '../stores/player'

const { t } = useI18n()
const isWide = useMediaQuery('(min-width: 768px)')
const route = useRoute()
const router = useRouter()
const library = useLibraryStore()
const player = usePlayerStore()

const TABS: LibraryLevel[] = [
  LibraryLevel.Genres,
  LibraryLevel.Artists,
  LibraryLevel.Albums,
  LibraryLevel.Tracks,
]

/**
 * The URL says where the pane is, so reading it is the only way in.
 *
 * Immediate, so the first render is a read of the address that was opened
 * rather than of a default the router is about to correct.
 */
const at = computed(() => positionFromRoute(route.params.level, route.query))

/** The part of the current address that a move to `level` keeps. */
function order(level: LibraryLevel) {
  return carriedSort(level, at.value.sort, at.value.descending)
}

/**
 * Navigating is pushing an address; nothing changes the pane directly.
 *
 * The search does not travel: a term that narrowed a list of artists would
 * almost never also match that artist's album titles, and a level that came back
 * empty would read as a library with nothing in it. The order does travel, in
 * both directions, because it is how the reader wants lists read rather than a
 * property of the one in front of them.
 */
function goTo(level: LibraryLevel, add: LibraryScope = {}) {
  const scope = { ...at.value.scope, ...add }
  void router.push(libraryRoute({ level, scope }, order(level)))
}

/**
 * Which album a tile means, not just what it is called.
 *
 * Thirteen titles in a real library name more than one record - three different
 * bands have an album called Live - so the tile's own artist has to travel with
 * it. It also beats the artist in scope, which is who was browsed through
 * rather than who the record is filed under.
 */
function albumScope(entry: AlbumEntry): LibraryScope {
  return { album: entry.album, artist: entry.artist }
}

function openAlbum(entry: AlbumEntry) {
  void router.push(
    libraryRoute(
      { level: LibraryLevel.Tracks, scope: { ...at.value.scope, ...albumScope(entry) } },
      order(LibraryLevel.Tracks),
    ),
  )
}

/** This same place, with one thing about how it is being read changed. */
function addressOf(changes: {
  query?: string
  sort?: string
  descending?: boolean
  albumArtists?: boolean
}) {
  const { query, sort, descending, albumArtists } = at.value
  return libraryRoute(at.value, { query, sort, descending, albumArtists, ...changes })
}

/**
 * Whether the artist list is every credit or the tag albums are filed under.
 *
 * Two different lists rather than a filter of one: a library of a thousand
 * credits is a few hundred album artists, and "X feat. Y" is a row in the first
 * and not in the second.
 */
const canPickArtistTag = computed(() => at.value.level === LibraryLevel.Artists)

const sortFields = computed(() => SORT_FIELDS[at.value.level])
/** Nothing is marked while a search orders itself by how well it matched. */
const activeSort = computed(() =>
  at.value.sort ?? (at.value.query === '' ? defaultSort(at.value) : ''),
)

/** Picking the order already showing reverses it; picking another starts at A. */
function applySort(field: string) {
  const descending = field === activeSort.value ? !at.value.descending : false
  void router.replace(addressOf({ sort: field, descending }))
}

/**
 * The menus of the flat rows, so a right-click can open the row's own one.
 *
 * A desktop reader expects the row's menu at the pointer, and the row has no
 * way to draw one; opening the button it already carries keeps the two from
 * drifting apart.
 */
const hintKey = computed(() => emptyHintKey(library.query, library.scope))

const rowMenus = ref<InstanceType<typeof QueueMenu>[]>([])

function openRowMenu(index: number, event: MouseEvent) {
  rowMenus.value[index]?.openAt(event)
}

function sortLabel(field: string) {
  return t(`library.sort.${field}`)
}

/**
 * Typing must not put a request on the wire per keystroke: each one is a scan on
 * the server for the tracks level, and the answers would race each other back.
 */
const SEARCH_DEBOUNCE_MS = 250

const term = ref('')
let debounce: number | undefined = undefined

watch(
  at,
  (next) => {
    term.value = next.query
    void library.show(
      { level: next.level, scope: next.scope },
      {
        query: next.query,
        sort: next.sort,
        descending: next.descending,
        albumArtists: next.albumArtists,
      },
    )
  },
  { immediate: true },
)

const tab = computed(() => libraryTab(at.value))
const up = computed(() => parentPosition(at.value))

/** One level out, keeping the order the list was being read in. */
function goUp() {
  if (up.value) void router.push(libraryRoute(up.value, order(up.value.level)))
}


// The search is part of the address, so a search is navigation like any other:
// that is what puts a result list in the history and keeps it across a reload.
watch(term, (value) => {
  window.clearTimeout(debounce)
  debounce = window.setTimeout(() => {
    if (value === at.value.query) return
    void router.replace(addressOf({ query: value }))
  }, SEARCH_DEBOUNCE_MS)
})

/** What the pane is showing: the scope drilled into, else the level itself. */
const heading = computed(() => {
  const { album, artist, genre } = library.scope
  return album ?? artist ?? genre ?? t(`library.tab.${library.level}`)
})

/** Only an album has more to say than its name. */
const albumDetail = computed(() =>
  at.value.scope.album === undefined ? '' : albumSummary(library.tracks),
)

function tabLabel(level: LibraryLevel) {
  return t(`library.tab.${level}`)
}

// The buttons play what the pane is showing, so they have to say which that is:
// "Play all" over a filtered list would read as playing the whole library.
const playAllLabel = computed(() =>
  library.scoped ? t('library.action.playThese', { scope: heading.value }) : t('library.action.playAll'),
)
const shuffleAllLabel = computed(() =>
  library.scoped
    ? t('library.action.shuffleThese', { scope: heading.value })
    : t('library.action.shuffleAll'),
)

async function queueScope(add: LibraryScope, mode: QueueMode) {
  await library.queueScope(add, { mode })
}

/**
 * A track's own queue actions.
 *
 * Three of them name this one track. The fourth means "start here and take the
 * rest with you", which is the whole scope rather than the page of it on screen,
 * so the server resolves it from the same filters this level is reading.
 */
async function queueTrack(src: string, mode: QueueMode) {
  await (mode === QueueMode.AddAll
    ? library.queueScope({}, { mode, play: src })
    : library.queue([src], mode))
}

const flatRows = computed<FlatRow[]>(() => {
  if (library.level === LibraryLevel.Genres) return library.genres
  if (library.level === LibraryLevel.Artists) return library.artists
  if (library.level === LibraryLevel.Tracks) return library.tracks
  return []
})

/** A track row carries a cover and a second line, so it is the taller of the two. */
/**
 * A flat track list reads as a table where there is room for one.
 *
 * A two-line row with the artist and album crammed under the title is a phone's
 * answer to a narrow column; given a window, every desktop player gives each
 * its own, which fits more of the library on screen and lets a column heading
 * be the thing you sort by.
 */
const asTable = computed(() => isWide.value && library.level === LibraryLevel.Tracks)

const rowHeight = computed(() => {
  if (library.level !== LibraryLevel.Tracks) return 49
  return asTable.value ? 44 : 60
})

const hasMore = computed(() => library.hasMore)
const loading = computed(() => library.loading)

const { list, containerProps, wrapperProps, scrollTo } = useLazyRows(flatRows, {
  rowHeight,
  hasMore,
  loading,
  loadMore: () => library.load(true),
})

// A new level, a drill-down or a new search is a different list: staying at the
// old offset would open it halfway down.
watch(
  () => [library.level, library.scope, library.query],
  () => scrollTo(0),
)

/**
 * The cover grid's own paging.
 *
 * It is not virtualized - its rows hold several items each, and a screenful of
 * album art is a few dozen elements rather than a few thousand - so it pages on
 * the scrollbar reaching the end rather than on a row index.
 */
function onGridScroll(event: Event) {
  const el = event.target as HTMLElement
  const remaining = el.scrollHeight - el.scrollTop - el.clientHeight
  if (remaining < el.clientHeight && library.hasMore && !library.loading) {
    void library.load(true)
  }
}

</script>

<template>
  <div class="flex h-full flex-col">
    <!-- The four levels, which are also the four tabs the Android app has. -->
    <nav class="flex border-b border-surface-2" role="tablist">
      <button
        v-for="entry in TABS"
        :key="entry"
        role="tab"
        class="flex-1 border-b-2 px-2 py-2.5 text-xs font-medium transition-colors"
        :class="
          tab === entry
            ? 'border-accent text-accent'
            : 'border-transparent text-outline hover:text-ink'
        "
        :aria-selected="tab === entry"
        @click="router.push(libraryRoute({ level: entry, scope: {} }))"
      >
        {{ tabLabel(entry) }}
      </button>
    </nav>

    <div class="flex items-center gap-2 border-b border-surface-2 p-3">
      <button
        v-if="up"
        class="-ml-1 rounded-control p-1 text-accent transition-colors"
        :aria-label="$t('common.action.back')"
        @click="goUp"
      >
        <IconBack class="size-5" />
      </button>

      <div class="relative min-w-0 flex-1">
        <IconSearch class="pointer-events-none absolute top-1/2 left-2 size-4 -translate-y-1/2 text-outline" />
        <input
          id="library-search"
          v-model="term"
          name="library-search"
          type="search"
          class="w-full rounded-control border border-surface-2 bg-transparent py-1.5 pr-8 pl-8 text-sm"
          :placeholder="$t('library.search.placeholder', { level: heading })"
        />
        <button
          v-if="term !== ''"
          class="absolute top-1/2 right-1 -translate-y-1/2 rounded-control p-1 text-outline transition-colors hover:text-ink"
          :aria-label="$t('library.search.clear')"
          @click="term = ''"
        >
          <IconClear class="size-4" />
        </button>
      </div>

      <!-- Only where it means something: the other levels have one list. -->
      <button
        v-if="canPickArtistTag"
        class="rounded-control px-2 py-1.5 text-2xs font-medium transition-colors"
        :class="
          at.albumArtists ? 'bg-accent-soft text-accent' : 'text-outline hover:text-ink'
        "
        :aria-pressed="at.albumArtists"
        :title="$t('library.albumArtists.hint')"
        @click="router.replace(addressOf({ albumArtists: !at.albumArtists }))"
      >
        {{ $t('library.albumArtists.label') }}
      </button>

      <SortMenu
        :label="$t('library.sort.label')"
        :fields="sortFields"
        :active="activeSort"
        :descending="at.descending"
        :name="sortLabel"
        @select="applySort"
      />

      <button
        class="tap-target rounded-control p-2 text-outline transition-colors hover:text-ink"
        :aria-label="playAllLabel"
        :title="playAllLabel"
        @click="library.playAll(false)"
      >
        <IconPlay class="size-4 fill-current" />
      </button>
      <button
        class="tap-target rounded-control p-2 text-outline transition-colors hover:text-ink"
        :aria-label="shuffleAllLabel"
        :title="shuffleAllLabel"
        @click="library.playAll(true)"
      >
        <IconShuffle class="size-4" />
      </button>
    </div>

    <div class="flex items-baseline gap-2 px-3 py-2 text-2xs text-outline">
      <span class="truncate">{{ heading }}</span>
      <span v-if="albumDetail" class="min-w-0 truncate opacity-70">{{ albumDetail }}</span>
      <span class="ml-auto shrink-0 tabular-nums">{{ library.total }}</span>
    </div>

    <TrackColumns
      v-if="asTable"
      :active="activeSort"
      :descending="at.descending"
      @sort="applySort"
    />

    <!-- Albums are a cover grid, so they page on their own; the flat levels go
         through one virtual list that renders only the rows on screen. -->
    <div
      v-if="library.level === LibraryLevel.Albums"
      v-show="library.albums.length > 0"
      class="flex-1 overflow-y-auto"
      @scroll="onGridScroll"
    >
      <ul class="grid grid-cols-2 gap-x-4 gap-y-5 p-4 sm:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-6">
        <li v-for="entry in library.albums" :key="`${entry.artist}/${entry.album}`">
          <button
            class="block w-full transition-transform active:scale-[0.98]"
            @click="openAlbum(entry)"
          >
            <!-- A blank tile reads as a cover still loading. The note says the
                 album has none, which is a different and permanent thing. -->
            <div
              class="flex aspect-square items-center justify-center overflow-hidden rounded-control bg-surface-2 text-outline"
            >
              <img
                v-if="coverUrl(entry.cover_hash)"
                :src="coverUrl(entry.cover_hash) ?? undefined"
                alt=""
                loading="lazy"
                class="h-full w-full object-cover"
              />
              <IconMusic v-else class="size-1/3 opacity-40" />
            </div>
          </button>

          <!-- The menu button is taller than the two lines of caption beside it,
               so its padding is pulled back out: left to itself it would set the
               caption's height and break the grid's vertical rhythm. -->
          <div class="mt-2 flex items-start gap-1">
            <button
              class="min-w-0 flex-1 text-left"
              @click="openAlbum(entry)"
            >
              <span class="block truncate text-sm/tight">
                {{ entry.album || $t('common.unknown.album') }}
              </span>
              <span class="mt-0.5 flex items-baseline gap-1.5 text-2xs/tight text-outline">
                <!-- The artist takes the whole line so the year is pushed to the
                     tile's edge, where it reads as a column down the grid rather
                     than as a word trailing each name at its own length. -->
                <span class="min-w-0 flex-1 truncate">
                  {{ entry.artist || $t('common.unknown.artist') }}
                </span>
                <!-- Derived from the album's tracks, so an album whose tags
                     carry no year simply has none to show. -->
                <span v-if="entry.year" class="shrink-0 tabular-nums opacity-70">
                  {{ entry.year }}
                </span>
              </span>
            </button>
            <QueueMenu
              class="-mt-1.5 -mr-2"
              :label="$t('library.action.more', { title: entry.album })"
              @select="(mode) => queueScope(albumScope(entry), mode)"
            />
          </div>
        </li>
      </ul>
    </div>

    <div v-else v-show="library.total > 0" v-bind="containerProps" class="flex-1">
      <div v-bind="wrapperProps">
        <div
          v-for="row in list"
          :key="row.index"
          class="flex cursor-pointer items-center border-b border-surface-2 transition-colors hover:bg-surface-2/40 active:bg-surface-2/70"
          :style="{ height: `${rowHeight}px` }"
          @contextmenu.prevent="openRowMenu(row.index, $event)"
        >
          <template v-if="isGenre(row.data)">
            <button
              class="min-w-0 flex-1 px-3 text-left"
              @click="goTo(LibraryLevel.Artists, { genre: row.data.genre })"
            >
              <!-- Tracks with no genre tag are a real group worth opening, so
                   the row is named rather than left blank and unclickable-looking. -->
              <span class="block truncate" :class="{ 'text-outline italic': !row.data.genre }">
                {{ row.data.genre || $t('common.unknown.genre') }}
              </span>
            </button>
            <span class="text-2xs tabular-nums text-outline">{{ row.data.count }}</span>
            <QueueMenu
              :label="$t('library.action.more', { title: row.data.genre })"
              :ref="(el) => (rowMenus[row.index] = el as InstanceType<typeof QueueMenu>)"
              @select="(mode) => queueScope({ genre: (row.data as GenreEntry).genre }, mode)"
            />
          </template>

          <template v-else-if="isArtist(row.data)">
            <button
              class="min-w-0 flex-1 px-3 text-left"
              @click="goTo(LibraryLevel.Albums, { artist: row.data.artist })"
            >
              <span class="block truncate">
                {{ row.data.artist || $t('common.unknown.artist') }}
              </span>
            </button>
            <span class="text-2xs tabular-nums text-outline">{{ row.data.count }}</span>
            <QueueMenu
              :label="$t('library.action.more', { title: row.data.artist })"
              :ref="(el) => (rowMenus[row.index] = el as InstanceType<typeof QueueMenu>)"
              @select="(mode) => queueScope({ artist: (row.data as ArtistEntry).artist }, mode)"
            />
          </template>

          <template v-else-if="isTrack(row.data)">
            <!-- The playing mark sits on the cover rather than beside it: given
                 a column of its own it would be blank on every row but one. -->
            <div
              class="relative ml-3 size-10 shrink-0 overflow-hidden rounded-control bg-surface-2"
            >
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
            <!-- Wide enough for columns, so the artist and the album take one
                 each rather than sharing a second line under the title. -->
            <button
              class="min-w-0 flex-1 px-3 text-left"
              :class="asTable ? TRACK_COLUMNS : ''"
              @click="queueTrack((row.data as Track).src, QueueMode.Now)"
            >
              <span
                class="min-w-0 truncate text-sm/tight"
                :class="{ 'text-accent': row.data.src === player.track?.src }"
              >
                <!-- The number is where a track sits, not what it is called, so
                     it stays out of the way of the name it is read with. -->
                <span
                  v-if="row.data.track_no"
                  class="mr-1.5 text-2xs tabular-nums text-outline"
                >
                  {{ row.data.track_no }}
                </span>{{ trackLabel(row.data.title, row.data.src) }}
              </span>

              <template v-if="asTable">
                <span class="min-w-0 truncate text-xs text-ink-soft">
                  {{ row.data.artist || $t('common.unknown.artist') }}
                </span>
                <span class="min-w-0 truncate text-xs text-outline">
                  {{ row.data.album || $t('common.unknown.album') }}
                </span>
              </template>

              <span v-else class="mt-0.5 block truncate text-2xs/tight text-outline">
                {{ row.data.artist || $t('common.unknown.artist') }} &middot;
                {{ row.data.album || $t('common.unknown.album') }}
              </span>
            </button>
            <span
              class="text-2xs tabular-nums text-outline"
              :class="{ [LENGTH_COLUMN]: asTable }"
            >
              {{ formatDuration(row.data.duration_ms) }}
            </span>
            <QueueMenu
              :label="$t('library.action.more', { title: row.data.title })"
              :ref="(el) => (rowMenus[row.index] = el as InstanceType<typeof QueueMenu>)"
              @select="(mode) => queueTrack((row.data as Track).src, mode)"
            />
          </template>
        </div>
      </div>
    </div>

    <p
      v-if="library.error"
      class="border-t border-surface-2 bg-rose-500/10 p-3 text-center text-xs text-rose-500"
      role="alert"
    >
      {{ $t('library.failed', { reason: library.error }) }}
    </p>

    <EmptyState
      v-else-if="!library.loading && library.total === 0"
      :icon="library.query === '' ? IconLibrary : IconNoResults"
      :title="
        library.query === ''
          ? $t('library.empty')
          : $t('library.search.empty', { query: library.query })
      "
      :hint="hintKey ? $t(hintKey) : undefined"
    />

    <p
      v-if="library.loading"
      class="shrink-0 p-2 text-center text-2xs text-outline"
      role="status"
    >
      {{ $t('common.state.loading') }}
    </p>
  </div>
</template>
