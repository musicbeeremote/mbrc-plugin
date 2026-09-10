/**
 * Drives the accent custom properties from whatever cover is on screen.
 *
 * The covers are served from this same origin, so the canvas they are drawn to
 * stays readable; a cross-origin one would throw on read, which is the one
 * failure this cannot avoid and simply falls back from.
 */

import type { Ref } from 'vue'
import { onUnmounted, watch } from 'vue'

import type { Accent } from './coverAccent'
import { SAMPLE_SIZE, accentFromPixels } from './coverAccent'

const HUE_PROPERTY = '--mbrc-accent-h'
const CHROMA_PROPERTY = '--mbrc-accent-c'

/** The lightness a borrowed hue renders at, which the theme owns and the art
 *  never does: a hue the art chose still has to be legible on this surface. */
const LIGHTNESS_PROPERTY = '--mbrc-accent-l'

/** How much of the art's hue the surfaces themselves take on. */
const TINT_PROPERTY = '--mbrc-tint-c'

function apply(accent: Accent | null): void {
  const root = document.documentElement
  if (!accent) {
    // Removing rather than writing amber back: the default lives in one place,
    // in the stylesheet, and this way it cannot drift from it.
    root.style.removeProperty(HUE_PROPERTY)
    root.style.removeProperty(CHROMA_PROPERTY)
    root.style.removeProperty(LIGHTNESS_PROPERTY)
    root.style.removeProperty(TINT_PROPERTY)
    return
  }
  root.style.setProperty(HUE_PROPERTY, accent.hue.toFixed(1))
  root.style.setProperty(CHROMA_PROPERTY, accent.chroma.toFixed(3))
  root.style.setProperty(LIGHTNESS_PROPERTY, 'var(--mbrc-accent-art-l)')
  root.style.setProperty(TINT_PROPERTY, 'var(--mbrc-tint-art-c)')
}

function sample(image: HTMLImageElement): Accent | null {
  const canvas = document.createElement('canvas')
  canvas.width = SAMPLE_SIZE
  canvas.height = SAMPLE_SIZE
  const context = canvas.getContext('2d', { willReadFrequently: false })
  if (!context) return null
  try {
    context.drawImage(image, 0, 0, SAMPLE_SIZE, SAMPLE_SIZE)
    return accentFromPixels(context.getImageData(0, 0, SAMPLE_SIZE, SAMPLE_SIZE).data)
  } catch {
    return null
  }
}

export function useCoverAccent(cover: Ref<string | null>): void {
  // Bumped on every change so a slow load that resolves after the track already
  // moved on cannot paint the app the previous cover's colour.
  let generation = 0

  watch(
    cover,
    (url) => {
      generation += 1
      const current = generation
      if (!url) {
        apply(null)
        return
      }
      const image = new Image()
      image.decoding = 'async'
      image.addEventListener('load', () => {
        if (generation === current) apply(sample(image))
      })
      image.addEventListener('error', () => {
        if (generation === current) apply(null)
      })
      image.src = url
    },
    { immediate: true },
  )

  onUnmounted(() => apply(null))
}
