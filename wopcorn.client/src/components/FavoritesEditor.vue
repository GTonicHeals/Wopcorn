<script setup lang="ts">
import { computed, ref, watch } from 'vue';

import BaseButton from '@/components/BaseButton.vue';
import BaseSheet from '@/components/BaseSheet.vue';
import PosterImage from '@/components/PosterImage.vue';
import IconChevronLeft from '@/components/icons/IconChevronLeft.vue';
import IconClose from '@/components/icons/IconClose.vue';
import { titleMetaShort } from '@/lib/titleMeta';
import { MAX_FAVORITES, moveFavorite, showcaseChanged, toggleFavorite } from '@/lib/profile';
import { useListsStore } from '@/stores/lists';
import { useTitlesStore } from '@/stores/titles';
import type { TitleCard } from '@/api/types';

/**
 * Editing the showcase (`PUT /api/me/favorites`).
 *
 * The sheet holds a **draft** and sends it whole on save, which is the same
 * shape as the endpoint: the body is the complete intended list, so add, remove
 * and reorder are one write and a cancelled edit costs nothing. Nothing here is
 * optimistic — there is no list to keep in step and no toast worth firing for a
 * dialog the user is looking at.
 *
 * Candidates come from the watched list, because in practice a favourite is
 * something you have seen. That is a choice about where the picker looks, not a
 * rule: the server keeps a favourite after it leaves the watched list, so
 * nothing here can silently drop one that no longer appears below.
 */
const props = withDefaults(
  defineProps<{
    open: boolean;
    /** The stored showcase, in stored order. */
    favorites: TitleCard[];
    /** The write is the parent's to make, so its outcome is the parent's to report. */
    saving?: boolean;
    error?: string;
  }>(),
  { saving: false, error: '' }
);

const emit = defineEmits<{
  'update:open': [boolean];
  save: [keys: string[]];
}>();

const lists = useListsStore();
const titles = useTitlesStore();

const draft = ref<string[]>([]);
const filter = ref('');

const saved = computed(() => props.favorites.map((title) => title.key));

// Opening is what resets the draft: a cancelled edit must not survive into the
// next one, and neither must a stale filter.
watch(
  () => props.open,
  (open) => {
    if (!open) return;

    draft.value = [...saved.value];
    filter.value = '';
    void lists.ensure('watched');
  },
  { immediate: true }
);

/**
 * Everything the picker can offer: the watched list, plus anything already in
 * the showcase that is no longer on it — a favourite must always be visible in
 * the editor that can remove it.
 */
const candidates = computed<TitleCard[]>(() => {
  const seen = new Set<string>();
  const result: TitleCard[] = [];

  for (const key of [...saved.value, ...lists.state.watched.entries.map((entry) => entry.key)]) {
    if (seen.has(key)) continue;
    seen.add(key);

    const title = titles.get(key);
    if (title) result.push(title);
  }

  return result;
});

const visible = computed(() => {
  const term = filter.value.trim().toLowerCase();
  if (!term) return candidates.value;
  return candidates.value.filter((title) => title.title.toLowerCase().includes(term));
});

/**
 * The strip at the top of the sheet: the draft in order, then empty slots up to
 * the cap.
 *
 * The empty slots are not decoration. Without them the strip grows as titles are
 * picked and shoves the picker down the sheet, so the second pick lands on
 * whatever slid under the cursor — and the cap only announces itself once it has
 * already been hit.
 */
const slots = computed<(TitleCard | null)[]>(() => {
  const filled = draft.value
    .map((key) => titles.get(key))
    .filter((title): title is TitleCard => title !== null);

  return [
    ...filled,
    ...Array.from({ length: Math.max(0, MAX_FAVORITES - filled.length) }, () => null)
  ];
});

const full = computed(() => draft.value.length >= MAX_FAVORITES);
const changed = computed(() => showcaseChanged(draft.value, saved.value));

function isChosen(key: string): boolean {
  return draft.value.includes(key);
}

function toggle(key: string): void {
  draft.value = toggleFavorite(draft.value, key);
}

function move(key: string, offset: number): void {
  draft.value = moveFavorite(draft.value, key, offset);
}

function save(): void {
  if (props.saving) return;
  emit('save', [...draft.value]);
}

function close(): void {
  emit('update:open', false);
}
</script>

<template>
  <BaseSheet
    :open="open"
    size="lg"
    title="Edit favourites"
    @update:open="emit('update:open', $event)"
  >
    <div class="editor">
      <p class="editor__lede">
        Up to {{ MAX_FAVORITES }}, in the order you want them. The first one lights the
        top of your profile.
      </p>

      <!--
        Always six slots, so picking never moves the grid below and the cap is
        visible before it is reached. Ordered, and the first one is named rather
        than numbered — the marquee is what the order is *for*.
      -->
      <ol class="chosen">
        <li v-for="(title, index) in slots" :key="title?.key ?? `slot-${index}`" class="chosen__slot">
          <template v-if="title">
            <PosterImage
              :path="title.posterPath"
              :title="title.title"
              :release-year="title.releaseYear"
              :width="96"
            />
            <p class="chosen__title">{{ title.title }}</p>
            <div class="chosen__controls">
              <button
                type="button"
                class="chosen__move"
                :disabled="index === 0"
                :aria-label="`Move ${title.title} earlier`"
                @click="move(title.key, -1)"
              >
                <IconChevronLeft />
              </button>
              <button
                type="button"
                class="chosen__move chosen__move--later"
                :disabled="index >= draft.length - 1"
                :aria-label="`Move ${title.title} later`"
                @click="move(title.key, 1)"
              >
                <IconChevronLeft />
              </button>
              <button
                type="button"
                class="chosen__remove"
                :aria-label="`Remove ${title.title} from favourites`"
                @click="toggle(title.key)"
              >
                <IconClose />
              </button>
            </div>
          </template>

          <template v-else>
            <span class="chosen__blank" aria-hidden="true" />
            <p class="chosen__title chosen__title--blank">
              {{ index === 0 ? 'Marquee' : 'Empty' }}
            </p>
          </template>
        </li>
      </ol>

      <label class="editor__filter">
        <span class="sr-only">Filter your watched titles</span>
        <input
          v-model="filter"
          type="search"
          class="editor__input"
          placeholder="Filter what you have watched"
        />
      </label>

      <p v-if="candidates.length === 0" class="editor__note">
        Mark something watched and it will show up here to pick from.
      </p>

      <p v-else-if="visible.length === 0" class="editor__note">
        Nothing you have watched matches “{{ filter }}”.
      </p>

      <ul v-else class="picker">
        <li v-for="title in visible" :key="title.key">
          <button
            type="button"
            class="picker__item"
            :class="{ 'picker__item--on': isChosen(title.key) }"
            :aria-pressed="isChosen(title.key)"
            :disabled="full && !isChosen(title.key)"
            @click="toggle(title.key)"
          >
            <PosterImage
              :path="title.posterPath"
              :title="title.title"
              :release-year="title.releaseYear"
              :width="84"
            />
            <span class="picker__title">{{ title.title }}</span>
            <span class="picker__meta">{{ titleMetaShort(title) }}</span>
          </button>
        </li>
      </ul>

      <p v-if="full" class="editor__note" role="status">
        That is all {{ MAX_FAVORITES }} slots. Remove one to swap it out.
      </p>

      <p v-if="error" class="editor__error" role="alert">{{ error }}</p>
    </div>

    <template #actions>
      <BaseButton variant="ghost" @click="close">Cancel</BaseButton>
      <BaseButton variant="primary" :loading="saving" :disabled="!changed" @click="save">
        Save favourites
      </BaseButton>
    </template>
  </BaseSheet>
</template>

<style scoped>
.editor {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.editor__lede {
  font-size: var(--text-sm);
  color: var(--text-muted);
  line-height: 1.5;
}

.editor__note {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.editor__error {
  font-size: var(--text-sm);
  font-weight: 600;
}

/* ------------------------------------------------------------------ chosen */

/*
 * Six slots, wrapped to fit. The cell is sized by its three controls, not by its
 * poster: three targets at the 44px floor (FR-H4) need about 140px between them,
 * and the poster is capped well below that so a slot reads as a thumbnail rather
 * than as another copy of the showcase. What has to stay constant is the count of
 * slots, so the picker below never moves — not the number of rows they sit in.
 */
.chosen {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: var(--space-2);
}

.chosen__slot :deep(.poster),
.chosen__blank {
  max-width: 88px;
}

.chosen__slot {
  min-width: 0;
}

.chosen__blank {
  display: block;
  aspect-ratio: 2 / 3;
  border: 1px dashed var(--border);
  border-radius: var(--radius-md);
}

.chosen__title {
  font-size: var(--text-xs);
  font-weight: 600;
  line-height: 1.3;
  margin: var(--space-1) 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chosen__title--blank {
  color: var(--text-muted);
  font-weight: 400;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  font-size: 10px;
}

.chosen__controls {
  display: flex;
  gap: 2px;
}

.chosen__move,
.chosen__remove {
  display: flex;
  align-items: center;
  justify-content: center;
  flex: 1;
  min-height: var(--tap-min);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text-muted);
}

.chosen__move:disabled {
  opacity: 0.4;
}

.chosen__move--later :deep(svg) {
  transform: scaleX(-1);
}

.chosen__move :deep(svg),
.chosen__remove :deep(svg) {
  width: 16px;
  height: 16px;
}

/* ------------------------------------------------------------------ picker */

.editor__input {
  width: 100%;
  min-height: var(--tap-min);
  padding: 0 var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface-raised);
  color: var(--text);
  font: inherit;
  /* 16px, not the body size — see base.css: below it iOS zooms in on focus. */
  font-size: 16px;
}

.picker {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(84px, 1fr));
  gap: var(--space-2);
  max-height: 46dvh;
  overflow-y: auto;
  /* Room for the focus ring on the bottom row. */
  padding: 2px;
}

.picker__item {
  display: flex;
  flex-direction: column;
  width: 100%;
  padding: 0;
  border: 0;
  background: none;
  color: inherit;
  text-align: left;
}

.picker__item:disabled {
  opacity: 0.4;
}

/*
 * Chosen is never colour alone (NFR-9): the poster gains a ring *and* the title
 * goes semibold in the accent, which here means "your pick".
 */
.picker__item--on :deep(.poster) {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.picker__title {
  font-size: 11px;
  font-weight: 500;
  line-height: 1.3;
  margin-top: var(--space-1);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.picker__item--on .picker__title {
  color: var(--accent);
  font-weight: 700;
}

.picker__meta {
  font-size: 10px;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

</style>
