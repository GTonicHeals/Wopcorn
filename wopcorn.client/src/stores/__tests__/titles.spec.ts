import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'title', fullPath: '/title/tv-1396' } },
    replace: vi.fn()
  }
}));

import { useListsStore } from '@/stores/lists';
import { useTitlesStore } from '@/stores/titles';
import type { TitleCard, TitleDetail } from '@/api/types';

/**
 * The shared map is what makes a toggle pressed on one screen light up on every
 * other, and it only works if there is exactly one entry per title. Since film
 * and TV ids collide, "one entry per title" means keyed by the canonical key
 * string — never by a bare TMDB id.
 */

const fetchMock = vi.fn();

function ok(body: unknown) {
  return { ok: true, status: 200, json: () => Promise.resolve(body) };
}

function card(overrides: Partial<TitleCard> = {}): TitleCard {
  return {
    key: 'movie-603',
    mediaType: 'movie',
    tmdbId: 603,
    seasonNumber: null,
    parentKey: null,
    title: 'The Matrix',
    releaseYear: 1999,
    posterPath: null,
    tmdbVoteAverage: null,
    runtimeMinutes: 136,
    episodeCount: null,
    seasonCount: null,
    seasonProgress: null,
    genreIds: [],
    lists: { watched: false, watchlist: false, queue: false },
    myRating: null,
    availableOn: [],
    suggestion: null,
    ...overrides
  };
}

function detail(overrides: Partial<TitleDetail> = {}): TitleDetail {
  return {
    ...card(),
    backdropPath: null,
    overview: 'An overview.',
    releaseDate: '1999-03-30',
    genres: [],
    director: null,
    creators: [],
    cast: [],
    seasons: [],
    friendsWatched: [],
    suggestedBy: [],
    myComment: null,
    stale: false,
    ...overrides
  };
}

beforeEach(() => {
  setActivePinia(createPinia());
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('keying', () => {
  it('holds a film and a series of the same TMDB id as two entries', () => {
    const titles = useTitlesStore();

    titles.upsert(card({ key: 'movie-1396', tmdbId: 1396, title: 'Mirror' }));
    titles.upsert(
      card({ key: 'tv-1396', mediaType: 'series', tmdbId: 1396, title: 'Breaking Bad' })
    );

    expect(titles.byKey.size).toBe(2);
    expect(titles.get('movie-1396')?.title).toBe('Mirror');
    expect(titles.get('tv-1396')?.title).toBe('Breaking Bad');
  });

  it('does not let a patch on one reach the other', () => {
    const titles = useTitlesStore();

    titles.upsert(card({ key: 'movie-1396', tmdbId: 1396 }));
    titles.upsert(card({ key: 'tv-1396', mediaType: 'series', tmdbId: 1396 }));

    titles.patch('tv-1396', { myRating: 9 });

    expect(titles.get('tv-1396')?.myRating).toBe(9);
    expect(titles.get('movie-1396')?.myRating).toBeNull();
  });

  it('merges a card over a detail without dropping the detail fields', () => {
    const titles = useTitlesStore();

    titles.upsert(detail({ overview: 'Neo takes a pill.' }));
    titles.upsert(card({ myRating: 8 }));

    const merged = titles.get('movie-603') as TitleDetail;
    expect(merged.overview).toBe('Neo takes a pill.');
    expect(merged.myRating).toBe(8);
  });
});

describe('loadDetail', () => {
  it('fetches by key and files the seasons it came with', async () => {
    const titles = useTitlesStore();

    fetchMock.mockResolvedValue(
      ok(
        detail({
          key: 'tv-1396',
          mediaType: 'series',
          tmdbId: 1396,
          title: 'Breaking Bad',
          seasonCount: 5,
          seasons: [
            {
              key: 'tv-1396-s1',
              seasonNumber: 1,
              name: 'Season 1',
              episodeCount: 7,
              airDate: '2008-01-20',
              posterPath: null,
              lists: { watched: true, watchlist: false, queue: false },
              myRating: 8
            }
          ]
        })
      )
    );

    await titles.loadDetail('tv-1396');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/titles/tv-1396');

    // The season rows land in the same map, already decorated — so their toggles
    // work from the series screen with no request each.
    const season = titles.get('tv-1396-s1');
    expect(season?.mediaType).toBe('season');
    expect(season?.parentKey).toBe('tv-1396');
    expect(season?.lists.watched).toBe(true);
    expect(season?.myRating).toBe(8);
    expect(season?.episodeCount).toBe(7);
  });

  it('sends the refresh POST when forced (FR-B7)', async () => {
    const titles = useTitlesStore();
    fetchMock.mockResolvedValue(ok(detail()));

    await titles.loadDetail('movie-603');
    await titles.loadDetail('movie-603', true);

    // noUncheckedIndexedAccess is on, so the call has to be narrowed before its
    // init can be read at all.
    const refresh = fetchMock.mock.calls[1];
    expect(refresh).toBeDefined();
    expect(refresh?.[0]).toBe('/api/titles/movie-603/refresh');
    expect((refresh?.[1] as RequestInit | undefined)?.method).toBe('POST');
  });

  it('does not refetch a detail it already holds', async () => {
    const titles = useTitlesStore();
    fetchMock.mockResolvedValue(ok(detail()));

    await titles.loadDetail('movie-603');
    await titles.loadDetail('movie-603');

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

describe('no cascade between a series and its seasons', () => {
  it('marking a season watched leaves the series alone', async () => {
    const titles = useTitlesStore();
    const lists = useListsStore();

    titles.upsert(
      card({ key: 'tv-1396', mediaType: 'series', tmdbId: 1396, title: 'Breaking Bad' })
    );
    titles.upsert(
      card({
        key: 'tv-1396-s2',
        mediaType: 'season',
        tmdbId: 1396,
        seasonNumber: 2,
        parentKey: 'tv-1396',
        title: 'Season 2'
      })
    );

    fetchMock.mockResolvedValue(
      ok({
        title: card({
          key: 'tv-1396-s2',
          mediaType: 'season',
          tmdbId: 1396,
          seasonNumber: 2,
          parentKey: 'tv-1396',
          title: 'Season 2',
          lists: { watched: true, watchlist: false, queue: false }
        }),
        addedAt: '2026-08-09T10:00:00.000Z',
        position: null,
        watchedOn: null,
        rating: null
      })
    );

    await lists.add('watched', 'tv-1396-s2');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/lists/watched/tv-1396-s2');
    expect(titles.get('tv-1396-s2')?.lists.watched).toBe(true);
    // The series is a different entry and stays untouched (D-2).
    expect(titles.get('tv-1396')?.lists.watched).toBe(false);
  });

  it('rating a series leaves its seasons alone', async () => {
    const titles = useTitlesStore();
    const lists = useListsStore();

    titles.upsert(card({ key: 'tv-1396', mediaType: 'series', tmdbId: 1396 }));
    titles.upsert(
      card({ key: 'tv-1396-s1', mediaType: 'season', tmdbId: 1396, parentKey: 'tv-1396' })
    );

    fetchMock.mockResolvedValue(
      ok({
        title: card({
          key: 'tv-1396',
          mediaType: 'series',
          tmdbId: 1396,
          myRating: 10,
          lists: { watched: true, watchlist: false, queue: false }
        }),
        addedAt: '2026-08-09T10:00:00.000Z',
        position: null,
        watchedOn: null,
        rating: 10
      })
    );

    await lists.setRating('tv-1396', 10);

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/titles/tv-1396/rating');
    expect(titles.get('tv-1396')?.myRating).toBe(10);
    expect(titles.get('tv-1396-s1')?.myRating).toBeNull();
    expect(titles.get('tv-1396-s1')?.lists.watched).toBe(false);
  });
});

describe('clear', () => {
  it('drops everything, because it all describes the signed-out user', () => {
    const titles = useTitlesStore();

    titles.upsert(card({ myRating: 7 }));
    titles.clear();

    expect(titles.byKey.size).toBe(0);
    expect(titles.get('movie-603')).toBeNull();
  });
});
