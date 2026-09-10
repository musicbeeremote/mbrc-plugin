import { defineComponent } from 'vue'
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import { useShortcuts } from './useShortcuts'
import type { Transport } from './useShortcuts'

function transport(volume = 50) {
  return {
    playPause: vi.fn<() => void>(),
    next: vi.fn<() => void>(),
    previous: vi.fn<() => void>(),
    volume,
    setVolume: vi.fn<(value: number) => void>(),
    toggleMute: vi.fn<() => void>(),
  }
}

/** Mounts a component that binds the shortcuts, and returns the transport. */
function bound(t: Transport) {
  const component = defineComponent({
    setup() {
      useShortcuts(() => t)
      return () => null
    },
  })
  const wrapper = mount(component)
  return () => wrapper.unmount()
}

function press(key: string, init: KeyboardEventInit = {}) {
  const event = new KeyboardEvent('keydown', { key, cancelable: true, ...init })
  document.dispatchEvent(event)
  return event
}

describe('the transport shortcuts', () => {
  it('drives the player from the keys a player has always used', () => {
    const t = transport()
    const unmount = bound(t)

    press(' ')
    press('ArrowRight')
    press('ArrowLeft')
    press('m')

    expect(t.playPause).toHaveBeenCalledTimes(1)
    expect(t.next).toHaveBeenCalledTimes(1)
    expect(t.previous).toHaveBeenCalledTimes(1)
    expect(t.toggleMute).toHaveBeenCalledTimes(1)
    unmount()
  })

  it('steps the volume without running past either end', () => {
    const loud = transport(98)
    const unmountLoud = bound(loud)
    press('ArrowUp')
    expect(loud.setVolume).toHaveBeenCalledWith(100)
    unmountLoud()

    const quiet = transport(2)
    const unmountQuiet = bound(quiet)
    press('ArrowDown')
    expect(quiet.setVolume).toHaveBeenCalledWith(0)
    unmountQuiet()
  })

  // A key pressed in the search box is text, not a command.
  it('stands down while something is being typed', () => {
    const t = transport()
    const unmount = bound(t)
    const input = document.createElement('input')
    document.body.append(input)

    input.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }))

    expect(t.playPause).not.toHaveBeenCalled()
    input.remove()
    unmount()
  })

  // Ctrl+ArrowLeft and friends belong to the browser.
  it('leaves a modified key alone', () => {
    const t = transport()
    const unmount = bound(t)

    press('ArrowRight', { ctrlKey: true })
    press(' ', { metaKey: true })

    expect(t.next).not.toHaveBeenCalled()
    expect(t.playPause).not.toHaveBeenCalled()
    unmount()
  })

  // Space scrolls and the arrows move a list, so a key this does not handle
  // has to reach the page untouched.
  it('only takes the keys it acts on', () => {
    const t = transport()
    const unmount = bound(t)

    expect(press(' ').defaultPrevented).toBe(true)
    expect(press('j').defaultPrevented).toBe(false)
    unmount()
  })

  it('stops listening once the app is gone', () => {
    const t = transport()
    bound(t)()

    press(' ')
    expect(t.playPause).not.toHaveBeenCalled()
  })
})
