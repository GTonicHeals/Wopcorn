import { titleCount } from '@/lib/format';
import type { TasteMatch } from '@/api/types';

/**
 * FR-G5/FR-G6, as one pure function (fe-07, task 4).
 *
 * The requirement is about honesty, so the rules are absolute:
 *
 * - a percentage is **never** produced without its sample size beside it;
 * - below the overlap threshold the percentage is not produced **at all** — the
 *   server still sends a `score`, and `qualified: false` is its instruction to
 *   the client to suppress it;
 * - `score: null` means the pair share no rated titles at all.
 *
 * This lives here rather than in the component so the unqualified case can be
 * tested directly. `TasteMatch.vue` renders the result and nothing else — no
 * caller may format a score by hand.
 */
export type TasteMatchDisplay =
  /** Above the threshold: a headline percentage and its sample size. */
  | { kind: 'match'; headline: string; detail: string }
  /** Some overlap, but not enough for a number. */
  | { kind: 'unqualified'; text: string }
  /** No shared rated titles at all. */
  | { kind: 'none'; text: string };

/** `3` → `"3 titles in common"`, `1` → `"1 title in common"`. */
function inCommon(sharedCount: number): string {
  return `${titleCount(sharedCount)} in common`;
}

/**
 * Returns `null` when there is nothing to say — a missing match object. Every
 * other case produces text, because "no overlap yet" is information too.
 */
export function describeTasteMatch(
  match: TasteMatch | null | undefined
): TasteMatchDisplay | null {
  if (!match) return null;

  const shared = Number.isFinite(match.sharedCount) ? Math.max(0, match.sharedCount) : 0;

  // `score: null` is exactly the zero-overlap case. A qualified match with no
  // score cannot happen, but if the server ever said so, silence beats a "null%".
  if (match.score === null || match.score === undefined || shared === 0) {
    return { kind: 'none', text: 'Nothing in common yet' };
  }

  if (!match.qualified) {
    return { kind: 'unqualified', text: `Not enough overlap yet — ${inCommon(shared)}` };
  }

  return {
    kind: 'match',
    headline: `${Math.round(match.score)}% match`,
    detail: `based on ${titleCount(shared)}`
  };
}

/**
 * The single sentence a screen reader hears, so the percentage and its sample
 * size arrive together in the accessibility tree as well as on screen.
 */
export function tasteMatchLabel(display: TasteMatchDisplay | null): string {
  if (!display) return '';
  return display.kind === 'match' ? `${display.headline}, ${display.detail}` : display.text;
}

/**
 * FR-G6's last clause: friends are **never** ordered by an unqualified score.
 * The server already sorts by display name; this exists so that any future
 * "sort by match" has one honest implementation to reach for.
 */
export function sortableScore(match: TasteMatch | null | undefined): number | null {
  return match && match.qualified && match.score !== null ? match.score : null;
}
