import type { TagValue } from '../api/tags'

function text(value: TagValue | undefined): string {
  return typeof value === 'string' ? value : ''
}

/**
 * The text fields after the stored values change from `before` to `values`.
 *
 * A field the user has typed in keeps what they typed: a reload lands about
 * two seconds after every save, and would otherwise wipe the next field being
 * edited. A field still showing what was stored follows the new value.
 */
export function followStoredValues(
  drafts: Record<string, string>,
  values: Record<string, TagValue>,
  before: Record<string, TagValue> | undefined,
): Record<string, string> {
  const next = { ...drafts }
  for (const [key, value] of Object.entries(values)) {
    const untouched = next[key] === undefined || next[key] === text(before?.[key])
    if (untouched) next[key] = text(value)
  }
  return next
}

/** The text fields for a freshly read track: what it has stored, nothing typed. */
export function draftsOf(values: Record<string, TagValue>): Record<string, string> {
  return Object.fromEntries(Object.entries(values).map(([key, value]) => [key, text(value)]))
}
