import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { usePreferences } from './usePreferences'

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('showing media', () => {
  it('is off until turned on', () => {
    expect(usePreferences().showMedia).toBe(false)
  })

  it('is remembered by the next visit', () => {
    usePreferences().setShowMedia(true)

    setActivePinia(createPinia())
    expect(usePreferences().showMedia).toBe(true)
  })

  it('opens with the default when storage cannot be read', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied')
    })
    expect(usePreferences().showMedia).toBe(false)
  })

  /** A browser that will not remember it still honours it for this visit. */
  it('takes effect when storage cannot be written', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied')
    })
    const preferences = usePreferences()
    preferences.setShowMedia(true)
    expect(preferences.showMedia).toBe(true)
  })
})
