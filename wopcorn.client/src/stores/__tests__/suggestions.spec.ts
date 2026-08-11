import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'friends', fullPath: '/friends' } },
    replace: vi.fn()
  }
}));

import { useSuggestionsStore } from '@/stores/suggestions';
import { useToastStore } from '@/stores/toasts';
import type { Suggestion, SuggestionsResponse, TitleCard, UserSummary } from '@/api/types';

/**
 * The suggestions store (plan 10).
 *
 * Two things here are worth pinning down and neither is layout: that a `409` is
 * reported as an ordinary outcome rather than shouted about in a toast, and that
 * an optimistic removal from the inbox is put back when the request fails — the
 * badge count is on screen while it is in flight.
 */

function user(id: string, displayName: string): UserSummary {
  return { id, displayName, avatarUrl: null };
}

function card(key: string): TitleCard {
  return {
    key,
    mediaType: 'movie',
    tmdbId: Number(key.split('-')[1]),
    seasonNumber: null,
    parentKey: null,
    title: 'A Film',
    releaseYear: 2021,
    posterPath: null,
    tmdbVoteAverage: null,
    runtimeMinutes: 100,
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

function suggestion(partial: Partial<Suggestion> = {}): Suggestion {
  return {
    id: 's1',
    from: user('u1', 'Sam'),
    to: user('u2', 'Nora'),
    title: card('movie-603'),
    target: 'queue',
    position: null,
    comment: null,
    fromRating: null,
    state: 'pending',
    sentAt: '2026-08-08T00:00:00.000Z',
    ...partial
  };
}

function response(partial: Partial<SuggestionsResponse> = {}): SuggestionsResponse {
  return { incoming: [], outgoing: [], ...partial };
}

const fetchMock = vi.fn();

function ok(body: unknown) {
  return { ok: true, status: 200, json: () => Promise.resolve(body) };
}

function noContent() {
  return { ok: true, status: 204, json: () => Promise.reject(new Error('no body')) };
}

function fail(status: number, code: string, message: string) {
  return { ok: false, status, json: () => Promise.resolve({ code, message }) };
}

beforeEach(() => {
  setActivePinia(createPinia());
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the inbox badge', () => {
  it('counts incoming suggestions and nothing you sent', async () => {
    fetchMock.mockResolvedValueOnce(
      ok(
        response({
          incoming: [suggestion({ id: 'a' }), suggestion({ id: 'b' })],
          outgoing: [suggestion({ id: 'c' })]
        })
      )
    );

    const store = useSuggestionsStore();
    await store.load();

    expect(store.pendingCount).toBe(2);
  });

  it('is empty again after a sign-out', async () => {
    fetchMock.mockResolvedValueOnce(ok(response({ incoming: [suggestion()] })));

    const store = useSuggestionsStore();
    await store.load();
    store.clear();

    // Incoming suggestions are addressed to whoever just signed out.
    expect(store.pendingCount).toBe(0);
    expect(store.status).toBe('idle');
  });
});

describe('sending', () => {
  it('reports a 409 as an outcome rather than a toast', async () => {
    fetchMock
      .mockResolvedValueOnce(
        fail(409, 'suggestion_pending', 'You have already suggested this to them.')
      )
      .mockResolvedValueOnce(ok(response()));

    const store = useSuggestionsStore();
    const toasts = useToastStore();

    const outcome = await store.send({ toUserId: 'u2', key: 'movie-603', target: 'queue' });

    expect(outcome).toBe('already_suggested');
    // The screen was stale, which the caller explains in context. Shouting it in
    // a toast as well would report one stale screen twice.
    expect(toasts.toasts).toHaveLength(0);
  });

  it('distinguishes a lost friendship from a real failure', async () => {
    fetchMock.mockResolvedValueOnce(fail(403, 'forbidden', 'Not friends.'));

    const store = useSuggestionsStore();
    expect(await store.send({ toUserId: 'u2', key: 'movie-603', target: 'queue' })).toBe(
      'not_friends'
    );
  });

  it('toasts anything it cannot explain', async () => {
    fetchMock.mockResolvedValueOnce(fail(500, 'error', 'Something broke.'));

    const store = useSuggestionsStore();
    const toasts = useToastStore();

    expect(await store.send({ toUserId: 'u2', key: 'movie-603', target: 'queue' })).toBe('error');
    expect(toasts.toasts).toHaveLength(1);
  });

  it('reloads both directions after a successful send', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(suggestion()))
      .mockResolvedValueOnce(ok(response({ outgoing: [suggestion()] })));

    const store = useSuggestionsStore();
    expect(await store.send({ toUserId: 'u2', key: 'movie-603', target: 'queue' })).toBe('sent');
    expect(store.outgoing).toHaveLength(1);
  });
});

describe('answering', () => {
  it('drops the row from the inbox at once and keeps it gone on success', async () => {
    fetchMock.mockResolvedValueOnce(ok(response({ incoming: [suggestion({ id: 's1' })] })));

    const store = useSuggestionsStore();
    await store.load();
    expect(store.pendingCount).toBe(1);

    fetchMock
      .mockResolvedValueOnce(ok(suggestion({ id: 's1', state: 'accepted' })))
      .mockResolvedValueOnce(ok(response()));

    expect(await store.accept('s1')).toBe(true);
    expect(store.pendingCount).toBe(0);
  });

  it('puts the row back when the answer fails', async () => {
    fetchMock.mockResolvedValueOnce(ok(response({ incoming: [suggestion({ id: 's1' })] })));

    const store = useSuggestionsStore();
    await store.load();

    fetchMock.mockResolvedValueOnce(fail(500, 'error', 'Nope.'));

    expect(await store.dismiss('s1')).toBe(false);
    // The badge dropped optimistically; a failure has to restore it or the
    // suggestion is invisible until the next full load.
    expect(store.pendingCount).toBe(1);
  });

  it('reloads rather than restoring when the suggestion is already gone', async () => {
    fetchMock.mockResolvedValueOnce(ok(response({ incoming: [suggestion({ id: 's1' })] })));

    const store = useSuggestionsStore();
    await store.load();

    fetchMock
      .mockResolvedValueOnce(fail(404, 'not_found', 'No longer waiting.'))
      .mockResolvedValueOnce(ok(response()));

    expect(await store.accept('s1')).toBe(false);
    // Answered on another device: the server's view wins over the snapshot.
    expect(store.pendingCount).toBe(0);
  });

  it('withdraws from the outgoing list, not the inbox', async () => {
    fetchMock.mockResolvedValueOnce(
      ok(response({ incoming: [suggestion({ id: 'in' })], outgoing: [suggestion({ id: 'out' })] }))
    );

    const store = useSuggestionsStore();
    await store.load();

    fetchMock.mockResolvedValueOnce(noContent()).mockResolvedValueOnce(ok(response()));

    expect(await store.withdraw('out')).toBe(true);
    expect(store.outgoing).toHaveLength(0);
  });
});

describe('sentTo', () => {
  it('finds a live suggestion of this title to this person', async () => {
    fetchMock.mockResolvedValueOnce(
      ok(response({ outgoing: [suggestion({ id: 'out', to: user('u2', 'Nora') })] }))
    );

    const store = useSuggestionsStore();
    await store.load();

    expect(store.sentTo('u2', 'movie-603')?.id).toBe('out');
    expect(store.sentTo('u3', 'movie-603')).toBeNull();
    expect(store.sentTo('u2', 'movie-999')).toBeNull();
  });

  it('ignores an accepted one, which no longer blocks a new suggestion', async () => {
    fetchMock.mockResolvedValueOnce(
      ok(response({ outgoing: [suggestion({ id: 'out', state: 'accepted' })] }))
    );

    const store = useSuggestionsStore();
    await store.load();

    // Re-suggesting after acceptance rewrites the row rather than conflicting,
    // so warning the sender off would be wrong.
    expect(store.sentTo('u2', 'movie-603')).toBeNull();
  });
});
