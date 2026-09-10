/**
 * Which rows of the full queue have already been heard, worked out locally.
 *
 * The server stamps every row with a `play_position` when the page is fetched,
 * counted forward from whatever was playing at that moment. That answer goes
 * stale the instant the track changes, and refreshing it is not cheap: the host
 * has no index into play order, so the core walks the whole queue forward to
 * rebuild the ranks. Refetching on every track change would pay for that walk
 * once a song.
 *
 * Nothing has to be asked for. Unshuffled, play order *is* list order, so the
 * playing row's own index is the mark and everything above it has been heard -
 * exact in both directions, so pressing back un-dims what it takes you back to.
 * Shuffled, the ranks the server sent still order the rows relative to each
 * other, so advancing only moves the mark along them; going back lands on a row
 * whose rank was never knowable (-1 says played, not when), and there the last
 * good answer is kept rather than guessed at.
 */

interface Ranked {
  src: string
  play_position: number
}

/** Marks no row, for a list with nothing playing in it. */
const NOTHING_HEARD = () => false

export function heardRows(
  items: readonly Ranked[],
  playing: string | undefined,
  shuffled: boolean,
): (index: number) => boolean {
  const current = playing === undefined ? -1 : items.findIndex((item) => item.src === playing)
  if (current < 0) return NOTHING_HEARD

  if (!shuffled) return (index) => index < current

  const mark = items[current]?.play_position ?? -1
  if (mark < 0) return (index) => items[index]?.play_position === -1
  return (index) => (items[index]?.play_position ?? -1) < mark
}
