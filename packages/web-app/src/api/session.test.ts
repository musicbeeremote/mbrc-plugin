import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  forgetLegacyToken,
  installId,
  keepIssuedToken,
  renewIdentity,
  storedClientToken,
} from './session'

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

describe('the pairing token', () => {
  it('is thrown away where an older build left one', () => {
    localStorage.setItem('mbrc.token', 'paired-before-the-cookie')
    forgetLegacyToken()
    expect(localStorage.getItem('mbrc.token')).toBeNull()
  })
})

describe('the install id', () => {
  it('is minted once and returned unchanged after that', () => {
    const first = installId()
    expect(first).not.toBe('')
    expect(installId()).toBe(first)
  })

  // `crypto.randomUUID` is a secure-context API, so it is missing on exactly the
  // http:// LAN address a phone reaches this server by. Reaching for it there
  // threw before the app drew anything.
  it('is minted where randomUUID does not exist', () => {
    const { getRandomValues } = globalThis.crypto
    vi.stubGlobal('crypto', { getRandomValues: getRandomValues.bind(globalThis.crypto) })

    const minted = installId()
    expect(minted).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u)
  })
})

describe('the identity token', () => {
  it('is kept out of a handshake reply that carries one', () => {
    keepIssuedToken({ client_token: 'issued' })
    expect(storedClientToken()).toBe('issued')
  })

  // A reply without one means the server already knew this id, so there is
  // nothing to keep and the token we presented is still the current one.
  it('is left alone by a reply that carries none', () => {
    keepIssuedToken({ client_token: 'issued' })
    keepIssuedToken({})
    keepIssuedToken(undefined)
    expect(storedClientToken()).toBe('issued')
  })

  it('is dropped along with the id it belongs to when the identity is renewed', () => {
    const first = installId()
    keepIssuedToken({ client_token: 'issued' })

    const renewed = renewIdentity()

    expect(renewed).not.toBe(first)
    expect(storedClientToken()).toBeNull()
    expect(installId()).toBe(renewed)
  })
})
