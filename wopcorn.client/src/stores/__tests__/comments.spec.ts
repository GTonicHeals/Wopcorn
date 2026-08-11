import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'title', fullPath: '/title/movie-603' } },
    replace: vi.fn()
  }
}));

import { useListsStore } from '@/stores/lists';
import { useTitlesStore } from '@/stores/titles';
import { isDetail } from '@/stores/titles';
import type { ListEntry, TitleCard, TitleDetail } from '@/api/types';

/**
 * Notes on watched titles, through the lists store (plan 10).
 *
 * The behaviour under test is that a note is a rating with words: writing one
 * marks the title watched in the same frame, and a failure puts everything back —
 * including the membership the optimistic write turned on.
 */

const KEY = 'movie-603';

function card(overrides: Partial<TitleCard> = {}): TitleCard {
  return {
    key: KEY,
    mediaType: 'movie',
    tmdbId: 603,
    seasonNumber: null,
    parentKey: null,
    title: 'The Matrix',
    releaseYear: 1999,
    posterPath: null,
    tmdbVoteAverage: 8.2,
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

function entry(comment: string | null): ListEntry {
  return {
    title: card({ lists: { watched: true, watchlist: false, queue: false } }),
    addedAt: '2026-08-10T00:00:00.000Z',
    position: null,
    watchedOn: null,
    rating: null,
    comment
  };
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

/** The stored note, read back off the shared title map. */
function storedNote(): string | null {
  const title = useTitlesStore().get(KEY);
  return isDetail(title) ? title.myComment : null;
}

beforeEach(() => {
  setActivePinia(createPinia());
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('writing a note', () => {
  it('marks the title watched in the same frame', async () => {
    const titles = useTitlesStore();
    titles.upsert(detail());

    let membershipDuringFlight = false;
    fetchMock.mockImplementationOnce(() => {
      membershipDuringFlight = titles.get(KEY)?.lists.watched ?? false;
      return Promise.resolve(ok(entry('Still holds up.')));
    });

    const lists = useListsStore();
    expect(await lists.setComment(KEY, 'Still holds up.')).toBe(true);

    // Optimistic, not "after the response": writing a note is watching it, and
    // the toggle must fill as the note is saved rather than a round trip later.
    expect(membershipDuringFlight).toBe(true);
    expect(titles.get(KEY)?.lists.watched).toBe(true);
    expect(storedNote()).toBe('Still holds up.');
  });

  it('writes back the server’s trimmed text, not the draft', async () => {
    const titles = useTitlesStore();
    titles.upsert(detail());
    fetchMock.mockResolvedValueOnce(ok(entry('Trimmed by the server.')));

    const lists = useListsStore();
    await lists.setComment(KEY, '   Trimmed by the server.   ');

    expect(storedNote()).toBe('Trimmed by the server.');
  });

  it('puts the note and the membership back when the write fails', async () => {
    const titles = useTitlesStore();
    titles.upsert(detail({ myComment: 'The old note.' }));
    fetchMock.mockResolvedValueOnce(fail(500, 'error', 'Nope.'));

    const lists = useListsStore();
    expect(await lists.setComment(KEY, 'A new note.')).toBe(false);

    // Both halves of the optimistic write are undone — a rollback that restored
    // the note but left the title watched would invent a watch nobody recorded.
    expect(storedNote()).toBe('The old note.');
    expect(titles.get(KEY)?.lists.watched).toBe(false);
  });
});

describe('clearing a note', () => {
  it('keeps the watched entry', async () => {
    const titles = useTitlesStore();
    titles.upsert(
      detail({
        myComment: 'Gone in a moment.',
        lists: { watched: true, watchlist: false, queue: false }
      })
    );
    fetchMock.mockResolvedValueOnce(noContent());

    const lists = useListsStore();
    expect(await lists.clearComment(KEY)).toBe(true);

    // The mirror of clearing a rating: the judgement goes, the fact of having
    // watched it stays.
    expect(storedNote()).toBeNull();
    expect(titles.get(KEY)?.lists.watched).toBe(true);
  });

  it('restores the note when the delete fails', async () => {
    const titles = useTitlesStore();
    titles.upsert(detail({ myComment: 'Not going anywhere.' }));
    fetchMock.mockResolvedValueOnce(fail(500, 'error', 'Nope.'));

    const lists = useListsStore();
    expect(await lists.clearComment(KEY)).toBe(false);
    expect(storedNote()).toBe('Not going anywhere.');
  });
});
