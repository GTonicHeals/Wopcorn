import { describe, expect, it } from 'vitest';

import { guessRegion, regionOptions } from '@/lib/regions';

describe('guessRegion', () => {
  it('takes the region subtag, upper-cased', () => {
    expect(guessRegion('en-GB')).toBe('GB');
    expect(guessRegion('nl-be')).toBe('BE');
  });

  it('skips a script subtag to reach the region', () => {
    expect(guessRegion('zh-Hant-TW')).toBe('TW');
    expect(guessRegion('en-Latn-US')).toBe('US');
  });

  it('is null when the tag carries no region', () => {
    // A language alone says nothing about where someone watches, and a guess
    // here would be applied to a screen that then shows a wrong answer.
    expect(guessRegion('en')).toBeNull();
    expect(guessRegion('')).toBeNull();
    expect(guessRegion(null)).toBeNull();
  });
});

describe('regionOptions', () => {
  it('is sorted by name rather than by code', () => {
    const names = regionOptions().map((option) => option.name);
    expect([...names].sort((a, b) => a.localeCompare(b))).toEqual(names);
  });

  it('appends a region it does not already offer', () => {
    // A region set from another device (or by a future version of this list)
    // must never be silently swapped for something else.
    const codes = regionOptions('ZZ').map((option) => option.code);
    expect(codes).toContain('ZZ');
    expect(codes.filter((code) => code === 'GB')).toHaveLength(1);
  });

  it('does not duplicate a region it already offers', () => {
    const codes = regionOptions('GB').map((option) => option.code);
    expect(codes.filter((code) => code === 'GB')).toHaveLength(1);
  });
});
