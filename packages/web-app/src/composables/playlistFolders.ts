/**
 * Playlists as the folder tree they actually are.
 *
 * MusicBee reports a playlist by its path relative to the playlists root, and
 * every separator in it is a directory: `tracks\subplaylst\Caraven` is `Caraven`
 * inside `subplaylst` inside `tracks`. Flattened into one list those paths read
 * as filenames, and a library with a few dozen of them reads as noise.
 *
 * The tree is derived rather than fetched. The server sends every playlist in
 * one page, so the structure is already in hand; asking for it a level at a time
 * would be a round trip for something we can compute.
 */

import type { PlaylistEntry } from '../api/types'

/** What a folder holds: the folders below it, and the playlists directly in it. */
export interface PlaylistFolder {
  folders: string[]
  playlists: PlaylistEntry[]
}

/** Splits a reported name into its directory segments. Both separators occur. */
export function playlistSegments(name: string): string[] {
  return name.split(/[\\/]/u).filter((segment) => segment !== '')
}

/** Whether `segments` starts with every segment of `prefix`, in order. */
function isUnder(segments: string[], prefix: string[]): boolean {
  return prefix.every((segment, index) => segments[index] === segment)
}

/**
 * The contents of one folder.
 *
 * `path` is the folder being looked at, as segments; an empty path is the root.
 * Folders are returned once each however many playlists are under them, and in
 * the order first met, which is the order the server listed them in.
 */
export function browsePlaylists(entries: PlaylistEntry[], path: string[]): PlaylistFolder {
  const folders: string[] = []
  const playlists: PlaylistEntry[] = []

  for (const entry of entries) {
    const segments = playlistSegments(entry.name)
    const rest = isUnder(segments, path) ? segments.slice(path.length) : []

    if (rest.length === 1) {
      playlists.push(entry)
    } else if (rest.length > 1) {
      // Deeper than this folder, so the next segment names a folder inside it.
      const [child] = rest
      if (!folders.includes(child)) folders.push(child)
    }
  }

  return { folders, playlists }
}

/** The name a playlist shows under, which is its last segment. */
export function playlistLabel(entry: PlaylistEntry): string {
  return playlistSegments(entry.name).at(-1) ?? entry.name
}
