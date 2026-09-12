/**
 * How long a list runs, in hours and minutes.
 *
 * The queue and a playlist ask the same question and must answer it the same
 * way. Not read to the second: a number that ticks down a digit at a time while
 * nothing is happening reads as a countdown rather than a length.
 */

import { useI18n } from 'vue-i18n'

/**
 * Whole minutes, split at the hour.
 *
 * Nothing at all for a length of nothing, and nothing for a value that is not a
 * length: a header saying "NaN h NaN min" is worse than one saying only how many
 * tracks there are.
 */
export function runTimeParts(ms: number | null | undefined): { hours: number; minutes: number } | undefined {
  if (ms === null || ms === undefined || !Number.isFinite(ms) || ms <= 0) return undefined
  const minutes = Math.round(ms / 60000)
  return { hours: Math.floor(minutes / 60), minutes: minutes % 60 }
}

/** The same length as a translated string, empty when there is none. */
export function useRunTime(): (ms: number | null | undefined) => string {
  const { t } = useI18n()
  return (ms) => {
    const parts = runTimeParts(ms)
    if (parts === undefined) return ''
    return parts.hours === 0
      ? t('common.runTime.minutes', parts.minutes)
      : t('common.runTime.hoursMinutes', { h: parts.hours, m: parts.minutes })
  }
}
