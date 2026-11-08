import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { artistKey } from '../api/display'
import { Op, WireEvent } from '../api/ops'
import { OpError } from '../api/parse'
import { ErrorCode } from '../api/types'

/** How long the names on screen must hold still before they are asked for. */
const ASK_DELAY_MS = 150

/** The most names the server takes in one `library_artist_images`. */
const MAX_PER_REQUEST = 500

/**
 * How long a name already asked for is left alone.
 *
 * The server queues what it has no picture for and announces each one that
 * lands, so asking again at once only repeats the request. Its queue is
 * bounded, though, and a name it dropped is queued again only when asked again.
 */
const ASK_AGAIN_MS = 10 * 60 * 1000

/**
 * How long until one name is asked again after no answer, or a "no pictures".
 *
 * Downloads can be turned on in MusicBee while this page is open, and one name
 * is enough to learn that, however many are on screen.
 */
const RECHECK_MS = 60 * 1000

/** Whether this browser last heard that the server sends pictures, for the first paint. */
const ENABLED_KEY = 'mbrc.artist-pictures'

function readRememberedEnabled(): boolean {
  try {
    return localStorage.getItem(ENABLED_KEY) === '1'
  } catch {
    return false
  }
}

function writeRememberedEnabled(on: boolean): void {
  try {
    localStorage.setItem(ENABLED_KEY, on ? '1' : '0')
  } catch {
    // A browser that keeps nothing draws the column once the server has answered.
  }
}

/** A refusal no retry on this connection will change. */
function isFinalRefusal(error: unknown): boolean {
  return (
    error instanceof OpError &&
    (error.code === ErrorCode.UnknownOp || error.code === ErrorCode.Forbidden)
  )
}

/**
 * Artist pictures (#116), by artist name.
 *
 * One map for every place a picture shows, so a picture announced while it
 * plays reaches the library row and the now-playing line alike. A page fills it
 * from what the server already sent; `want` names what is on screen now, and
 * only what is still there once scrolling pauses is asked for, never the whole
 * library.
 */
export const useArtistImagesStore = defineStore('artistImages', () => {
  const hashes = ref(new Map<string, string>())
  /** Hashes whose file failed to load, which no answer brings back until reconnecting. */
  const broken = ref(new Set<string>())
  /** Whether the server will download pictures; unknown until it has answered once. */
  const enabled = ref<boolean | null>(null)
  const rememberedEnabled = ref(readRememberedEnabled())

  /**
   * Whether artist rows carry a picture column: once the server downloads
   * pictures, or has some stored, so a library without any keeps its plain list.
   * Until it answers, what it said last time, so the rows do not shift under the
   * first paint.
   */
  const showsPictures = computed(
    () => (enabled.value ?? rememberedEnabled.value) || hashes.value.size > 0,
  )

  const asked = new Map<string, number>()
  let shown: string[] = []
  let timer: ReturnType<typeof setTimeout> | null = null
  let recheck: ReturnType<typeof setTimeout> | null = null
  /** A server that refuses the op, which no recheck will change. */
  let refused = false

  function setEnabled(on: boolean): void {
    enabled.value = on
    rememberedEnabled.value = on
    writeRememberedEnabled(on)
  }

  function hashFor(name: string): string | undefined {
    return hashes.value.get(artistKey(name))
  }

  /** Records what the server said of `name`; no hash means it has none now. */
  function remember(name: string, hash: string | undefined): void {
    const key = artistKey(name)
    if (hash === undefined) hashes.value.delete(key)
    else if (!broken.value.has(hash)) hashes.value.set(key, hash)
  }

  function isBroken(hash: string): boolean {
    return broken.value.has(hash)
  }

  /** Stops showing a picture whose file failed to load, under every name using it. */
  function markBroken(hash: string): void {
    broken.value.add(hash)
    for (const [key, held] of hashes.value) {
      if (held === hash) hashes.value.delete(key)
    }
  }

  /** The names on screen that have no picture here and are due, by key. */
  function dueNames(): Map<string, string> {
    const now = Date.now()
    const due = new Map<string, string>()
    for (const name of shown) {
      const key = artistKey(name)
      const last = asked.get(key)
      const isDue = last === undefined || now - last >= ASK_AGAIN_MS
      if (key !== '' && !hashes.value.has(key) && isDue && !due.has(key)) due.set(key, name)
    }
    return due
  }

  /** Sends `artists`; true when the server will send pictures for them. */
  async function send(artists: string[]): Promise<boolean> {
    try {
      const result = await client.call(Op.LibraryArtistImages, { artists })
      setEnabled(result.enabled)
      for (const item of result.artists) remember(item.artist, item.image_hash)
      return result.enabled
    } catch (error) {
      refused = isFinalRefusal(error)
      if (refused) setEnabled(false)
      return false
    }
  }

  /** Asks for up to `limit` due names, then the rest while the server says yes. */
  async function ask(limit = MAX_PER_REQUEST): Promise<void> {
    const batch = [...dueNames()].slice(0, limit)
    if (batch.length === 0) return
    const now = Date.now()
    for (const [key] of batch) asked.set(key, now)
    if (await send(batch.map(([, name]) => name))) {
      await ask()
      return
    }
    for (const [key] of batch) asked.delete(key)
    if (refused || recheck !== null) return
    recheck = setTimeout(() => {
      recheck = null
      void ask(1)
    }, RECHECK_MS)
  }

  /** Names what is on screen now, replacing what was named before. */
  function want(names: Iterable<string>): void {
    shown = [...names]
    if (timer !== null) clearTimeout(timer)
    timer = null
    if (refused || recheck !== null) return
    timer = setTimeout(() => {
      timer = null
      void ask()
    }, ASK_DELAY_MS)
  }

  function bind(): void {
    client.on(WireEvent.LibraryArtistImageUpdated, (data) => {
      remember(data.artist, data.image_hash)
    })
    // What landed while the socket was down was never announced, and the
    // setting or the server's queue may have changed.
    client.on(WireEvent.ConnectionChanged, (data) => {
      if (!data.connected) return
      enabled.value = null
      refused = false
      asked.clear()
      broken.value.clear()
      if (recheck !== null) clearTimeout(recheck)
      recheck = null
      want(shown)
    })
  }

  return { enabled, showsPictures, hashFor, remember, isBroken, markBroken, want, bind }
})
