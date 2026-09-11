import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { OpError } from '../api/parse'
import { Op, WireEvent } from '../api/ops'
import type { TrackDetails } from '../api/responses'
import { LastfmStatus, LyricsType, PlayState, RepeatMode, ShuffleMode } from '../api/types'
import type { Lyrics, Track } from '../api/types'

/**
 * off -> shuffle -> autodj -> off, the order MusicBee's own button cycles in.
 *
 * Auto-DJ is a shuffle mode rather than a separate toggle: it keeps feeding the
 * queue from the library instead of only reordering what is in it.
 */
const SHUFFLE_CYCLE: Record<ShuffleMode, ShuffleMode> = {
  [ShuffleMode.Off]: ShuffleMode.Shuffle,
  [ShuffleMode.Shuffle]: ShuffleMode.AutoDj,
  [ShuffleMode.AutoDj]: ShuffleMode.Off,
}

/**
 * Now playing and transport state.
 *
 * Most V6 events are markers meaning "re-query" rather than state to trust, so
 * this store refetches on an event instead of patching from its payload. The
 * exceptions are the events that do carry their new value (volume, mute, play
 * state), where refetching would be a round trip for something already in hand.
 */
export const usePlayerStore = defineStore('player', () => {
  const connected = ref(false)
  /** Whether another attempt is on a timer, or the app has stopped trying. */
  const retrying = ref(true)
  const track = ref<Track | null>(null)
  const playState = ref<PlayState>(PlayState.Undefined)
  const volume = ref(0)
  const muted = ref(false)

  /**
   * What the volume reads as on screen.
   *
   * Muted is nought however loud the player is underneath: a slider sitting at
   * 99 next to a crossed-out speaker describes a room that is silent.
   */
  const shownVolume = computed(() => (muted.value ? 0 : volume.value))
  const shuffle = ref<ShuffleMode>(ShuffleMode.Off)
  const repeat = ref<RepeatMode>(RepeatMode.None)
  const rating = ref<number | null>(null)
  const positionMs = ref(0)
  const durationMs = ref(0)
  const listOrder = ref<number | null>(null)
  const details = ref<TrackDetails | null>(null)
  const lyrics = ref<Lyrics>({ type: LyricsType.None, lines: [] })
  const scrobbling = ref(false)
  /** Whether the track is loved or banned, which is a tag on it, not an account. */
  const lastfm = ref<LastfmStatus>(LastfmStatus.Normal)
  /** Why scrobbling could not be turned on, when the server said so. */
  const scrobblingRefusal = ref<string | null>(null)

  async function refreshStatus() {
    const status = await client.call(Op.PlayerStatus)
    playState.value = status.play_state
    volume.value = status.volume
    muted.value = status.muted
    shuffle.value = status.shuffle
    repeat.value = status.repeat
    scrobbling.value = status.scrobbling
  }

  /**
   * Turns scrobbling on or off.
   *
   * The server refuses to turn it on with no last.fm account configured, which
   * is a state only it knows; the refusal is surfaced rather than swallowed, so
   * a switch that does not move says why.
   */
  async function setScrobbling(enabled: boolean) {
    try {
      const result = await client.call(Op.PlayerSetScrobbling, { enabled })
      scrobbling.value = result.enabled
      scrobblingRefusal.value = null
    } catch (error) {
      // A refusal changed nothing, so there is no new state to read; the
      // switch stays where it was and carries the reason it did not move.
      if (!(error instanceof OpError)) throw error
      scrobblingRefusal.value = error.message
    }
  }

  async function refreshNowPlaying() {
    const state = await client.call(Op.NowPlayingState, { include_list_order: true })
    track.value = state.track
    positionMs.value = state.position_ms
    durationMs.value = state.duration_ms
    listOrder.value = state.list_order
    rating.value = state.track?.rating ?? null
    lastfm.value = state.lfm_status
  }

  async function refreshPosition() {
    const position = await client.call(Op.NowPlayingPosition)
    positionMs.value = position.position_ms
    durationMs.value = position.duration_ms
  }

  /**
   * The extended tag panel. Fetched on demand rather than with the track: it is
   * eighteen fields nothing shows until someone asks to see them.
   */
  async function refreshDetails() {
    details.value = await client.call(Op.NowPlayingDetails)
  }

  async function refreshLyrics() {
    lyrics.value = await client.call(Op.NowPlayingLyrics)
  }

  async function refreshAll() {
    await Promise.all([refreshStatus(), refreshNowPlaying(), refreshLyrics()])
  }

  async function playPause() {
    await client.call(Op.PlayerPlayPause)
  }
  async function next() {
    await client.call(Op.PlayerNext)
  }
  async function previous() {
    await client.call(Op.PlayerPrevious)
  }
  async function stop() {
    await client.call(Op.PlayerStop)
  }

  async function setVolume(value: number) {
    const result = await client.call(Op.PlayerSetVolume, { volume: value })
    volume.value = result.volume
  }
  async function setMuted(value: boolean) {
    const result = await client.call(Op.PlayerSetMute, { muted: value })
    muted.value = result.muted
  }
  async function setShuffle(mode: ShuffleMode) {
    const result = await client.call(Op.PlayerSetShuffle, { mode })
    shuffle.value = result.mode
  }
  async function setRepeat(mode: RepeatMode) {
    const result = await client.call(Op.PlayerSetRepeat, { mode })
    repeat.value = result.mode
  }

  /** Advances shuffle one step, so the button reaches all three modes. */
  async function cycleShuffle() {
    await setShuffle(SHUFFLE_CYCLE[shuffle.value])
  }

  async function seek(ms: number) {
    const position = await client.call(Op.NowPlayingSeek, { position_ms: Math.round(ms) })
    positionMs.value = position.position_ms
    durationMs.value = position.duration_ms
  }

  async function setRating(value: number | null) {
    const result = await client.call(Op.NowPlayingSetRating, { rating: value })
    rating.value = result.rating
  }

  /** Choosing the status already set clears it, as the rating stars do. */
  async function setLastfm(status: LastfmStatus) {
    const wanted = lastfm.value === status ? LastfmStatus.Normal : status
    const result = await client.call(Op.NowPlayingSetLfm, { status: wanted })
    lastfm.value = result.lfm_status
  }

  /**
   * Local tick so the elapsed time moves between position fetches.
   *
   * A stream reports no duration, and there is nothing to clamp its clock to:
   * it counts up from when it started. Treating "no duration" as "nothing to
   * advance" left that clock frozen until the next poll, half a minute later.
   */
  function advance(ms: number) {
    if (playState.value !== PlayState.Playing) return
    positionMs.value =
      durationMs.value > 0
        ? Math.min(positionMs.value + ms, durationMs.value)
        : positionMs.value + ms
  }

  function bind() {
    client.on(WireEvent.ConnectionChanged, (data) => {
      connected.value = data.connected
      retrying.value = data.retrying
      if (connected.value) void refreshAll()
    })
    client.on(WireEvent.PlayStateChanged, (data) => {
      playState.value = data.play_state
    })
    client.on(WireEvent.VolumeChanged, (data) => {
      volume.value = data.volume
    })
    client.on(WireEvent.MuteChanged, (data) => {
      muted.value = data.muted
    })
    // Polled by the core rather than announced by MusicBee, which is why these
    // three arrive at all: without them the buttons only ever showed what this
    // browser had asked for, never what someone did in MusicBee's own window.
    client.on(WireEvent.ShuffleChanged, (data) => {
      shuffle.value = data.shuffle
    })
    client.on(WireEvent.RepeatChanged, (data) => {
      repeat.value = data.repeat
    })
    client.on(WireEvent.ScrobblingChanged, (data) => {
      scrobbling.value = data.scrobbling
    })
    client.on(WireEvent.NowPlayingChanged, () => {
      void refreshNowPlaying()
      // The panels are about the track that just changed, so a stale one is
      // worse than an empty one. Details are only refetched while something is
      // holding them; lyrics always, because whether the track has any is shown
      // on the button before anyone opens the panel.
      if (details.value !== null) void refreshDetails()
      lyrics.value = { type: LyricsType.None, lines: [] }
      void refreshLyrics()
    })
    client.on(WireEvent.NowPlayingLyricsChanged, () => {
      void refreshLyrics()
    })
  }

  return {
    connected,
    retrying,
    track,
    playState,
    volume,
    muted,
    shownVolume,
    scrobbling,
    scrobblingRefusal,
    lastfm,
    shuffle,
    repeat,
    rating,
    positionMs,
    durationMs,
    listOrder,
    details,
    lyrics,
    refreshAll,
    refreshDetails,
    refreshLyrics,
    refreshStatus,
    refreshNowPlaying,
    refreshPosition,
    playPause,
    next,
    previous,
    stop,
    setVolume,
    setMuted,
    setScrobbling,
    setShuffle,
    setRepeat,
    cycleShuffle,
    seek,
    setRating,
    setLastfm,
    advance,
    bind,
  }
})
