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

export const RadioEntrySchema = z.object({
  name: z.string(),
  url: z.string(),
})
export type RadioEntry = z.infer<typeof RadioEntrySchema>
