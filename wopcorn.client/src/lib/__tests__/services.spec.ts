import { describe, expect, it } from 'vitest';

import { matchesServices, namedProviders, viewerServices } from '@/lib/services';
import type { WatchProvider } from '@/api/types';

/**
 * The rules the streaming surfaces share. All of them turn on one distinction:
 * `availableOn` is a list of ids, and the directory is what those ids are called.
 */

const NETFLIX: WatchProvider = { id: 8, name: 'Netflix', logoPath: '/netflix.jpg' };
const PRIME: WatchProvider = { id: 9, name: 'Prime Video', logoPath: '/prime.jpg' };
const APPLE: WatchProvider = { id: 350, name: 'Apple TV', logoPath: null };

// The directory arrives in TMDB's display_priority order and stays in it.
const DIRECTORY = [NETFLIX, PRIME, APPLE];

describe('viewerServices', () => {
  it('is empty when nothing is configured', () => {
    // Every streaming control keys off this: no services means no chip, no
    // filter group, and no nag — the list looks exactly as it did.
    expect(viewerServices(DIRECTORY, [])).toEqual([]);
  });

  it('keeps the directory order rather than the order the ids arrived in', () => {
    expect(viewerServices(DIRECTORY, [350, 8]).map((p) => p.name)).toEqual([
      'Netflix',
      'Apple TV'
    ]);
  });

  it('ignores a configured id the directory does not carry', () => {
    // A service configured in one region and read in another.
    expect(viewerServices(DIRECTORY, [8, 999])).toEqual([NETFLIX]);
  });
});

describe('namedProviders', () => {
  it('drops ids the directory cannot name', () => {
    // A badge exists to be recognised; a mystery square is noise.
    expect(namedProviders(DIRECTORY, [8, 4242])).toEqual([NETFLIX]);
  });

  it('is empty for an empty availableOn', () => {
    // The array cannot distinguish "not fetched" from "on none of your
    // services", so the badge row must render nothing rather than claim either.
    expect(namedProviders(DIRECTORY, [])).toEqual([]);
  });
});

describe('matchesServices', () => {
  it('treats an empty selection as no filter, never as match-nothing', () => {
    expect(matchesServices([], [])).toBe(true);
    expect(matchesServices([8], [])).toBe(true);
  });

  it('keeps a title carried by any one of the selected services', () => {
    expect(matchesServices([9, 350], [8, 350])).toBe(true);
  });

  it('drops a title on none of them', () => {
    expect(matchesServices([9], [8, 350])).toBe(false);
    // The unfetched and the unavailable look the same here, which is correct:
    // both mean "I cannot tell you that you can watch this tonight".
    expect(matchesServices([], [8])).toBe(false);
  });
});
