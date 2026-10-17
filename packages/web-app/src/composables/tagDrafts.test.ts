import { describe, expect, it } from 'vitest'

import { draftsOf, followStoredValues } from './tagDrafts'

describe('the tag text fields', () => {
  // The reload after a save lands while the user is typing in the next field.
  it('keep what the user typed when the stored values reload', () => {
    const before = { title: 'Old', album: 'Album' }
    const typing = { title: 'Old', album: 'Album, being retyped' }

    const after = followStoredValues(typing, { title: 'New', album: 'Album' }, before)

    expect(after).toStrictEqual({ title: 'New', album: 'Album, being retyped' })
  })

  it('show a field the reload adds, and a multi-value field as no text', () => {
    expect(followStoredValues({}, { title: 'T', genre: ['Rock'] }, undefined)).toStrictEqual({
      title: 'T',
      genre: '',
    })
  })

  it('start from the stored values for a new track', () => {
    expect(draftsOf({ title: 'T', genre: ['Rock'] })).toStrictEqual({ title: 'T', genre: '' })
  })
})
