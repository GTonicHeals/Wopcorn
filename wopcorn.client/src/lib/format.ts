/**
 * Presentation formatting shared by cards, list rows, and the film screen.
 * Pure functions — no store, no DOM.
 */

/** `155` → `"2h 35m"`, `35` → `"35m"`, `120` → `"2h"`. Null in, null out. */
export function formatRuntime(minutes: number | null | undefined): string | null {
  if (minutes === null || minutes === undefined || !Number.isFinite(minutes) || minutes <= 0) {
    return null;
  }

  const whole = Math.round(minutes);
  const hours = Math.floor(whole / 60);
  const rest = whole % 60;

  if (hours === 0) return `${rest}m`;
  if (rest === 0) return `${hours}h`;
  return `${hours}h ${rest}m`;
}

/** TMDB's vote average, one decimal. */
export function formatScore(score: number | null | undefined): string | null {
  if (score === null || score === undefined || !Number.isFinite(score) || score <= 0) return null;
  return score.toFixed(1);
}

/** `"2026-01-12T…"` → `"12 Jan"`. Used for "added {date}" on the queue hero. */
export function formatShortDate(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return null;
  return new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short' }).format(date);
}

/** `"2026-01-12T…"` → `"12 January 2026"`. Used for "Friends since". */
export function formatFullDate(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return null;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'long' }).format(date);
}

/** `2021` → `"2021"`; an unknown year is simply absent from the meta row. */
export function formatYear(year: number | null | undefined): string | null {
  return year === null || year === undefined ? null : String(year);
}

/** Joins the parts of a meta row, dropping the ones that have no value. */
export function metaLine(parts: (string | null | undefined)[]): string {
  return parts.filter((part): part is string => Boolean(part)).join(' · ');
}

/** `1` → `"1 title"`, `84` → `"84 titles"`. */
export function titleCount(count: number): string {
  return `${count} ${count === 1 ? 'title' : 'titles'}`;
}

/**
 * The header beside a list: `"92 titles · 214h 51m"`.
 *
 * The total sums **only the runtimes that are known**, and it will understate —
 * a series whose `episode_run_time` is empty contributes nothing. That is better
 * than inventing episode lengths and better than hiding the total, so the count
 * counts titles and the duration says what it can. With nothing known at all the
 * duration is dropped rather than shown as `0m`.
 */
export function listTotals(runtimes: (number | null)[]): string {
  const known = runtimes.filter((m): m is number => typeof m === 'number' && m > 0);
  const minutes = known.reduce((sum, m) => sum + m, 0);

  return metaLine([titleCount(runtimes.length), formatRuntime(minutes)]);
}

/** `5` → `"5 seasons"`, `1` → `"1 season"`. */
export function seasonCount(count: number): string {
  return `${count} ${count === 1 ? 'season' : 'seasons'}`;
}

/** `13` → `"13 episodes"`, `1` → `"1 episode"`. */
export function episodeCount(count: number): string {
  return `${count} ${count === 1 ? 'episode' : 'episodes'}`;
}
