/**
 * The regions the settings screen offers.
 *
 * TMDB publishes availability per ISO-3166-1 region and there is no endpoint in
 * this app's contract that enumerates them, so this list is a UI affordance
 * rather than a source of truth: it covers the markets JustWatch actually has
 * data for, and the server rejects a region it cannot serve with
 * `400 validation_failed` regardless of what is offered here. Adding a market is
 * one line.
 *
 * Names come from `Intl.DisplayNames` so they read in the user's own language,
 * with the bare code as the fallback wherever that is unavailable.
 */
const CODES = [
  'AE', 'AR', 'AT', 'AU', 'BE', 'BR', 'CA', 'CH', 'CL', 'CO', 'CZ', 'DE', 'DK',
  'EE', 'EG', 'ES', 'FI', 'FR', 'GB', 'GR', 'HK', 'HU', 'ID', 'IE', 'IL', 'IN',
  'IT', 'JP', 'KR', 'LT', 'LV', 'MX', 'MY', 'NL', 'NO', 'NZ', 'PE', 'PH', 'PL',
  'PT', 'RO', 'RU', 'SA', 'SE', 'SG', 'SK', 'TH', 'TR', 'TW', 'UA', 'US', 'VE',
  'ZA'
];

export type RegionOption = { code: string; name: string };

let displayNames: Intl.DisplayNames | null | undefined;

/** The localised name of a region code, or the code itself. */
export function regionName(code: string): string {
  if (displayNames === undefined) {
    try {
      displayNames = new Intl.DisplayNames(undefined, { type: 'region' });
    } catch {
      // Not every runtime has it — jsdom in particular.
      displayNames = null;
    }
  }

  return displayNames?.of(code) ?? code;
}

/**
 * Every offered region, sorted by name in the user's locale. `current` is
 * appended when it is not one of ours, so a region set from another device (or
 * by a future version of this list) is never silently swapped for something else.
 */
export function regionOptions(current?: string | null): RegionOption[] {
  const codes = current && !CODES.includes(current) ? [...CODES, current] : CODES;

  return codes
    .map((code) => ({ code, name: regionName(code) }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

/**
 * The region subtag of the browser's language, upper-cased — `en-GB` gives `GB`.
 *
 * This is only ever a **pre-selection the user confirms**, never a silent
 * default: setup nobody completes is a feature nobody has, but a region guessed
 * wrong and applied without asking is a wrong answer presented as a fact.
 */
export function guessRegion(language?: string | null): string | null {
  // Only an omitted argument reaches for the browser; an explicit null is a
  // caller saying "there is no tag", which must not become a guess.
  const tag =
    language === undefined
      ? typeof navigator === 'undefined'
        ? null
        : navigator.language
      : language;

  if (!tag) return null;

  // `en-GB`, `zh-Hant-TW`, `en-Latn-US` — the region subtag is the two-letter one.
  const region = tag
    .split('-')
    .slice(1)
    .find((part) => /^[A-Za-z]{2}$/.test(part));

  return region ? region.toUpperCase() : null;
}
