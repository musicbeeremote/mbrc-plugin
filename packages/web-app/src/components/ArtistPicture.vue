<script setup lang="ts">
/**
 * An artist's stored picture, or the slot when there is none to show: no hash,
 * or a file that failed to load, which the store then stops offering.
 */
import { computed } from 'vue'

import { coverUrl } from '../api/display'
import { useArtistImagesStore } from '../stores/artistImages'

const props = defineProps<{ hash: string | undefined; imageClass: string }>()
const artistImages = useArtistImagesStore()

const url = computed(() =>
  props.hash !== undefined && !artistImages.isBroken(props.hash) ? coverUrl(props.hash) : null,
)

function onError(): void {
  if (props.hash !== undefined) artistImages.markBroken(props.hash)
}
</script>

<template>
  <img v-if="url" :src="url" alt="" loading="lazy" :class="imageClass" @error="onError" />
  <slot v-else />
</template>
