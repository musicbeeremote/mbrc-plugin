/**
 * Translations, one message module per feature.
 *
 * Nobody may ever translate this, exactly as with the Android app, so the point
 * of the setup is that adding a language later is a directory and a line in
 * `SUPPORTED_LOCALES` rather than a sweep through every component for hardcoded
 * English.
 *
 * Locales other than the fallback load on demand, so a language nobody selects
 * costs the bundle nothing. English is bundled because it is the fallback and
 * the app must render before any async work completes.
 */

import { createI18n } from 'vue-i18n'

import en from './locales/en'

export const DEFAULT_LOCALE = 'en'

/** Locale code to display name, in that locale. Add a language by adding a row. */
export const SUPPORTED_LOCALES: Record<string, string> = {
  en: 'English',
}

const LOCALE_KEY = 'mbrc.locale'

/**
 * Every locale directory, as a lazy import each.
 *
 * A glob rather than a template-literal import so the bundler can see the set
 * at build time and give each locale its own chunk; a bare dynamic import on a
 * runtime string cannot be split and ends up in the entry bundle.
 */
const LOCALE_LOADERS = import.meta.glob<{ default: unknown }>('./locales/*/index.ts') as Record<
  string,
  () => Promise<{ default: MessageSchema }>
>

/**
 * The locale to open with: the one the user chose, else the closest match for
 * the browser's own preference, else the fallback.
 */
export function initialLocale(): string {
  try {
    const stored = localStorage.getItem(LOCALE_KEY)
    if (stored && stored in SUPPORTED_LOCALES) return stored
  } catch {
    /* private browsing: fall through to the browser preference */
  }
  for (const candidate of navigator.languages ?? []) {
    const [base] = candidate.split('-')
    if (base && base in SUPPORTED_LOCALES) return base
  }
  return DEFAULT_LOCALE
}

/** English is the schema every other locale is checked against. */
export type MessageSchema = typeof en

// Typed as a record rather than the literal `{ en }`, so the locale parameter
// stays `string`. Inferred from the literal it would narrow to `'en'` and
// `setLocale` could never be handed anything else.
const messages: Record<string, MessageSchema> = { en }

export const i18n = createI18n<[MessageSchema], string>({
  // Composition API mode: `legacy: true` would put `$t` on the component
  // instance and is not what `useI18n` expects.
  legacy: false,
  locale: initialLocale(),
  fallbackLocale: DEFAULT_LOCALE,
  messages,
})

/** Switches locale, loading its messages the first time it is asked for. */
export async function setLocale(locale: string): Promise<void> {
  if (!(locale in SUPPORTED_LOCALES)) return
  if (!i18n.global.availableLocales.includes(locale)) {
    const load = LOCALE_LOADERS[`./locales/${locale}/index.ts`]
    if (!load) return
    const loaded = await load()
    i18n.global.setLocaleMessage(locale, loaded.default)
  }
  i18n.global.locale = locale
  try {
    localStorage.setItem(LOCALE_KEY, locale)
  } catch {
    /* the choice applies to this visit even when it cannot be remembered */
  }
}
