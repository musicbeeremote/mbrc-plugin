import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { client } from '../api/client'
import { OpError } from '../api/parse'
import { Op, WireEvent } from '../api/ops'
import type { LibraryScope, OpRequests } from '../api/ops'
import type { OpResponses } from '../api/responses'
import { LibraryLevel, defaultSort } from '../composables/libraryLevels'
import type { Position } from '../composables/libraryLevels'
import { QueueMode } from '../api/types'
import type {
  AlbumEntry,
  ArtistEntry,
  GenreEntry,
  PlaylistEntry,
  Track,
} from '../api/types'

export { LibraryLevel } from '../composables/libraryLevels'

/** What a level is showing beyond its scope: a search, and an order. */
interface ViewOptions {
  query?: string
  sort?: string
  descending?: boolean
  albumArtists?: boolean
}

/** How a scope goes into the queue: where, from which track, in what order. */
interface QueueOptions {
  mode?: QueueMode
  /** The track to start from, for "play all from here". */
  play?: string
  shuffle?: boolean
}

/** One page per scroll batch. Large enough that a phone rarely pages twice. */
const PAGE_SIZE = 100

/** The ops that answer a `{total, offset, items}` page. */
type ListOp =
  | typeof Op.LibraryGenres
  | typeof Op.LibraryArtists
  | typeof Op.LibraryAlbums
  | typeof Op.LibraryTracks
  | typeof Op.LibraryRadio
  | typeof Op.PlaylistList

/**
 * Library browsing: four levels and a scope, both read off the URL.
 *
 * Every list is served `{total, offset, items}`, so the store keeps `total` and
 * appends pages rather than holding the whole library: a large library must
 * never be pulled into the browser to show the first screen of it.
 *
 * Where the view is, is the router's answer, not the store's: `show` is called
 * with what the URL said and the store reads that position. Nothing here decides
 * where to go, so Back, a reload and a pasted link all arrive the same way.
 *
 * Search and queueing are the same scope the level is already reading, handed to
 * the server as parameters. The browser holds one page of the library and so can
 * never enumerate an artist locally the way the Android app does; naming the
 * scope is what lets it queue one anyway.
 */
export const useLibraryStore = defineStore('library', () => {
  const here = ref<Position>({ level: LibraryLevel.Artists, scope: {} })
  const level = computed(() => here.value.level)
  const scope = computed(() => here.value.scope)

  const query = ref('')

  /** The order asked for, or nothing to take the level's own default. */
  const sort = ref<string | undefined>(undefined)
  const descending = ref(false)
  /** Whether the artist level browses album artists rather than every credit. */
  const albumArtists = ref(false)

  const genres = ref<GenreEntry[]>([])
  const artists = ref<ArtistEntry[]>([])
  const albums = ref<AlbumEntry[]>([])
  const tracks = ref<Track[]>([])
  const playlists = ref<PlaylistEntry[]>([])

  const total = ref(0)
  const loading = ref(false)

  /**
   * Why the level on screen is not what was asked for.
   *
   * A refused request has to say so. Left unhandled it reads as a pane where
   * nothing happened, which is indistinguishable from a library with nothing
   * in it and sends the reader looking for the fault in the wrong place.
   */
  const failure = ref<string | null>(null)

  /** How many of the current level are on screen, which is where paging resumes. */
  const shown = computed(() => {
    if (level.value === LibraryLevel.Genres) return genres.value.length
    if (level.value === LibraryLevel.Artists) return artists.value.length
    if (level.value === LibraryLevel.Albums) return albums.value.length
    return tracks.value.length
  })
  const hasMore = computed(() => shown.value < total.value)

  /** The `query` parameter, or nothing at all when the box is empty. */
  function searchArg(): { query?: string } {
    const trimmed = query.value.trim()
    return trimmed === '' ? {} : { query: trimmed }
  }

  async function page<K extends ListOp>(
    op: K,
    data: OpRequests[K],
    offset: number,
  ): Promise<OpResponses[K]['items']> {
    const result = await client.call(op, { ...data, offset, limit: PAGE_SIZE })
    total.value = result.total
    return result.items
  }

  /**
   * Reads the current level.
   *
   * `append` continues the level already on screen; without it the level is
   * replaced, which is what a tab change, a drill-down or a new search wants.
   */
  async function load(append = false): Promise<void> {
    loading.value = true
    failure.value = null
    const offset = append ? shown.value : 0
    // Only the fields this level actually filters by: passing an album to a
    // genre listing would be a request the server is right to find odd.
    const { genre, artist, album } = scope.value
    // A search with no order asked for is answered by how well each row
    // matches; naming one is what says to read it another way instead.
    const ordered = sort.value ?? (query.value.trim() === '' ? defaultSort(here.value) : undefined)
    const narrow = {
      ...searchArg(),
      ...(ordered === undefined ? {} : { sort: ordered }),
      ...(descending.value ? { order: 'desc' as const } : {}),
    }
    try {
      switch (level.value) {
        case LibraryLevel.Genres: {
          const items = await page(Op.LibraryGenres, narrow, offset)
          genres.value = append ? [...genres.value, ...items] : items
          break
        }
        case LibraryLevel.Artists: {
          const items = await page(
            Op.LibraryArtists,
            { ...narrow, genre, album_artists: albumArtists.value },
            offset,
          )
          artists.value = append ? [...artists.value, ...items] : items
          break
        }
        case LibraryLevel.Albums: {
          const items = await page(Op.LibraryAlbums, { ...narrow, artist }, offset)
          albums.value = append ? [...albums.value, ...items] : items
          break
        }
        case LibraryLevel.Tracks: {
          // The artist goes with the album: a title is shared often enough
          // that without it three bands' records read as one album.
          const items = await page(Op.LibraryTracks, { ...narrow, album, artist }, offset)
          tracks.value = append ? [...tracks.value, ...items] : items
          break
        }
        default: {
          break
        }
      }
    } catch (error) {
      failure.value = error instanceof OpError ? error.message : String(error)
    } finally {
      loading.value = false
    }
  }

  /**
   * Reads the position a URL named.
   *
   * The one way in: a tab, a drill-down, Back and a pasted link are all the same
   * navigation by the time they reach here, because each of them is a URL first.
   */
  async function show(position: Position, view: ViewOptions = {}): Promise<void> {
    here.value = position
    query.value = view.query ?? ''
    sort.value = view.sort
    descending.value = view.descending ?? false
    albumArtists.value = view.albumArtists ?? false
    await load()
  }

  async function loadPlaylists() {
    loading.value = true
    try {
      playlists.value = await page(Op.PlaylistList, {}, 0)
    } finally {
      loading.value = false
    }
  }

  async function playPlaylist(url: string) {
    await client.call(Op.PlaylistPlay, { url })
  }

  async function queue(paths: string[], mode: QueueMode = QueueMode.Last) {
    await client.call(Op.NowPlayingQueue, { paths, mode })
  }

  async function playNow(paths: string[], play: string) {
    await client.call(Op.NowPlayingQueue, { paths, mode: QueueMode.Now, play })
  }

  /**
   * Queues everything a scope selects, resolved by the server.
   *
   * The counterpart to [`queue`]: that one names the tracks, this one names the
   * scope. An artist's tracks are not on screen to be named, and fetching them
   * only to send them straight back is a round trip that buys nothing.
   */
  async function queueScope(
    add: LibraryScope,
    { mode = QueueMode.Last, play, shuffle = false }: QueueOptions = {},
  ): Promise<number> {
    // The search narrows what the pane is showing, so it belongs to queueing
    // the pane. Naming a row is already the whole answer, and carrying the term
    // into it queues nothing: a search matches the names on screen, while a
    // queue matches track titles and artists, which are not the same words.
    const narrow = Object.keys(add).length === 0 ? searchArg() : {}
    const result = await client.call(Op.LibraryQueue, {
      ...scope.value,
      ...narrow,
      ...add,
      mode,
      ...(play ? { play } : {}),
      ...(shuffle ? { shuffle } : {}),
    })
    return result.count
  }

  /** Whether anything narrows the current level: a drill-down or a search. */
  const scoped = computed(
    () => Object.keys(scope.value).length > 0 || query.value.trim() !== '',
  )

  /**
   * Plays everything the pane is currently showing.
   *
   * At the root of a tab that is the whole library, and MusicBee has its own
   * command for it. Narrowed by a genre, an artist or a search it is not: the
   * library-wide command cannot be scoped, so the same filters that produced
   * the list produce what gets played.
   */
  async function playAll(shuffle = false): Promise<void> {
    if (scoped.value) {
      await queueScope({}, { mode: QueueMode.Now, shuffle })
      return
    }
    await client.call(Op.LibraryPlayAll, { shuffle })
  }

  function bind() {
    client.on(WireEvent.LibraryChanged, () => {
      void load()
    })
  }

  return {
    level,
    scope,
    query,
    sort,
    descending,
    albumArtists,
    genres,
    artists,
    albums,
    tracks,
    playlists,
    total,
    loading,
    error: failure,
    hasMore,
    scoped,
    load,
    show,
    loadPlaylists,
    playPlaylist,
    queue,
    playNow,
    queueScope,
    playAll,
    bind,
  }
})
