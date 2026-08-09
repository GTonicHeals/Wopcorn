/**
 * The missing-poster treatment (fe-06, task 2).
 *
 * A film without artwork gets a duotone drawn deterministically from its release
 * decade, with the title set small in the display face — never a broken image and
 * never a grey box. These are poster-area colours, not UI tokens: they carry no
 * text of their own beyond a 50%-opacity wordmark, so they need no contrast check.
 */

type Duotone = { from: string; to: string };

const PRE_1970: Duotone = { from: '#8A8560', to: '#1C1B10' };

const BY_DECADE: Record<number, Duotone> = {
  1970: { from: '#5A5A3C', to: '#222416' },
  1980: { from: '#4A7DA6', to: '#0A1522' },
  1990: { from: '#7A1E2B', to: '#1E060A' },
  2000: { from: '#3FA08A', to: '#0A241C' }
};

/** 2010s and later, and anything with no year at all. */
const MODERN: Duotone = { from: '#D98A3E', to: '#2B1608' };

export function decadeDuotone(releaseYear: number | null | undefined): Duotone {
  if (releaseYear === null || releaseYear === undefined || !Number.isFinite(releaseYear)) {
    return MODERN;
  }
  if (releaseYear < 1970) return PRE_1970;
  if (releaseYear >= 2010) return MODERN;

  const decade = Math.floor(releaseYear / 10) * 10;
  return BY_DECADE[decade] ?? MODERN;
}

/** The gradient exactly as the plan specifies it. */
export function decadeGradient(releaseYear: number | null | undefined): string {
  const { from, to } = decadeDuotone(releaseYear);
  return `linear-gradient(160deg, ${from}, ${to})`;
}

/**
 * The wordmark printed on a placeholder. Long titles would wrap into the poster
 * at 0.24em tracking, so they are cut to the first few words.
 */
export function placeholderLabel(title: string): string {
  const trimmed = title.trim();
  if (trimmed.length <= 18) return trimmed;

  const words = trimmed.split(/\s+/);
  const kept: string[] = [];
  let length = 0;

  for (const word of words) {
    if (length > 0 && length + 1 + word.length > 18) break;
    kept.push(word);
    length += (length > 0 ? 1 : 0) + word.length;
  }

  return kept.length > 0 ? kept.join(' ') : trimmed.slice(0, 18);
}
