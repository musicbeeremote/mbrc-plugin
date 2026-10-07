<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import IconTags from '~icons/lucide/tags'
import IconX from '~icons/lucide/x'

import { ErrorCode } from '../api/types'
import type { TagField } from '../api/tags'
import { useTagsStore } from '../stores/tags'

import EmptyState from './EmptyState.vue'

/**
 * The playing track's tags, editable one field at a time.
 *
 * A field saves when it is left or Enter is pressed, rather than behind a Save
 * button for the whole sheet: each write is checked against the track it was
 * opened on, and one field refused is easier to show than a form half saved.
 * A multi-value field is a row of chips, because a value with a `;` in a text
 * box is a separator the user has to know about.
 */
const { t } = useI18n()
const tags = useTagsStore()

const editable = computed(() => tags.canEdit)
const showAll = ref(false)

/** Single-value text as typed, and the chip being typed per multi-value field. */
const drafts = ref<Record<string, string>>({})
const adding = ref<Record<string, string>>({})

watch(
  () => tags.values,
  (values) => {
    drafts.value = Object.fromEntries(
      Object.entries(values).map(([key, value]) => [key, typeof value === 'string' ? value : '']),
    )
    adding.value = {}
  },
  { immediate: true },
)

function hasValue(key: string): boolean {
  const value = tags.values[key]
  return Array.isArray(value) ? value.length > 0 : Boolean(value)
}

const UNNAMED_CUSTOM = /^Custom\d+$/u

/**
 * The fields worth a row: every built-in one, and a custom field once it has a
 * name or a value. Sixteen empty "CustomN" rows bury the ones in use.
 */
const visibleFields = computed(() =>
  tags.fields.filter(
    (field) =>
      showAll.value ||
      !field.key.startsWith('custom') ||
      !UNNAMED_CUSTOM.test(field.name) ||
      hasValue(field.key),
  ),
)

const hiddenCount = computed(() => tags.fields.length - visibleFields.value.length)

function items(key: string): string[] {
  const value = tags.values[key]
  return Array.isArray(value) ? value : []
}

function commitText(field: TagField) {
  const text = drafts.value[field.key] ?? ''
  if (text === (tags.values[field.key] ?? '')) return
  void tags.save(field.key, text)
}

function addItem(field: TagField) {
  const text = (adding.value[field.key] ?? '').trim()
  if (tags.saving !== null || !text || items(field.key).includes(text)) return
  void tags.save(field.key, [...items(field.key), text])
}

function removeItem(field: TagField, item: string) {
  void tags.save(
    field.key,
    items(field.key).filter((existing) => existing !== item),
  )
}

/** The refusal under a field, in words the user can act on. */
function refusalFor(key: string): string | null {
  const { refusal } = tags
  if (refusal?.key !== key) return null
  return refusal.code === ErrorCode.StaleTrack ? t('player.tags.stale') : refusal.message
}

const readOnlyNote = computed(() => {
  if (tags.lockReason === null) return null
  return tags.lockReason === 'party' ? t('player.tags.hostOnly') : t('player.tags.readOnly')
})
</script>

<template>
  <p v-if="!tags.loaded" class="py-6 text-center text-2xs text-outline" role="status">
    {{ $t('player.tags.loading') }}
  </p>

  <EmptyState
    v-else-if="tags.path === null"
    :icon="IconTags"
    :title="$t('player.nothingPlaying')"
  />

  <div v-else class="space-y-4 py-1">
    <p v-if="readOnlyNote" class="rounded-control bg-surface-2 px-3 py-2 text-2xs text-ink-soft" role="note">
      {{ readOnlyNote }}
    </p>

    <div v-for="field in visibleFields" :key="field.key" class="space-y-1">
      <div class="flex items-baseline justify-between gap-2">
        <label :for="`tag-${field.key}`" class="text-2xs font-medium text-outline">
          {{ field.name || field.key }}
        </label>
        <span v-if="tags.saving === field.key" class="text-2xs text-accent" role="status">
          {{ $t('player.tags.saving') }}
        </span>
      </div>

      <template v-if="field.multi_value">
        <ul class="flex flex-wrap gap-1.5">
          <li
            v-for="item in items(field.key)"
            :key="item"
            class="flex items-center gap-1 rounded-full bg-surface-2 py-1 pr-1.5 pl-3 text-sm"
          >
            {{ item }}
            <button
              v-if="editable"
              class="rounded-full p-0.5 text-outline transition-colors hover:text-ink"
              :aria-label="$t('player.tags.remove', { value: item, field: field.name })"
              :disabled="tags.saving !== null"
              @click="removeItem(field, item)"
            >
              <IconX class="size-3.5" />
            </button>
          </li>
          <li v-if="!editable && items(field.key).length === 0" class="text-sm text-outline">-</li>
        </ul>
        <input
          v-if="editable"
          :id="`tag-${field.key}`"
          v-model="adding[field.key]"
          type="text"
          :list="`tag-suggest-${field.key}`"
          class="w-full rounded-control border border-surface-2 bg-transparent px-3 py-1.5 text-sm placeholder:text-outline"
          :placeholder="$t('player.tags.add', { field: field.name })"
          :disabled="tags.saving !== null"
          @focus="tags.loadSuggestions(field.key)"
          @keydown.enter.prevent="addItem(field)"
          @change="addItem(field)"
        />
        <datalist :id="`tag-suggest-${field.key}`">
          <option
            v-for="suggestion in tags.suggestions[field.key] ?? []"
            :key="suggestion.value"
            :value="suggestion.value"
          >
            {{ $t('player.tags.uses', suggestion.count) }}
          </option>
        </datalist>
      </template>

      <template v-else>
        <input
          v-if="editable"
          :id="`tag-${field.key}`"
          v-model="drafts[field.key]"
          type="text"
          class="w-full rounded-control border border-surface-2 bg-transparent px-3 py-1.5 text-sm"
          :disabled="tags.saving !== null && tags.saving !== field.key"
          @change="commitText(field)"
          @keydown.enter.prevent="($event.target as HTMLInputElement).blur()"
        />
        <p v-else :id="`tag-${field.key}`" class="text-sm break-words text-ink-soft">
          {{ tags.values[field.key] || '-' }}
        </p>
      </template>

      <p v-if="refusalFor(field.key)" class="text-2xs text-rose-500" role="alert">
        {{ refusalFor(field.key) }}
      </p>
    </div>

    <button
      v-if="hiddenCount > 0 || showAll"
      class="text-2xs text-outline transition-colors hover:text-ink"
      @click="showAll = !showAll"
    >
      {{ showAll ? $t('player.tags.showFewer') : $t('player.tags.showAll', { count: hiddenCount }) }}
    </button>
  </div>
</template>
