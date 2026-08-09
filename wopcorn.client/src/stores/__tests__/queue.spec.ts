import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'lists', fullPath: '/queue' } },
    replace: vi.fn()
  }
}));

import { moveWithin, useQueueStore } from '@/stores/queue';
import { useToastStore } from '@/stores/toasts';

type FakeResponse = {
  ok: boolean;
  status: number;
  json: () => Promise<unknown>;
};

function respond(status: number, body: unknown): FakeResponse {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body)
  };
}

const fetchMock = vi.fn();

function lastBody(): unknown {
  const init = fetchMock.mock.calls.at(-1)?.[1] as RequestInit | undefined;
  return typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
}

beforeEach(() => {
  setActivePinia(createPinia());
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('moveWithin', () => {
  it('moves one item and leaves the rest in order', () => {
    expect(moveWithin(['a', 'b', 'c', 'd'], 0, 2)).toEqual(['b', 'c', 'a', 'd']);
    expect(moveWithin(['a', 'b', 'c', 'd'], 3, 0)).toEqual(['d', 'a', 'b', 'c']);
  });

  it('is a no-op for a move that goes nowhere or out of bounds', () => {
    expect(moveWithin(['a', 'b', 'c'], 1, 1)).toEqual(['a', 'b', 'c']);
    expect(moveWithin(['a', 'b', 'c'], 5, 0)).toEqual(['a', 'b', 'c']);
  });

  it('is generic, so the queue could hold anything the server keys by', () => {
    expect(moveWithin([1, 2, 3], 0, 2)).toEqual([2, 3, 1]);
  });
});

/**
 * The queue mixes media types in one order, so these use a film, a series and a
 * season rather than three films — a queue of one kind would not exercise the
 * thing that changed.
 */
const FILM = 'movie-78';
const SERIES = 'tv-1396';
const SEASON = 'tv-1396-s2';
const OTHER = 'movie-79';

describe('queue reorder persistence (FR-D5)', () => {
  it('applies the new order at once and keeps the server echo', async () => {
    const queue = useQueueStore();
    queue.setKeys([FILM, SERIES, SEASON]);

    fetchMock.mockResolvedValue(respond(200, { keys: [SEASON, FILM, SERIES] }));

    const result = await queue.persist([SEASON, FILM, SERIES]);

    expect(result).toBe('ok');
    expect(queue.keys).toEqual([SEASON, FILM, SERIES]);
    expect(lastBody()).toEqual({ keys: [SEASON, FILM, SERIES] });
  });

  it('reconciles to the server order on 409 queue_out_of_sync', async () => {
    const queue = useQueueStore();
    const toasts = useToastStore();
    queue.setKeys([FILM, SERIES, SEASON]);

    // The queue changed elsewhere: the season is gone and another film arrived.
    fetchMock.mockResolvedValue(
      respond(409, {
        code: 'queue_out_of_sync',
        message: 'Your queue changed somewhere else. This is its current order.',
        keys: [OTHER, FILM, SERIES]
      })
    );

    const result = await queue.persist([SEASON, SERIES, FILM]);

    expect(result).toBe('conflict');
    // Neither the optimistic order nor the pre-drag order — the server's.
    expect(queue.keys).toEqual([OTHER, FILM, SERIES]);
    expect(toasts.toasts).toHaveLength(1);
    expect(toasts.toasts[0]?.message).toBe(
      'Your queue changed elsewhere — showing the latest order.'
    );
  });

  it('falls back to the previous order when the 409 carries no keys', async () => {
    const queue = useQueueStore();
    queue.setKeys([FILM, SERIES, SEASON]);

    fetchMock.mockResolvedValue(
      respond(409, { code: 'queue_out_of_sync', message: 'Out of sync.' })
    );

    await queue.persist([SEASON, SERIES, FILM]);

    expect(queue.keys).toEqual([FILM, SERIES, SEASON]);
  });

  it('ignores a 409 body whose keys are not strings', async () => {
    const queue = useQueueStore();
    queue.setKeys([FILM, SERIES]);

    // The old wire format carried numbers. A body that does not match the
    // contract is not reconciled against — the previous order stands.
    fetchMock.mockResolvedValue(
      respond(409, { code: 'queue_out_of_sync', message: 'Out of sync.', keys: [1, 2] })
    );

    await queue.persist([SERIES, FILM]);

    expect(queue.keys).toEqual([FILM, SERIES]);
  });

  it('rolls the optimistic reorder back on any other failure', async () => {
    const queue = useQueueStore();
    const toasts = useToastStore();
    queue.setKeys([FILM, SERIES, SEASON]);

    fetchMock.mockResolvedValue(
      respond(503, { code: 'tmdb_unavailable', message: 'TMDB is not responding.' })
    );

    const result = await queue.persist([SEASON, SERIES, FILM]);

    expect(result).toBe('error');
    expect(queue.keys).toEqual([FILM, SERIES, SEASON]);
    expect(toasts.toasts[0]?.message).toBe('TMDB is not responding.');
  });
});

describe('queue sort presets (FR-D3)', () => {
  it('takes the rewritten order from the response', async () => {
    const queue = useQueueStore();
    queue.setKeys([FILM, SERIES, SEASON]);

    fetchMock.mockResolvedValue(respond(200, { keys: [SERIES, SEASON, FILM] }));

    await expect(queue.applyPreset('title', 'asc')).resolves.toBe(true);
    expect(queue.keys).toEqual([SERIES, SEASON, FILM]);
    expect(lastBody()).toEqual({ preset: 'title', dir: 'asc' });
  });
});
