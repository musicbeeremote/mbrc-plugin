<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'

import { coverUrl } from '../api/display'
import type { Track } from '../api/types'
import type { ArtistPart } from '../composables/artistParts'
import { artistParts, artistPicture } from '../composables/artistParts'
import { LibraryLevel } from '../composables/libraryLevels'
import { libraryRoute } from '../router/locations'
import { useArtistImagesStore } from '../stores/artistImages'

/**
 * A track's artists, each a link to that artist's albums.
 *
 * A split track names its artists, then its guests after "feat."; a track
 * nobody split is the display string MusicBee shows, as one link. A stream's
 * artist is the song on air, which the library does not hold, so it stays text.
 *
 * With `pictures`, each name the server has a picture for leads with it.
 */
const props = defineProps<{ track: Track | null | undefined; pictures?: boolean }>()
const { t } = useI18n()
const artistImages = useArtistImagesStore()

function pictureOf(name: string): string | null {
  if (!props.pictures) return null
  return coverUrl(artistPicture(props.track, name, artistImages.hashFor))
}

const parts = computed(() => artistParts(props.track))
const isStream = computed(() => props.track?.src.includes('://') ?? false)

/** An interpolated string, since the template compiler trims the spaces of literal text. */
function joinerText(joiner: ArtistPart['joiner']): string {
  if (joiner === 'feat') return ` ${t('player.featuring')} `
  if (joiner === 'comma') return ', '
  return ''
}

function albumsOf(name: string) {
  return libraryRoute({ level: LibraryLevel.Albums, scope: { artist: name } })
}
</script>

<template>
  <span v-if="isStream || parts.length === 0">{{ track?.artist_display }}</span>
  <span v-else>
    <template v-for="(part, i) in parts" :key="`${i}:${part.name}`">
      {{ joinerText(part.joiner) }}<RouterLink
        :to="albumsOf(part.name)"
        class="hover:text-ink hover:underline focus-visible:underline"
        ><img
          v-if="pictureOf(part.name)"
          :src="pictureOf(part.name) ?? undefined"
          alt=""
          class="mr-1 inline-block size-5 rounded-full object-cover align-[-0.3em]"
        />{{ part.label }}</RouterLink
      >
    </template>
  </span>
</template>
