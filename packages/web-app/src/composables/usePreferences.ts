/**
 * The app's own settings, remembered per browser as the theme is.
 *
 * A store rather than a bare composable so every reader sees one value: the
 * media store, the navigation and the settings menu all react to the same
 * switch. A browser that cannot store anything still honours a change for the
 * rest of its session and opens with the defaults next time.
 */

import { defineStore } from 'pinia'
import { ref } from 'vue'

const SHOW_MEDIA_KEY = 'mbrc.showMedia'

/**
 * Whether audiobooks, videos and the inbox are shown. Off unless turned on.
 *
 * Off by default because most libraries file nothing there, and asking the
 * server for those lists costs a scan of the library on every change.
 */
export function storedShowMedia(): boolean {
  try {
    return localStorage.getItem(SHOW_MEDIA_KEY) === '1'
  } catch {
    return false
  }
}

export const usePreferences = defineStore('preferences', () => {
  const showMedia = ref(storedShowMedia())

  function setShowMedia(on: boolean): void {
    showMedia.value = on
    try {
      localStorage.setItem(SHOW_MEDIA_KEY, on ? '1' : '0')
    } catch {
      // Not remembered, but still in force for this session.
    }
  }

  return { showMedia, setShowMedia }
})
