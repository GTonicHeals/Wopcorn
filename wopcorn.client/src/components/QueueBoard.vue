<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';
import draggable from 'vuedraggable';

import BaseButton from '@/components/BaseButton.vue';
import BaseSheet from '@/components/BaseSheet.vue';
import EmptyState from '@/components/EmptyState.vue';
import IconChevronDown from '@/components/icons/IconChevronDown.vue';
import IconChevronUp from '@/components/icons/IconChevronUp.vue';
import IconGrip from '@/components/icons/IconGrip.vue';
import IconLayers from '@/components/icons/IconLayers.vue';
import IconSort from '@/components/icons/IconSort.vue';
import PosterImage from '@/components/PosterImage.vue';
import TypeChip from '@/components/TypeChip.vue';
import { formatShortDate, metaLine } from '@/lib/format';
import { decadeGradient } from '@/lib/posters';
import { isQuickViewClick } from '@/lib/quickView';
import { titlePath } from '@/lib/titleKey';
import { titleMetaShort } from '@/lib/titleMeta';
import { useConfigStore } from '@/stores/config';
import { useListsStore } from '@/stores/lists';
import { moveWithin, useQueueStore } from '@/stores/queue';
import { isDetail, useTitlesStore } from '@/stores/titles';
import type { QueueSortPreset, SortDirection, TitleCard } from '@/api/types';

/**
 * The queue (FR-D1…FR-D5) — the app's signature screen.
 *
 * It is the only ordered list in the app, so it is the **only** place numerals
 * appear. Position 1 is not a row at all: it expands into the "Up next" hero.
 * Rows 2..n are the draggable list, each carrying its position as a display-face
 * numeral, a grip, and — because a drag-only reorder fails NFR-8 outright —
 * working Move up / Move down buttons that write the same endpoint.
 *
 * The queue mixes media types in one order: a film, a series and a season sit in
 * the same list and reorder against each other. The type chip on a row is what
 * says which is which.
 */
const props = withDefaults(
  defineProps<{
    /**
     * Rows emit `select` instead of navigating — the lists' quick view. The
     * **hero** is deliberately left out: it is a place to go, not a row to skim,
     * and it is the one title in the queue whose details are already on screen.
     */
    quickView?: boolean;
  }>(),
  { quickView: false }
);

const emit = defineEmits<{ select: [key: string] }>();

const config = useConfigStore();
const titles = useTitlesStore();
const lists = useListsStore();
const queue = useQueueStore();

/** Capture phase, so the click is claimed before vue-router follows the link. */
function onCaptureClick(event: MouseEvent, key: string): void {
  if (!props.quickView || !isQuickViewClick(event)) return;

  event.preventDefault();
  event.stopPropagation();
  emit('select', key);
}

// ------------------------------------------------------------------- state

/**
 * The draggable list holds positions 2..n. The hero is `queue.ids[0]` and is not
 * part of it; dropping a row at the very top of this list promotes it.
 */
const rows = ref<string[]>([]);

watch(
  () => queue.keys,
  (keys) => {
    rows.value = keys.slice(1);
  },
  { immediate: true }
);

const heroKey = computed<string | null>(() => queue.keys[0] ?? null);
const heroTitle = computed<TitleCard | null>(() =>
  heroKey.value === null ? null : titles.get(heroKey.value)
);
const heroEntry = computed(() =>
  heroKey.value === null ? null : lists.entryOf('queue', heroKey.value)
);

function titleOf(key: string): TitleCard | null {
  return titles.get(key);
}

/**
 * The hero wants a backdrop, which only a `TitleDetail` carries — a queue loaded
 * from `GET /api/lists/queue` holds cards, so whoever is up next gets their
 * detail fetched here. `loadDetail` is cached and deduplicated, so this costs
 * one request per title, once, and nothing on a revisit. Until it lands (or when
 * TMDB has no backdrop at all) the decade duotone from task 2 stands in rather
 * than an empty box.
 */
watch(
  heroKey,
  (key) => {
    if (key !== null) void titles.loadDetail(key).catch(() => undefined);
  },
  { immediate: true }
);

const heroBackdrop = computed(() =>
  config.backdropUrl(isDetail(heroTitle.value) ? heroTitle.value.backdropPath : null, 780)
);

const heroFallback = computed(() => decadeGradient(heroTitle.value?.releaseYear ?? null));

const heroMeta = computed(() => {
  const title = heroTitle.value;
  if (!title) return '';
  const added = formatShortDate(heroEntry.value?.addedAt);
  return metaLine([titleMetaShort(title), added ? `added ${added}` : null]);
});

// --------------------------------------------------------------- reordering

async function commit(next: string[]): Promise<void> {
  const result = await queue.persist(next);
  if (result === 'conflict') {
    // The authoritative order may contain titles this screen has never seen.
    await lists.load('queue', true);
  }
}

/** Builds the complete order from the current rows, optionally promoting row 2. */
function fullOrder(list: string[], promote: boolean): string[] | null {
  const hero = heroKey.value;
  if (hero === null) return null;

  const head = list[0];
  if (promote && head !== undefined) {
    return [head, hero, ...list.slice(1)];
  }
  return [hero, ...list];
}

type DragEnd = { oldIndex?: number | null; newIndex?: number | null };

function onDragEnd(event: DragEnd): void {
  if (event.oldIndex === event.newIndex) return;

  const next = fullOrder(rows.value, event.newIndex === 0);
  if (next) void commit(next);
}

function moveUp(index: number): void {
  // Row 2's "Move up" promotes it past the hero; everything else swaps in place.
  const next =
    index === 0
      ? fullOrder(rows.value, true)
      : fullOrder(moveWithin(rows.value, index, index - 1), false);

  if (next) void commit(next);
}

function moveDown(index: number): void {
  if (index >= rows.value.length - 1) return;
  const next = fullOrder(moveWithin(rows.value, index, index + 1), false);
  if (next) void commit(next);
}

// ------------------------------------------------------------------ presets

const presetOpen = ref(false);
const preset = ref<QueueSortPreset>('added');
const presetDir = ref<SortDirection>('desc');

const presets: { value: QueueSortPreset; label: string }[] = [
  { value: 'added', label: 'Date added' },
  { value: 'title', label: 'Title' },
  { value: 'runtime', label: 'Runtime' },
  { value: 'score', label: 'TMDB score' }
];

/** Only worth warning about when there is a hand-made order to overwrite. */
const overwritesOrder = computed(() => queue.keys.length > 1);

async function applyPreset(): Promise<void> {
  presetOpen.value = false;
  const ok = await queue.applyPreset(preset.value, presetDir.value);
  // Positions were rewritten server-side; the entries themselves are unchanged.
  if (!ok) await lists.load('queue', true);
}
</script>

<template>
  <div class="queue">
    <div class="queue__tools">
      <button
        type="button"
        class="queue__sort"
        :disabled="queue.keys.length === 0"
        @click="presetOpen = true"
      >
        <IconSort />
        <span>Sort</span>
      </button>
    </div>

    <EmptyState
      v-if="queue.keys.length === 0"
      headline="Your queue is empty"
      body="Add titles to the queue and put them in the order you mean to watch them."
    >
      <template #icon><IconLayers /></template>
    </EmptyState>

    <template v-else>
      <!-- № 1 is tonight's answer, not a row. -->
      <RouterLink v-if="heroTitle" class="hero" :to="titlePath(heroTitle.key)">
        <span
          class="hero__art"
          :style="
            heroBackdrop
              ? { backgroundImage: `url(${heroBackdrop})` }
              : { backgroundImage: heroFallback }
          "
          aria-hidden="true"
        />
        <span class="hero__scrim" aria-hidden="true" />
        <span class="hero__band">
          <span class="hero__eyebrow">Up next</span>
          <span class="hero__title">{{ heroTitle.title }}</span>
          <span v-if="heroMeta" class="hero__meta">
            <TypeChip :media-type="heroTitle.mediaType" />
            {{ heroMeta }}
          </span>
        </span>
      </RouterLink>

      <draggable
        v-model="rows"
        class="queue__rows"
        tag="ul"
        item-key="id"
        handle=".drag-handle"
        :animation="150"
        :delay="0"
        :touch-start-threshold="5"
        :force-fallback="true"
        ghost-class="queue-ghost"
        @end="onDragEnd"
      >
        <template #item="{ element, index }">
          <li class="queue-row" @click.capture="onCaptureClick($event, element)">
            <template v-if="titleOf(element)">
              <span class="queue-row__numeral" aria-hidden="true">{{ index + 2 }}</span>

              <RouterLink class="queue-row__link" :to="titlePath(element)">
                <span class="queue-row__poster">
                  <PosterImage
                    :path="titleOf(element)!.posterPath"
                    :title="titleOf(element)!.title"
                    :release-year="titleOf(element)!.releaseYear"
                    :width="46"
                  />
                </span>
                <span class="queue-row__text">
                  <b class="queue-row__title">{{ titleOf(element)!.title }}</b>
                  <span class="queue-row__meta">
                    <TypeChip :media-type="titleOf(element)!.mediaType" />
                    {{ titleMetaShort(titleOf(element)!) }}
                  </span>
                </span>
              </RouterLink>

              <!-- NFR-8: the keyboard path is mandatory, not a nicety. -->
              <span class="queue-row__moves">
                <button
                  type="button"
                  class="queue-row__move"
                  :aria-label="`Move ${titleOf(element)!.title} up`"
                  @click="moveUp(index)"
                >
                  <IconChevronUp />
                </button>
                <button
                  type="button"
                  class="queue-row__move"
                  :disabled="index >= rows.length - 1"
                  :aria-label="`Move ${titleOf(element)!.title} down`"
                  @click="moveDown(index)"
                >
                  <IconChevronDown />
                </button>
              </span>

              <span class="queue-row__grip drag-handle" aria-hidden="true">
                <IconGrip />
              </span>
            </template>
          </li>
        </template>
      </draggable>
    </template>

    <BaseSheet v-model:open="presetOpen" title="Sort the queue">
      <fieldset class="preset">
        <legend class="sr-only">Sort by</legend>
        <label v-for="option in presets" :key="option.value" class="preset__option">
          <input v-model="preset" type="radio" name="queue-preset" :value="option.value" />
          <span>{{ option.label }}</span>
        </label>
      </fieldset>

      <div class="preset__dir" role="group" aria-label="Direction">
        <button
          type="button"
          class="preset__dir-btn"
          :class="{ 'preset__dir-btn--on': presetDir === 'asc' }"
          :aria-pressed="presetDir === 'asc'"
          @click="presetDir = 'asc'"
        >
          Ascending
        </button>
        <button
          type="button"
          class="preset__dir-btn"
          :class="{ 'preset__dir-btn--on': presetDir === 'desc' }"
          :aria-pressed="presetDir === 'desc'"
          @click="presetDir = 'desc'"
        >
          Descending
        </button>
      </div>

      <p v-if="overwritesOrder" class="preset__warning">
        This replaces your current queue order.
      </p>

      <template #actions>
        <BaseButton variant="ghost" @click="presetOpen = false">Cancel</BaseButton>
        <BaseButton variant="primary" @click="applyPreset">Sort queue</BaseButton>
      </template>
    </BaseSheet>
  </div>
</template>

<style scoped>
.queue {
  padding: 0 var(--space-4) var(--space-8);
}

.queue__tools {
  display: flex;
  justify-content: flex-end;
  margin-bottom: var(--space-2);
}

.queue__sort {
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

.queue__sort :deep(svg) {
  width: 16px;
  height: 16px;
}

/* ------------------------------------------------------------------ hero */

.hero {
  position: relative;
  display: block;
  aspect-ratio: 16 / 10;
  border-radius: var(--radius-lg);
  border: 1px solid var(--poster-edge);
  overflow: hidden;
  text-decoration: none;
  color: inherit;
  margin-bottom: var(--space-4);
}

.hero__art {
  position: absolute;
  inset: 0;
  background-size: cover;
  background-position: center;
}

.hero__scrim {
  position: absolute;
  inset: 35% 0 0;
  background: var(--scrim);
}

/*
 * Solid, not translucent: `--text-muted` on a partial scrim over a bright
 * backdrop cannot be guaranteed to clear 4.5:1, and NFR-9 is not probabilistic.
 * The gradient above dissolves the art into this band, so it still reads as one
 * image.
 */
.hero__band {
  position: absolute;
  inset: auto 0 0 0;
  display: flex;
  flex-direction: column;
  background: var(--bg);
  padding: var(--space-2) var(--space-4) var(--space-3);
}

.hero__eyebrow {
  font-size: 11px;
  letter-spacing: 0.26em;
  text-transform: uppercase;
  color: var(--accent);
  font-weight: 700;
}

.hero__title {
  font-family: var(--font-display);
  font-size: 28px;
  font-weight: 500;
  line-height: 1.05;
  color: var(--text);
  margin-top: 3px;
}

.hero__meta {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  font-size: 12px;
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
  margin-top: 3px;
}

/* ------------------------------------------------------------------ rows */

.queue__rows {
  display: flex;
  flex-direction: column;
}

.queue-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) 0;
  border-bottom: 1px solid var(--border);
  background: var(--bg);
}

.queue-row:last-child {
  border-bottom: 0;
}

/*
 * The queue is the only ordered list in the app, so these are the only numerals
 * in the app. Display face, lining figures.
 */
.queue-row__numeral {
  flex: none;
  width: 26px;
  text-align: center;
  font-family: var(--font-display);
  font-size: 24px;
  line-height: 1;
  color: var(--text-muted);
  font-variant-numeric: lining-nums tabular-nums;
}

.queue-row__link {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: var(--space-3);
  text-decoration: none;
  color: inherit;
}

.queue-row__poster {
  flex: none;
  width: 46px;
  display: block;
}

.queue-row__text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.queue-row__title {
  font-size: 13.5px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.queue-row__meta {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.queue-row__moves {
  flex: none;
  display: flex;
  flex-direction: column;
}

.queue-row__move {
  width: 32px;
  height: 22px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 0;
  background: none;
  color: var(--text-muted);
  padding: 0;
}

.queue-row__move:disabled {
  opacity: 0.4;
}

.queue-row__move :deep(svg) {
  width: 18px;
  height: 18px;
}

/*
 * On touch, the stacked 32×22 pair is below the FR-H4 floor. Lay the two
 * chevrons side by side at full tap size; pointer-accurate devices keep the
 * compact stack.
 */
@media (pointer: coarse) {
  .queue-row__moves {
    flex-direction: row;
  }

  .queue-row__move {
    width: var(--tap-min);
    height: var(--tap-min);
  }
}

/* At least 44px square so dragging never fights vertical scrolling. */
.queue-row__grip {
  flex: none;
  width: var(--tap-min);
  height: var(--tap-min);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-muted);
  cursor: grab;
  touch-action: none;
}

/* ---------------------------------------------------------------- presets */

.preset {
  border: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
}

.preset__option {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: var(--tap-min);
  font-size: var(--text-base);
}

.preset__option input {
  width: 20px;
  height: 20px;
  accent-color: var(--accent);
}

.preset__dir {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-1);
  background: var(--surface-raised);
  border-radius: var(--radius-full);
  padding: 3px;
  margin-top: var(--space-3);
}

.preset__dir-btn {
  min-height: var(--tap-min);
  border: 0;
  background: none;
  border-radius: var(--radius-full);
  color: var(--text-muted);
  font-size: var(--text-sm);
  font-weight: 600;
}

.preset__dir-btn--on {
  background: var(--accent);
  color: var(--accent-ink);
}

.preset__warning {
  margin-top: var(--space-3);
  font-size: var(--text-xs);
  color: var(--text-muted);
  border-left: 2px solid var(--border);
  padding-left: var(--space-3);
}

@media (min-width: 900px) {
  .queue {
    padding: 0 var(--space-6) var(--space-8);
    max-width: 720px;
  }
}
</style>

<style>
/* Sortable puts this on the dragged element itself; it must not be scoped away. */
.queue-ghost {
  opacity: 0.4;
}
</style>
