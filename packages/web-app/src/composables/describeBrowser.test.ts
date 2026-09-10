import { describe, expect, it } from 'vitest'

import { describeBrowser } from './describeBrowser'

const CHROME_ANDROID =
  'Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Mobile Safari/537.36'
const SAFARI_IPHONE =
  'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1'
const FIREFOX_WINDOWS = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0'

describe('naming a browser for the paired list', () => {
  it('takes the two words the browser offers when it offers them', () => {
    const named = describeBrowser(CHROME_ANDROID, {
      brands: [
        { brand: 'Not/A)Brand', version: '99' },
        { brand: 'Vivaldi', version: '7' },
      ],
      platform: 'Android',
    })

    expect(named).toBe('Vivaldi on Android')
  })

  // Every Chromium browser claims to be Safari and Chrome as well as itself, so
  // the order the agent is read in is the whole of the answer.
  it('reads the agent for the name that identifies it', () => {
    expect(describeBrowser(CHROME_ANDROID)).toBe('Chrome on Android')
    expect(describeBrowser(SAFARI_IPHONE)).toBe('Safari on iPhone')
    expect(describeBrowser(FIREFOX_WINDOWS)).toBe('Firefox on Windows')
    expect(describeBrowser(CHROME_ANDROID.replace('Chrome', 'Vivaldi Chrome'))).toBe(
      'Vivaldi on Android',
    )
  })

  // Measured, not imagined: a Chromium here answers `{brands: [], platform: ''}`,
  // and an empty string kept as an answer left the platform out of the name.
  it('treats an empty answer as no answer', () => {
    const named = describeBrowser(FIREFOX_WINDOWS, { brands: [], platform: '' })

    expect(named).toBe('Firefox on Windows')
  })

  it('never answers with nothing, however little it was told', () => {
    expect(describeBrowser('')).toBe('A browser')
    expect(describeBrowser('Mozilla/5.0 (Windows NT 10.0)')).toBe('A browser on Windows')
  })

  // The point of the whole thing: a row in a list, not a paragraph.
  it('stays short enough to read in a list', () => {
    for (const agent of [CHROME_ANDROID, SAFARI_IPHONE, FIREFOX_WINDOWS, '']) {
      expect(describeBrowser(agent).length).toBeLessThanOrEqual(30)
    }
  })
})
