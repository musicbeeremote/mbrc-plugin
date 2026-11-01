/**
 * The rows of the library's browse pages: genres, artists, albums and radio.
 *
 * Counts and names rather than tracks; a track row is the canonical track in
 * `types.ts`.
 */

import { z } from 'zod'

export const GenreEntrySchema = z.object({
  genre: z.string(),
  count: z.number(),
})
export type GenreEntry = z.infer<typeof GenreEntrySchema>

export const ArtistEntrySchema = z.object({
  artist: z.string(),
  count: z.number(),
  /** The artist's picture, present only when the server has one stored. */
  image_hash: z.string().optional(),
})
export type ArtistEntry = z.infer<typeof ArtistEntrySchema>

export const AlbumEntrySchema = z.object({
  album: z.string(),
  artist: z.string(),
  count: z.number(),
  cover_hash: z.string().optional(),
  /** Derived from the album's tracks; absent when none of them carry a year. */
  year: z.number().optional(),
  /** The earliest original year its tracks carry, absent when none do. */
  original_year: z.number().optional(),
})
export type AlbumEntry = z.infer<typeof AlbumEntrySchema>

/** The categories MusicBee files apart from the music library. */
export const MediaSource = {
  Audiobooks: 'audiobooks',
  Videos: 'videos',
  Inbox: 'inbox',
} as const
export type MediaSource = (typeof MediaSource)[keyof typeof MediaSource]

const MEDIA_SOURCES: readonly string[] = Object.values(MediaSource)

export function isMediaSource(value: unknown): value is MediaSource {
  return typeof value === 'string' && MEDIA_SOURCES.includes(value)
}

/**
 * A category with something in it, and roughly how many files.
 *
 * The source stays a string: a category this build has not heard of is left
 * out by the store rather than failing the whole answer.
 */
export const MediaSourceEntrySchema = z.object({
  source: z.string(),
  count: z.number(),
})
export type MediaSourceEntry = z.infer<typeof MediaSourceEntrySchema>

export const RadioEntrySchema = z.object({
  name: z.string(),
  url: z.string(),
})
export type RadioEntry = z.infer<typeof RadioEntrySchema>
