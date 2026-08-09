import { describe, expect, it } from 'vitest';

import { describeTasteMatch, sortableScore, tasteMatchLabel } from '@/lib/tasteMatch';
import type { TasteMatch } from '@/api/types';

function match(score: number | null, sharedCount: number, qualified: boolean): TasteMatch {
  return { score, sharedCount, qualified };
}

/**
 * FR-G6 is a truthfulness requirement, so these are the tests that matter most
 * on this screen: a percentage may never appear without its sample size, and may
 * never appear at all below the overlap threshold.
 */
describe('describeTasteMatch (FR-G5, FR-G6)', () => {
  it('renders a qualified score with its sample size', () => {
    const display = describeTasteMatch(match(78, 24, true));

    expect(display).toEqual({
      kind: 'match',
      headline: '78% match',
      detail: 'based on 24 titles'
    });
  });

  it('never emits a percentage when qualified is false', () => {
    // The server still sends a score below the threshold; the client must not
    // headline it. MinimumOverlap is 5, so 4 shared films is unqualified.
    const display = describeTasteMatch(match(91, 4, false));

    expect(display).toEqual({
      kind: 'unqualified',
      text: 'Not enough overlap yet — 4 titles in common'
    });
    expect(JSON.stringify(display)).not.toContain('%');
    expect(JSON.stringify(display)).not.toContain('91');
  });

  it('says so plainly when there is no overlap at all', () => {
    // score is null exactly when the pair share nothing.
    expect(describeTasteMatch(match(null, 0, false))).toEqual({
      kind: 'none',
      text: 'Nothing in common yet'
    });
  });

  it('treats a zero sharedCount as no overlap even if a score came with it', () => {
    expect(describeTasteMatch(match(50, 0, false))?.kind).toBe('none');
  });

  it('suppresses the number for a qualified match with no score', () => {
    // Cannot happen server-side, but silence beats rendering "null% match".
    expect(describeTasteMatch(match(null, 30, true))?.kind).toBe('none');
  });

  it('says "1 title in common" rather than "1 films"', () => {
    expect(describeTasteMatch(match(60, 1, false))).toEqual({
      kind: 'unqualified',
      text: 'Not enough overlap yet — 1 title in common'
    });
  });

  it('has nothing to say without a match object', () => {
    expect(describeTasteMatch(null)).toBeNull();
    expect(describeTasteMatch(undefined)).toBeNull();
  });
});

describe('tasteMatchLabel', () => {
  it('keeps the score and its basis in one announcement', () => {
    // A screen reader must not be able to hear the percentage on its own.
    expect(tasteMatchLabel(describeTasteMatch(match(78, 24, true)))).toBe(
      '78% match, based on 24 titles'
    );
  });

  it('reads the unqualified text verbatim', () => {
    expect(tasteMatchLabel(describeTasteMatch(match(91, 3, false)))).toBe(
      'Not enough overlap yet — 3 titles in common'
    );
  });
});

describe('sortableScore (FR-G6: never rank by an unqualified score)', () => {
  it('yields a number only for a qualified match', () => {
    expect(sortableScore(match(78, 24, true))).toBe(78);
    expect(sortableScore(match(91, 3, false))).toBeNull();
    expect(sortableScore(match(null, 0, false))).toBeNull();
    expect(sortableScore(null)).toBeNull();
  });
});
