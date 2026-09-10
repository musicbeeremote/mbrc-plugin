/**
 * Transport on the keyboard.
 *
 * A remote open on a desktop is competing with the player it controls, where
 * space and the arrow keys have meant this since Winamp. Bound on the document
 * rather than a focused element, because the thing being controlled is the
 * whole app rather than whatever the last click landed on.
 *
 * A key pressed while typing is text: the handler stands down inside an input,
 * a textarea and anything contenteditable. It also stands down for a modified
 * key, which belongs to the browser.
 */

import { onBeforeUnmount, onMounted } from 'vue'

/** How much one arrow press moves the volume. */
const VOLUME_STEP = 5

export interface Transport {
  playPause: () => void
  next: () => void
  previous: () => void
  volume: number
  setVolume: (value: number) => void
  toggleMute: () => void
}

function isTyping(target: EventTarget | null): boolean {
  const el = target as HTMLElement | null
  if (!el) return false
  const tag = el.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable
}

export function useShortcuts(transport: () => Transport): void {
  function onKeydown(event: KeyboardEvent) {
    if (event.ctrlKey || event.metaKey || event.altKey || isTyping(event.target)) return

    const t = transport()
    const step = (delta: number) =>
      t.setVolume(Math.min(Math.max(t.volume + delta, 0), 100))

    switch (event.key) {
      case ' ': {
        t.playPause()
        break
      }
      case 'ArrowRight': {
        t.next()
        break
      }
      case 'ArrowLeft': {
        t.previous()
        break
      }
      case 'ArrowUp': {
        step(VOLUME_STEP)
        break
      }
      case 'ArrowDown': {
        step(-VOLUME_STEP)
        break
      }
      case 'm':
      case 'M': {
        t.toggleMute()
        break
      }
      default: {
        return
      }
    }
    // Only once a key is known to be ours: space scrolls and the arrows move a
    // list, and taking those from a key we did not handle would break the page.
    event.preventDefault()
  }

  onMounted(() => document.addEventListener('keydown', onKeydown))
  onBeforeUnmount(() => document.removeEventListener('keydown', onKeydown))
}
