import { describe, expect, it } from 'vitest';

import { intentLabel, placementPosition, targetLabel } from '@/lib/suggestions';

/**
 * The translation between "how soon" and a queue index (plan 10).
 *
 * Worth its own module and its own suite because the number never appears in the
 * interface at either end: the suggester picks an intention, the server clamps
 * whatever integer that produces, and the recipient reads an intention back.
 * Three coarse buckets are the only thing that survives the round trip intact.
 */

describe('placementPosition', () => {
  it('sends nothing at all for "no rush"', () => {
    // Not `queueLength`: appending is what an absent position already means, and
    // a number equal to today's length would claim an intent nobody expressed.
    expect(placementPosition('end', 0)).toBeNull();
    expect(placementPosition('end', 12)).toBeNull();
  });

  it('puts "next up" at the front', () => {
    expect(placementPosition('top', 0)).toBe(0);
    expect(placementPosition('top', 12)).toBe(0);
  });

  it('aims "fairly soon" at the middle', () => {
    expect(placementPosition('middle', 12)).toBe(6);
    expect(placementPosition('middle', 7)).toBe(3);
  });

  it('never lets "fairly soon" collide with "next up"', () => {
    // On a queue of nought or one the midpoint is 0, which would make the two
    // choices mean the same thing and the second one a lie.
    expect(placementPosition('middle', 0)).toBe(1);
    expect(placementPosition('middle', 1)).toBe(1);
    expect(placementPosition('middle', 2)).toBe(1);
  });

  it('treats a nonsensical length as an empty queue', () => {
    expect(placementPosition('middle', -5)).toBe(1);
  });
});

describe('intentLabel', () => {
  it('names the list a suggestion is for', () => {
    expect(intentLabel('watchlist', null)).toBe('for your watchlist');
    expect(intentLabel('queue', null)).toBe('for your queue');
  });

  it('reads a queue position back as an intention, never as a slot', () => {
    expect(intentLabel('queue', 0)).toBe('for your queue, next up');
    expect(intentLabel('queue', 4)).toBe('for your queue, fairly soon');
    // The stored integer is deliberately absent from both.
    expect(intentLabel('queue', 4)).not.toContain('4');
  });

  it('ignores a position on a watchlist suggestion', () => {
    // The server discards it too — the watchlist has no order to have an
    // opinion about.
    expect(intentLabel('watchlist', 3)).toBe('for your watchlist');
  });
});

describe('targetLabel', () => {
  it('reads as the tail of a sentence', () => {
    expect(targetLabel('queue')).toBe('queue');
    expect(targetLabel('watchlist')).toBe('watchlist');
  });
});
