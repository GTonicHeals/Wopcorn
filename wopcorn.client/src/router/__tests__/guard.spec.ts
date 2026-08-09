import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { mount } from '@vue/test-utils';

import App from '@/App.vue';
import router from '@/router';
import type { UserSummary } from '@/api/types';

const ADA: UserSummary = { id: 'ada', displayName: 'Ada', avatarUrl: null };

const CONFIG = {
  imageBaseUrl: 'https://image.tmdb.org/t/p/',
  posterSizes: ['w92', 'w185', 'original'],
  backdropSizes: ['w780', 'original'],
  profileSizes: ['w45', 'original'],
  attribution: { text: 'Attribution.', logoUrl: '/tmdb-logo.svg' }
};

let signedIn = true;
let pinia: Pinia;

function respond(status: number, body: unknown) {
  return { ok: status >= 200 && status < 300, status, json: () => Promise.resolve(body) };
}

const fetchMock = vi.fn((input: unknown) => {
  const url = String(input);
  if (url.startsWith('/api/auth/me')) {
    return Promise.resolve(
      signedIn
        ? respond(200, ADA)
        : respond(401, { code: 'unauthenticated', message: 'You need to sign in.' })
    );
  }
  if (url.startsWith('/api/config')) return Promise.resolve(respond(200, CONFIG));
  return Promise.resolve(respond(404, { code: 'not_found', message: 'No.' }));
});

beforeEach(() => {
  pinia = createPinia();
  setActivePinia(pinia);
  fetchMock.mockClear();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('route guard', () => {
  it('sends an anonymous visitor to /login and remembers where they were going', async () => {
    signedIn = false;

    await router.push('/watched');
    await router.isReady();

    expect(router.currentRoute.value.name).toBe('login');
    expect(router.currentRoute.value.query.next).toBe('/watched');
  });

  it('lands a signed-in visitor on the deep link itself', async () => {
    signedIn = true;

    await router.push('/watched');
    await router.isReady();

    expect(router.currentRoute.value.name).toBe('lists');
    expect(router.currentRoute.value.params.list).toBe('watched');
  });

  it('keeps a signed-in visitor off the auth screens', async () => {
    signedIn = true;

    await router.push('/login');

    expect(router.currentRoute.value.name).not.toBe('login');
  });
});

// A smoke test, not a layout assertion: it proves the shell, the nav, and the
// auth screen mount without a runtime error. fe-06/fe-07 will churn the markup,
// so nothing here looks at it.
describe('app mounts', () => {
  it('renders the authenticated shell', async () => {
    signedIn = true;
    await router.push('/watched');
    await router.isReady();

    const wrapper = mount(App, { global: { plugins: [pinia, router] } });
    await router.isReady();

    expect(wrapper.find('nav[aria-label="Primary"]').exists()).toBe(true);
    wrapper.unmount();
  });

  it('renders the sign-in screen without the shell', async () => {
    signedIn = false;
    await router.push('/me');
    await router.isReady();

    const wrapper = mount(App, { global: { plugins: [pinia, router] } });

    expect(router.currentRoute.value.name).toBe('login');
    expect(wrapper.find('nav[aria-label="Primary"]').exists()).toBe(false);
    wrapper.unmount();
  });
});
