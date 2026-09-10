/**
 * Which line of synced lyrics the playhead is in.
 *
 * Only LRC-timed lyrics have a current line at all: plain lyrics are words with
 * no clock attached, and highlighting one of them would be a guess. The server
 * says which kind it sent, so this never has to infer it.
 */

import { LyricsType } from '../api/types'
import type { Lyrics } from '../api/types'

/** The index of the line being sung, or -1 when nothing is. */
export function activeLyricLine(lyrics: Lyrics, positionMs: number): number {
  if (lyrics.type !== LyricsType.Synced) return -1
  // The last line whose timestamp has passed. Walking back from the end stops
  // at the first hit, which is that line.
  for (let index = lyrics.lines.length - 1; index >= 0; index -= 1) {
    const at = lyrics.lines[index].at_ms
    if (at !== undefined && at <= positionMs) return index
  }
  return -1
}
