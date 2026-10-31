import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { artistKey } from '../api/display'
import { Op, WireEvent } from '../api/ops'

/** How long the names wanted for pictures gather before one request asks for them all. */
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
 * Artist pictures (#116), by artist name.
 *
 * One map for every place a picture shows, so a picture announced while it
 * plays reaches the library row and the now-playing line alike. A page fills it
 * from what the server already sent; `want` asks for the names on screen that
 * have none, batched into one request, never the whole library.
 */
export const useArtistImagesStore = defineStore('artistImages', () => {
  const hashes = ref(new Map<string, string>())
  /** Whether the server will download pictures; unknown until it has answered once. */
  const enabled = ref<boolean | null>(null)

  /**
   * Whether artist rows carry a picture column: once the server downloads
   * pictures, or has some stored, so a library without any keeps its plain list.
   */
  const showsPictures = computed(() => enabled.value === true || hashes.value.size > 0)

  const asked = new Map<string, number>()
  const waiting = new Map<string, string>()
  let timer: ReturnType<typeof setTimeout> | null = null

  function hashFor(name: string): string | undefined {
    return hashes.value.get(artistKey(name))
  }

  function remember(name: string, hash: string | undefined): void {
    if (hash) hashes.value.set(artistKey(name), hash)
  }

  /** Sends what is waiting, one request at a time, until nothing is. */
  async function askAll(): Promise<void> {
    const batch = [...waiting].slice(0, MAX_PER_REQUEST)
    const now = Date.now()
    for (const [key] of batch) {
      waiting.delete(key)
      asked.set(key, now)
    }
    try {
      const result = await client.call(Op.LibraryArtistImages, {
        artists: batch.map(([, name]) => name),
      })
      enabled.value = result.enabled
      for (const item of result.artists) remember(item.artist, item.image_hash)
    } catch {
      // A server without the op, or one that refused it, sends no pictures
      // this connection; reconnecting asks again.
      enabled.value = false
      waiting.clear()
    }
    if (waiting.size > 0) await askAll()
  }

  function isDue(key: string, now: number): boolean {
    const last = asked.get(key)
    return key !== '' && !hashes.value.has(key) && (last === undefined || now - last >= ASK_AGAIN_MS)
  }

  /** Asks the server for the pictures of the `names` on screen that have none here. */
  function want(names: Iterable<string>): void {
    if (enabled.value === false) return
    const now = Date.now()
    for (const name of names) {
      const key = artistKey(name)
      if (!waiting.has(key) && isDue(key, now)) waiting.set(key, name)
    }
    if (waiting.size === 0 || timer !== null) return
    timer = setTimeout(() => {
      timer = null
      void askAll()
    }, ASK_DELAY_MS)
  }

  function bind(): void {
    client.on(WireEvent.LibraryArtistImageUpdated, (data) => {
      remember(data.artist, data.image_hash)
    })
    // The setting may have changed while the socket was down.
    client.on(WireEvent.ConnectionChanged, (data) => {
      if (!data.connected) return
      enabled.value = null
      asked.clear()
    })
  }

  return { hashes, enabled, showsPictures, hashFor, remember, want, bind }
})
