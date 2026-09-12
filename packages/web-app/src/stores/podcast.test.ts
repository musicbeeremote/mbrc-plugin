import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { QueueMode } from '../api/types'

import { usePodcastStore } from './podcast'

const { call } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data: Record<string, unknown>) => Promise<unknown>>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on: vi.fn<() => () => void>() } as unknown as typeof V6Client,
}))

function lastCall(): [string, Record<string, unknown>] {
  return call.mock.calls.at(-1) as [string, Record<string, unknown>]
}

function subscriptions(count: number, total = count) {
  return {
    total,
    offset: 0,
    items: Array.from({ length: count }, (_, i) => ({
      id: `sub-${i}`,
      title: `Show ${i}`,
      grouping: '',
      genre: 'Technology',
      description: '',
      downloaded_count: i,
    })),
  }
}

function episodes(count: number, total = count) {
  return {
    total,
    offset: 0,
    items: Array.from({ length: count }, (_, i) => ({
      index: i,
      id: `ep-${i}`,
      title: `Episode ${i}`,
      date: '2026-01-15T00:00:00Z',
      description: '',
      duration_ms: 60_000,
      is_downloaded: false,
      has_been_played: false,
    })),
  }
}

describe('podcast store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    call.mockReset()
  })

  it('reads subscriptions and knows the server holds more', async () => {
    call.mockResolvedValueOnce(subscriptions(2, 5))
    const podcast = usePodcastStore()
    await podcast.load()

    expect(podcast.total).toBe(5)
    expect(podcast.subscriptions).toHaveLength(2)
    expect(podcast.hasMore).toBe(true)
  })

  it('continues the list from where it stopped rather than re-reading it', async () => {
    call.mockResolvedValueOnce(subscriptions(2, 5))
    const podcast = usePodcastStore()
    await podcast.load()

    call.mockResolvedValueOnce(subscriptions(3, 5))
    await podcast.load(true)

    expect(lastCall()[1]).toMatchObject({ offset: 2 })
    expect(podcast.subscriptions).toHaveLength(5)
    expect(podcast.hasMore).toBe(false)
  })

  /** A slow feed must never show the previous subscription's episodes. */
  it('drops the episodes held when a different subscription is opened', async () => {
    call.mockResolvedValueOnce(episodes(3))
    const podcast = usePodcastStore()
    await podcast.openSubscription('sub-0')
    expect(podcast.episodes).toHaveLength(3)

    let held: number | undefined = undefined
    call.mockImplementationOnce(async () => {
      held = podcast.episodes.length
      return episodes(1)
    })
    await podcast.openSubscription('sub-1')

    expect(held).toBe(0)
    expect(podcast.openId).toBe('sub-1')
  })

  it('appends the next page of episodes to the ones already read', async () => {
    call.mockResolvedValueOnce(episodes(2, 4))
    const podcast = usePodcastStore()
    await podcast.openSubscription('sub-0')
    expect(podcast.hasMoreEpisodes).toBe(true)

    call.mockResolvedValueOnce(episodes(2, 4))
    await podcast.openSubscription('sub-0', true)
    expect(lastCall()[1]).toMatchObject({ id: 'sub-0', offset: 2 })
    expect(podcast.episodes).toHaveLength(4)
  })

  it('plays an episode of the open subscription, now unless told otherwise', async () => {
    call.mockResolvedValueOnce(episodes(2))
    const podcast = usePodcastStore()
    await podcast.openSubscription('sub-0')

    call.mockResolvedValueOnce({})
    await podcast.play(1)
    expect(lastCall()).toStrictEqual([
      'podcast_episode_play',
      { id: 'sub-0', index: 1, mode: QueueMode.Now },
    ])

    call.mockResolvedValueOnce({})
    await podcast.play(0, QueueMode.Last)
    expect(lastCall()[1]).toMatchObject({ mode: QueueMode.Last })
  })

  /** Without a subscription open there is no id to play an index against. */
  it('plays nothing when no subscription is open', async () => {
    const podcast = usePodcastStore()
    await podcast.play(0)
    expect(call).not.toHaveBeenCalled()
  })
})
