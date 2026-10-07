import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'
import { OpError } from '../api/parse'
import { ErrorCode } from '../api/types'

import { usePermissionsStore } from './permissions'

import { useTagsStore } from './tags'

const { call, on } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data?: Record<string, unknown>) => Promise<unknown>>(),
  on: vi.fn<(event: string, listener: () => void) => () => void>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on } as unknown as typeof V6Client,
}))

const FIELDS = [
  { key: 'title', name: 'Title', multi_value: false },
  { key: 'genre', name: 'Genre', multi_value: true },
]

/** A server playing `/a.flac`, answering each op as MusicBee would. */
function serve(tags: Record<string, unknown>, write?: () => unknown) {
  call.mockImplementation(async (op, data) => {
    if (op === 'tag_fields') return { fields: FIELDS }
    if (op === 'now_playing_tags') return { path: '/a.flac', tags }
    if (op === 'tag_values') return { key: data?.key, values: [{ value: 'Rock', count: 3 }] }
    if (op === 'now_playing_set_tag') return write ? write() : { ...data }
    return {}
  })
}

function ops(): string[] {
  return call.mock.calls.map(([op]) => op)
}

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
  on.mockReset()
})

describe('the tag editor store', () => {
  it('reads the field list once and the values on every open', async () => {
    serve({ title: 'A', genre: ['Rock'] })
    const tags = useTagsStore()

    await tags.load()
    await tags.load()

    expect(ops().filter((op) => op === 'tag_fields')).toHaveLength(1)
    expect(ops().filter((op) => op === 'now_playing_tags')).toHaveLength(2)
    expect(tags.values.genre).toStrictEqual(['Rock'])
  })

  it('writes against the track it was opened on and keeps the read-back', async () => {
    serve({ title: 'A', genre: ['Rock'] }, () => ({
      path: '/a.flac',
      key: 'genre',
      value: ['Rock', 'Jazz'],
    }))
    const tags = useTagsStore()
    await tags.load()

    await tags.save('genre', ['Rock', 'Jazz'])

    expect(call).toHaveBeenCalledWith('now_playing_set_tag', {
      path: '/a.flac',
      key: 'genre',
      value: ['Rock', 'Jazz'],
    })
    expect(tags.values.genre).toStrictEqual(['Rock', 'Jazz'])
  })

  // The song changed between opening the editor and saving: nothing was
  // written, and the editor must now show the song that is playing.
  it('reloads after a write to a track no longer playing', async () => {
    serve({ title: 'A' }, () => {
      throw new OpError({ code: ErrorCode.StaleTrack, message: 'moved on' })
    })
    const tags = useTagsStore()
    await tags.load()
    call.mockClear()

    await tags.save('title', 'B')

    expect(tags.refusal).toMatchObject({ key: 'title', code: ErrorCode.StaleTrack })
    expect(ops()).toContain('now_playing_tags')
  })

  it('asks for a field\'s values once, when it is first edited', async () => {
    serve({})
    const tags = useTagsStore()

    await tags.loadSuggestions('genre')
    await tags.loadSuggestions('genre')

    expect(ops().filter((op) => op === 'tag_values')).toHaveLength(1)
    expect(tags.suggestions.genre).toStrictEqual([{ value: 'Rock', count: 3 }])
  })

  // An edit in MusicBee's own window, or from another client.
  it('reloads on a tag change only while the editor is open', async () => {
    serve({})
    const tags = useTagsStore()
    tags.bind()
    const changed = on.mock.calls.find(([event]) => event === 'now_playing_tags_changed')?.[1]

    changed?.()
    expect(ops()).not.toContain('now_playing_tags')

    tags.show()
    await vi.waitFor(() => expect(ops()).toContain('now_playing_tags'))
    call.mockClear()
    changed?.()
    await vi.waitFor(() => expect(ops()).toContain('now_playing_tags'))
  })

  // Party Mode keeps tag edits for the host; a guest still reads them.
  it('locks editing for a Party Mode guest and says it is Party Mode', async () => {
    serve({ title: 'A' })
    const tags = useTagsStore()
    await tags.load()
    expect(tags.canEdit).toBe(true)

    usePermissionsStore().apply({ party_mode: true, role: 'guest', allowed: ['queue_add'] })

    expect(tags.canEdit).toBe(false)
    expect(tags.lockReason).toBe('party')
    usePermissionsStore().apply({ party_mode: false, role: 'host', allowed: [] })
    expect(tags.lockReason).toBe('denied')
  })
})
