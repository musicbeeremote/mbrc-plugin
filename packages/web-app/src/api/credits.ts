import { z } from 'zod'

/** One credited name on a track. List pages leave out performers. */
export const CreditSchema = z.object({
  name: z.string(),
  role: z.enum(['artist', 'guest', 'performer', 'remixer']),
  /** A performer's instrument or part. */
  detail: z.string().optional(),
  /** The artist's picture, on one track's own view and only when one is stored. */
  image_hash: z.string().optional(),
})
export type Credit = z.infer<typeof CreditSchema>

/**
 * Who is on a track. A row shows `artist_display`, the user's own text;
 * `artist` names each artist and guest to browse by.
 */
export const peopleFields = {
  artist: z.array(z.string()),
  artist_display: z.string(),
  artist_credits: z.array(CreditSchema),
  composer: z.array(z.string()),
}
