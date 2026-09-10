/**
 * A short name for this browser, for the list of paired browsers in MusicBee.
 *
 * A user agent string is the wrong thing to send: it is unreadable, it runs off
 * the end of any row it is put in, and it says far more about the machine than
 * naming one browser in a list needs. Two words are enough to tell one device
 * from another, which is the only question that list has to answer.
 *
 * `userAgentData` gives those two words directly where it exists. Where it does
 * not, the user agent is read for the few names worth telling apart rather than
 * parsed properly - a wrong guess here costs a label, and nothing else reads it.
 */

interface Brand {
  brand: string
  version: string
}

interface UserAgentData {
  brands?: Brand[]
  platform?: string
}

/** Chromium lists a deliberate nonsense brand to catch exactly this parsing. */
const GREASE = /not.*brand/iu

const ENGINES: [RegExp, string][] = [
  [/\bVivaldi\b/u, 'Vivaldi'],
  [/\bEdg\b/u, 'Edge'],
  [/\bOPR\b|\bOpera\b/u, 'Opera'],
  [/\bFirefox\b/u, 'Firefox'],
  [/\bSamsungBrowser\b/u, 'Samsung Internet'],
  // Chrome claims Safari and Safari does not claim Chrome, so this order holds.
  [/\bChrome\b|\bCriOS\b/u, 'Chrome'],
  [/\bSafari\b/u, 'Safari'],
]

const PLATFORMS: [RegExp, string][] = [
  [/\bAndroid\b/u, 'Android'],
  [/\biPhone\b/u, 'iPhone'],
  [/\biPad\b/u, 'iPad'],
  [/\bWindows\b/u, 'Windows'],
  [/\bMac OS X\b|\bMacintosh\b/u, 'macOS'],
  [/\bCrOS\b/u, 'ChromeOS'],
  [/\bLinux\b/u, 'Linux'],
]

function firstMatch(text: string, table: [RegExp, string][]): string | undefined {
  return table.find(([pattern]) => pattern.test(text))?.[1]
}

/** Absent and empty mean the same here: a browser that answered with nothing. */
function said(value: string | undefined): string | undefined {
  const trimmed = value?.trim()
  return trimmed === undefined || trimmed === '' ? undefined : trimmed
}

export function describeBrowser(agent: string, data?: UserAgentData): string {
  const branded = said(data?.brands?.find((brand) => !GREASE.test(brand.brand))?.brand)
  const name = branded ?? firstMatch(agent, ENGINES)
  const platform = said(data?.platform) ?? firstMatch(agent, PLATFORMS)

  if (name && platform) return `${name} on ${platform}`
  if (name) return name
  if (platform) return `A browser on ${platform}`
  return 'A browser'
}

/** What this browser calls itself, for the pairing request. */
export function thisBrowser(): string {
  const data = (navigator as unknown as { userAgentData?: UserAgentData }).userAgentData
  return describeBrowser(navigator.userAgent, data)
}
