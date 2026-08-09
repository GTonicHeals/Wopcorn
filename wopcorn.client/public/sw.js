/*
 * Wopcorn service worker — hand-written on purpose (fe-05, task 11).
 *
 * Its only job is making the app installable and letting a cold standalone
 * launch paint something. Offline is explicitly out of scope (FR-H7): caching
 * API responses would serve stale lists and ratings, which is worse than not
 * working offline at all. Everything except a navigation falls through
 * untouched — including every /api/ request.
 *
 * Bump CACHE_VERSION to evict the old shell.
 */

const CACHE_VERSION = 'wopcorn-shell-v1';
const SHELL_URLS = ['/', '/index.html'];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE_VERSION)
      .then((cache) => cache.addAll(SHELL_URLS))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((names) => names.filter((name) => name !== CACHE_VERSION))
      .then((stale) => Promise.all(stale.map((name) => caches.delete(name))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const request = event.request;

  // Only document navigations. Assets, avatars, TMDB images, and every /api/
  // call go straight to the network with no worker in the way.
  if (request.mode !== 'navigate') return;

  event.respondWith(
    fetch(request)
      .then((response) => {
        // Keep the shell fresh for the next cold start.
        const copy = response.clone();
        caches.open(CACHE_VERSION).then((cache) => cache.put('/index.html', copy));
        return response;
      })
      .catch(() =>
        caches
          .match('/index.html', { cacheName: CACHE_VERSION })
          .then((cached) => cached ?? Response.error())
      )
  );
});
