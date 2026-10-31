/** Pages the media store tests feed it, shaped as the server sends them. */

import type { Page } from '../api/types'

export function albums(count: number, total = count): Page<Record<string, unknown>> {
  return {
    total,
    offset: 0,
    items: Array.from({ length: count }, (_, i) => ({
      album: `Book ${i}`,
      artist: `Author ${i}`,
      count: 10,
    })),
  }
}

export function chapters(count: number, total = count): Page<Record<string, unknown>> {
  return {
    total,
    offset: 0,
    items: Array.from({ length: count }, (_, i) => ({
      src: `C:/books/dune/${i + 1}.mp3`,
      artist: ['Herbert'],
      artist_display: 'Herbert',
      title: `Chapter ${i + 1}`,
      album: 'Dune',
      album_artist: 'Herbert',
      track_no: i + 1,
      disc_no: 1,
      genre: [],
      year: null,
      duration_ms: 60_000,
      rating: null,
      date_added: null,
    })),
  }
}
