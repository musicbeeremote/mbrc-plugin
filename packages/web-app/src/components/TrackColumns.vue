<script lang="ts">
/**
 * The column template, shared with the rows.
 *
 * Stated once because a heading that does not sit over its own column is worse
 * than no heading: the two were written apart and drifted by eighty pixels.
 *
 * The three share what is spare in proportion. Capping the artist and the album
 * instead sent every pixel past a certain width into the title, which on a full
 * screen left a title against a lake of nothing and the other two crushed
 * against the right edge.
 */
export const TRACK_COLUMNS =
  'grid grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)] items-center gap-3'

/** The cover, the length and the menu, which the headings only reserve. */
export const COVER_COLUMN = 'w-13 shrink-0'
export const LENGTH_COLUMN = 'w-16 shrink-0 text-right'
export const MENU_COLUMN = 'w-8 shrink-0'
</script>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { scrollbarWidth } from '../composables/scrollbarWidth'

/**
 * The headings of the wide track list.
 *
 * A heading you can sort by is what makes a table a table rather than rows with
 * more columns in them, so each one is the control for its own order and the
 * one in use says which way it runs.
 *
 * Length has no heading of its own: the protocol cannot order by it, and a
 * heading that does nothing when pressed is worse than a label.
 */
const props = defineProps<{ active: string; descending: boolean }>()
const emit = defineEmits<{ sort: [field: string] }>()

const { t } = useI18n()

const COLUMNS = ['title', 'artist', 'album'] as const

/** The rows scroll and these do not, so they leave the same width for it. */
const gutter = ref(0)
onMounted(() => (gutter.value = scrollbarWidth()))

function label(field: string): string {
  const name = t(`library.sort.${field}`)
  if (props.active !== field) return name
  return `${name} ${props.descending ? '↓' : '↑'}`
}
</script>

<template>
  <!-- No padding of its own: the cover column already spans the margin the rows
       put before their artwork, and the inner padding matches theirs. -->
  <div
    class="flex items-center border-b border-surface-2 py-1.5 text-2xs text-outline"
    :style="{ paddingRight: `${gutter}px` }"
  >
    <span :class="COVER_COLUMN"></span>
    <div class="min-w-0 flex-1 px-3" :class="TRACK_COLUMNS">
      <span v-for="field in COLUMNS" :key="field" class="min-w-0 truncate">
        <button
          class="transition-colors hover:text-ink"
          :class="{ 'text-ink': active === field }"
          @click="emit('sort', field)"
        >
          {{ label(field) }}
        </button>
      </span>
    </div>
    <span :class="LENGTH_COLUMN">{{ $t('library.sort.duration') }}</span>
    <span :class="MENU_COLUMN"></span>
  </div>
</template>
