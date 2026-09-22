<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import IconPlaylists from '~icons/lucide/list'
import IconPlus from '~icons/lucide/plus'

import { OpError } from '../api/parse'
import { playlistLabel, playlistSegments } from '../composables/playlistFolders'
import { playlistTracksRoute } from '../router/locations'
import { useLibraryStore } from '../stores/library'
import { usePlaylistStore } from '../stores/playlist'
import { usePlaylistPicker } from '../stores/playlistPicker'

import PanelSheet from './PanelSheet.vue'

const { t } = useI18n()
const router = useRouter()
const picker = usePlaylistPicker()
const library = useLibraryStore()
const playlist = usePlaylistStore()

/** Characters Windows refuses in a file name, which is what a playlist name becomes. */
const RESERVED = /[<>:"/\\|?*]/u

/** How long the result stays on screen before the sheet closes itself. */
const DONE_MS = 1200

const name = ref('')
const busy = ref(false)
const failure = ref('')
const done = ref('')
let closing: ReturnType<typeof setTimeout> | undefined = undefined

const creating = computed(() => picker.source === null)
const title = computed(() => {
  if (creating.value) return t('playlists.pick.newTitle')
  if (picker.source && 'now_playing' in picker.source) return t('playlists.pick.saveQueueTitle')
  return t('playlists.pick.title')
})

/** Only lists that can take tracks: an auto playlist is a rule, not a list. */
const targets = computed(() => library.playlists.filter((entry) => entry.editable))

const trimmed = computed(() => name.value.trim())
const nameProblem = computed(() =>
  RESERVED.test(trimmed.value) ? t('playlists.pick.reserved') : '',
)
const canCreate = computed(() => trimmed.value !== '' && nameProblem.value === '' && !busy.value)

// A sheet opened again starts clean: a result or an error from the last time
// would read as the answer to what is being asked now.
watch(
  () => picker.open,
  (open) => {
    if (!open) return
    clearTimeout(closing)
    name.value = ''
    failure.value = ''
    done.value = ''
    if (library.playlists.length === 0) void library.loadPlaylists()
  },
)

function finish(message: string) {
  done.value = message
  closing = setTimeout(() => picker.hide(), DONE_MS)
}

async function run(action: () => Promise<void>) {
  busy.value = true
  failure.value = ''
  try {
    await action()
  } catch (error) {
    failure.value = error instanceof OpError ? error.message : String(error)
  } finally {
    busy.value = false
  }
}

function addTo(url: string, label: string) {
  const { source } = picker
  if (!source) return
  void run(async () => {
    const added = await playlist.addTo(url, source)
    finish(t('playlists.pick.added', { count: added, name: label }, added))
  })
}

/**
 * Creates the playlist, holding the tracks the sheet was opened with.
 *
 * Opened only to create one, it opens the new playlist too: an empty playlist
 * made and then left unseen is one the next step is to go and find.
 */
function create() {
  if (!canCreate.value) return
  const label = trimmed.value
  const { folder } = picker
  void run(async () => {
    const url = await playlist.create(label, {
      folder,
      ...(picker.source ? { source: picker.source } : {}),
    })
    if (creating.value) {
      picker.hide()
      await router.push(playlistTracksRoute(url, playlistSegments(folder)))
      return
    }
    finish(t('playlists.pick.created', { name: label }))
  })
}

function folderOf(entryName: string): string {
  return playlistSegments(entryName).slice(0, -1).join(' / ')
}
</script>

<template>
  <PanelSheet :open="picker.open" :title="title" @close="picker.hide()">
    <p v-if="done" class="py-6 text-center text-sm" role="status">{{ done }}</p>

    <template v-else>
      <form class="flex items-center gap-2" @submit.prevent="create">
        <input
          v-model="name"
          type="text"
          class="min-w-0 flex-1 rounded-control bg-surface-2 px-3 py-2 text-sm outline-none placeholder:text-outline"
          :placeholder="$t('playlists.pick.namePlaceholder')"
          :aria-label="$t('playlists.pick.namePlaceholder')"
          :aria-invalid="nameProblem !== ''"
        />
        <button
          type="submit"
          class="flex items-center gap-1 rounded-full bg-accent px-3 py-2 text-sm text-white transition-opacity disabled:opacity-40"
          :disabled="!canCreate"
        >
          <IconPlus class="size-4" />
          {{ $t('playlists.pick.create') }}
        </button>
      </form>
      <p v-if="nameProblem" class="mt-1 text-2xs text-rose-500">{{ nameProblem }}</p>
      <p v-if="failure" class="mt-2 text-xs text-rose-500" role="alert">
        {{ $t('playlists.pick.failed', { reason: failure }) }}
      </p>

      <template v-if="!creating">
        <h3 class="mt-4 mb-1 text-2xs font-medium tracking-wide text-outline uppercase">
          {{ $t('playlists.pick.existing') }}
        </h3>
        <p v-if="targets.length === 0 && !library.loading" class="py-3 text-sm text-outline">
          {{ $t('playlists.pick.none') }}
        </p>
        <ul>
          <li v-for="entry in targets" :key="entry.url">
            <button
              class="flex w-full items-center gap-3 rounded-control px-2 py-2.5 text-left transition-colors hover:bg-surface-2/60 disabled:opacity-40"
              :disabled="busy"
              @click="addTo(entry.url, playlistLabel(entry))"
            >
              <IconPlaylists class="size-4 shrink-0 text-outline" />
              <span class="min-w-0 flex-1 truncate text-sm">{{ playlistLabel(entry) }}</span>
              <span v-if="folderOf(entry.name)" class="truncate text-2xs text-outline">
                {{ folderOf(entry.name) }}
              </span>
            </button>
          </li>
        </ul>
      </template>
    </template>
  </PanelSheet>
</template>
