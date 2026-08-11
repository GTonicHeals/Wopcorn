import { describe, expect, it } from 'vitest';

import { groupActivity } from '@/lib/feedGroups';
import type { ActivityItem, TitleCard } from '@/api/types';

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

type Overrides = Partial<Pick<ActivityItem, 'kind' | 'rating' | 'occurredAt'>> & {
  user?: string;
  tmdbId?: number;
};

let nextId = 0;

function event(overrides: Overrides = {}): ActivityItem {
  nextId += 1;

  return {
    id: `a${nextId}`,
    user: { id: overrides.user ?? 'u1', displayName: 'Ada', avatarUrl: null },
    kind: overrides.kind ?? 'added_queue',
    title: film(overrides.tmdbId ?? nextId),
    rating: overrides.rating ?? null,
    occurredAt: overrides.occurredAt ?? '2026-08-09T10:00:00.000Z'
  };
}

/** The title keys each group ended up holding, in order. */
function shape(items: ActivityItem[]): string[][] {
  return groupActivity(items).map((group) => group.items.map((item) => item.title.key));
}

describe('groupActivity', () => {
  it('collapses one friend doing the same thing to several titles', () => {
    const groups = groupActivity([
      event({ tmdbId: 1 }),
      event({ tmdbId: 2 }),
      event({ tmdbId: 3 })
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0]?.items).toHaveLength(3);
    expect(groups[0]?.kind).toBe('added_queue');
  });

  it('takes its id and timestamp from the newest event in the group', () => {
    const newest = event({ occurredAt: '2026-08-09T10:00:00.000Z' });
    const older = event({ occurredAt: '2026-08-09T09:00:00.000Z' });

    const groups = groupActivity([newest, older]);

    expect(groups[0]?.id).toBe(newest.id);
    expect(groups[0]?.occurredAt).toBe(newest.occurredAt);
  });

  it('splits on the friend', () => {
    expect(
      shape([
        event({ user: 'u1', tmdbId: 1 }),
        event({ user: 'u2', tmdbId: 2 }),
        event({ user: 'u1', tmdbId: 3 })
      ])
    ).toEqual([['movie-1'], ['movie-2'], ['movie-3']]);
  });

  it('splits on the action', () => {
    expect(
      shape([
        event({ kind: 'added_queue', tmdbId: 1 }),
        event({ kind: 'watched', tmdbId: 2 }),
        event({ kind: 'watched', tmdbId: 3 })
      ])
    ).toEqual([['movie-1'], ['movie-2', 'movie-3']]);
  });

  // The stars sit on the group's one line, so a group of ratings could not say
  // which star count belonged to which poster.
  it('never groups ratings, even two in a row from one friend', () => {
    expect(
      shape([
        event({ kind: 'rated', rating: 5, tmdbId: 1 }),
        event({ kind: 'rated', rating: 2, tmdbId: 2 })
      ])
    ).toEqual([['movie-1'], ['movie-2']]);
  });

  it('splits when adjacent events are more than a day apart', () => {
    expect(
      shape([
        event({ tmdbId: 1, occurredAt: '2026-08-09T10:00:00.000Z' }),
        event({ tmdbId: 2, occurredAt: '2026-08-09T09:00:00.000Z' }),
        // 23h from the one above it: still inside the window, so it joins.
        event({ tmdbId: 3, occurredAt: '2026-08-08T10:00:00.000Z' }),
        event({ tmdbId: 4, occurredAt: '2026-08-01T10:00:00.000Z' })
      ])
    ).toEqual([['movie-1', 'movie-2', 'movie-3'], ['movie-4']]);
  });

  it('measures the window from the last event in the group, not the first', () => {
    // Each step is 20h — under the window — so a chain of them stays one group
    // even though the ends are two days apart.
    expect(
      shape([
        event({ tmdbId: 1, occurredAt: '2026-08-09T10:00:00.000Z' }),
        event({ tmdbId: 2, occurredAt: '2026-08-08T14:00:00.000Z' }),
        event({ tmdbId: 3, occurredAt: '2026-08-07T18:00:00.000Z' })
      ])
    ).toEqual([['movie-1', 'movie-2', 'movie-3']]);
  });

  it('never holds the same title twice, so no card can render twice', () => {
    expect(shape([event({ tmdbId: 7 }), event({ tmdbId: 7 })])).toEqual([
      ['movie-7'],
      ['movie-7']
    ]);
  });

  it('keeps an unreadable timestamp out of the group above it', () => {
    expect(shape([event({ tmdbId: 1 }), event({ tmdbId: 2, occurredAt: 'not a date' })])).toEqual([
      ['movie-1'],
      ['movie-2']
    ]);
  });

  it('groups an appended page exactly as it would one whole array', () => {
    const first = [event({ tmdbId: 1 }), event({ tmdbId: 2 })];
    const second = [event({ tmdbId: 3 }), event({ user: 'u2', tmdbId: 4 })];

    expect(shape([...first, ...second])).toEqual([
      ['movie-1', 'movie-2', 'movie-3'],
      ['movie-4']
    ]);
  });

  it('has nothing to say about an empty feed', () => {
    expect(groupActivity([])).toEqual([]);
  });
});
