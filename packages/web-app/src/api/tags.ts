/**
 * The tag-editing payloads (`tag_fields`, `tag_values`, `now_playing_tags`).
 *
 * A key names a field, never the user's name for it; see `docs/protocol-v6.md`.
 */

import { z } from 'zod'

/** A tag value: an array for a multi-value field, a string otherwise. */
export const TagValueSchema = z.union([z.string(), z.array(z.string())])
export type TagValue = z.infer<typeof TagValueSchema>

export const TagFieldSchema = z.object({
  key: z.string(),
  /** What the user calls the field in MusicBee, such as "Energy" for custom1. */
  name: z.string(),
  multi_value: z.boolean(),
})
export type TagField = z.infer<typeof TagFieldSchema>

export const NowPlayingTagsSchema = z.object({
  /** Null when nothing is playing. */
  path: z.string().nullable(),
  tags: z.record(z.string(), TagValueSchema),
})
export type NowPlayingTags = z.infer<typeof NowPlayingTagsSchema>

export const TagValueCountSchema = z.object({ value: z.string(), count: z.number() })
export type TagValueCount = z.infer<typeof TagValueCountSchema>
