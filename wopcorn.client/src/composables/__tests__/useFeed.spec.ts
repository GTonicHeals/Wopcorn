import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { effectScope } from 'vue';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'feed', fullPath: '/' } },
    replace: vi.fn()
  }
}));

import { useFeed } from '@/composables/useFeed';
import { useTitlesStore } from '@/stores/titles';
import type { ActivityItem, FeedResponse, TitleCard } from '@/api/types';

function film(tmdbId: number): TitleCard {
  return {
    key: `movie-${tmdbId}`,
    mediaType: 'movie',
    tmdbId,
    seasonNumber: null,
    parentKey: null,
    title: `Film ${tmdbId}`,
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
    availableOn: [],
    suggestion: null
  };
}

function item(id: string, tmdbId = Number(id.replace(/\D/g, '')) || 1): ActivityItem {
  return {
    id,
    user: { id: 'u1', displayName: 'Ada', avatarUrl: null },
    kind: 'watched',
    title: film(tmdbId),
    rating: null,
    occurredAt: '2026-08-09T10:00:00.000Z'
  };
}

function page(items: ActivityItem[], nextCursor: string | null): FeedResponse {
  return { items, nextCursor };
}

const fetchMock = vi.fn();

function ok(body: unknown) {
  return { ok: true, status: 200, json: () => Promise.resolve(body) };
}

function fail(status: number, body: unknown) {
  return { ok: false, status, json: () => Promise.resolve(body) };
}

function urls(): string[] {
  return fetchMock.mock.calls.map((call) => String(call[0]));
}

let scope: ReturnType<typeof effectScope>;

function run<T>(factory: () => T): T {
  scope = effectScope();
  return scope.run(factory) as T;
}

beforeEach(() => {
  setActivePinia(createPinia());
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  scope?.stop();
  vi.unstubAllGlobals();
});

describe('useFeed paging (FR-G3)', () => {
  it('echoes the opaque cursor back verbatim, URL-encoded', async () => {
    // A base64url cursor can carry characters that must survive a query string.
    const cursor = 'MjAyNi0wOC0wOVQxMDowMDowMFo=|a+b/c';

    fetchMock
      .mockResolvedValueOnce(ok(page([item('e1')], cursor)))
      .mockResolvedValueOnce(ok(page([item('e2')], null)));

    const feed = run(() => useFeed());

    await feed.loadMore();
    await feed.loadMore();

    expect(urls()[0]).toBe('/api/feed?limit=20');
    expect(urls()[1]).toBe(`/api/feed?limit=20&cursor=${encodeURIComponent(cursor)}`);

    // Decoding what we sent must give back exactly what we were handed.
    const sent = new URL(urls()[1]!, 'https://x').searchParams.get('cursor');
    expect(sent).toBe(cursor);
  });

  it('keeps paging after a short page — only nextCursor: null stops it', async () => {
    // The server drops rows whose film is missing from the cache, so a page can
    // be shorter than `limit` while there is still more behind it.
    fetchMock
      .mockResolvedValueOnce(ok(page([item('e1'), item('e2')], 'c1')))
      .mockResolvedValueOnce(ok(page([item('e3')], null)));

    const feed = run(() => useFeed());

    await feed.loadMore();
    expect(feed.hasMore.value).toBe(true);

    await feed.loadMore();

    expect(feed.hasMore.value).toBe(false);
    expect(feed.items.value.map((entry) => entry.id)).toEqual(['e1', 'e2', 'e3']);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not fetch again once nextCursor is null', async () => {
    fetchMock.mockResolvedValueOnce(ok(page([item('e1')], null)));

    const feed = run(() => useFeed());
    await feed.loadMore();
    await feed.loadMore();
    await feed.loadMore();

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('renders an event once even if two pages overlap', async () => {
    // Activity landing between requests can push a row onto the next page too.
    fetchMock
      .mockResolvedValueOnce(ok(page([item('e1'), item('e2')], 'c1')))
      .mockResolvedValueOnce(ok(page([item('e2'), item('e3')], null)));

    const feed = run(() => useFeed());
    await feed.loadMore();
    await feed.loadMore();

    expect(feed.items.value.map((entry) => entry.id)).toEqual(['e1', 'e2', 'e3']);
  });

  it('collapses a sentinel and a button firing together into one request', async () => {
    fetchMock.mockResolvedValue(ok(page([item('e1')], 'c1')));

    const feed = run(() => useFeed());
    await Promise.all([feed.loadMore(), feed.loadMore(), feed.loadMore()]);

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('restarts from no cursor when the server rejects one', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(page([item('e1')], 'stale-cursor')))
      .mockResolvedValueOnce(
        fail(400, { code: 'validation_failed', message: 'That feed position is not one we recognise.' })
      )
      .mockResolvedValueOnce(ok(page([item('e1'), item('e2')], null)));

    const feed = run(() => useFeed());
    await feed.loadMore();
    await feed.loadMore();

    // Restarted, not stranded, and without duplicating the first page.
    expect(urls()[2]).toBe('/api/feed?limit=20');
    expect(feed.items.value.map((entry) => entry.id)).toEqual(['e1', 'e2']);
    expect(feed.status.value).toBe('ready');
    expect(feed.error.value).toBeNull();
  });

  it('gives up after one restart rather than looping', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(page([item('e1')], 'c1')))
      .mockResolvedValue(
        fail(400, { code: 'validation_failed', message: 'That feed position is not one we recognise.' })
      );

    const feed = run(() => useFeed());
    await feed.loadMore();
    await feed.loadMore();

    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(feed.error.value?.code).toBe('validation_failed');
  });
});

describe('useFeed state (NFR-10)', () => {
  it('is an error screen only while there is nothing to show', async () => {
    fetchMock.mockResolvedValueOnce(
      fail(503, { code: 'tmdb_unavailable', message: 'TMDB is not responding.' })
    );

    const feed = run(() => useFeed());
    await feed.loadMore();

    expect(feed.status.value).toBe('error');
    expect(feed.error.value?.code).toBe('tmdb_unavailable');
  });

  it('keeps the loaded pages when a later one fails', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(page([item('e1')], 'c1')))
      .mockResolvedValueOnce(fail(500, { code: 'server_error', message: 'Something broke.' }));

    const feed = run(() => useFeed());
    await feed.loadMore();
    await feed.loadMore();

    expect(feed.status.value).toBe('ready');
    expect(feed.items.value).toHaveLength(1);
    expect(feed.error.value?.message).toBe('Something broke.');
  });

  it('refresh drops everything and asks for page one again', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(page([item('e1')], 'c1')))
      .mockResolvedValueOnce(ok(page([item('e9')], null)));

    const feed = run(() => useFeed());
    await feed.loadMore();
    await feed.refresh();

    expect(urls()[1]).toBe('/api/feed?limit=20');
    expect(feed.items.value.map((entry) => entry.id)).toEqual(['e9']);
  });
});

describe('useFeed and the titles store', () => {
  it('puts every title through the shared map so toggles stay in sync', async () => {
    fetchMock.mockResolvedValueOnce(ok(page([item('e1', 55), item('e2', 66)], null)));

    const feed = run(() => useFeed());
    const titles = useTitlesStore();

    await feed.loadMore();

    // Keyed by the title key, never by the bare TMDB id.
    expect(titles.get('movie-55')?.title).toBe('Film 55');
    expect(titles.get('movie-66')?.title).toBe('Film 66');
  });
});
