/**
 * How the wire's values are written on screen.
 *
 * Apart from the schemas in `types.ts` because they are not part of the
 * contract: what a duration or a missing title looks like is this client's
 * choice, and a module that parses what the server said should not also decide
 * how it reads.
 */

/**
 * What a track is called when its title tag is empty.
 *
 * The file name, because it is the one thing such a track always has and the
 * only thing that tells two of them apart - "Untitled" three times in a row
 * names nothing.
 */
export function trackLabel(title: string, src: string): string {
  if (title.trim() !== '') return title
  const file = src.split(/[\\/]/u).at(-1) ?? src
  return file.replace(/\.[^.]+$/u, '') || file
}

/** The URL that renders a cover, or null when the album has none cached. */
export function coverUrl(hash: string | undefined): string | null {
  return hash ? `/api/cover/${hash}` : null
}

/**
 * The URL for the playing track's own artwork, at the size a full pane wants.
 *
 * `coverUrl` above serves the album cache, which is built at a grid cell's size:
 * showing it on a pane is showing an enlargement. This route renders the source
 * artwork per request instead, so it stays sharp however large it is drawn.
 *
 * The hash goes in as a cache key, not an argument. The server ignores it and
 * the browser uses it to tell one track's art from the next, which is what lets
 * the response be cached immutably despite the URL being fixed.
 */
export function nowPlayingCoverUrl(hash: string | undefined): string | null {
  return hash ? `/api/cover/now-playing?v=${encodeURIComponent(hash)}` : null
}

/** `m:ss`, or `h:mm:ss` past an hour. Nulls render as a placeholder, not 0:00. */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms) || ms < 0) return '--:--'
  const total = Math.floor(ms / 1000)
  const seconds = total % 60
  const minutes = Math.floor(total / 60) % 60
  const hours = Math.floor(total / 3600)
  const pad = (n: number) => String(n).padStart(2, '0')
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${minutes}:${pad(seconds)}`
}
