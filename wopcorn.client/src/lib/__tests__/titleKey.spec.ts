import { describe, expect, it } from 'vitest';

import {
  TITLE_KEY_PATTERN,
  format,
  isSeason,
  isSeries,
  isValid,
  parentOf,
  parse,
  titlePath
} from '@/lib/titleKey';

/**
 * The grammar is the one thing both tracks have to agree on, so it is tested
 * directly rather than through a component. The server's `TitleKey` is the other
 * half of this contract; a change here without a change there is a wire break.
 */

describe('parse', () => {
  it('reads a film', () => {
    expect(parse('movie-603')).toEqual({
      key: 'movie-603',
      mediaType: 'movie',
      tmdbId: 603,
      seasonNumber: null
    });
  });

  it('reads a series', () => {
    expect(parse('tv-1396')).toEqual({
      key: 'tv-1396',
      mediaType: 'series',
      tmdbId: 1396,
      seasonNumber: null
    });
  });

  it('reads a season, whose tmdbId is its series', () => {
    expect(parse('tv-1396-s2')).toEqual({
      key: 'tv-1396-s2',
      mediaType: 'season',
      tmdbId: 1396,
      seasonNumber: 2
    });
  });

  it('accepts season 0, which is TMDB specials', () => {
    // Not an edge case to be defended against — `-s0` is a real, legal season.
    expect(parse('tv-1396-s0')).toEqual({
      key: 'tv-1396-s0',
      mediaType: 'season',
      tmdbId: 1396,
      seasonNumber: 0
    });
  });

  it('keeps a film and a series of the same id apart', () => {
    // 1396 is Mirror (1975) as a film and Breaking Bad as a series. This is the
    // collision the whole key format exists for.
    const film = parse('movie-1396');
    const series = parse('tv-1396');

    expect(film?.tmdbId).toBe(series?.tmdbId);
    expect(film?.mediaType).not.toBe(series?.mediaType);
    expect(film?.key).not.toBe(series?.key);
  });

  it.each([
    ['tv-abc', 'a non-numeric id'],
    ['movie-1-s2', 'a film with a season'],
    ['', 'the empty string'],
    ['movie-', 'a missing id'],
    ['tv-', 'a missing id'],
    ['show-5', 'an unknown prefix'],
    ['movie-1.5', 'a decimal id'],
    ['movie-+7', 'a signed id'],
    [' movie-7', 'leading whitespace'],
    ['movie-7 ', 'trailing whitespace'],
    ['tv-01', 'a redundant leading zero'],
    ['tv-1396-s', 'a season marker with no number'],
    ['tv-1396-s2-s3', 'two season markers'],
    ['MOVIE-7', 'the wrong case']
  ])('rejects %s (%s)', (key) => {
    expect(parse(key)).toBeNull();
    expect(isValid(key)).toBe(false);
  });

  it('rejects a non-string without throwing', () => {
    expect(parse(null)).toBeNull();
    expect(parse(undefined)).toBeNull();
  });
});

describe('format', () => {
  it.each([
    ['movie-603', 'movie', 603, null],
    ['tv-1396', 'series', 1396, null],
    ['tv-1396-s2', 'season', 1396, 2],
    ['tv-1396-s0', 'season', 1396, 0]
  ] as const)('builds %s', (expected, mediaType, tmdbId, seasonNumber) => {
    expect(format(mediaType, tmdbId, seasonNumber)).toBe(expected);
  });
});

describe('round trip', () => {
  it.each(['movie-603', 'movie-1396', 'tv-1396', 'tv-1396-s0', 'tv-1396-s2', 'tv-66732-s11'])(
    '%s survives parse → format unchanged',
    (key) => {
      const parsed = parse(key);
      expect(parsed).not.toBeNull();
      expect(format(parsed!.mediaType, parsed!.tmdbId, parsed!.seasonNumber)).toBe(key);
    }
  );
});

describe('predicates', () => {
  it('identifies seasons and series', () => {
    expect(isSeason('tv-1396-s2')).toBe(true);
    expect(isSeason('tv-1396')).toBe(false);
    expect(isSeason('movie-603')).toBe(false);
    expect(isSeason('nonsense')).toBe(false);

    expect(isSeries('tv-1396')).toBe(true);
    expect(isSeries('tv-1396-s2')).toBe(false);
  });

  it('finds a season parent and nothing else', () => {
    expect(parentOf('tv-1396-s2')).toBe('tv-1396');
    expect(parentOf('tv-1396-s0')).toBe('tv-1396');

    // A film and a series have no parent — and neither does a bad key.
    expect(parentOf('tv-1396')).toBeNull();
    expect(parentOf('movie-603')).toBeNull();
    expect(parentOf('tv-abc')).toBeNull();
  });
});

describe('titlePath', () => {
  it('needs no escaping, which is why the separator is a dash', () => {
    expect(titlePath('tv-1396-s2')).toBe('/title/tv-1396-s2');
    expect(encodeURIComponent('tv-1396-s2')).toBe('tv-1396-s2');
  });
});

describe('TITLE_KEY_PATTERN', () => {
  it('matches everything the parser accepts, so the router never over-rejects', () => {
    for (const key of ['movie-603', 'tv-1396', 'tv-1396-s2', 'tv-1396-s0']) {
      expect(TITLE_KEY_PATTERN.test(key)).toBe(true);
      expect(parse(key)).not.toBeNull();
    }
  });

  it('is a superset the parser then narrows, never the other way round', () => {
    // The route constraint is coarse — it lets `tv-01` through and `parse`
    // rejects it. A key the pattern rejected but the parser accepted would be a
    // page that 404s on a legal identifier, which is the failure that matters.
    expect(TITLE_KEY_PATTERN.test('tv-01')).toBe(true);
    expect(parse('tv-01')).toBeNull();
  });
});
