import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router';

import AppShell from '@/components/AppShell.vue';
import LoginView from '@/views/LoginView.vue';
import RegisterView from '@/views/RegisterView.vue';
import { safeNextPath } from '@/router/next';
import { useAuthStore } from '@/stores/auth';
import type { ListName } from '@/api/types';

declare module 'vue-router' {
  interface RouteMeta {
    /** Signed-in only; anonymous visitors bounce to /login?next=… */
    requiresAuth?: boolean;
    /** Signed-out only; signed-in visitors bounce away from the auth screens. */
    anonymousOnly?: boolean;
  }
}

const routes: RouteRecordRaw[] = [
  {
    // AppShell wraps every authenticated route; its <RouterView> renders these.
    path: '/',
    component: AppShell,
    meta: { requiresAuth: true },
    children: [
      { path: '', name: 'feed', component: () => import('@/views/FeedView.vue') },
      { path: 'search', name: 'search', component: () => import('@/views/SearchView.vue') },
      {
        // One component for all three lists; the list is a route param.
        path: ':list(watched|watchlist|queue)',
        name: 'lists',
        component: () => import('@/views/ListsView.vue'),
        props: (route) => ({ list: route.params.list as ListName })
      },
      {
        /*
         * Constrained to the title-key grammar, so a malformed key falls through
         * to the catch-all and renders NotFoundView instead of firing a request
         * the server would only answer with a 400.
         *
         * Spelled as three top-level alternatives rather than
         * `(movie|tv)-\d+(-s\d+)?`, because vue-router's path parser reads a
         * custom param regexp up to the first unescaped `)` — a nested group
         * truncates the pattern and throws at router construction.
         */
        path: 'title/:key(movie-\\d+|tv-\\d+|tv-\\d+-s\\d+)',
        name: 'title',
        component: () => import('@/views/TitleView.vue'),
        props: (route) => ({ titleKey: route.params.key as string })
      },

      /*
       * Permanent. The API deliberately kept no `/api/films/*` shim — the client
       * ships in this repo and was updated in the same change — but bookmarks and
       * links already sent out are outside this repo's control, and every one of
       * them names a film.
       */
      {
        path: 'film/:tmdbId(\\d+)',
        redirect: (route) => `/title/movie-${route.params.tmdbId as string}`
      },
      { path: 'friends', name: 'friends', component: () => import('@/views/FriendsView.vue') },
      {
        // Someone else's profile. A link to your *own* id lands here too and the
        // view resolves it to the same page `/profile` renders — the profile is
        // one screen, not two.
        path: 'u/:userId',
        name: 'profile',
        component: () => import('@/views/ProfileView.vue'),
        props: true
      },
      {
        // Your own, with no id in the URL to copy out of the address bar.
        path: 'profile',
        name: 'my-profile',
        component: () => import('@/views/ProfileView.vue')
      },
      { path: 'me', name: 'me', component: () => import('@/views/MeView.vue') },

      // Inside the shell so a mistyped URL still has navigation. Static routes
      // outrank a catch-all, so /login and /register are unaffected.
      {
        path: ':pathMatch(.*)*',
        name: 'not-found',
        component: () => import('@/views/NotFoundView.vue')
      }
    ]
  },

  // The auth screens are eagerly imported: they are the first thing a signed-out
  // visitor sees, and a chunk request there is a visible delay.
  { path: '/login', name: 'login', component: LoginView, meta: { anonymousOnly: true } },
  { path: '/register', name: 'register', component: RegisterView, meta: { anonymousOnly: true } },

  // Lazy, unlike the two above: these are reached from a link in a mail or from
  // /login, never as the first paint of a cold session.
  {
    path: '/forgot-password',
    name: 'forgot-password',
    component: () => import('@/views/ForgotPasswordView.vue'),
    meta: { anonymousOnly: true }
  },
  {
    // Deliberately *not* anonymousOnly. Someone who still has a live session on
    // one device and is resetting from a mailed link on another would otherwise
    // be bounced away and lose the token, which is single-use.
    path: '/reset-password',
    name: 'reset-password',
    component: () => import('@/views/ResetPasswordView.vue')
  }
];

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
  scrollBehavior(to, from, saved) {
    return saved ?? { top: 0 };
  }
});

router.beforeEach(async (to) => {
  const auth = useAuthStore();

  // One boot check for the whole session; the store deduplicates concurrent calls.
  if (auth.status === 'loading') {
    await auth.boot();
  }

  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    const next = to.fullPath;
    return { name: 'login', query: next && next !== '/' ? { next } : {} };
  }

  if (to.meta.anonymousOnly && auth.isAuthenticated) {
    return safeNextPath(to.query.next);
  }

  return true;
});

export default router;
