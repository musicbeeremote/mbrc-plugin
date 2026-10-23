<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'

import type { Track } from '../api/types'
import type { ArtistPart } from '../composables/artistParts'
import { artistParts } from '../composables/artistParts'
import { LibraryLevel } from '../composables/libraryLevels'
import { libraryRoute } from '../router/locations'

/**
 * A track's artists, each a link to that artist's albums.
 *
 * A split track names its artists, then its guests after "feat."; a track
 * nobody split is the display string MusicBee shows, as one link. A stream's
 * artist is the song on air, which the library does not hold, so it stays text.
 */
const props = defineProps<{ track: Track | null | undefined }>()
const { t } = useI18n()

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
    <template v-for="part in parts" :key="part.name">
      {{ joinerText(part.joiner) }}<RouterLink
        :to="albumsOf(part.name)"
        class="hover:text-ink hover:underline focus-visible:underline"
        >{{ part.label }}</RouterLink
      >
    </template>
  </span>
</template>
