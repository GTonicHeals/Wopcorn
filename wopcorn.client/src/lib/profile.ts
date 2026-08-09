/**
 * The profile screen's arithmetic and its showcase editing rules.
 *
 * Pure functions — no store, no DOM, no component. The showcase is edited as a
 * plain array of keys and only sent when the user saves, so every one of these
 * is a value in, a value out: the sheet holds a draft, these move it about, and
 * `PUT /api/me/favorites` takes whatever it ends up as.
 */

import { formatRuntime, titleCount } from '@/lib/format';
import type { RatingStats, RuntimeOnRecord } from '@/api/types';

/**
 * Six slots, matching `FavoritesService.MaxFavorites`. It is a design number
 * rather than a storage one: the showcase is a single row of posters, and a row
 * that wraps stops being a showcase and starts being another list.
 */
export const MAX_FAVORITES = 6;

/**
 * "214h 51m on record", and whether that figure is a floor rather than a total.
 *
 * Null when nothing is known — a watched list of series with no episode length
 * has a real size and no duration at all, and `0m` would be a lie about both.
 */
export type RuntimeSummary = {
  /** The duration itself, already formatted. */
  value: string;
  /** True when some watched titles have no runtime, so `value` understates. */
  approximate: boolean;
  /** The reason, spelled out — never left to the "at least" to imply. */
  note: string | null;
};

export function runtimeOnRecord(runtime: RuntimeOnRecord | null | undefined): RuntimeSummary | null {
  if (!runtime || runtime.knownTitles <= 0) return null;

  const value = formatRuntime(runtime.minutes);
  if (!value) return null;

  const unknown = Math.max(0, runtime.unknownTitles);

  return {
    value,
    approximate: unknown > 0,
    note: unknown > 0 ? `${titleCount(unknown)} have no runtime recorded` : null
  };
}

/**
 * The average rating on the app's five-star scale — ratings are stored as 1–10
 * half-stars, and every other surface divides by two to show them.
 */
export function averageStars(stats: RatingStats | null | undefined): string | null {
  if (!stats || stats.average === null || stats.average === undefined) return null;
  if (!Number.isFinite(stats.average)) return null;
  return (stats.average / 2).toFixed(1);
}

/** `"12 March 2024"`, for the "Member since" line. */
export { formatFullDate as memberSince } from '@/lib/format';

/**
 * Bar widths for the genre block, relative to the most-watched genre rather
 * than to the watched total — the shape being compared is one taste against
 * itself, not against the whole catalogue.
 */
export function genreShares<T extends { count: number }>(
  genres: T[]
): (T & { width: string })[] {
  const largest = Math.max(1, ...genres.map((genre) => genre.count));
  return genres.map((genre) => ({ ...genre, width: `${(genre.count / largest) * 100}%` }));
}

// ------------------------------------------------------------- showcase edits
//
// Every function below returns a new array and never mutates its argument, so a
// draft can be diffed against what was saved to decide whether the save button
// does anything.

/** Adds to the end, removes if already there, and refuses a seventh. */
export function toggleFavorite(keys: string[], key: string): string[] {
  if (keys.includes(key)) {
    return keys.filter((entry) => entry !== key);
  }

  return keys.length >= MAX_FAVORITES ? keys : [...keys, key];
}

/**
 * Moves one slot by `offset`, clamped at both ends — a move off either edge is
 * a no-op rather than a wrap, because the first slot is the one the profile
 * takes its backdrop from and wrapping into it would be a surprise.
 */
export function moveFavorite(keys: string[], key: string, offset: number): string[] {
  const from = keys.indexOf(key);
  if (from < 0) return keys;

  const to = from + offset;
  if (to < 0 || to >= keys.length) return keys;

  const next = [...keys];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved!);
  return next;
}

/** True when the draft differs from what is stored, in order as well as membership. */
export function showcaseChanged(draft: string[], saved: string[]): boolean {
  return draft.length !== saved.length || draft.some((key, index) => key !== saved[index]);
}
