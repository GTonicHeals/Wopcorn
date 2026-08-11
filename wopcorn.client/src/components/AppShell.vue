<script setup lang="ts">
import { computed, onMounted, ref, type Component } from 'vue';
import { RouterLink, RouterView, useRoute } from 'vue-router';

import GlobalSearch from '@/components/GlobalSearch.vue';
import IconHome from '@/components/icons/IconHome.vue';
import IconLayers from '@/components/icons/IconLayers.vue';
import IconPeople from '@/components/icons/IconPeople.vue';
import IconSearch from '@/components/icons/IconSearch.vue';
import IconUser from '@/components/icons/IconUser.vue';
import ToastHost from '@/components/ToastHost.vue';
import UserAvatar from '@/components/UserAvatar.vue';
import { useSearchHotkey } from '@/composables/useSearchHotkey';
import { useAuthStore } from '@/stores/auth';
import { useFriendsStore } from '@/stores/friends';

type NavItem = {
  key: string;
  label: string;
  to: string;
  icon: Component;
  /** Route names that should light this item up. */
  matches: string[];
};

const route = useRoute();
const auth = useAuthStore();
const friends = useFriendsStore();

// Exactly five destinations, in this order, on both layouts.
const items: NavItem[] = [
  { key: 'feed', label: 'Feed', to: '/', icon: IconHome, matches: ['feed'] },
  { key: 'search', label: 'Search', to: '/search', icon: IconSearch, matches: ['search'] },
  { key: 'lists', label: 'Lists', to: '/watched', icon: IconLayers, matches: ['lists'] },
  {
    key: 'friends',
    label: 'Friends',
    to: '/friends',
    icon: IconPeople,
    matches: ['friends', 'profile']
  },
  // "You" is the profile, not the settings behind it — the page other people
  // see is the one worth landing on, and Settings is a button on it.
  { key: 'me', label: 'You', to: '/profile', icon: IconUser, matches: ['me', 'my-profile'] }
];

const activeKey = computed(() => {
  const name = typeof route.name === 'string' ? route.name : '';
  return items.find((item) => item.matches.includes(name))?.key ?? '';
});

/**
 * FR-F4: the badge has to be right on whatever screen the app opens on, so the
 * one `GET /api/friends` call happens here rather than on the friends view. The
 * store deduplicates, so navigating there does not fetch it twice.
 */
onMounted(() => void friends.load());

const pendingCount = computed(() => friends.pendingCount);
const badgeLabel = computed(() =>
  pendingCount.value > 99 ? '99+' : String(pendingCount.value)
);

/**
 * Search from anywhere. The overlay lives here, above the router view, so it
 * survives navigation and is reachable from every screen.
 *
 * There are deliberately only two ways in. On desktop, the field-shaped button
 * below the wordmark; everywhere, `/` and Ctrl/Cmd-K. Mobile gets no new
 * affordance — the bottom bar holds exactly five destinations, one of which is
 * already Search, and a sixth would break both the grid and the thumb targets.
 */
const searchOpen = ref(false);

useSearchHotkey(() => {
  searchOpen.value = true;
});
</script>

<template>
  <div class="shell">
    <nav class="shell__nav" aria-label="Primary">
      <p class="shell__wordmark">Wopcorn</p>

      <!-- Hidden on mobile, like the wordmark: the bar below is a five-column
           grid and this is not one of the five. -->
      <button type="button" class="shell__search" @click="searchOpen = true">
        <span class="shell__search-icon" aria-hidden="true"><IconSearch /></span>
        <span class="shell__search-label">Search films</span>
        <kbd class="shell__search-key" aria-hidden="true">/</kbd>
      </button>

      <RouterLink
        v-for="item in items"
        :key="item.key"
        :to="item.to"
        class="nav-item"
        :class="{ 'nav-item--active': activeKey === item.key }"
        :aria-current="activeKey === item.key ? 'page' : undefined"
      >
        <span class="nav-item__icon">
          <UserAvatar v-if="item.key === 'me'" :user="auth.user" :size="24" />
          <component :is="item.icon" v-else :filled="activeKey === item.key" />

          <span
            v-if="item.key === 'friends' && pendingCount > 0"
            class="nav-item__badge"
          >{{ badgeLabel }}<span class="sr-only"> pending friend requests</span></span>
        </span>
        <span class="nav-item__label">{{ item.label }}</span>
      </RouterLink>
    </nav>

    <main class="shell__main">
      <div class="shell__inner">
        <RouterView />
      </div>
    </main>

    <GlobalSearch v-model:open="searchOpen" />

    <!-- Reverted optimistic mutations and the queue conflict speak through here. -->
    <ToastHost />
  </div>
</template>

<style scoped>
.shell {
  min-height: 100dvh;
}

/* ---------------------------------------------------- mobile: bottom nav bar */

.shell__nav {
  position: fixed;
  inset: auto 0 0 0;
  z-index: 20;
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  /* The 0px fallbacks matter: an unsupported env() with no fallback poisons the
     whole calc(), and the bar loses its height rather than its inset. */
  height: calc(var(--nav-height) + env(safe-area-inset-bottom, 0px));
  padding-bottom: env(safe-area-inset-bottom, 0px);
  background: var(--surface);
  border-top: 1px solid var(--border);
}

.shell__wordmark {
  display: none;
}

.shell__search {
  display: none;
}

.nav-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 3px;
  min-width: var(--tap-min);
  min-height: var(--tap-min);
  text-decoration: none;
  color: var(--text-muted);
}

/*
 * Active is never colour-only (NFR-9): the icon switches from outline to filled
 * and the label goes semibold, on top of the accent.
 */
.nav-item--active {
  color: var(--accent);
}

.nav-item--active .nav-item__label {
  font-weight: 600;
}

.nav-item__icon {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
}

.nav-item__icon :deep(svg) {
  width: 22px;
  height: 22px;
}

.nav-item__label {
  font-size: 11px;
  line-height: 1;
}

.nav-item__badge {
  position: absolute;
  top: -5px;
  left: 60%;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: var(--radius-full);
  background: var(--accent);
  color: var(--accent-ink);
  font-size: 10px;
  font-weight: 700;
  line-height: 16px;
  text-align: center;
}

.shell__main {
  /* The bar never covers content. */
  padding-bottom: calc(var(--nav-height) + env(safe-area-inset-bottom, 0px) + var(--space-4));
}

.shell__inner {
  max-width: var(--content-max);
  margin: 0 auto;
}

/* -------------------------------------------------- desktop: left sidebar */

@media (min-width: 900px) {
  .shell {
    display: grid;
    grid-template-columns: var(--sidebar-width) 1fr;
  }

  .shell__nav {
    position: sticky;
    top: 0;
    right: auto;
    bottom: auto;
    left: auto;
    align-self: start;
    height: 100dvh;
    display: flex;
    flex-direction: column;
    gap: 2px;
    grid-template-columns: none;
    padding: var(--space-6) var(--space-3);
    background: transparent;
    border-top: 0;
    border-right: 1px solid var(--border);
  }

  .shell__wordmark {
    display: block;
    font-family: var(--font-display);
    font-size: 20px;
    font-weight: 500;
    letter-spacing: 0.02em;
    padding: 0 var(--space-3) var(--space-4);
  }

  /*
   * Shaped like the field it opens rather than like a nav item, because it is
   * not a sixth destination — pressing it leaves you on the screen you were on.
   */
  .shell__search {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    width: 100%;
    min-height: 38px;
    margin-bottom: var(--space-3);
    padding: 0 var(--space-2) 0 var(--space-3);
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    background: var(--surface);
    font: inherit;
    color: var(--text-muted);
    text-align: left;
  }

  .shell__search-icon {
    flex: none;
    line-height: 0;
  }

  .shell__search-icon :deep(svg) {
    width: 16px;
    height: 16px;
  }

  .shell__search-label {
    flex: 1;
    min-width: 0;
    font-size: var(--text-sm);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .shell__search-key {
    flex: none;
    padding: 1px 5px;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    font: inherit;
    font-size: 11px;
    line-height: 1.4;
  }

  .nav-item {
    flex-direction: row;
    justify-content: flex-start;
    gap: var(--space-3);
    padding: 0 var(--space-3);
    border-radius: var(--radius-md);
  }

  .nav-item--active {
    background: var(--surface);
  }

  .nav-item__label {
    font-size: var(--text-sm);
    line-height: 1.2;
  }

  .nav-item__badge {
    position: static;
    margin-left: auto;
  }

  .shell__main {
    padding-bottom: var(--space-8);
  }
}
</style>
