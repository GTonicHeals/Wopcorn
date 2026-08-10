import { beforeEach, describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

import ProviderBadges from '@/components/ProviderBadges.vue';
import { useAuthStore } from '@/stores/auth';
import { useConfigStore } from '@/stores/config';

/**
 * The one claim the badge row makes is "you can watch this tonight", so the case
 * that matters most is the one where it must say nothing at all.
 */
describe('ProviderBadges', () => {
  beforeEach(() => {
    setActivePinia(createPinia());

    const auth = useAuthStore();
    auth.region = 'GB';
    auth.providerIds = [8, 9, 350, 337];

    useConfigStore().providersByRegion = new Map([
      [
        'GB',
        [
          { id: 8, name: 'Netflix', logoPath: '/netflix.jpg' },
          { id: 9, name: 'Prime Video', logoPath: '/prime.jpg' },
          { id: 350, name: 'Apple TV', logoPath: null },
          { id: 337, name: 'Disney Plus', logoPath: '/disney.jpg' }
        ]
      ]
    ]);
  });

  function mountBadges(providerIds: number[]) {
    return mount(ProviderBadges, { props: { providerIds } });
  }

  it('renders nothing at all for an empty array', () => {
    // Empty covers three states — no services set, not fetched, and on none of
    // them — and the card cannot tell them apart, so it must claim none of them.
    // No skeleton, no "not available".
    expect(mountBadges([]).html()).toBe('<!--v-if-->');
  });

  it('renders nothing when the directory cannot name any of the ids', () => {
    expect(mountBadges([4242]).html()).toBe('<!--v-if-->');
  });

  it('shows at most three logos and counts the rest', () => {
    const wrapper = mountBadges([8, 9, 350, 337]);

    expect(wrapper.findAll('.provider-logo')).toHaveLength(3);
    expect(wrapper.text()).toContain('+1');
  });

  it('names every service it is reporting, not just the ones it drew', () => {
    const wrapper = mountBadges([8, 9, 350, 337]);

    expect(wrapper.get('p').attributes('aria-label')).toBe(
      'On Netflix, Prime Video, Apple TV, Disney Plus'
    );
  });

  it('falls back to an initial rather than a blank square with no logo path', () => {
    const wrapper = mountBadges([350]);

    expect(wrapper.find('img').exists()).toBe(false);
    expect(wrapper.text()).toContain('A');
  });
});
