import { describe, expect, it } from 'vitest'

import { runTimeParts } from './runTime'

describe('runTimeParts', () => {
  it('has nothing to say about a list with no length', () => {
    expect(runTimeParts(0)).toBeUndefined()
    expect(runTimeParts(-1)).toBeUndefined()
  })

  /** A header reading "NaN h NaN min" is worse than one with no length at all. */
  it('has nothing to say about a value that is not a length', () => {
    expect(runTimeParts(Number.NaN)).toBeUndefined()
    expect(runTimeParts(Number.POSITIVE_INFINITY)).toBeUndefined()
    expect(runTimeParts(null)).toBeUndefined()
    expect(runTimeParts(undefined)).toBeUndefined()
  })

  it('rounds to whole minutes', () => {
    expect(runTimeParts(90_000)).toStrictEqual({ hours: 0, minutes: 2 })
  })

  /** Rounding that crosses the hour must not leave "60 min" on screen. */
  it('carries a rounded-up hour rather than showing sixty minutes', () => {
    expect(runTimeParts(3_599_000)).toStrictEqual({ hours: 1, minutes: 0 })
  })

  it('splits hours from the minutes left over', () => {
    expect(runTimeParts(3_960_000)).toStrictEqual({ hours: 1, minutes: 6 })
  })
})
