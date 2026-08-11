import type { SuggestionTarget } from '@/api/types';

/**
 * How a suggestion reads, on both ends of it (plan 10).
 *
 * The interesting part is that a queue suggestion's `position` is a **number on
 * the wire and never a number in the interface**. It is read once, against a
 * queue the suggester cannot see, clamped to that queue's length, and never
 * re-asserted afterwards — so "put this at 7" is a promise the data cannot keep.
 * Three coarse intentions survive all of that honestly, which is why this module
 * translates in both directions rather than showing the stored integer anywhere.
 */

/** What the suggester meant, in their own terms. */
export type QueuePlacement = 'top' | 'middle' | 'end';

/** "for your queue" — the list a suggestion names, as it reads in a sentence. */
export function targetLabel(target: SuggestionTarget): string {
  return target === 'queue' ? 'queue' : 'watchlist';
}

/**
 * A placement as the 0-based index to send.
 *
 * `end` is `null` rather than `queueLength`: appending is what the server does
 * with no position at all, and sending a number that happens to equal the length
 * would claim an intent the user did not express — the queue may well have grown
 * by the time it lands.
 *
 * `middle` is clamped to at least 1 so it can never collide with `top` on a queue
 * of one, where "fairly soon" and "next up" would otherwise mean the same thing.
 */
export function placementPosition(
  placement: QueuePlacement,
  queueLength: number
): number | null {
  if (placement === 'end') return null;
  if (placement === 'top') return 0;
  return Math.max(1, Math.floor(Math.max(queueLength, 0) / 2));
}

/**
 * The reverse, for the recipient: what the stored position was asking for.
 *
 * Only ever "next up" or "fairly soon" — a null position produced no intent to
 * report, and reading an exact slot back to the recipient would invite them to
 * treat it as a rule when the server has already clamped it.
 */
export function intentLabel(target: SuggestionTarget, position: number | null): string {
  const where = `for your ${targetLabel(target)}`;
  if (target !== 'queue' || position === null) return where;
  return position === 0 ? `${where}, next up` : `${where}, fairly soon`;
}
