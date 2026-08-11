import { describe, expect, it } from 'vitest';

import { matchesQuery, normalizeForSearch } from '@/lib/titleFilter';

describe('normalizeForSearch', () => {
  it('casefolds and collapses punctuation to single spaces', () => {
    expect(normalizeForSearch('X-Men: Days of Future Past')).toBe('x men days of future past');
    expect(normalizeForSearch('Léon: The Professional')).toBe('leon the professional');
    expect(normalizeForSearch('  Wall·E  ')).toBe('wall e');
  });

  it('keeps digits, which are part of plenty of titles', () => {
    expect(normalizeForSearch('Blade Runner 2049')).toBe('blade runner 2049');
    expect(normalizeForSearch('Se7en')).toBe('se7en');
  });

  it('does not strip letters outside Latin', () => {
    expect(normalizeForSearch('千と千尋の神隠し')).toBe('千と千尋の神隠し');
  });
});

describe('matchesQuery', () => {
  it('matches across the punctuation nobody types', () => {
    expect(matchesQuery('X-Men', 'X men')).toBe(true);
    expect(matchesQuery('X-Men: Days of Future Past', 'x men')).toBe(true);
    expect(matchesQuery('Spider-Man: No Way Home', 'spider man')).toBe(true);
  });

  it('matches when the spaces are left out entirely', () => {
    expect(matchesQuery('X-Men', 'xmen')).toBe(true);
    expect(matchesQuery('Spider-Man: No Way Home', 'spiderman')).toBe(true);
  });

  it('matches a run from the middle of a title', () => {
    expect(matchesQuery('The Lord of the Rings', 'rings')).toBe(true);
    expect(matchesQuery('The Lord of the Rings', 'of the')).toBe(true);
  });

  it('ignores accents in either direction', () => {
    expect(matchesQuery('Amélie', 'amelie')).toBe(true);
    expect(matchesQuery('Amelie', 'amélie')).toBe(true);
  });

  it('rejects a title that simply is not it', () => {
    expect(matchesQuery('The Matrix', 'x men')).toBe(false);
    expect(matchesQuery('Breaking Bad', 'better call')).toBe(false);
  });

  // The field starts empty and is cleared back to empty, and neither state may
  // hide the list the user came to look at.
  it('matches everything on a blank or whitespace-only query', () => {
    expect(matchesQuery('The Matrix', '')).toBe(true);
    expect(matchesQuery('The Matrix', '   ')).toBe(true);
    expect(matchesQuery('The Matrix', '-')).toBe(true);
  });
});
