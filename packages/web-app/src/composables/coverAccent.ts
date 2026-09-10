/**
 * The accent colour, taken from the album art.
 *
 * The art supplies a hue and a chroma; `style.css` owns the lightness per theme.
 * Splitting it that way is what stops a dark or washed-out cover from producing
 * an accent that cannot be read against either background.
 */

/** Below this the art has no colour worth borrowing, and amber stays. */
const MIN_CHROMA = 0.04

/** Chroma is clamped into a band: too little reads grey, too much vibrates. */
const CHROMA_FLOOR = 0.07
const CHROMA_CEILING = 0.19

/** Sampling grid. 24x24 is far more pixels than a hue average needs, and small
 *  enough that the draw and the read are imperceptible. */
export const SAMPLE_SIZE = 24

/** A hue in degrees and a chroma, or null when the art has no usable colour. */
export interface Accent {
  hue: number
  chroma: number
}

/** sRGB channel (0-255) to linear light. */
function linearize(channel: number): number {
  const c = channel / 255
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
}

/**
 * The a/b chromatic pair of a colour in OKLab.
 *
 * OKLab rather than HSL because hue averaging has to happen in a perceptually
 * even space: averaging HSL hues weights a dull blue the same as a vivid red,
 * and the result is a colour that appears in neither.
 */
function chromaticPair(r: number, g: number, b: number): [number, number] {
  const lr = linearize(r)
  const lg = linearize(g)
  const lb = linearize(b)

  const l = Math.cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb)
  const m = Math.cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb)
  const s = Math.cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb)

  return [
    1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s,
  ]
}

/**
 * Reduces sampled pixels to one accent.
 *
 * Each pixel is weighted by its own chroma, so a mostly-grey sleeve with one
 * saturated element takes that element's hue rather than the mud around it.
 * Averaging the a/b pair rather than the hue angle keeps that correct across the
 * 360-degree wrap, where a numeric mean of angles is not.
 */
export function accentFromPixels(pixels: Uint8ClampedArray): Accent | null {
  let sumA = 0
  let sumB = 0
  let weight = 0

  for (let i = 0; i + 3 < pixels.length; i += 4) {
    // Transparent pixels have no colour to contribute, only the value the
    // canvas happened to leave under them.
    if (pixels[i + 3] >= 128) {
      const [a, b] = chromaticPair(pixels[i], pixels[i + 1], pixels[i + 2])
      const chroma = Math.hypot(a, b)
      sumA += a * chroma
      sumB += b * chroma
      weight += chroma
    }
  }

  if (weight === 0) return null

  const a = sumA / weight
  const b = sumB / weight
  const chroma = Math.hypot(a, b)
  if (chroma < MIN_CHROMA) return null

  const hue = (Math.atan2(b, a) * 180) / Math.PI
  return {
    hue: (hue + 360) % 360,
    chroma: Math.min(Math.max(chroma, CHROMA_FLOOR), CHROMA_CEILING),
  }
}
