import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { OpError } from '../api/parse'
import { ErrorCode, LyricsType, PlayState, ShuffleMode } from '../api/types'

import { usePlayerStore } from './player'

// Hoisted with the mock factory, which vitest lifts above every other statement
// in the file: a plain `const` here is not yet initialised when it runs.
const { call } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data?: Record<string, unknown>) => Promise<unknown>>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on: vi.fn<() => () => void>() } as unknown as typeof V6Client,
}))

function ops(): string[] {
  return call.mock.calls.map(([op]) => op)
}

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
  call.mockResolvedValue({})
})

describe('the shuffle button', () => {
  // Three modes on one button. A two-state toggle can never reach auto-DJ, which
  // is how it stayed unreachable.
  it('cycles off, shuffle, auto-DJ and back', async () => {
    const player = usePlayerStore()
    const seen: ShuffleMode[] = []
    call.mockImplementation(async (_op, data) => {
      seen.push((data as { mode: ShuffleMode }).mode)
      return { mode: (data as { mode: ShuffleMode }).mode }
    })

    await player.cycleShuffle()
    await player.cycleShuffle()
    await player.cycleShuffle()

    expect(seen).toStrictEqual([ShuffleMode.Shuffle, ShuffleMode.AutoDj, ShuffleMode.Off])
    expect(player.shuffle).toBe(ShuffleMode.Off)
  })
})

describe('the volume readout', () => {
  it('is nought while muted, and the level again once it is not', async () => {
    const player = usePlayerStore()
    call.mockResolvedValue({ muted: true })
    player.volume = 80

    expect(player.shownVolume).toBe(80)
    await player.setMuted(true)
    expect(player.shownVolume).toBe(0)

    call.mockResolvedValue({ muted: false })
    await player.setMuted(false)
    // The level survives the mute rather than being turned down to reach it.
    expect(player.shownVolume).toBe(80)
  })
})

describe('the rating', () => {
  // MusicBee stores half values and the server takes them, so the client must
  // pass one through rather than round it.
  it('sends a half star unrounded', async () => {
    const player = usePlayerStore()
    call.mockResolvedValue({ rating: 3.5 })

    await player.setRating(3.5)

    expect(call).toHaveBeenCalledWith('now_playing_set_rating', { rating: 3.5 })
    expect(player.rating).toBe(3.5)
  })

  it('clears a rating with null rather than zero', async () => {
    const player = usePlayerStore()
    call.mockResolvedValue({ rating: null })

    await player.setRating(null)

    expect(call).toHaveBeenCalledWith('now_playing_set_rating', { rating: null })
  })
})

describe('the track panels', () => {
  it('asks for details only when something wants them', async () => {
    const player = usePlayerStore()
    call.mockResolvedValue({ position_ms: 0, duration_ms: 0, track: null, list_order: null })

    await player.refreshNowPlaying()
    expect(ops()).not.toContain('now_playing_details')
  })

  // The lyrics button is lit for a track that has some, so their presence is
  // known before anyone opens the panel. That cannot be answered lazily.
  it('reads lyrics with the track, not on opening the panel', async () => {
    const player = usePlayerStore()
    call.mockResolvedValue({ type: LyricsType.Plain, lines: [{ text: 'hello' }] })

    await player.refreshAll()
    expect(ops()).toContain('now_playing_lyrics')
    expect(player.lyrics.lines).toHaveLength(1)
  })

  it('starts with no lyrics rather than an empty synced set', () => {
    const player = usePlayerStore()
    expect(player.lyrics.type).toBe(LyricsType.None)
    expect(player.details).toBeNull()
  })
})

describe('love and ban', () => {
  // They write MusicBee's own Love rating, so they need no last.fm account -
  // only enabling scrobbling does. A loved track has to show that it is loved.
  it('reads the state with the playing track', async () => {
    const player = usePlayerStore()
    call.mockResolvedValue({
      track: null,
      list_order: null,
      position_ms: 0,
      duration_ms: 0,
      lfm_status: 'love',
    })

    await player.refreshNowPlaying()
    expect(player.lastfm).toBe('love')
  })

  it('clears the status when the one already set is chosen again', async () => {
    const player = usePlayerStore()
    call.mockResolvedValue({
      track: null,
      list_order: null,
      position_ms: 0,
      duration_ms: 0,
      lfm_status: 'love',
    })
    await player.refreshNowPlaying()

    call.mockResolvedValue({ lfm_status: 'normal' })
    await player.setLastfm('love')

    expect(call).toHaveBeenCalledWith('now_playing_set_lfm', { status: 'normal' })
    expect(player.lastfm).toBe('normal')
  })
})

describe('scrobbling', () => {
  it('reads the state with the rest of the player', async () => {
    const player = usePlayerStore()
    call.mockResolvedValue({
      play_state: PlayState.Playing,
      volume: 50,
      muted: false,
      shuffle: ShuffleMode.Off,
      repeat: 'none',
      scrobbling: true,
    })

    await player.refreshStatus()
    expect(player.scrobbling).toBe(true)
  })

  // The server refuses to turn it on with no last.fm account configured, which
  // is a state only it knows. A switch that does not move has to say why.
  it('says why the server refused rather than looking broken', async () => {
    const player = usePlayerStore()
    call.mockRejectedValue(
      new OpError({
        code: ErrorCode.Unavailable,
        message: 'scrobbling requires a configured last.fm account',
      }),
    )

    await player.setScrobbling(true)

    expect(player.scrobbling).toBe(false)
    expect(player.scrobblingRefusal).toContain('last.fm account')
  })

  it('clears the refusal once it is accepted', async () => {
    const player = usePlayerStore()
    call.mockRejectedValue(new OpError({ code: ErrorCode.Unavailable, message: 'no account' }))
    await player.setScrobbling(true)
    expect(player.scrobblingRefusal).toBe('no account')

    call.mockResolvedValue({ enabled: true })
    await player.setScrobbling(true)

    expect(player.scrobbling).toBe(true)
    expect(player.scrobblingRefusal).toBeNull()
  })
})

describe('the elapsed clock', () => {
  it('advances up to the end of a track and no further', () => {
    const player = usePlayerStore()
    player.playState = PlayState.Playing
    player.durationMs = 1000
    player.positionMs = 900

    player.advance(250)
    expect(player.positionMs).toBe(1000)
  })

  // A stream reports no duration. Reading that as "nothing to advance" froze the
  // clock between polls, half a minute apart.
  it('keeps counting for a stream, which has no end to stop at', () => {
    const player = usePlayerStore()
    player.playState = PlayState.Playing
    player.durationMs = 0
    player.positionMs = 5000

    player.advance(250)
    player.advance(250)
    expect(player.positionMs).toBe(5500)
  })

  it('stands still while nothing is playing', () => {
    const player = usePlayerStore()
    player.playState = PlayState.Paused
    player.durationMs = 0
    player.positionMs = 5000

    player.advance(250)
    expect(player.positionMs).toBe(5000)
  })
})
