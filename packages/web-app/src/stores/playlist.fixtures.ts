import type { Page } from '../api/types'

type WirePlaylistPage = Page<Record<string, unknown>> & {
  name: string
  version: string
  editable: boolean
}

/** A playlist page as the server sends it: `count` of `total` tracks from `from`. */
export function page(count: number, total: number, from = 0): WirePlaylistPage {
  return {
    total,
    offset: from,
    name: 'My Playlist',
    version: 'abc123',
    editable: true,
    items: Array.from({ length: count }, (_, i) => ({
      src: `/${from + i}.mp3`,
      artist: 'A',
      title: `T${from + i}`,
      album: '',
      album_artist: '',
      track_no: 0,
      disc_no: 0,
      genre: '',
      year: null,
      duration_ms: null,
      rating: null,
      date_added: null,
      order: from + i,
    })),
  }
}
