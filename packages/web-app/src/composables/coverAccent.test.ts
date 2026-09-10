import { describe, expect, it } from 'vitest'

import { accentFromPixels } from './coverAccent'

/** One flat colour, repeated: the shape `getImageData` hands back. */
function flat(
  [r, g, b]: [number, number, number],
  { alpha = 255, count = 16 }: { alpha?: number; count?: number } = {},
): Uint8ClampedArray {
  const pixels = new Uint8ClampedArray(count * 4)
  for (let i = 0; i < count; i += 1) pixels.set([r, g, b, alpha], i * 4)
  return pixels
}

function mix(first: Uint8ClampedArray, second: Uint8ClampedArray): Uint8ClampedArray {
  const both = new Uint8ClampedArray(first.length + second.length)
  both.set(first)
  both.set(second, first.length)
  return both
}

describe('the accent taken from cover art', () => {
  // OKLCH hue angles for the sRGB primaries. Wide tolerances: the point is that
  // red art produces a red accent, not that the conversion hits a given decimal.
  it.each([
    ['red', flat([255, 0, 0]), 29],
    ['green', flat([0, 255, 0]), 142],
    ['blue', flat([0, 0, 255]), 264],
  ])('follows the hue of %s art', (_name, pixels, expected) => {
    const accent = accentFromPixels(pixels)
    expect(accent).not.toBeNull()
    expect(accent?.hue).toBeCloseTo(expected, -1)
  })

  // A sleeve that is mostly grey with one coloured element should take the
  // element's hue. Weighting each pixel by its own chroma is what does that;
  // a flat mean would return the grey.
  it('takes its hue from the coloured part of a mostly grey sleeve', () => {
    const accent = accentFromPixels(mix(flat([128, 128, 128], { alpha: 255, count: 60 }), flat([0, 0, 255], { alpha: 255, count: 4 })))
    expect(accent?.hue).toBeCloseTo(264, -1)
  })

  it('finds no accent in art with no colour in it', () => {
    expect(accentFromPixels(flat([128, 128, 128]))).toBeNull()
    expect(accentFromPixels(flat([255, 255, 255]))).toBeNull()
    expect(accentFromPixels(flat([0, 0, 0]))).toBeNull()
  })

  it('finds no accent when every pixel is transparent', () => {
    expect(accentFromPixels(flat([255, 0, 0], { alpha: 0 }))).toBeNull()
    expect(accentFromPixels(new Uint8ClampedArray(0))).toBeNull()
  })

  // Unclamped, a vivid sleeve gives an accent that vibrates against the
  // background and a faint one gives an accent indistinguishable from grey.
  it('keeps chroma inside the readable band whatever the art does', () => {
    const vivid = accentFromPixels(flat([255, 0, 255]))
    const faint = accentFromPixels(flat([150, 120, 100]))
    for (const accent of [vivid, faint]) {
      expect(accent?.chroma).toBeGreaterThanOrEqual(0.07)
      expect(accent?.chroma).toBeLessThanOrEqual(0.19)
    }
  })

  // Between "no colour at all" and "enough colour to borrow" there is a band
  // where the art is tinted but the tint is not worth the app changing colour
  // for. That band keeps amber rather than producing a near-grey accent.
  it('leaves a barely-tinted sleeve on the default accent', () => {
    expect(accentFromPixels(flat([120, 110, 130]))).toBeNull()
  })
})
