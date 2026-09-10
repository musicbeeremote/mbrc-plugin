<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import IconBack from '~icons/lucide/chevron-left'
import IconFolder from '~icons/lucide/folder'
import IconPlaylists from '~icons/lucide/list'

import EmptyState from '../components/EmptyState.vue'
import { browsePlaylists, playlistLabel } from '../composables/playlistFolders'
import { playlistPathFromRoute, playlistsRoute } from '../router/locations'
import { useLibraryStore } from '../stores/library'

const route = useRoute()
const router = useRouter()
const library = useLibraryStore()

/** The folder being looked at, as segments. Empty is the playlists root. */
const path = computed(() => playlistPathFromRoute(route.query))

const here = computed(() => browsePlaylists(library.playlists, path.value))

const heading = computed(() => path.value.at(-1) ?? '')

onMounted(() => {
  void library.loadPlaylists()
})
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center gap-2 border-b border-surface-2 p-3">
      <button
        v-if="path.length > 0"
        class="-ml-1 rounded-control p-1 text-accent transition-colors"
        :aria-label="$t('common.action.back')"
        @click="router.push(playlistsRoute(path.slice(0, -1)))"
      >
        <IconBack class="size-5" />
      </button>
      <span class="truncate text-sm font-medium">
        {{ path.length === 0 ? $t('playlists.title') : heading }}
      </span>
      <!-- The trail, so a folder three deep still says where it is. -->
      <span v-if="path.length > 1" class="ml-auto truncate text-2xs text-outline">
        {{ path.slice(0, -1).join(' / ') }}
      </span>
    </div>

    <ul
      v-show="here.folders.length > 0 || here.playlists.length > 0"
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

      <li v-for="entry in here.playlists" :key="entry.url">
        <button
          class="flex w-full items-center gap-3 border-b border-surface-2 px-3 py-3 text-left transition-colors hover:bg-surface-2/40 active:bg-surface-2/70"
          @click="library.playPlaylist(entry.url)"
        >
          <IconPlaylists class="size-5 shrink-0 text-outline" />
          <span class="min-w-0 flex-1 truncate">{{ playlistLabel(entry) }}</span>
        </button>
      </li>
    </ul>

    <EmptyState
      v-if="!library.loading && here.folders.length === 0 && here.playlists.length === 0"
      :icon="IconPlaylists"
      :title="$t('playlists.empty')"
    />
  </div>
</template>
