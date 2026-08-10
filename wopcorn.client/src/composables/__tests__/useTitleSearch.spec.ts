import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { effectScope, nextTick } from 'vue';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'search', fullPath: '/search' } },
    replace: vi.fn()
  }
}));

import { SEARCH_DEBOUNCE_MS, useTitleSearch } from '@/composables/useTitleSearch';
import type { TitleCard } from '@/api/types';

function film(tmdbId: number, title: string): TitleCard {
  return {
    key: `movie-${tmdbId}`,
    mediaType: 'movie',
    tmdbId,
    seasonNumber: null,
    parentKey: null,
    title,
    releaseYear: 2021,
    posterPath: null,
    tmdbVoteAverage: 7.2,
    runtimeMinutes: 120,
    episodeCount: null,
    seasonCount: null,
    seasonProgress: null,
    genreIds: [],
    lists: { watched: false, watchlist: false, queue: false },
    myRating: null,
    availableOn: []
  };
}

/** A series row, to prove a mixed page keeps TMDB's own ordering. */
function series(tmdbId: number, title: string): TitleCard {
  return {
    ...film(tmdbId, title),
    key: `tv-${tmdbId}`,
    mediaType: 'series',
    runtimeMinutes: null,
    seasonCount: 5
  };
}

function page(results: TitleCard[]) {
  return { page: 1, totalPages: 1, totalResults: results.length, results };
}

/**
 * Each call parks until the test resolves it by hand — the only way to force the
 * ordering the race guard exists for.
 */
const fetchMock = vi.fn();
let settle: ((body: unknown) => void)[] = [];

function queueDeferred(): void {
  fetchMock.mockImplementation(
    () =>
      new Promise((resolve) => {
        settle.push((body: unknown) =>
          resolve({ ok: true, status: 200, json: () => Promise.resolve(body) })
        );
      })
  );
}

/** Lets the promise chain inside `api()` finish without advancing the clock. */
async function flush(): Promise<void> {
  for (let i = 0; i < 8; i++) await Promise.resolve();
  await nextTick();
}

let scope: ReturnType<typeof effectScope>;

function run<T>(factory: () => T): T {
  scope = effectScope();
  return scope.run(factory) as T;
}

beforeEach(() => {
  setActivePinia(createPinia());
  vi.useFakeTimers();
  fetchMock.mockReset();
  settle = [];
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  scope?.stop();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('useTitleSearch race guard (NFR-1)', () => {
  it('does not let a slow older response overwrite a newer one', async () => {
    queueDeferred();
    const search = run(() => useTitleSearch());

    // Query n-1.
    search.query.value = 'du';
    await nextTick();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    await flush();

    // Query n, typed before the first answer arrives.
    search.query.value = 'dune';
    await nextTick();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    await flush();

    expect(settle).toHaveLength(2);

    // The newer query answers first.
    settle[1]?.(page([film(2, 'Dune')]));
    await flush();
    expect(search.resultKeys.value).toEqual(['movie-2']);

    // …and then the older, slower one lands. It must be discarded.
    settle[0]?.(page([film(1, 'Duck Soup'), film(3, 'Dumbo')]));
    await flush();

    expect(search.resultKeys.value).toEqual(['movie-2']);
    expect(search.renderedQuery.value).toBe('dune');
    expect(search.status.value).toBe('ready');
  });

  it('aborts the superseded request', async () => {
    queueDeferred();
    const search = run(() => useTitleSearch());

    search.query.value = 'du';
    await nextTick();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    await flush();

    const firstInit = fetchMock.mock.calls[0]?.[1] as RequestInit | undefined;
    const firstSignal = firstInit?.signal;
    expect(firstSignal?.aborted).toBe(false);

    search.query.value = 'dune';
    await nextTick();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    await flush();

    expect(firstSignal?.aborted).toBe(true);
  });
});

describe('useTitleSearch debounce', () => {
  it('issues one request for a burst of keystrokes', async () => {
    queueDeferred();
    const search = run(() => useTitleSearch());

    for (const term of ['d', 'du', 'dun', 'dune']) {
      search.query.value = term;
      await nextTick();
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 50);
    }

    expect(fetchMock).not.toHaveBeenCalled();

    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    await flush();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/titles/search?q=dune&page=1');
  });

  it('empties the grid immediately when the field is cleared, without a request', async () => {
    queueDeferred();
    const search = run(() => useTitleSearch());

    search.query.value = 'dune';
    await nextTick();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    await flush();
    settle[0]?.(page([film(2, 'Dune')]));
    await flush();
    expect(search.resultKeys.value).toEqual(['movie-2']);

    fetchMock.mockClear();
    search.query.value = '';
    await nextTick();
    await flush();

    expect(search.resultKeys.value).toEqual([]);
    expect(search.status.value).toBe('idle');
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('useTitleSearch across media types', () => {
  it('keeps films and series in the order the server sent them', async () => {
    queueDeferred();
    const search = run(() => useTitleSearch());

    search.query.value = 'bad';
    await nextTick();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    await flush();

    // TMDB's own relevance ordering across the two types is the whole reason
    // search is one /search/multi call rather than two merged by hand (D-5), so
    // nothing here may re-sort it.
    settle[0]?.(page([film(1396, 'Mirror'), series(1396, 'Breaking Bad')]));
    await flush();

    expect(search.resultKeys.value).toEqual(['movie-1396', 'tv-1396']);

    // The same TMDB id twice, and two distinct entries — which is exactly what
    // the key exists to make possible.
    expect(search.results.value.map((title) => title.title)).toEqual([
      'Mirror',
      'Breaking Bad'
    ]);
  });

  it('sends a type filter when one is asked for', async () => {
    queueDeferred();
    const search = run(() => useTitleSearch(() => ['series']));

    search.query.value = 'bad';
    await nextTick();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    await flush();

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/titles/search?q=bad&page=1&type=series');
  });
});
