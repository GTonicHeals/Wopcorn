import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'title', fullPath: '/t/movie-603' } },
    replace: vi.fn()
  }
}));

import WhereToWatch from '@/components/WhereToWatch.vue';
import { useAuthStore } from '@/stores/auth';
import { useTitlesStore } from '@/stores/titles';
import type { TitleAvailability } from '@/api/types';

/**
 * The block's whole job is to be honest about three different answers: we have
 * never looked, we looked and nobody carries it, and here is who does. Only the
 * middle one is an empty list, and none of them is an error.
 */
describe('WhereToWatch', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    useAuthStore().region = 'GB';
  });

  async function mountBlock(availability: TitleAvailability | null) {
    const titles = useTitlesStore();
    if (availability) {
      titles.availabilityByKey = new Map([['movie-603', availability]]);
    }
    // The component fetches after mount; the store is pre-filled, so resolve to
    // whatever is already there rather than reaching for fetch.
    titles.loadAvailability = vi.fn().mockResolvedValue(availability);

    const wrapper = mount(WhereToWatch, { props: { titleKey: 'movie-603' } });
    await flushPromises();
    return wrapper;
  }

  it('says "unknown" rather than rendering empty when it has never looked', async () => {
    const wrapper = await mountBlock({
      region: 'GB',
      fetchedAt: null,
      link: null,
      offers: []
    });

    expect(wrapper.text()).toContain('Availability unknown');
    expect(wrapper.text()).toContain('Try again');
  });

  it('says nobody carries it when the fetch came back with nothing', async () => {
    // A timestamp with no offers is a different answer from no timestamp, and
    // this is the one place in the UI that distinction is visible.
    const wrapper = await mountBlock({
      region: 'GB',
      fetchedAt: '2026-08-09T12:00:00Z',
      link: null,
      offers: []
    });

    expect(wrapper.text()).toContain('No streaming service carries this here');
    expect(wrapper.text()).not.toContain('unknown');
  });

  it('leads with what is included and keeps rent behind a disclosure', async () => {
    const wrapper = await mountBlock({
      region: 'GB',
      fetchedAt: '2026-08-09T12:00:00Z',
      link: 'https://example.test/watch',
      offers: [
        { kind: 'flatrate', providers: [{ id: 8, name: 'Netflix', logoPath: '/n.jpg' }] },
        { kind: 'rent', providers: [{ id: 9, name: 'Prime Video', logoPath: '/p.jpg' }] }
      ]
    });

    expect(wrapper.text()).toContain('Included with');
    expect(wrapper.text()).toContain('Netflix');
    // The common question is "is it included", not "what would it cost".
    expect(wrapper.text()).not.toContain('Prime Video');

    await wrapper.get('.watch__disclosure').trigger('click');
    expect(wrapper.text()).toContain('Prime Video');
  });

  it('labels the region, so a wrong answer reads as a wrong region', async () => {
    const wrapper = await mountBlock({
      region: 'BE',
      fetchedAt: '2026-08-09T12:00:00Z',
      link: null,
      offers: []
    });

    // Intl.DisplayNames is absent in jsdom, so the code itself is the fallback.
    expect(wrapper.get('.watch__region').text()).toMatch(/BE|Belgium/);
  });

  it('renders nothing at all until the viewer has said where they watch', async () => {
    useAuthStore().region = null;

    const wrapper = await mountBlock(null);

    expect(wrapper.find('section').exists()).toBe(false);
  });
});
