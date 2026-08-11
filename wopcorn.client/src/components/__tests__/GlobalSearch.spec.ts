import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { nextTick, type Ref } from 'vue';

import GlobalSearch from '@/components/GlobalSearch.vue';
import { useTitleSearch } from '@/composables/useTitleSearch';
import type { TitleCard } from '@/api/types';

const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }));

// api/client.ts imports the router to handle 401s; with vue-router stubbed the
// real module cannot build one, and booting auth is not this test's business.
vi.mock('@/router', () => ({
  default: { currentRoute: { value: { name: 'feed', fullPath: '/' } }, replace: vi.fn() }
}));

/**
 * The search plumbing has its own tests (`useTitleSearch.spec.ts`); stubbing it
 * here leaves exactly what the palette adds — the keyboard walk over the rows,
 * what Enter means at each moment, and the `aria-activedescendant` wiring that
 * makes a listbox driven from a text field readable.
 */
vi.mock('@/composables/useTitleSearch', async () => {
  const { ref } = await import('vue');
  const state = {
    query: ref(''),
    results: ref([]),
    renderedQuery: ref(''),
    status: ref('idle'),
    isLoading: ref(false),
    totalResults: ref(0),
    runNow: vi.fn()
  };

  return { useTitleSearch: () => state, SEARCH_DEBOUNCE_MS: 250 };
});

type SearchState = {
  query: Ref<string>;
  results: Ref<TitleCard[]>;
  renderedQuery: Ref<string>;
  status: Ref<string>;
  isLoading: Ref<boolean>;
  totalResults: Ref<number>;
  runNow: ReturnType<typeof vi.fn>;
};

const search = useTitleSearch() as unknown as SearchState;

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
    availableOn: [],
    suggestion: null
  };
}

/** A series row, which the palette has to label and route differently. */
function series(tmdbId: number, title: string): TitleCard {
  return {
    ...film(tmdbId, title),
    key: `tv-${tmdbId}`,
    mediaType: 'series',
    runtimeMinutes: null,
    seasonCount: 5
  };
}

async function mountOpen(titles: TitleCard[], totalResults = titles.length) {
  search.query.value = titles.length > 0 ? 'dune' : '';
  search.results.value = titles;
  search.renderedQuery.value = titles.length > 0 ? 'dune' : '';
  search.status.value = titles.length > 0 ? 'ready' : 'idle';
  search.totalResults.value = totalResults;

  const wrapper = mount(GlobalSearch, {
    props: { open: false },
    attachTo: document.body,
    global: { stubs: { PosterImage: true, StarDisplay: true } }
  });

  await wrapper.setProps({ open: true });
  await nextTick();
  return wrapper;
}

function press(wrapper: Awaited<ReturnType<typeof mountOpen>>, key: string) {
  return wrapper.get('#global-search-input').trigger('keydown', { key });
}

function activeOptionIds(wrapper: Awaited<ReturnType<typeof mountOpen>>): string[] {
  return wrapper
    .findAll('[role="option"]')
    .filter((option) => option.attributes('aria-selected') === 'true')
    .map((option) => option.attributes('id') ?? '');
}

beforeEach(() => {
  push.mockReset();
  search.runNow.mockReset();
});

describe('GlobalSearch keyboard walk', () => {
  it('highlights the top hit as soon as results land, so Enter has a target', async () => {
    const wrapper = await mountOpen([film(1, 'Dune'), film(2, 'Dune: Part Two')]);

    expect(activeOptionIds(wrapper)).toEqual(['global-search-option-0']);
    expect(wrapper.get('#global-search-input').attributes('aria-activedescendant')).toBe(
      'global-search-option-0'
    );
  });

  it('walks down and wraps back to the top', async () => {
    const wrapper = await mountOpen([film(1, 'A'), film(2, 'B')]);

    await press(wrapper, 'ArrowDown');
    expect(activeOptionIds(wrapper)).toEqual(['global-search-option-1']);

    await press(wrapper, 'ArrowDown');
    expect(activeOptionIds(wrapper)).toEqual(['global-search-option-0']);
  });

  it('walks up from the top to the last row', async () => {
    const wrapper = await mountOpen([film(1, 'A'), film(2, 'B'), film(3, 'C')]);

    await press(wrapper, 'ArrowUp');
    expect(activeOptionIds(wrapper)).toEqual(['global-search-option-2']);
  });

  it('shows at most six rows, however many came back', async () => {
    const many = Array.from({ length: 20 }, (_, index) => film(index + 1, `Film ${index + 1}`));
    const wrapper = await mountOpen(many, 20);

    expect(wrapper.findAll('[role="option"]')).toHaveLength(6);
  });
});

describe('GlobalSearch selection', () => {
  it('opens the highlighted title and closes itself', async () => {
    const wrapper = await mountOpen([film(1, 'A'), film(2, 'B')]);

    await press(wrapper, 'ArrowDown');
    await press(wrapper, 'Enter');

    expect(push).toHaveBeenCalledWith('/title/movie-2');
    expect(wrapper.emitted('update:open')?.at(-1)).toEqual([false]);
  });

  it('opens a clicked row', async () => {
    const wrapper = await mountOpen([film(1, 'A'), film(2, 'B')]);

    const rows = wrapper.findAll('[role="option"]');
    await rows[1]?.trigger('click');

    expect(push).toHaveBeenCalledWith('/title/movie-2');
  });

  it('re-runs the query rather than navigating when there is nothing to open', async () => {
    const wrapper = await mountOpen([]);

    await press(wrapper, 'Enter');

    expect(push).not.toHaveBeenCalled();
    expect(search.runNow).toHaveBeenCalledTimes(1);
  });

  it('hands off to the search screen with the query when there is more to see', async () => {
    const wrapper = await mountOpen([film(1, 'A'), film(2, 'B')], 214);
    search.renderedQuery.value = 'dune part';
    await nextTick();

    const all = wrapper.get('.palette__all');
    expect(all.text()).toContain('214');

    await all.trigger('click');
    expect(push).toHaveBeenCalledWith('/search?q=dune%20part');
  });

  it('offers no handoff when the rows are the whole answer', async () => {
    const wrapper = await mountOpen([film(1, 'A'), film(2, 'B')], 2);

    expect(wrapper.find('.palette__all').exists()).toBe(false);
  });

  it('routes a series to its own screen, not to a film of the same id', async () => {
    const wrapper = await mountOpen([series(1396, 'Breaking Bad')]);

    await press(wrapper, 'Enter');

    // `/title/movie-1396` would be Mirror (1975) — a different film entirely.
    expect(push).toHaveBeenCalledWith('/title/tv-1396');
  });
});

describe('GlobalSearch type chips', () => {
  it('labels a series and leaves films unlabelled', async () => {
    const wrapper = await mountOpen([film(1, 'Mirror'), series(1396, 'Breaking Bad')]);

    const rows = wrapper.findAll('[role="option"]');

    // The default needs no label; a chip on every row would be noise.
    expect(rows[0]?.text()).not.toContain('Series');
    expect(rows[1]?.text()).toContain('Series');
  });
});
