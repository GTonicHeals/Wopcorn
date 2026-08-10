import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'queue', fullPath: '/queue' } },
    replace: vi.fn()
  }
}));

// vuedraggable pulls in Sortable, which needs a real layout engine. The board's
// own logic is what is under test, so the list renders as a plain <ul>.
vi.mock('vuedraggable', () => ({
  default: {
    name: 'draggable',
    props: ['modelValue', 'tag', 'itemKey', 'disabled'],
    template: '<ul><li v-for="(el, i) in modelValue" :key="el"><slot name="item" :element="el" :index="i" /></li></ul>'
  }
}));

import QueueBoard from '@/components/QueueBoard.vue';
import { useAuthStore } from '@/stores/auth';
import { useQueueStore } from '@/stores/queue';
import { useTitlesStore } from '@/stores/titles';
import type { TitleCard } from '@/api/types';

/**
 * The queue is the app's signature screen, and plan 09 gave it a filter that can
 * change which title is "Up next". These cover that rule and the fact that the
 * board mounts at all — a `const` referenced before its declaration throws only
 * at runtime, and only once something evaluates it.
 */

function card(key: string, title: string, availableOn: number[] = []): TitleCard {
  return {
    key,
    mediaType: 'movie',
    tmdbId: Number(key.split('-')[1]),
    seasonNumber: null,
    parentKey: null,
    title,
    releaseYear: 1999,
    posterPath: null,
    tmdbVoteAverage: null,
    runtimeMinutes: 120,
    episodeCount: null,
    seasonCount: null,
    seasonProgress: null,
    genreIds: [],
    lists: { watched: false, watchlist: false, queue: true },
    myRating: null,
    availableOn
  };
}

const RouterLinkStub = {
  props: ['to'],
  template: '<a :href="to"><slot /></a>'
};

function mountBoard() {
  return mount(QueueBoard, {
    global: {
      stubs: {
        RouterLink: RouterLinkStub,
        PosterImage: true,
        ProviderBadges: true,
        BaseSheet: true,
        EmptyState: true
      }
    }
  });
}

describe('QueueBoard', () => {
  beforeEach(() => {
    setActivePinia(createPinia());

    const titles = useTitlesStore();
    // Position 1 is on nothing; positions 2 and 3 are on Netflix.
    titles.upsertMany([
      card('movie-1', 'Unwatchable Tonight'),
      card('movie-2', 'On Netflix', [8]),
      card('movie-3', 'Also On Netflix', [8])
    ]);
    titles.loadDetail = vi.fn().mockResolvedValue(undefined);

    useQueueStore().keys = ['movie-1', 'movie-2', 'movie-3'];
  });

  function configureServices() {
    const auth = useAuthStore();
    auth.region = 'GB';
    auth.providerIds = [8];
  }

  it('mounts with no services configured', async () => {
    // Guards the crash class this screen is prone to: the hero has an immediate
    // watcher, so anything it reads must be declared before it.
    const wrapper = mountBoard();
    await flushPromises();

    expect(wrapper.text()).toContain('Unwatchable Tonight');
    // No services means no filter control at all — not a disabled one.
    expect(wrapper.text()).not.toContain('On my services');
  });

  it('offers the filter once services are configured', async () => {
    configureServices();

    const wrapper = mountBoard();
    await flushPromises();

    expect(wrapper.text()).toContain('On my services');
    // Position 1 is still position 1 until the filter is actually on.
    expect(wrapper.get('.hero').text()).toContain('Unwatchable Tonight');
  });

  it('promotes the first watchable title to Up next while filtering', async () => {
    configureServices();

    const wrapper = mountBoard();
    await flushPromises();

    const chip = wrapper
      .findAll('button')
      .find((button) => button.text().includes('On my services'));
    await chip!.trigger('click');
    await flushPromises();

    // "Up next" under this filter has to mean "up next among what you can
    // watch" — a hero nobody can play is what the filter exists to prevent.
    expect(wrapper.get('.hero').text()).toContain('On Netflix');
    expect(wrapper.get('.hero').text()).not.toContain('Unwatchable Tonight');
  });

  it('does not repeat the promoted hero as a row', async () => {
    configureServices();

    const wrapper = mountBoard();
    await flushPromises();

    const chip = wrapper
      .findAll('button')
      .find((button) => button.text().includes('On my services'));
    await chip!.trigger('click');
    await flushPromises();

    const visibleRows = wrapper
      .findAll('.queue-row')
      .filter((row) => row.attributes('style') !== 'display: none;');

    expect(visibleRows).toHaveLength(1);
    expect(visibleRows[0]!.text()).toContain('Also On Netflix');
  });

  it('says so when nothing in the queue is watchable', async () => {
    configureServices();
    // Nothing carries anything now.
    useTitlesStore().upsertMany([
      card('movie-2', 'On Netflix'),
      card('movie-3', 'Also On Netflix')
    ]);

    const wrapper = mountBoard();
    await flushPromises();

    const chip = wrapper
      .findAll('button')
      .find((button) => button.text().includes('On my services'));
    await chip!.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('Nothing in your queue is on your services');
    expect(wrapper.find('.hero').exists()).toBe(false);
  });

  it('drops the filter if the services go away', async () => {
    configureServices();

    const wrapper = mountBoard();
    await flushPromises();

    const chip = wrapper
      .findAll('button')
      .find((button) => button.text().includes('On my services'));
    await chip!.trigger('click');
    await flushPromises();
    expect(wrapper.get('.hero').text()).toContain('On Netflix');

    // Signing out, or clearing services in another tab.
    useAuthStore().providerIds = [];
    await flushPromises();

    expect(wrapper.get('.hero').text()).toContain('Unwatchable Tonight');
  });
});
