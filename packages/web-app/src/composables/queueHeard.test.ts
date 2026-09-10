import { describe, expect, it } from 'vitest'

import { heardRows } from './queueHeard'

/** A queue whose play ranks are counted from `current`, the way the server sends them. */
function queue(count: number, current: number) {
  return Array.from({ length: count }, (_, index) => ({
    src: `/${index}.mp3`,
    play_position: index < current ? -1 : index - current,
  }))
}

describe('which rows have been heard', () => {
  it('marks everything above the playing row', () => {
    const heard = heardRows(queue(5, 2), '/2.mp3', false)

    expect([0, 1, 2, 3, 4].map((index) => heard(index))).toStrictEqual([true, true, false, false, false])
  })

  // The whole point: the ranks the server sent are counted from the track that
  // was playing when the page was fetched, and the queue moves on without it.
  it('follows the queue forward without being told again', () => {
    const stamped = queue(5, 2)

    expect(heardRows(stamped, '/3.mp3', false)(2)).toBe(true)
    expect(heardRows(stamped, '/4.mp3', false)(3)).toBe(true)
  })

  // What prompted this: a track you go back before is ahead of you again.
  it('un-marks a row the queue goes back before', () => {
    const stamped = queue(5, 3)

    expect(heardRows(stamped, '/3.mp3', false)(2)).toBe(true)
    expect(heardRows(stamped, '/2.mp3', false)(2)).toBe(false)
  })

  describe('shuffled', () => {
    // List order is not play order, so the mark moves along the server's ranks
    // rather than along the rows.
    const shuffled = [
      { src: '/a.mp3', play_position: 2 },
      { src: '/b.mp3', play_position: -1 },
      { src: '/c.mp3', play_position: 0 },
      { src: '/d.mp3', play_position: 1 },
    ]

    it('marks by play rank, not by row', () => {
      const heard = heardRows(shuffled, '/c.mp3', true)

      expect([0, 1, 2, 3].map((index) => heard(index))).toStrictEqual([false, true, false, false])
    })

    it('moves the mark as the queue advances', () => {
      const heard = heardRows(shuffled, '/d.mp3', true)

      expect([0, 1, 2, 3].map((index) => heard(index))).toStrictEqual([false, true, true, false])
    })

    // A played row's rank is -1, which says that it was played and not when, so
    // there is no mark to move to. The last good answer stands.
    it('keeps what it had when the queue goes back to a played row', () => {
      const heard = heardRows(shuffled, '/b.mp3', true)

      expect([0, 1, 2, 3].map((index) => heard(index))).toStrictEqual([false, true, false, false])
    })
  })

  it('marks nothing when the playing track is not in the loaded rows', () => {
    const heard = heardRows(queue(3, 1), '/elsewhere.mp3', false)

    expect([0, 1, 2].map((index) => heard(index))).toStrictEqual([false, false, false])
  })
})
