/**
 * Light, dark, or whatever the system says.
 *
 * Three states rather than a switch: a reader who has not chosen should follow
 * the machine, including when the machine changes its mind at sunset, and that
 * is not something a two-way toggle can express. The choice is written to the
 * root element, which the stylesheet reads in both directions, so an explicit
 * light beats a dark system and not only the other way round.
 *
 * It is remembered per browser, which is the right scope: the same library seen
 * from a phone at night and a desktop by day is two readers as far as this is
 * concerned.
 */

import type { Ref } from 'vue'
import { onMounted, ref } from 'vue'

export const Theme = {
  Light: 'light',
  Dark: 'dark',
  Auto: 'auto',
} as const
export type Theme = (typeof Theme)[keyof typeof Theme]

const STORAGE_KEY = 'mbrc.theme'

/** The order the control cycles in: away from the system, then back to it. */
const ORDER: Theme[] = [Theme.Auto, Theme.Light, Theme.Dark]

function isTheme(value: unknown): value is Theme {
  return ORDER.includes(value as Theme)
}

/** What was chosen last, or Auto when nothing was, or storage is unreadable. */
export function storedTheme(): Theme {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    return isTheme(saved) ? saved : Theme.Auto
  } catch {
    return Theme.Auto
  }
}

/** The theme after this one in the cycle. */
export function nextTheme(current: Theme): Theme {
  return ORDER[(ORDER.indexOf(current) + 1) % ORDER.length] as Theme
}

/**
 * Writes the choice to the root, removing it for Auto.
 *
 * Removed rather than set to "auto": the stylesheet answers the system only
 * when nothing is stamped, so an attribute it does not know would pin the app
 * to light forever.
 */
export function applyTheme(theme: Theme): void {
  const root = document.documentElement
  if (theme === Theme.Auto) {
    delete root.dataset.theme
    return
  }
  root.dataset.theme = theme
}

export interface ThemeControl {
  theme: Ref<Theme>
  set: (theme: Theme) => void
  cycle: () => void
}

export function useTheme(): ThemeControl {
  const theme = ref<Theme>(Theme.Auto)

  function set(next: Theme): void {
    theme.value = next
    applyTheme(next)
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // A browser that will not remember it still honours it for this session.
    }
  }

  onMounted(() => set(storedTheme()))

  return { theme, set, cycle: () => set(nextTheme(theme.value)) }
}
