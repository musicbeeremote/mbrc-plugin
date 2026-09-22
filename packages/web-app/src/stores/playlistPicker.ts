/**
 * The one "add to playlist" sheet, opened from wherever tracks are listed.
 *
 * A store rather than a component per view: the library, the queue, a playlist
 * and now playing all open the same sheet, and each holding its own would
 * render four dialogs that can never be open together.
 */

import { defineStore } from 'pinia'
import { ref } from 'vue'

import type { PlaylistSource } from '../api/ops'

export const usePlaylistPicker = defineStore('playlistPicker', () => {
  const open = ref(false)
  /** The tracks to add, or nothing when the sheet only creates a playlist. */
  const source = ref<PlaylistSource | null>(null)
  /** Where a playlist created here is filed, relative to the playlists root. */
  const folder = ref('')

  function show(tracks: PlaylistSource | null, at = '') {
    source.value = tracks
    folder.value = at
    open.value = true
  }

  function hide() {
    open.value = false
  }

  return { open, source, folder, show, hide }
})
