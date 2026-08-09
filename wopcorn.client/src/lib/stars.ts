/**
 * The half-star arithmetic (fe-06, task 3).
 *
 * Ratings are integers 1–10 on the wire and in the database; halves and stars
 * exist only here (FR-E2). Kept as pure functions so the pointer→value mapping —
 * the thing most likely to be built wrong — is unit-testable without a DOM.
 */

export const MIN_RATING = 1;
export const MAX_RATING = 10;

export function clampRating(value: number): number {
  if (!Number.isFinite(value)) return MIN_RATING;
  return Math.min(MAX_RATING, Math.max(MIN_RATING, Math.round(value)));
}

/**
 * Pointer x → half-star value across the full width of the control, including
 * both edges. The hit area is the whole row, not the glyphs, which is what makes
 * this reliable under a thumb.
 */
export function valueFromPointer(clientX: number, rect: { left: number; width: number }): number {
  if (rect.width <= 0) return MIN_RATING;
  const ratio = (clientX - rect.left) / rect.width;
  return clampRating(Math.ceil(ratio * MAX_RATING));
}

/** How much of the five-star row is filled, as a percentage. */
export function fillPercent(value: number | null | undefined): number {
  if (value === null || value === undefined || !Number.isFinite(value)) return 0;
  return Math.min(100, Math.max(0, (value / MAX_RATING) * 100));
}

/** `aria-valuetext`, and the sentence a screen reader announces. */
export function ratingText(value: number | null | undefined): string {
  if (value === null || value === undefined) return 'Not rated';

  const whole = Math.floor(value / 2);
  const half = value % 2 === 1;

  if (whole === 0) return 'Half a star';
  if (!half) return `${whole} ${whole === 1 ? 'star' : 'stars'}`;
  return `${whole} and a half stars`;
}

/**
 * The histogram's axis label: `"0.5"`, `"1.0"`, `"1.5"` … `"5.0"`.
 *
 * Always one decimal, so ten labels stack into one straight column under
 * `font-variant-numeric: tabular-nums`. The vulgar-fraction form this used to
 * return (`"4½"`) set a different width on every other row and, with the display
 * fonts unshipped, drew the ½ from whatever fallback the platform had — a ragged
 * edge down the side of an otherwise regular chart.
 *
 * This is the *visual* label only. What a screen reader hears is
 * {@link ratingText}, which still says "4 and a half stars".
 */
export function starLabel(value: number): string {
  return (value / 2).toFixed(1);
}
