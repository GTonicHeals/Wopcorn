/**
 * Feed timestamps (fe-07, task 2).
 *
 * The feed shows "2h ago"; the exact instant lives in the `title` attribute and
 * in `<time datetime>`, so nothing is actually lost by rounding. Pure functions,
 * so the boundaries are unit-testable without a DOM or a fake clock in a view.
 */

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;
const WEEK = 7 * DAY;
/** Display-only averages. Nothing is scheduled or compared against these. */
const MONTH = 30 * DAY;
const YEAR = 365 * DAY;

function plural(value: number, unit: string): string {
  return `${value} ${unit}${value === 1 ? '' : 's'} ago`;
}

/**
 * `"2026-08-09T09:12:00Z"` → `"2h ago"`.
 *
 * A timestamp in the future — clock skew between the server and the device, not
 * a real event — reads as "just now" rather than a negative age.
 */
export function relativeTime(iso: string | null | undefined, now: number = Date.now()): string {
  if (!iso) return '';

  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '';

  const elapsed = now - then;
  if (elapsed < MINUTE) return 'just now';
  if (elapsed < HOUR) return `${Math.floor(elapsed / MINUTE)}m ago`;
  if (elapsed < DAY) return `${Math.floor(elapsed / HOUR)}h ago`;
  if (elapsed < WEEK) return `${Math.floor(elapsed / DAY)}d ago`;
  if (elapsed < MONTH) return plural(Math.floor(elapsed / WEEK), 'week');
  if (elapsed < YEAR) return plural(Math.floor(elapsed / MONTH), 'month');
  return plural(Math.floor(elapsed / YEAR), 'year');
}

/** The `title` attribute behind a relative timestamp — full date and time, local. */
export function absoluteTime(iso: string | null | undefined): string {
  if (!iso) return '';

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'long',
    timeStyle: 'short'
  }).format(date);
}
