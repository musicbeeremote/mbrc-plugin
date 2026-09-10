import { describe, expect, it } from 'vitest'

import { OpError, decodeJson, parseError, parseResponse } from './parse'

const STATUS = {
  play_state: 'playing',
  volume: 42,
  muted: false,
  shuffle: 'off',
  repeat: 'none',
  scrobbling: true,
}

describe('parsing an op response', () => {
  it('returns the payload when it is the shape the op promises', () => {
    expect(parseResponse('player_status', STATUS)).toStrictEqual(STATUS)
  })

  // The wire is the one place data arrives unchecked. A payload that is not the
  // promised shape would otherwise surface much later, as a render of
  // `undefined` somewhere unrelated to the op that returned it.
  it('refuses a payload that is missing a field, naming the field', () => {
    expect(() => parseResponse('player_status', { volume: 42 })).toThrow(OpError)
    expect(() => parseResponse('player_status', { volume: 42 })).toThrow(
      expect.objectContaining({ code: 'internal', field: 'play_state' }),
    )
  })

  it('refuses a field of the wrong type', () => {
    expect(() => parseResponse('player_set_volume', { volume: '42' })).toThrow(OpError)
  })

  // V6 grows additively, so a field this build has not heard of is dropped
  // rather than treated as an error: a client that refused one could never have
  // anything added to it.
  it('drops a field this build does not know and keeps the rest', () => {
    expect(parseResponse('player_status', { ...STATUS, invented_later: true })).toStrictEqual(
      STATUS,
    )
  })

  // A command answers `{}` today and may answer something later.
  it('accepts anything for an op that answers nothing', () => {
    expect(parseResponse('player_play', {})).toStrictEqual({})
    expect(parseResponse('player_play', undefined)).toStrictEqual({})
  })

  it('parses a page by parsing every item in it', () => {
    const page = { total: 1, offset: 0, items: [{ genre: 'Jazz', count: 5 }] }
    expect(parseResponse('library_genres', page)).toStrictEqual(page)
    expect(() => parseResponse('library_genres', { total: 1, offset: 0, items: [{}] })).toThrow(
      OpError,
    )
  })

  // The server omits `lines` entirely when a track has no lyrics, and every
  // reader treats them as a list. Without a default that is a TypeError on the
  // first track nobody has written words for.
  it('reads lyrics that carry no lines at all', () => {
    expect(parseResponse('now_playing_lyrics', { type: 'none' })).toStrictEqual({
      type: 'none',
      lines: [],
    })
  })
})

describe('parsing an error', () => {
  it('reads a well-formed protocol error', () => {
    expect(parseError({ code: 'stale_list', message: 'the queue moved' })).toStrictEqual({
      code: 'stale_list',
      message: 'the queue moved',
    })
  })

  // Refusing to parse an error would turn a server that reported a problem into
  // a client that reports nothing, so an unknown code is kept as it arrived.
  it('keeps a code this build predates', () => {
    expect(parseError({ code: 'invented_later', message: 'hm' })).toMatchObject({
      code: 'invented_later',
    })
  })

  it('yields nothing for something that is not an error at all', () => {
    expect(parseError({ message: 'no code' })).toBeNull()
    expect(parseError(undefined)).toBeNull()
  })
})

describe('decoding a frame', () => {
  // One malformed frame must not take the socket down with it.
  it('yields nothing rather than throwing on a frame that is not JSON', () => {
    expect(decodeJson('not json')).toBeNull()
    expect(decodeJson('{"a":1}')).toStrictEqual({ a: 1 })
  })
})
