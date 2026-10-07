import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { Op, WireEvent } from '../api/ops'
import { OpError } from '../api/parse'
import { Capability } from '../api/permissions'
import type { TagField, TagValue, TagValueCount } from '../api/tags'

import { usePermissionsStore } from './permissions'

/** Why the last save of a field did not land, by the server's own code. */
export interface TagRefusal {
  key: string
  code: string
  message: string
}

/**
 * The playing track's tags, for the editor.
 *
 * The field list is the same for every track, so it is read once per session.
 * Values are read only while the editor is open, and a field's suggestions only
 * once someone starts editing it: `tag_values` counts a field across the whole
 * library, which is not worth doing for a field nobody touches.
 */
export const useTagsStore = defineStore('tags', () => {
  const fields = ref<TagField[]>([])
  const path = ref<string | null>(null)
  const values = ref<Record<string, TagValue>>({})
  const suggestions = ref<Record<string, TagValueCount[]>>({})
  const saving = ref<string | null>(null)
  const refusal = ref<TagRefusal | null>(null)
  const open = ref(false)
  /** Until the first read lands, which is not the same as nothing playing. */
  const loaded = ref(false)

  const permissions = usePermissionsStore()

  /**
   * Why this browser may read tags but not change them, or null when it may.
   *
   * Party Mode keeps tag edits for the host, so a guest is told who can rather
   * than shown a field that does nothing.
   */
  const lockReason = computed<'party' | 'denied' | null>(() => {
    if (permissions.can(Capability.LibraryEdit)) return null
    return permissions.partyMode ? 'party' : 'denied'
  })

  const canEdit = computed(() => lockReason.value === null && path.value !== null)

  async function load() {
    if (fields.value.length === 0) {
      const listed = await client.call(Op.TagFields)
      fields.value = listed.fields
    }
    const read = await client.call(Op.NowPlayingTags, {})
    path.value = read.path
    values.value = read.tags
    loaded.value = true
  }

  async function loadSuggestions(key: string) {
    if (suggestions.value[key]) return
    const counted = await client.call(Op.TagValues, { key })
    suggestions.value = { ...suggestions.value, [key]: counted.values }
  }

  /**
   * Writes one field and keeps what MusicBee read back, which is what it
   * stored rather than what was sent.
   *
   * A track that moved on while the editor was open is refused by the server
   * and reloaded here, so the editor shows the song now playing rather than
   * offering to save into it.
   */
  async function save(key: string, value: TagValue) {
    if (path.value === null) return
    saving.value = key
    refusal.value = null
    try {
      const written = await client.call(Op.NowPlayingSetTag, { path: path.value, key, value })
      values.value = { ...values.value, [key]: written.value }
      const { [key]: _stale, ...rest } = suggestions.value
      suggestions.value = rest
    } catch (error) {
      if (!(error instanceof OpError)) throw error
      refusal.value = { key, code: error.code, message: error.message }
      await load()
    } finally {
      saving.value = null
    }
  }

  function show() {
    open.value = true
    refusal.value = null
    void load()
  }

  function hide() {
    open.value = false
  }

  function bind() {
    client.on(WireEvent.NowPlayingTagsChanged, () => {
      if (open.value) void load()
    })
    client.on(WireEvent.NowPlayingChanged, () => {
      if (open.value) void load()
    })
  }

  return {
    fields,
    path,
    values,
    suggestions,
    saving,
    refusal,
    open,
    loaded,
    lockReason,
    canEdit,
    load,
    loadSuggestions,
    save,
    show,
    hide,
    bind,
  }
})
