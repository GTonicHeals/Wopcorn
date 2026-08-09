<script setup lang="ts">
import { computed, watch } from 'vue';
import { useRoute } from 'vue-router';

import EmptyState from '@/components/EmptyState.vue';
import ErrorState from '@/components/ErrorState.vue';
import IconSearch from '@/components/icons/IconSearch.vue';
import ScreenHeader from '@/components/ScreenHeader.vue';
import SpinnerBlock from '@/components/SpinnerBlock.vue';
import TitleGrid from '@/components/TitleGrid.vue';
import { useTitleSearch } from '@/composables/useTitleSearch';

/**
 * FR-B1/FR-B2. The debounce and the out-of-order-response guard live in
 * `useTitleSearch`; this screen is the input and the four states around it.
 *
 * Films and series come back interleaved by TMDB's own relevance, and the type
 * chip on each card is what tells them apart — there is no type filter here,
 * because narrowing a search you are still typing is a worse answer than seeing
 * both.
 *
 * The field sits at the **top** of the content area on mobile: the bottom nav
 * already owns thumb space, and a second bottom element would collide with the
 * software keyboard.
 */
const { query, results, renderedQuery, status, isLoading, error, totalResults, runNow } =
  useTitleSearch();

/**
 * `?q=` seeds the field — this is where the global palette hands off when its
 * six rows are not the whole answer. Read one way only: typing here does not
 * rewrite the URL, which would put a history entry behind every keystroke.
 */
const route = useRoute();

watch(
  () => route.query.q,
  (raw) => {
    const seed = typeof raw === 'string' ? raw : '';
    // Arriving with the same term already rendered would restart the search for
    // results that are already on screen.
    if (seed.length > 0 && seed !== query.value) query.value = seed;
  },
  { immediate: true }
);

const hasResults = computed(() => results.value.length > 0);
const showFirstLoad = computed(() => isLoading.value && !hasResults.value);
const showNoResults = computed(
  () => status.value === 'ready' && !hasResults.value && renderedQuery.value.length > 0
);
const count = computed(() =>
  status.value === 'ready' && totalResults.value > 0
    ? `${totalResults.value.toLocaleString()} results`
    : undefined
);
</script>

<template>
  <div>
    <ScreenHeader title="Search" :count="count" />

    <div class="search__bar">
      <label class="sr-only" for="title-search">Search films and series</label>
      <span class="search__icon" aria-hidden="true"><IconSearch /></span>
      <input
        id="title-search"
        v-model="query"
        class="search__input"
        type="search"
        enterkeyhint="search"
        autocomplete="off"
        autocapitalize="none"
        spellcheck="false"
        placeholder="Film or series title"
        @keydown.enter.prevent="runNow"
      />
    </div>

    <!-- A failure with nothing to fall back on takes the screen; otherwise the
         previous results stay put and the error is the one thing that changes. -->
    <ErrorState v-if="status === 'error' && !hasResults" :error="error" @retry="runNow" />

    <SpinnerBlock v-else-if="showFirstLoad" label="Searching" />

    <EmptyState
      v-else-if="!hasResults && renderedQuery.length === 0"
      headline="Find something to watch"
      body="Search TMDB for a film or a series, then add it to Watched, your Watchlist, or the Queue."
    >
      <template #icon><IconSearch /></template>
    </EmptyState>

    <EmptyState
      v-else-if="showNoResults"
      :headline="`Nothing found for “${renderedQuery}”`"
      body="Try a shorter title, or the original-language title."
    >
      <template #icon><IconSearch /></template>
    </EmptyState>

    <!-- While the next set loads the current one stays on screen, dimmed. -->
    <TitleGrid v-else :titles="results" :stale="isLoading" />
  </div>
</template>

<style scoped>
.search__bar {
  position: sticky;
  top: 0;
  z-index: 10;
  display: flex;
  align-items: center;
  gap: var(--space-2);
  margin: 0 var(--space-4) var(--space-4);
  padding: 0 var(--space-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.search__icon {
  flex: none;
  color: var(--text-muted);
  line-height: 0;
}

.search__icon :deep(svg) {
  width: 18px;
  height: 18px;
}

.search__input {
  flex: 1;
  min-width: 0;
  /* FR-H4: at least 44px, and 16px text so iOS does not zoom on focus. */
  min-height: var(--tap-min);
  border: 0;
  background: none;
  font-size: 16px;
  color: var(--text);
}

.search__input::placeholder {
  color: var(--text-muted);
}

@media (min-width: 900px) {
  .search__bar {
    margin: 0 var(--space-6) var(--space-4);
    max-width: 480px;
  }
}
</style>
