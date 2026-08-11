<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import DiscoveryRow from '@/components/DiscoveryRow.vue';
import EmptyState from '@/components/EmptyState.vue';
import ErrorState from '@/components/ErrorState.vue';
import FeedGroup from '@/components/FeedGroup.vue';
import IconPeople from '@/components/icons/IconPeople.vue';
import ScreenHeader from '@/components/ScreenHeader.vue';
import SpinnerBlock from '@/components/SpinnerBlock.vue';
import { useFeed } from '@/composables/useFeed';
import { groupActivity } from '@/lib/feedGroups';
import type { DiscoverFeed } from '@/api/types';

/**
 * The friends activity feed (FR-G2, FR-G3), and the home route.
 *
 * "Browse" comes first and the activity below it: the feed is friends-only and
 * never contains your own activity, so an empty feed is the *normal* state for
 * someone who has not added anyone yet. Rails at the top mean the home screen
 * always opens on something to watch rather than on an apology, and the empty
 * state keeps its prominent link to `/friends`.
 *
 * Paging has two triggers on purpose (NFR-8). The `IntersectionObserver` is the
 * comfortable one; the button is the one a keyboard or screen-reader user can
 * reach, and the recovery path when the observer misfires. Both call the same
 * function, which shares a single in-flight promise, so they cannot double-fire.
 */
const { items, status, error, hasMore, loadingMore, loadMore, refresh } = useFeed();

/**
 * One entry per burst rather than per event — six films queued in a sitting is
 * one line and one row of posters. Purely how it reads; `items` is untouched and
 * still the thing paging appends to.
 */
const groups = computed(() => groupActivity(items.value));

const rails: { feed: DiscoverFeed; title: string }[] = [
  { feed: 'popular', title: 'Popular' },
  { feed: 'top-rated', title: 'Top rated' },
  { feed: 'now-playing', title: 'Now playing' }
];

/**
 * `idle` counts as loading: the first fetch is fired from `onMounted`, which
 * runs *after* the first render, and without this the screen shows "Your feed is
 * quiet" for a frame before it has asked anything.
 */
const isFirstLoad = computed(() => status.value === 'idle' || status.value === 'loading');

const sentinel = ref<HTMLElement | null>(null);
let observer: IntersectionObserver | null = null;

function disconnect(): void {
  observer?.disconnect();
  observer = null;
}

function observe(element: HTMLElement | null): void {
  disconnect();
  // Absent in jsdom, and in any browser old enough to lack it the button still
  // works — which is the whole reason the button exists.
  if (!element || typeof IntersectionObserver === 'undefined') return;

  observer = new IntersectionObserver(
    (entries) => {
      if (!entries.some((entry) => entry.isIntersecting)) return;
      if (!hasMore.value) return;
      // `loadMore` ignores a call while a request is in flight.
      void loadMore();
    },
    { rootMargin: '400px 0px' }
  );

  observer.observe(element);
}

// The sentinel only exists while there is more to fetch, so re-observe when it
// appears and drop the observer when it goes.
watch(sentinel, (element) => observe(element));

onMounted(() => {
  void loadMore();
});

onBeforeUnmount(disconnect);
</script>

<template>
  <div>
    <ScreenHeader title="Feed" />

    <!-- Always present, and first: somewhere to go before the feed has anything
         to say. -->
    <section class="browse" aria-labelledby="feed-browse">
      <h2 id="feed-browse" class="browse__title">Browse</h2>
      <DiscoveryRow v-for="rail in rails" :key="rail.feed" :feed="rail.feed" :title="rail.title" />
    </section>

    <section class="activity" aria-labelledby="feed-activity">
      <h2 id="feed-activity" class="activity__title">From your friends</h2>

      <SpinnerBlock v-if="isFirstLoad" label="Loading your feed" />

      <ErrorState v-else-if="status === 'error'" :error="error" @retry="refresh()" />

      <template v-else>
        <!-- Friends-only and never your own activity: empty is normal, not broken. -->
        <EmptyState
          v-if="items.length === 0"
          headline="Your feed is quiet"
          body="Activity from your friends shows up here — what they watched, queued, and rated."
        >
          <template #icon><IconPeople /></template>
          <template #action>
            <RouterLink to="/friends" class="feed__cta">Find your friends</RouterLink>
          </template>
        </EmptyState>

        <template v-else>
          <ul class="feed">
            <li v-for="group in groups" :key="group.id" class="feed__row">
              <FeedGroup :group="group" />
            </li>
          </ul>

          <div class="feed__more">
            <!-- A failure part-way down keeps what is already on screen (NFR-10). -->
            <p v-if="error" class="feed__error" role="alert">{{ error.message }}</p>

            <template v-if="hasMore">
              <span ref="sentinel" class="feed__sentinel" aria-hidden="true" />
              <BaseButton variant="secondary" :loading="loadingMore" @click="loadMore()">
                Load more
              </BaseButton>
            </template>

            <p v-else class="feed__end">You are all caught up.</p>
          </div>
        </template>
      </template>
    </section>
  </div>
</template>

<style scoped>
.feed {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

/*
 * No `content-visibility` here: a group is any number of cards tall, so there is
 * no honest `contain-intrinsic-size` to give it. `TitleGrid` sets it per cell
 * instead, which is the same guarantee at a size that is actually known (NFR-2).
 */
.feed__row {
  min-width: 0;
}

.feed__cta {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--tap-min);
  padding: 0 var(--space-4);
  border-radius: var(--radius-md);
  background: var(--accent);
  color: var(--accent-ink);
  font-size: var(--text-sm);
  font-weight: 600;
  text-decoration: none;
}

.feed__more {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-6) var(--space-4);
}

/* Zero-height marker: it triggers paging, it is not a layout element. */
.feed__sentinel {
  display: block;
  width: 100%;
  height: 1px;
}

.feed__error {
  font-size: var(--text-sm);
  text-align: center;
}

.feed__end {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

/* The rule divides the two halves of the screen, so it belongs to the lower one. */
.activity {
  padding-top: var(--space-2);
  border-top: 1px solid var(--border);
  margin-top: var(--space-2);
}

.browse__title,
.activity__title {
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
  padding: var(--space-4) var(--space-4) var(--space-3);
}

@media (min-width: 900px) {
  .feed__more {
    padding: var(--space-6);
  }

  .browse__title,
  .activity__title {
    padding: var(--space-4) var(--space-6) var(--space-3);
  }
}
</style>
