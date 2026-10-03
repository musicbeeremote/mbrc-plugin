<script setup lang="ts">
/**
 * What stands in for a missing cover: the name's initials on a colour of its
 * own, so a page of untagged singles is not a column of identical notes.
 *
 * Fills its parent, which must be positioned. A name with no letters or digits
 * gets the plain note instead.
 */
import { computed } from 'vue'
import IconMusic from '~icons/lucide/music'

import { placeholderHue, placeholderInitials } from '../api/display'

const props = defineProps<{ name: string; large?: boolean }>()

const initials = computed(() => placeholderInitials(props.name))
const hue = computed(() => placeholderHue(props.name))
</script>

<template>
  <div
    v-if="initials"
    class="cover-placeholder absolute inset-0 grid place-items-center font-semibold select-none"
    :class="large ? 'text-2xl' : 'text-xs'"
    :style="{ '--placeholder-h': hue }"
    aria-hidden="true"
  >
    {{ initials }}
  </div>
  <IconMusic
    v-else
    class="absolute inset-0 m-auto text-outline opacity-40"
    :class="large ? 'size-1/3' : 'size-1/2'"
  />
</template>
