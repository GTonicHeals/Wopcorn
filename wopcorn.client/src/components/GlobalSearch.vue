<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { useRouter } from 'vue-router';

import IconClose from '@/components/icons/IconClose.vue';
import IconSearch from '@/components/icons/IconSearch.vue';
import IconStar from '@/components/icons/IconStar.vue';
import PosterImage from '@/components/PosterImage.vue';
import StarDisplay from '@/components/StarDisplay.vue';
import TypeChip from '@/components/TypeChip.vue';
import { useTitleSearch } from '@/composables/useTitleSearch';
import { formatScore } from '@/lib/format';
import { wrapIndex } from '@/lib/hotkeys';
import { titlePath } from '@/lib/titleKey';
import { titleMetaShort } from '@/lib/titleMeta';

/**
 * Search from anywhere: a modal palette over whatever screen is open, reachable
 * by `/`, Ctrl/Cmd-K, or the sidebar field on desktop.
 *
 * It is a *launcher*, not a second search screen. It shows the top few hits and
 * hands off to `/search?q=…` for the full grid and the list toggles, which is
 * why a row here has no controls on it: with the keyboard driving the list,
 * Enter has to mean exactly one thing.
 *
 * Built on native `<dialog>` + `showModal()` like `BaseSheet`, for the focus
 * trapping, Esc, and focus restoration to the opener that come with it.
 */
const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ 'update:open': [boolean] }>();

/** Enough to recognise the title you meant; the rest is a screen's job. */
const MAX_ROWS = 6;

const router = useRouter();
const { query, results, renderedQuery, status, isLoading, totalResults, runNow } = useTitleSearch();

const dialog = ref<HTMLDialogElement | null>(null);
const input = ref<HTMLInputElement | null>(null);
const activeIndex = ref(-1);

const rows = computed(() =>
  results.value.slice(0, MAX_ROWS).map((title) => ({
    title,
    meta: titleMetaShort(title),
    score: formatScore(title.tmdbVoteAverage)
  }))
);
const hasRows = computed(() => rows.value.length > 0);
const showNoResults = computed(
  () => status.value === 'ready' && !hasRows.value && renderedQuery.value.length > 0
);
const showError = computed(() => status.value === 'error' && !hasRows.value);
const remaining = computed(() => Math.max(0, totalResults.value - rows.value.length));

/** `/search` seeds its own field from `?q=`, so the handoff keeps the query. */
const allResultsPath = computed(() => `/search?q=${encodeURIComponent(renderedQuery.value)}`);

function optionId(index: number): string {
  return `global-search-option-${index}`;
}

const activeDescendant = computed(() =>
  activeIndex.value >= 0 && activeIndex.value < rows.value.length
    ? optionId(activeIndex.value)
    : undefined
);

function close(): void {
  emit('update:open', false);
}

function move(delta: number): void {
  activeIndex.value = wrapIndex(activeIndex.value, delta, rows.value.length);
}

function openTitle(index: number): void {
  const row = rows.value[index];
  if (!row) return;

  close();
  void router.push(titlePath(row.title.key));
}

function seeAll(): void {
  close();
  void router.push(allResultsPath.value);
}

/**
 * Enter opens the highlighted row. With nothing highlighted — which only happens
 * before the first response — it re-runs the query rather than navigating, so an
 * impatient Enter is never a mis-navigation.
 */
function onEnter(): void {
  if (activeIndex.value >= 0 && hasRows.value) {
    openTitle(activeIndex.value);
    return;
  }

  void runNow();
}

// A new result set always highlights its top hit, so Enter has a target the
// moment results land; an empty set has nothing to highlight. Immediate, because
// reopening the palette on the previous results must highlight them too.
watch(
  rows,
  (next) => {
    activeIndex.value = next.length > 0 ? 0 : -1;
  },
  { immediate: true }
);

watch(
  () => props.open,
  async (open) => {
    const element = dialog.value;
    if (!element) return;

    if (open) {
      // jsdom and very old browsers may not implement showModal.
      if (typeof element.showModal === 'function') {
        if (!element.open) element.showModal();
      } else {
        element.setAttribute('open', '');
      }

      // Reopening keeps the last query but selects it, so typing replaces it and
      // the previous results are still there to fall back on.
      await nextTick();
      input.value?.focus();
      input.value?.select();
    } else if (element.open) {
      element.close();
    }
  },
  { flush: 'post' }
);

/** Esc closes the element itself; the state has to follow it. */
function onNativeClose(): void {
  if (props.open) close();
}

/** A click that lands on the dialog rather than the panel is the backdrop. */
function onBackdropClick(event: MouseEvent): void {
  if (event.target === dialog.value) close();
}

onBeforeUnmount(() => {
  if (dialog.value?.open) dialog.value.close();
});
</script>

<template>
  <dialog
    ref="dialog"
    class="palette"
    aria-label="Search titles"
    @close="onNativeClose"
    @click="onBackdropClick"
  >
    <div class="palette__panel">
      <div class="palette__bar">
        <span class="palette__icon" aria-hidden="true"><IconSearch /></span>

        <label class="sr-only" for="global-search-input">Search films and series</label>
        <input
          id="global-search-input"
          ref="input"
          v-model="query"
          class="palette__input"
          type="text"
          role="combobox"
          aria-autocomplete="list"
          aria-controls="global-search-results"
          :aria-expanded="hasRows"
          :aria-activedescendant="activeDescendant"
          enterkeyhint="search"
          autocomplete="off"
          autocapitalize="none"
          spellcheck="false"
          placeholder="Search films and series"
          @keydown.down.prevent="move(1)"
          @keydown.up.prevent="move(-1)"
          @keydown.enter.prevent="onEnter"
        />

        <span class="palette__hint" aria-hidden="true">Esc</span>
        <button type="button" class="palette__close" aria-label="Close search" @click="close">
          <IconClose />
        </button>
      </div>

      <div class="palette__body">
        <!-- Results outlive a failed refresh: the list only gives way to the
             error when there is nothing left to show. -->
        <p v-if="showError" class="palette__note palette__note--error">
          Search is unavailable right now.
          <button type="button" class="palette__retry" @click="runNow">Try again</button>
        </p>

        <p v-else-if="isLoading && !hasRows" class="palette__note" role="status">Searching…</p>

        <p v-else-if="showNoResults" class="palette__note">
          Nothing found for “{{ renderedQuery }}”.
        </p>

        <p v-else-if="!hasRows" class="palette__note">
          Type a title. <kbd>↑</kbd><kbd>↓</kbd> to choose, <kbd>↵</kbd> to open.
        </p>

        <ul
          v-show="hasRows"
          id="global-search-results"
          class="palette__list"
          :class="{ 'palette__list--stale': isLoading }"
          role="listbox"
          aria-label="Search results"
        >
          <li
            v-for="(row, index) in rows"
            :id="optionId(index)"
            :key="row.title.key"
            class="row"
            :class="{ 'row--active': index === activeIndex }"
            role="option"
            :aria-selected="index === activeIndex"
            @click="openTitle(index)"
            @mousemove="activeIndex = index"
          >
            <span class="row__poster">
              <PosterImage
                :path="row.title.posterPath"
                :title="row.title.title"
                :release-year="row.title.releaseYear"
                :width="40"
              />
            </span>

            <span class="row__text">
              <span class="row__title">{{ row.title.title }}</span>
              <span class="row__meta">
                <TypeChip :media-type="row.title.mediaType" />
                <span v-if="row.meta">{{ row.meta }}</span>
                <span v-if="row.score" class="row__score">
                  <span v-if="row.meta" aria-hidden="true">·</span>
                  <IconStar filled />
                  <span>{{ row.score }}</span>
                  <span class="sr-only">TMDB score</span>
                </span>
              </span>
            </span>

            <!-- Gold here is the user's own rating and nothing else. -->
            <StarDisplay
              v-if="row.title.myRating !== null"
              :value="row.title.myRating"
              :size="12"
            />

            <!-- The highlight is never colour alone (NFR-9). -->
            <span v-if="index === activeIndex" class="row__enter" aria-hidden="true">↵</span>
          </li>
        </ul>

        <button
          v-if="hasRows && remaining > 0"
          type="button"
          class="palette__all"
          @click="seeAll"
        >
          See all {{ totalResults.toLocaleString() }} results
        </button>
      </div>
    </div>
  </dialog>
</template>

<style scoped>
.palette {
  width: 100%;
  max-width: 100%;
  /* Mobile: the palette *is* the screen — a centred box would be fighting the
     software keyboard for the same 200px. */
  height: 100dvh;
  max-height: 100dvh;
  margin: 0;
  padding: 0;
  border: 0;
  background: none;
  color: var(--text);
}

.palette::backdrop {
  background: rgba(0, 0, 0, 0.55);
}

.palette__panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg);
}

.palette__bar {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex: none;
  padding: var(--space-2) var(--space-3);
  padding-top: calc(var(--space-2) + env(safe-area-inset-top));
  border-bottom: 1px solid var(--border);
}

.palette__icon {
  flex: none;
  color: var(--text-muted);
  line-height: 0;
}

.palette__icon :deep(svg) {
  width: 18px;
  height: 18px;
}

.palette__input {
  flex: 1;
  min-width: 0;
  /* FR-H4: 44px tall, and 16px text so iOS does not zoom the page on focus. */
  min-height: var(--tap-min);
  border: 0;
  background: none;
  font-size: 16px;
  color: var(--text);
}

.palette__input::placeholder {
  color: var(--text-muted);
}

.palette__hint {
  display: none;
}

.palette__close {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--tap-min);
  height: var(--tap-min);
  border: 0;
  background: none;
  border-radius: var(--radius-full);
  color: var(--text-muted);
}

.palette__body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: var(--space-2) 0 calc(var(--space-4) + env(safe-area-inset-bottom));
}

.palette__note {
  padding: var(--space-4);
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.palette__note--error {
  color: var(--text);
}

.palette__note kbd {
  display: inline-block;
  min-width: 18px;
  padding: 1px 4px;
  margin: 0 1px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-raised);
  font: inherit;
  font-size: 11px;
  text-align: center;
}

.palette__retry {
  border: 0;
  background: none;
  padding: 0;
  font: inherit;
  color: var(--text-muted);
  text-decoration: underline;
}

.palette__list {
  list-style: none;
  margin: 0;
  padding: 0;
}

/* The previous results stay put while the next set loads, dimmed. */
.palette__list--stale {
  opacity: 0.55;
}

.row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: var(--tap-min);
  padding: var(--space-2) var(--space-4);
  cursor: pointer;
}

.row--active {
  background: var(--surface-raised);
}

.row__poster {
  flex: none;
  width: 34px;
  line-height: 0;
}

.row__text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.row__title {
  font-size: var(--text-base);
  font-weight: 600;
  line-height: 1.25;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.row__meta {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.row__score {
  display: inline-flex;
  align-items: center;
  gap: 3px;
}

.row__score :deep(svg) {
  width: 11px;
  height: 11px;
}

.row__enter {
  flex: none;
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.palette__all {
  display: block;
  width: 100%;
  min-height: var(--tap-min);
  padding: var(--space-3) var(--space-4);
  margin-top: var(--space-1);
  border: 0;
  border-top: 1px solid var(--border);
  background: none;
  font: inherit;
  font-size: var(--text-sm);
  color: var(--text-muted);
  text-align: left;
}

@media (min-width: 900px) {
  .palette {
    max-width: 560px;
    height: auto;
    /* High enough that the list grows downwards into empty space, not over the
       field someone is still typing in. */
    margin: 9vh auto auto;
  }

  .palette__panel {
    height: auto;
    max-height: 74vh;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius-lg);
    overflow: hidden;
    box-shadow: 0 24px 56px rgba(0, 0, 0, 0.38);
  }

  .palette__bar {
    padding-top: var(--space-2);
  }

  .palette__hint {
    display: block;
    flex: none;
    padding: 2px 6px;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    font-size: 11px;
    color: var(--text-muted);
  }

  .palette__close {
    display: none;
  }

  .palette__body {
    padding-bottom: var(--space-2);
  }
}
</style>
