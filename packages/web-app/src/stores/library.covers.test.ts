import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { call, handler, on } from '../api/fake-client'

import { LibraryLevel, useLibraryStore } from './library'
import { openTab } from './library.fixtures'

vi.mock(import('../api/client'), () => import('../api/fake-client'))

const TOTAL = 250

/** Answers every page of 250 albums, each with `hash` as its cover. */
function albumsCovered(hash?: string) {
  call.mockImplementation(async (_op, data) => {
    const { offset = 0, limit = 100 } = data as { offset?: number; limit?: number }
    const items = Array.from({ length: Math.min(limit, TOTAL - offset) }, (_, i) => ({
      album: `Album ${offset + i}`,
      artist: 'A',
      count: 1,
      ...(hash ? { cover_hash: hash } : {}),
    }))
    return { total: TOTAL, offset, items }
  })
}

/** A library showing the first two pages of albums, none with a cover yet. */
async function showingAlbums() {
  albumsCovered()
  const library = useLibraryStore()
  library.bind()
  await openTab(library, LibraryLevel.Albums)
  await library.load(true)
  call.mockClear()
  return library
}

/** Lets answers already given reach the store. */
async function settle() {
  await new Promise<void>((resolve) => {
    setTimeout(resolve, 0)
  })
}

async function buildFinished(building = false) {
  handler('cover_cache_changed')({ building })
  await settle()
}

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
  on.mockReset()
})

describe('when a cover build finishes', () => {
  it('shows the new covers on every row, keeping the rows on screen', async () => {
    const library = await showingAlbums()
    albumsCovered('c1')

    await buildFinished()

    expect(library.albums).toHaveLength(200)
    expect(library.albums[0]?.album).toBe('Album 0')
    expect(library.albums[199]?.album).toBe('Album 199')
    expect(library.albums.every((album) => album.cover_hash === 'c1')).toBe(true)
  })

  it('reads nothing while a build is still running', async () => {
    await showingAlbums()

    await buildFinished(true)

    expect(call).not.toHaveBeenCalled()
  })

  it('reads nothing on a level with no covers', async () => {
    albumsCovered()
    const library = useLibraryStore()
    library.bind()
    await openTab(library, LibraryLevel.Artists)
    call.mockClear()

    await buildFinished()

    expect(call).not.toHaveBeenCalled()
  })

  it('drops the answer when the list moved on meanwhile', async () => {
    const library = await showingAlbums()
    const answers: ((rows: unknown) => void)[] = []
    const unanswered = async () =>
      new Promise((resolve) => {
        answers.push(resolve)
      })
    call.mockImplementation(unanswered)
    handler('cover_cache_changed')({ building: false })
    const refreshing = [...answers]

    albumsCovered()
    await openTab(library, LibraryLevel.Albums)
    for (const answer of refreshing) {
      answer({ total: TOTAL, offset: 0, items: [{ album: 'Stale', artist: 'A', count: 1 }] })
    }
    await settle()

    expect(refreshing).toHaveLength(2)
    expect(library.albums[0]?.album).toBe('Album 0')
  })
})
