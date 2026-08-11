import { beforeEach, describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

import TitleCard from '@/components/TitleCard.vue';
import { useAuthStore } from '@/stores/auth';
import { useConfigStore } from '@/stores/config';
import type { TitleCard as TitleCardData } from '@/api/types';

/**
 * The card's layout did not change when series and seasons arrived — what
 * changed is what fills it. These cover exactly that: the meta line per media
 * type, the chip that appears on two of the three, and the progress line that
 * only a series the viewer has started can show.
 */

const RouterLinkStub = {
  props: ['to'],
  template: '<a :href="to"><slot /></a>'
};

function mountCard(title: TitleCardData, props: Record<string, unknown> = {}) {
  return mount(TitleCard, {
    attachTo: document.body,
    props: { title, ...props },
    global: {
      stubs: {
        RouterLink: RouterLinkStub,
        PosterImage: true,
        StarDisplay: true,
        ListToggles: true
      }
    }
  });
}

function film(overrides: Partial<TitleCardData> = {}): TitleCardData {
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

function series(overrides: Partial<TitleCardData> = {}): TitleCardData {
  return film({
    key: 'tv-1396',
    mediaType: 'series',
    tmdbId: 1396,
    title: 'Breaking Bad',
    releaseYear: 2008,
    // TMDB's episode_run_time is [] for Breaking Bad — null is ordinary here.
    runtimeMinutes: null,
    episodeCount: 62,
    seasonCount: 5,
    ...overrides
  });
}

function season(overrides: Partial<TitleCardData> = {}): TitleCardData {
  return film({
    key: 'tv-1396-s2',
    mediaType: 'season',
    tmdbId: 1396,
    seasonNumber: 2,
    parentKey: 'tv-1396',
    title: 'Season 2',
    releaseYear: 2009,
    runtimeMinutes: null,
    episodeCount: 13,
    seasonCount: null,
    ...overrides
  });
}

beforeEach(() => {
  setActivePinia(createPinia());
});

describe('meta line per media type', () => {
  it('a film reads year · runtime', () => {
    const meta = mountCard(film()).get('.title-card__meta').text();

    expect(meta).toContain('1999');
    expect(meta).toContain('2h 16m');
  });

  it('a series reads year · seasons, not a runtime it does not have', () => {
    const meta = mountCard(series()).get('.title-card__meta').text();

    expect(meta).toContain('2008');
    expect(meta).toContain('5 seasons');
    // A bare year would read like missing data rather than a series.
    expect(meta).not.toContain('m');
  });

  it('a season leads with its own number and its episode count', () => {
    const meta = mountCard(season()).get('.title-card__meta').text();

    expect(meta).toContain('Season 2');
    expect(meta).toContain('13 episodes');
  });

  it('singularises a one-season series and a one-episode season', () => {
    expect(mountCard(series({ seasonCount: 1 })).get('.title-card__meta').text()).toContain(
      '1 season'
    );
    expect(mountCard(season({ episodeCount: 1 })).get('.title-card__meta').text()).toContain(
      '1 episode'
    );
  });

  it('shows a series runtime when TMDB actually gave one', () => {
    const meta = mountCard(series({ runtimeMinutes: 1200 })).get('.title-card__meta').text();

    expect(meta).toContain('5 seasons');
    expect(meta).toContain('20h');
  });
});

describe('type chip', () => {
  it('labels a series and a season', () => {
    expect(mountCard(series()).text()).toContain('Series');
    expect(mountCard(season()).text()).toContain('Season');
  });

  it('leaves a film unlabelled', () => {
    // The default needs no label, and a chip on every card is noise.
    const text = mountCard(film()).text();

    expect(text).not.toContain('Series');
    expect(text).not.toContain('Season');
  });
});

describe('season progress', () => {
  it('shows on a series with at least one watched season', () => {
    const card = mountCard(series({ seasonProgress: { watched: 3, total: 5 } }));

    expect(card.get('.title-card__progress').text()).toBe('3 / 5 seasons watched');
  });

  it('reads 5 / 5 without implying the series itself is watched', () => {
    const card = mountCard(
      series({ seasonProgress: { watched: 5, total: 5 }, lists: { watched: false, watchlist: false, queue: false } })
    );

    expect(card.get('.title-card__progress').text()).toBe('5 / 5 seasons watched');
  });

  it('shows nothing when no season has been watched', () => {
    // Null from the server, and `0 / 5` would not be progress anyway.
    expect(mountCard(series()).find('.title-card__progress').exists()).toBe(false);
    expect(
      mountCard(series({ seasonProgress: { watched: 0, total: 5 } }))
        .find('.title-card__progress')
        .exists()
    ).toBe(false);
  });

  it('never shows on a film or a season', () => {
    expect(mountCard(film()).find('.title-card__progress').exists()).toBe(false);
    expect(mountCard(season()).find('.title-card__progress').exists()).toBe(false);
  });
});

describe('streaming badges', () => {
  function withDirectory() {
    useAuthStore().region = 'GB';
    useConfigStore().providersByRegion = new Map([
      ['GB', [{ id: 8, name: 'Netflix', logoPath: '/netflix.jpg' }]]
    ]);
  }

  it('renders nothing for an empty availableOn', () => {
    withDirectory();

    // Empty means one of "no services set", "not fetched" and "on none of them",
    // and the card cannot tell which — so it claims none of them.
    expect(mountCard(film()).find('.providers').exists()).toBe(false);
  });

  it('renders the viewer\'s services when there are some', () => {
    withDirectory();

    const card = mountCard(film({ availableOn: [8] }));

    expect(card.get('.providers').attributes('aria-label')).toBe('On Netflix');
  });
});

describe('navigation', () => {
  it('links by key, so a series never lands on a film of the same id', () => {
    expect(mountCard(series()).get('a').attributes('href')).toBe('/title/tv-1396');
    expect(mountCard(film({ key: 'movie-1396', tmdbId: 1396 })).get('a').attributes('href')).toBe(
      '/title/movie-1396'
    );
    expect(mountCard(season()).get('a').attributes('href')).toBe('/title/tv-1396-s2');
  });
});

/**
 * Opt-in, and off unless a list asks for it: the same card navigates on the
 * search screen, the feed, and a profile.
 */
describe('quick view', () => {
  function clickLink(card: ReturnType<typeof mountCard>, init: MouseEventInit = {}): MouseEvent {
    const event = new MouseEvent('click', { bubbles: true, cancelable: true, ...init });
    card.get('a').element.dispatchEvent(event);
    return event;
  }

  it('emits the key and cancels the navigation when asked to', () => {
    const card = mountCard(film(), { quickView: true });
    const event = clickLink(card);

    expect(card.emitted('select')).toEqual([['movie-603']]);
    // vue-router's own handler bails on an already-cancelled click.
    expect(event.defaultPrevented).toBe(true);
  });

  it('navigates as usual when it is not', () => {
    const card = mountCard(film());
    const event = clickLink(card);

    expect(card.emitted('select')).toBeUndefined();
    expect(event.defaultPrevented).toBe(false);
  });

  it('leaves a ctrl-click to the browser', () => {
    const card = mountCard(film(), { quickView: true });
    const event = clickLink(card, { ctrlKey: true });

    expect(card.emitted('select')).toBeUndefined();
    expect(event.defaultPrevented).toBe(false);
  });
});
