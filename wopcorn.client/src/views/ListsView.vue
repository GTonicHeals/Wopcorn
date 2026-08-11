<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import BaseSheet from '@/components/BaseSheet.vue';
import EmptyState from '@/components/EmptyState.vue';
import ErrorState from '@/components/ErrorState.vue';
import IconClose from '@/components/icons/IconClose.vue';
import IconGrid from '@/components/icons/IconGrid.vue';
import IconLayers from '@/components/icons/IconLayers.vue';
import IconRows from '@/components/icons/IconRows.vue';
import IconSearch from '@/components/icons/IconSearch.vue';
import IconSort from '@/components/icons/IconSort.vue';
import ListTable from '@/components/ListTable.vue';
import QueueBoard from '@/components/QueueBoard.vue';
import ScreenHeader from '@/components/ScreenHeader.vue';
import SpinnerBlock from '@/components/SpinnerBlock.vue';
import TitleGrid from '@/components/TitleGrid.vue';
import TitleQuickView from '@/components/TitleQuickView.vue';
import { listTotals, titleCount } from '@/lib/format';
import { matchesServices, viewerServices } from '@/lib/services';
import { matchesQuery } from '@/lib/titleFilter';
import { readViewMode, writeViewMode, type ViewMode } from '@/lib/viewMode';
import { useAuthStore } from '@/stores/auth';
import { useConfigStore } from '@/stores/config';
import { defaultDirection, useListsStore } from '@/stores/lists';
import { useTitlesStore } from '@/stores/titles';
import type { ListName, ListSort, MediaType, SortDirection, TitleCard } from '@/api/types';

/**
 * One component for all three lists (FR-C4, FR-C5); the route says which.
 *
 * Sort is a server round trip — `GET /api/lists/{list}?sort&dir` is the
 * authoritative ordering, and `rating` only means anything on Watched. Type,
 * genre and decade filters are applied here instead: the decade options are
 * derived from the entries present, which needs the unfiltered set anyway, and
 * the response's `count` is deliberately the unfiltered total — the type filter
 * does not move it either — so "showing 12 of 84" has both halves without a
 * second request.
 *
 * The queue hides sort and filters entirely — it has one true order, and
 * `PUT /api/queue/order` requires the complete list, which a filtered view
 * cannot supply.
 */
const props = defineProps<{ list: ListName }>();

const auth = useAuthStore();
const config = useConfigStore();
const titles = useTitlesStore();
const lists = useListsStore();

const tabs: { list: ListName; label: string; to: string }[] = [
  { list: 'watched', label: 'Watched', to: '/watched' },
  { list: 'watchlist', label: 'Watchlist', to: '/watchlist' },
  { list: 'queue', label: 'Queue', to: '/queue' }
];

const title = computed(() => tabs.find((tab) => tab.list === props.list)?.label ?? 'Lists');
const state = computed(() => lists.state[props.list]);

// ------------------------------------------------------------------ filters

const selectedGenres = ref<number[]>([]);
const selectedDecades = ref<number[]>([]);
const selectedTypes = ref<MediaType[]>([]);
const selectedServices = ref<number[]>([]);
const filtersOpen = ref(false);
const sortOpen = ref(false);

/**
 * The type-to-filter field, matched by `lib/titleFilter`.
 *
 * It is deliberately *not* a fifth entry in the filter sheet and gets no chip in
 * the row below the tools: the field is on screen with its own text in it and
 * its own clear button, so a chip would be a second place saying the same thing
 * and a second place to clear it from. It does count as narrowing everywhere
 * that matters — the header's "showing 12 of 84" and the empty state.
 *
 * The queue has none of this. It has one true order and `PUT /api/queue/order`
 * needs the complete list, which is the same reason it hides sort and filters.
 */
const query = ref('');
const trimmedQuery = computed(() => query.value.trim());

/** Plural labels, because these narrow a list rather than name one thing. */
const TYPE_FILTERS: { value: MediaType; label: string }[] = [
  { value: 'movie', label: 'Films' },
  { value: 'series', label: 'Series' },
  { value: 'season', label: 'Seasons' }
];

watch(
  () => props.list,
  (list) => {
    selectedGenres.value = [];
    selectedDecades.value = [];
    selectedTypes.value = [];
    selectedServices.value = [];
    query.value = '';
    void lists.ensure(list);
    if (list !== 'queue') void titles.loadGenres();
  },
  { immediate: true }
);

// The directory names the badges and the filter options; it is cached per region.
watch(
  () => auth.region,
  (region) => void config.loadProviders(region),
  { immediate: true }
);

const entryTitles = computed(() =>
  state.value.entries
    .map((entry) => titles.get(entry.key))
    .filter((title): title is TitleCard => title !== null)
);

function decadeOf(year: number | null): number | null {
  return year === null ? null : Math.floor(year / 10) * 10;
}

const decadeOptions = computed(() => {
  const decades = new Set<number>();
  for (const title of entryTitles.value) {
    const decade = decadeOf(title.releaseYear);
    if (decade !== null) decades.add(decade);
  }
  return [...decades].sort((a, b) => b - a);
});

const genreOptions = computed(() => titles.genres);

/**
 * Only the types actually present. A "Seasons" checkbox on a list holding none
 * is a control whose only possible effect is to empty the screen.
 */
const typeOptions = computed(() => {
  const present = new Set(entryTitles.value.map((title) => title.mediaType));
  return TYPE_FILTERS.filter((option) => present.has(option.value));
});

/**
 * The viewer's own services, in the directory's order.
 *
 * **When none are configured this is empty and none of the streaming controls
 * render** — no empty group, no disabled chip, no nag. The filter appears when it
 * can do something, and until then the list looks exactly as it did.
 */
const serviceOptions = computed(() =>
  viewerServices(config.providersByRegion.get(auth.region ?? '') ?? [], auth.providerIds)
);

/** The sheet's four, which is what its Clear button and its count answer for. */
const hasFilters = computed(
  () =>
    selectedGenres.value.length > 0 ||
    selectedDecades.value.length > 0 ||
    selectedTypes.value.length > 0 ||
    selectedServices.value.length > 0
);

/** Those four *or* the field: anything at all standing between you and the list. */
const isNarrowed = computed(() => hasFilters.value || trimmedQuery.value !== '');

/** True once every configured service is selected — what the one-tap chip sets. */
const onMyServices = computed(
  () =>
    serviceOptions.value.length > 0 &&
    selectedServices.value.length === serviceOptions.value.length
);

function toggleOnMyServices(): void {
  selectedServices.value = onMyServices.value
    ? []
    : serviceOptions.value.map((provider) => provider.id);
}

const visible = computed(() =>
  entryTitles.value.filter((title) => {
    // First, because while you are typing it is the one doing nearly all the work.
    if (!matchesQuery(title.title, trimmedQuery.value)) {
      return false;
    }

    if (selectedTypes.value.length > 0 && !selectedTypes.value.includes(title.mediaType)) {
      return false;
    }

    // `availableOn` already *is* the answer the `service=` query parameter gives —
    // the viewer's own services, flatrate, in their region — so this narrows the
    // rows already on screen rather than costing a round trip.
    if (!matchesServices(title.availableOn, selectedServices.value)) {
      return false;
    }

    if (
      selectedGenres.value.length > 0 &&
      !title.genreIds.some((id) => selectedGenres.value.includes(id))
    ) {
      return false;
    }

    if (selectedDecades.value.length > 0) {
      const decade = decadeOf(title.releaseYear);
      if (decade === null || !selectedDecades.value.includes(decade)) return false;
    }

    return true;
  })
);

function toggleType(type: MediaType): void {
  selectedTypes.value = selectedTypes.value.includes(type)
    ? selectedTypes.value.filter((existing) => existing !== type)
    : [...selectedTypes.value, type];
}

function toggleGenre(id: number): void {
  selectedGenres.value = selectedGenres.value.includes(id)
    ? selectedGenres.value.filter((existing) => existing !== id)
    : [...selectedGenres.value, id];
}

function toggleDecade(decade: number): void {
  selectedDecades.value = selectedDecades.value.includes(decade)
    ? selectedDecades.value.filter((existing) => existing !== decade)
    : [...selectedDecades.value, decade];
}

function toggleService(id: number): void {
  selectedServices.value = selectedServices.value.includes(id)
    ? selectedServices.value.filter((existing) => existing !== id)
    : [...selectedServices.value, id];
}

/**
 * The sheet's button, and so the sheet's four only — it sits beside them, and
 * emptying a field it does not show would be a change with no visible cause.
 */
function clearFilters(): void {
  selectedGenres.value = [];
  selectedDecades.value = [];
  selectedTypes.value = [];
  selectedServices.value = [];
}

/** The empty state's button, which has to actually bring the list back. */
function clearAll(): void {
  clearFilters();
  query.value = '';
}

const activeChips = computed(() => [
  ...selectedServices.value.map((id) => ({
    key: `s${id}`,
    label: serviceOptions.value.find((provider) => provider.id === id)?.name ?? `Service ${id}`,
    remove: () => toggleService(id)
  })),
  ...selectedTypes.value.map((type) => ({
    key: `t${type}`,
    label: TYPE_FILTERS.find((option) => option.value === type)?.label ?? type,
    remove: () => toggleType(type)
  })),
  ...selectedGenres.value.map((id) => ({
    key: `g${id}`,
    label: titles.genreName(id) || `Genre ${id}`,
    remove: () => toggleGenre(id)
  })),
  ...selectedDecades.value.map((decade) => ({
    key: `d${decade}`,
    label: `${decade}s`,
    remove: () => toggleDecade(decade)
  }))
]);

const filterSummary = computed(() => activeChips.value.map((chip) => chip.label).join(', '));

/**
 * Two different dead ends, worded apart. A sheet full of checkboxes that between
 * them exclude everything is a different mistake from four letters that match
 * nothing, and the second one wants to say what it searched — people arrive at
 * this field expecting the Search screen's reach.
 */
const emptyHeadline = computed(() =>
  hasFilters.value
    ? 'Nothing matches those filters'
    : `Nothing here matches “${trimmedQuery.value}”`
);

const emptyBody = computed(() => {
  if (!hasFilters.value) {
    return `Try fewer letters. This filters the titles already on your ${title.value.toLowerCase()} list — it does not search TMDB.`;
  }

  return trimmedQuery.value === ''
    ? `No title in this list matches ${filterSummary.value}.`
    : `No title in this list matches “${trimmedQuery.value}” and ${filterSummary.value}.`;
});

const clearLabel = computed(() => (hasFilters.value ? 'Clear filters' : 'Clear the filter'));

// --------------------------------------------------------------------- sort

type SortOption = { value: ListSort; label: string };

const sortOptions = computed<SortOption[]>(() => {
  const base: SortOption[] = [
    { value: 'added', label: 'Date added' },
    { value: 'title', label: 'Title' },
    { value: 'year', label: 'Release year' },
    { value: 'runtime', label: 'Runtime' },
    { value: 'score', label: 'TMDB score' }
  ];

  // `rating` is only valid on Watched — nothing else has one.
  return props.list === 'watched' ? [...base, { value: 'rating', label: 'Your rating' }] : base;
});

const draftSort = ref<ListSort>('added');
const draftDir = ref<SortDirection>('desc');

function openSort(): void {
  draftSort.value = state.value.sort;
  draftDir.value = state.value.dir;
  sortOpen.value = true;
}

function pickSort(value: ListSort): void {
  draftSort.value = value;
  draftDir.value = defaultDirection(value);
}

async function applySort(): Promise<void> {
  sortOpen.value = false;
  await lists.setSort(props.list, draftSort.value, draftDir.value);
}

const sortLabel = computed(() => {
  const option = sortOptions.value.find((entry) => entry.value === state.value.sort);
  const arrow = state.value.dir === 'desc' ? '↓' : '↑';
  return `Sorted by ${(option?.label ?? 'date added').toLowerCase()} ${arrow}`;
});

// --------------------------------------------------------------- view mode

/**
 * Both Watched and Watchlist offer the poster wall and the dense row view: the
 * grid is for browsing a collection, the rows for scanning a long list, and
 * which one a list wants depends on how many titles are in it rather than on
 * which list it is. The choice is one preference shared by both screens.
 *
 * The queue has its own ordered board, which is not a mode at all — its rows
 * carry position numerals and drag handles that `ListTable` deliberately lacks.
 */
const viewMode = ref<ViewMode>(readViewMode());
const canSwitchView = computed(() => props.list !== 'queue');

function setViewMode(mode: ViewMode): void {
  viewMode.value = mode;
  writeViewMode(mode);
}

const showTable = computed(() => canSwitchView.value && viewMode.value === 'table');

// --------------------------------------------------------------- quick view

/**
 * Tapping a title here opens it in a dialog rather than leaving the screen — a
 * list is where you rate things, and the round trip through the title screen
 * costs the scroll position, the filters and the tab. The cards stay links, so
 * ctrl-click and "open in new tab" still reach the title screen, and the dialog
 * carries a **Full details** link for everything it deliberately leaves out.
 *
 * One dialog for the whole screen, keyed by title: a `<dialog>` per row would be
 * hundreds of them on a long list (the same reason `ListToggles` mounts its sheet
 * lazily).
 */
const quickKey = ref<string | null>(null);
const quickOpen = ref(false);

async function openQuickView(key: string): Promise<void> {
  quickKey.value = key;
  // BaseSheet calls showModal() on a false→true transition, so the dialog has to
  // exist with `open: false` for one tick before being opened.
  await nextTick();
  quickOpen.value = true;
}

// Switching tabs is leaving the thing you tapped behind.
watch(
  () => props.list,
  () => {
    quickOpen.value = false;
  }
);

// ------------------------------------------------------------------- header

/**
 * `"92 titles · 214h 51m"` — a count of titles beside the sum of the runtimes
 * that are *known*. It understates whenever a series has no `episode_run_time`,
 * which is often; that beats inventing episode lengths, and it beats hiding the
 * total.
 */
const headerCount = computed(() => {
  if (state.value.status !== 'ready') return undefined;
  if (props.list === 'queue') return titleCount(state.value.count);
  return isNarrowed.value
    ? `showing ${visible.value.length} of ${state.value.count}`
    : listTotals(entryTitles.value.map((title) => title.runtimeMinutes));
});
</script>

<template>
  <div>
    <ScreenHeader :title="title" :count="headerCount" />

    <div class="lists__tabs">
      <nav class="segmented" aria-label="Your lists">
        <RouterLink
          v-for="tab in tabs"
          :key="tab.list"
          :to="tab.to"
          class="segmented__item"
          :class="{ 'segmented__item--on': tab.list === props.list }"
          :aria-current="tab.list === props.list ? 'page' : undefined"
        >
          {{ tab.label }}
        </RouterLink>
      </nav>
    </div>

    <SpinnerBlock v-if="state.status === 'loading' && state.entries.length === 0" />

    <ErrorState
      v-else-if="state.status === 'error'"
      :error="state.error"
      @retry="lists.load(props.list, true)"
    />

    <!-- The queue brings its own controls: presets instead of sort, no filters. -->
    <QueueBoard v-else-if="props.list === 'queue'" quick-view @select="openQuickView" />

    <template v-else>
      <!--
        Above the tools row, not in it: it needs the full width, and something you
        type outranks two buttons that open a sheet. Not sticky — nothing else on
        this screen is, and a bar that detached from the tabs above it would look
        like it belonged to the rows rather than to the list.

        `type="search"` for the keyboard it summons, with the WebKit clear button
        suppressed in favour of the one below, which is 44px and on every browser.
      -->
      <div v-if="state.entries.length > 0" class="filterbar">
        <label class="sr-only" for="list-filter">Filter your {{ title.toLowerCase() }} list</label>
        <span class="filterbar__icon" aria-hidden="true"><IconSearch /></span>
        <input
          id="list-filter"
          v-model="query"
          class="filterbar__input"
          type="search"
          enterkeyhint="done"
          autocomplete="off"
          autocapitalize="none"
          spellcheck="false"
          placeholder="Filter by title"
        />
        <button
          v-if="query !== ''"
          type="button"
          class="filterbar__clear"
          @click="query = ''"
        >
          <IconClose />
          <span class="sr-only">Clear the title filter</span>
        </button>
      </div>

      <div class="tools">
        <button type="button" class="tools__btn" @click="openSort">
          <IconSort />
          <span>{{ sortLabel }}</span>
        </button>
        <button type="button" class="tools__btn" @click="filtersOpen = true">
          Filters<span v-if="hasFilters"> ({{ activeChips.length }})</span>
        </button>

        <!--
          The payoff, one tap: which of these can I actually watch tonight. Absent
          entirely until services are configured — a disabled control that can
          never do anything is worse than no control.

          It is the signed-in user's own state, so it takes the accent, like every
          other piece of gold in this app.
        -->
        <button
          v-if="serviceOptions.length > 0"
          type="button"
          class="tools__btn"
          :class="{ 'tools__btn--on': onMyServices }"
          :aria-pressed="onMyServices"
          @click="toggleOnMyServices"
        >
          On my services
        </button>

        <!-- Pressed state is a fill *and* a filled icon, never colour alone. -->
        <div v-if="canSwitchView" class="viewswitch" role="group" aria-label="View">
          <button
            type="button"
            class="viewswitch__btn"
            :class="{ 'viewswitch__btn--on': viewMode === 'grid' }"
            :aria-pressed="viewMode === 'grid'"
            @click="setViewMode('grid')"
          >
            <IconGrid :filled="viewMode === 'grid'" />
            <span class="sr-only">Poster grid</span>
          </button>
          <button
            type="button"
            class="viewswitch__btn"
            :class="{ 'viewswitch__btn--on': viewMode === 'table' }"
            :aria-pressed="viewMode === 'table'"
            @click="setViewMode('table')"
          >
            <IconRows :filled="viewMode === 'table'" />
            <span class="sr-only">Table</span>
          </button>
        </div>
      </div>

      <!-- Active filters stay visible without opening the sheet. -->
      <ul v-if="hasFilters" class="chips">
        <li v-for="chip in activeChips" :key="chip.key">
          <button type="button" class="chip chip--on" @click="chip.remove()">
            <span>{{ chip.label }}</span>
            <IconClose />
            <span class="sr-only">Remove filter</span>
          </button>
        </li>
      </ul>

      <EmptyState
        v-if="state.entries.length === 0"
        :headline="`Your ${title.toLowerCase()} list is empty`"
        body="Search for a film or a series and add it here."
      >
        <template #icon><IconLayers /></template>
      </EmptyState>

      <EmptyState v-else-if="visible.length === 0" :headline="emptyHeadline" :body="emptyBody">
        <template #icon><IconLayers /></template>
        <template #action>
          <BaseButton variant="secondary" @click="clearAll">{{ clearLabel }}</BaseButton>
        </template>
      </EmptyState>

      <ListTable v-else-if="showTable" :titles="visible" quick-view @select="openQuickView" />

      <TitleGrid v-else :titles="visible" quick-view @select="openQuickView" />
    </template>

    <TitleQuickView v-if="quickKey" v-model:open="quickOpen" :title-key="quickKey" />

    <!-- ------------------------------------------------------------ sheets -->

    <BaseSheet v-model:open="sortOpen" title="Sort">
      <fieldset class="sheet-group">
        <legend class="sr-only">Sort by</legend>
        <label v-for="option in sortOptions" :key="option.value" class="sheet-option">
          <input
            type="radio"
            name="list-sort"
            :value="option.value"
            :checked="draftSort === option.value"
            @change="pickSort(option.value)"
          />
          <span>{{ option.label }}</span>
        </label>
      </fieldset>

      <div class="dir" role="group" aria-label="Direction">
        <button
          type="button"
          class="dir__btn"
          :class="{ 'dir__btn--on': draftDir === 'asc' }"
          :aria-pressed="draftDir === 'asc'"
          @click="draftDir = 'asc'"
        >
          Ascending
        </button>
        <button
          type="button"
          class="dir__btn"
          :class="{ 'dir__btn--on': draftDir === 'desc' }"
          :aria-pressed="draftDir === 'desc'"
          @click="draftDir = 'desc'"
        >
          Descending
        </button>
      </div>

      <template #actions>
        <BaseButton variant="ghost" @click="sortOpen = false">Cancel</BaseButton>
        <BaseButton variant="primary" @click="applySort">Apply</BaseButton>
      </template>
    </BaseSheet>

    <BaseSheet v-model:open="filtersOpen" title="Filters">
      <!-- Streaming sits above Type: "what can I watch tonight" outranks "what
           kind of thing is it". Absent when there are no services configured. -->
      <fieldset v-if="serviceOptions.length > 0" class="sheet-group">
        <legend class="sheet-legend">Streaming</legend>
        <label v-for="provider in serviceOptions" :key="provider.id" class="sheet-option">
          <input
            type="checkbox"
            :checked="selectedServices.includes(provider.id)"
            @change="toggleService(provider.id)"
          />
          <span>{{ provider.name }}</span>
        </label>
      </fieldset>

      <!-- Type sits above Genre: it is the coarsest cut, and the likeliest
           reason someone opened this sheet at all. -->
      <fieldset v-if="typeOptions.length > 1" class="sheet-group">
        <legend class="sheet-legend">Type</legend>
        <label v-for="option in typeOptions" :key="option.value" class="sheet-option">
          <input
            type="checkbox"
            :checked="selectedTypes.includes(option.value)"
            @change="toggleType(option.value)"
          />
          <span>{{ option.label }}</span>
        </label>
      </fieldset>

      <fieldset v-if="genreOptions.length > 0" class="sheet-group">
        <legend class="sheet-legend">Genre</legend>
        <label v-for="genre in genreOptions" :key="genre.id" class="sheet-option">
          <input
            type="checkbox"
            :checked="selectedGenres.includes(genre.id)"
            @change="toggleGenre(genre.id)"
          />
          <span>{{ genre.name }}</span>
        </label>
      </fieldset>

      <fieldset v-if="decadeOptions.length > 0" class="sheet-group">
        <legend class="sheet-legend">Decade</legend>
        <label v-for="decade in decadeOptions" :key="decade" class="sheet-option">
          <input
            type="checkbox"
            :checked="selectedDecades.includes(decade)"
            @change="toggleDecade(decade)"
          />
          <span>{{ decade }}s</span>
        </label>
      </fieldset>

      <template #actions>
        <BaseButton variant="ghost" :disabled="!hasFilters" @click="clearFilters">
          Clear filters
        </BaseButton>
        <BaseButton variant="primary" @click="filtersOpen = false">Done</BaseButton>
      </template>
    </BaseSheet>
  </div>
</template>

<style scoped>
.lists__tabs {
  padding: 0 var(--space-4);
}

.segmented {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--space-1);
  background: var(--surface-raised);
  border-radius: var(--radius-full);
  padding: 3px;
}

.segmented__item {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: var(--tap-min);
  border-radius: var(--radius-full);
  text-decoration: none;
  color: var(--text-muted);
  font-size: var(--text-sm);
  font-weight: 600;
}

.segmented__item--on {
  background: var(--accent);
  color: var(--accent-ink);
}

.filterbar {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  margin: var(--space-3) var(--space-4) 0;
  padding-left: var(--space-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.filterbar__icon {
  flex: none;
  color: var(--text-muted);
  line-height: 0;
}

.filterbar__icon :deep(svg) {
  width: 18px;
  height: 18px;
}

.filterbar__input {
  flex: 1;
  min-width: 0;
  /* FR-H4: at least 44px, and 16px text so iOS does not zoom on focus. */
  min-height: var(--tap-min);
  padding: 0;
  border: 0;
  background: none;
  font-size: 16px;
  color: var(--text);
}

.filterbar__input::placeholder {
  color: var(--text-muted);
}

/* WebKit's own is 12px and unstyleable; the button beside it is the tap target. */
.filterbar__input::-webkit-search-cancel-button {
  display: none;
}

.filterbar__clear {
  display: flex;
  align-items: center;
  justify-content: center;
  flex: none;
  /* Clearing is undoing your own typing, not a piece of state — no accent. */
  width: var(--tap-min);
  min-height: var(--tap-min);
  border: 0;
  background: none;
  color: var(--text-muted);
}

.filterbar__clear :deep(svg) {
  width: 16px;
  height: 16px;
}

.tools {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  padding: var(--space-3) var(--space-4) var(--space-2);
}

.tools__btn {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--tap-min);
  padding: 0 var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-full);
  background: none;
  color: var(--text-muted);
  font-size: var(--text-xs);
  font-weight: 600;
}

.tools__btn :deep(svg) {
  width: 16px;
  height: 16px;
}

/* An active filter is the user's own state, so it earns the accent. */
.tools__btn--on {
  border-color: var(--accent);
  background: var(--accent);
  color: var(--accent-ink);
}

/* Pushed to the far end of the tools row — a display switch, not a filter. */
.viewswitch {
  display: flex;
  gap: 2px;
  margin-left: auto;
  padding: 3px;
  background: var(--surface-raised);
  border-radius: var(--radius-full);
}

.viewswitch__btn {
  display: flex;
  align-items: center;
  justify-content: center;
  /* FR-H4 floor, same as every other control in this row. */
  width: var(--tap-min);
  min-height: var(--tap-min);
  border: 0;
  background: none;
  border-radius: var(--radius-full);
  color: var(--text-muted);
}

/* The chosen view is the user's own state, so it earns the accent. */
.viewswitch__btn--on {
  background: var(--accent);
  color: var(--accent-ink);
}

.viewswitch__btn :deep(svg) {
  width: 17px;
  height: 17px;
}

.chips {
  display: flex;
  gap: var(--space-2);
  flex-wrap: wrap;
  padding: 0 var(--space-4) var(--space-3);
}

.chip {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  min-height: 32px;
  padding: 0 var(--space-2) 0 var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-full);
  background: none;
  color: var(--text-muted);
  font-size: 11.5px;
}

/* An active filter is the user's own state, so it earns the accent. */
.chip--on {
  border-color: var(--accent);
  color: var(--text);
}

.chip--on :deep(svg) {
  width: 14px;
  height: 14px;
  color: var(--accent);
}

.sheet-group {
  border: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  margin-bottom: var(--space-3);
}

.sheet-legend {
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
  padding-bottom: var(--space-1);
}

.sheet-option {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: var(--tap-min);
  font-size: var(--text-base);
}

.sheet-option input {
  width: 20px;
  height: 20px;
  flex: none;
  accent-color: var(--accent);
}

.dir {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-1);
  background: var(--surface-raised);
  border-radius: var(--radius-full);
  padding: 3px;
}

.dir__btn {
  min-height: var(--tap-min);
  border: 0;
  background: none;
  border-radius: var(--radius-full);
  color: var(--text-muted);
  font-size: var(--text-sm);
  font-weight: 600;
}

.dir__btn--on {
  background: var(--accent);
  color: var(--accent-ink);
}

@media (min-width: 900px) {
  .lists__tabs {
    padding: 0 var(--space-6);
    max-width: 420px;
  }

  .tools,
  .chips {
    padding-left: var(--space-6);
    padding-right: var(--space-6);
  }

  /* Long enough to hold a title, short enough not to read as the page's search. */
  .filterbar {
    max-width: 420px;
    margin-left: var(--space-6);
    margin-right: var(--space-6);
  }
}
</style>
