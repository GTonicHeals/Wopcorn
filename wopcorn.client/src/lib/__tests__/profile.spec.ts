import { describe, expect, it } from 'vitest';

import {
  MAX_FAVORITES,
  averageStars,
  genreShares,
  moveFavorite,
  runtimeOnRecord,
  showcaseChanged,
  toggleFavorite
} from '@/lib/profile';

describe('runtimeOnRecord', () => {
  it('reads as a total when every watched title has a runtime', () => {
    const summary = runtimeOnRecord({ minutes: 221, knownTitles: 2, unknownTitles: 0 });

    expect(summary).toEqual({ value: '3h 41m', approximate: false, note: null });
  });

  it('flags a floor, and says why, when some runtimes are missing', () => {
    const summary = runtimeOnRecord({ minutes: 221, knownTitles: 2, unknownTitles: 3 });

    expect(summary?.approximate).toBe(true);
    expect(summary?.note).toBe('3 titles have no runtime recorded');
  });

  it('says nothing at all rather than 0m when no runtime is known', () => {
    // An entirely-series watched list: a real size, no duration. `0m` would
    // misdescribe both.
    expect(runtimeOnRecord({ minutes: 0, knownTitles: 0, unknownTitles: 12 })).toBeNull();
    expect(runtimeOnRecord(null)).toBeNull();
  });
});

describe('averageStars', () => {
  it('halves the stored 1–10 rating onto the five-star scale', () => {
    expect(averageStars({ count: 4, average: 7.5, distribution: [] })).toBe('3.8');
  });

  it('is null for someone who has rated nothing', () => {
    expect(averageStars({ count: 0, average: null, distribution: [] })).toBeNull();
  });
});

describe('genreShares', () => {
  it('scales against the most-watched genre, not the total', () => {
    const shares = genreShares([{ count: 8 }, { count: 4 }, { count: 1 }]);

    expect(shares.map((share) => share.width)).toEqual(['100%', '50%', '12.5%']);
  });

  it('survives an empty list without dividing by zero', () => {
    expect(genreShares([])).toEqual([]);
  });
});

describe('toggleFavorite', () => {
  it('adds to the end so the order is the one the user built', () => {
    expect(toggleFavorite(['movie-1'], 'movie-2')).toEqual(['movie-1', 'movie-2']);
  });

  it('removes a key that is already in the showcase', () => {
    expect(toggleFavorite(['movie-1', 'movie-2'], 'movie-1')).toEqual(['movie-2']);
  });

  it('refuses a seventh title', () => {
    const full = Array.from({ length: MAX_FAVORITES }, (_, index) => `movie-${index}`);

    expect(toggleFavorite(full, 'movie-99')).toEqual(full);
    // Removing still works when full — otherwise the showcase would be a trap.
    expect(toggleFavorite(full, 'movie-0')).toHaveLength(MAX_FAVORITES - 1);
  });
});

describe('moveFavorite', () => {
  const keys = ['a', 'b', 'c'];

  it('swaps with the neighbour in the given direction', () => {
    expect(moveFavorite(keys, 'c', -1)).toEqual(['a', 'c', 'b']);
    expect(moveFavorite(keys, 'a', 1)).toEqual(['b', 'a', 'c']);
  });

  it('clamps at both ends rather than wrapping', () => {
    // Wrapping would drop something into the first slot — the one the profile
    // takes its backdrop from — as a side effect of a nudge.
    expect(moveFavorite(keys, 'a', -1)).toEqual(keys);
    expect(moveFavorite(keys, 'c', 1)).toEqual(keys);
  });

  it('leaves the array alone when the key is not in it', () => {
    expect(moveFavorite(keys, 'z', -1)).toEqual(keys);
  });

  it('does not mutate its argument', () => {
    moveFavorite(keys, 'a', 1);
    expect(keys).toEqual(['a', 'b', 'c']);
  });
});

describe('showcaseChanged', () => {
  it('notices a reorder, not only an add or a remove', () => {
    expect(showcaseChanged(['a', 'b'], ['b', 'a'])).toBe(true);
    expect(showcaseChanged(['a', 'b'], ['a', 'b'])).toBe(false);
    expect(showcaseChanged([], [])).toBe(false);
    expect(showcaseChanged(['a'], [])).toBe(true);
  });
});
