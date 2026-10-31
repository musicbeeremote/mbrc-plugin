import type { Page } from '../api/types'

type WireQueuePage = Page<Record<string, unknown>> & { version: number }

/** A page of `count` rows from a queue of `total`, rows ordered from `offset`. */
export function page(
  count: number,
  total: number,
  { version = 1, offset = 0 } = {},
): WireQueuePage {
  return {
    total,
    offset,
    version,
    items: Array.from({ length: count }, (_, i) => ({
      src: `/${offset + i}.mp3`,
      artist: 'A',
      title: `T${offset + i}`,
      album: '',
      album_artist: '',
      track_no: 0,
      disc_no: 0,
      genre: '',
      year: null,
      duration_ms: null,
      rating: null,
      date_added: null,
      order: offset + i,
      position: offset + i,
      play_position: offset + i,
    })),
  }
}
