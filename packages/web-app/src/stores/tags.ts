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

  /** Why the last read failed, shown with a retry; null when it did not. */
  const failure = ref<string | null>(null)

  /**
   * The read the editor shows. A track change, a tag change and a refused
   * write can each start one while another is under way, and only the latest
   * may land: an older answer would put the previous track back in the editor.
   */
  let latestLoad = 0

  async function load() {
    latestLoad += 1
    const mine = latestLoad
    try {
      if (fields.value.length === 0) {
        const listed = await client.call(Op.TagFields)
        if (mine !== latestLoad) return
        fields.value = listed.fields
      }
      const read = await client.call(Op.NowPlayingTags, {})
      if (mine !== latestLoad) return
      path.value = read.path
      values.value = read.tags
      failure.value = null
    } catch (error) {
      if (mine !== latestLoad) return
      failure.value = error instanceof Error ? error.message : String(error)
    }
    loaded.value = true
  }

  /** Forgets what was shown, so nothing is editable until the next read lands. */
  function reload() {
    loaded.value = false
    void load()
  }

  /** Suggestions are a help, not the field: a failed read leaves them out. */
  async function loadSuggestions(key: string) {
    if (suggestions.value[key]) return
    const counted = await client.call(Op.TagValues, { key }).catch(() => null)
    if (counted) suggestions.value = { ...suggestions.value, [key]: counted.values }
  }

  /**
   * Writes one field and keeps what MusicBee read back, which is what it
   * stored rather than what was sent.
   *
   * A track that moved on while the editor was open is refused by the server
   * and reloaded here, so the editor shows the song now playing rather than
   * offering to save into it. Resolves to whether the value was written.
   */
  async function save(key: string, value: TagValue): Promise<boolean> {
    if (path.value === null) return false
    saving.value = key
    refusal.value = null
    try {
      const written = await client.call(Op.NowPlayingSetTag, { path: path.value, key, value })
      values.value = { ...values.value, [key]: written.value }
      const { [key]: _stale, ...rest } = suggestions.value
      suggestions.value = rest
      return true
    } catch (error) {
      const code = error instanceof OpError ? error.code : 'unreachable'
      const message = error instanceof Error ? error.message : String(error)
      refusal.value = { key, code, message }
      await load()
      return false
    } finally {
      saving.value = null
    }
  }

  function show() {
    open.value = true
    refusal.value = null
    reload()
  }

  function hide() {
    open.value = false
  }

  function bind() {
    client.on(WireEvent.NowPlayingTagsChanged, () => {
      if (open.value) void load()
    })
    client.on(WireEvent.NowPlayingChanged, () => {
      if (open.value) reload()
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
    failure,
    lockReason,
    canEdit,
    load,
    reload,
    loadSuggestions,
    save,
    show,
    hide,
    bind,
  }
})
