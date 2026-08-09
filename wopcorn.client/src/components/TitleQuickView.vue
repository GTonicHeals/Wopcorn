<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';

import BaseSheet from '@/components/BaseSheet.vue';
import IconStar from '@/components/icons/IconStar.vue';
import ListToggles from '@/components/ListToggles.vue';
import PosterImage from '@/components/PosterImage.vue';
import StarRating from '@/components/StarRating.vue';
import TypeChip from '@/components/TypeChip.vue';
import { formatScore } from '@/lib/format';
import { titlePath } from '@/lib/titleKey';
import { seasonProgressLabel, titleMeta } from '@/lib/titleMeta';
import { useListsStore } from '@/stores/lists';
import { isDetail, useTitlesStore } from '@/stores/titles';

/**
 * The quick view: tapping a title in a list opens it here rather than leaving
 * the list. It exists for the two things a list makes you want — change a rating
 * and remind yourself what the thing is — without losing your scroll position,
 * your filters, or your place in a long queue.
 *
 * It is a `BaseSheet`, so closing is tap-away, the X, or Esc, all for free.
 *
 * Everything on it reads out of the shared titles store by key, which is what
 * makes it a *view* rather than a copy: a star set here is the same optimistic
 * write the title screen does, and the card behind the dialog updates with it.
 *
 * The synopsis needs `GET /api/titles/{key}`, which the list responses do not
 * carry, so it is fetched on open — cached and deduplicated by the store, so
 * reopening the same title costs nothing. When that request fails the sheet
 * simply has no synopsis: the rating and the toggles are the point, and they
 * work off data that is already here.
 */
const props = defineProps<{ titleKey: string; open: boolean }>();

const emit = defineEmits<{ 'update:open': [boolean] }>();

const titles = useTitlesStore();
const lists = useListsStore();

const title = computed(() => titles.get(props.titleKey));
const detail = computed(() => (isDetail(title.value) ? title.value : null));

const loading = ref(false);

watch(
  () => [props.titleKey, props.open] as const,
  ([key, open]) => {
    if (!open) return;

    loading.value = true;
    void titles
      .loadDetail(key)
      .catch(() => undefined)
      .finally(() => {
        loading.value = false;
      });
  },
  { immediate: true }
);

/**
 * A season alone is "Season 2", which names nothing on its own — the dialog has
 * no series screen around it to supply the rest, so the heading carries it.
 */
const heading = computed(() => {
  const current = title.value;
  if (!current) return '';

  if (current.mediaType === 'season' && current.parentKey) {
    const parent = titles.get(current.parentKey);
    if (parent) return `${parent.title} · ${current.title}`;
  }

  return current.title;
});

const metaText = computed(() => (title.value ? titleMeta(title.value) : ''));
const score = computed(() => formatScore(title.value?.tmdbVoteAverage));
const progress = computed(() => seasonProgressLabel(title.value?.seasonProgress));

function onRate(value: number): void {
  void lists.setRating(props.titleKey, value);
}

function onClearRating(): void {
  void lists.clearRating(props.titleKey);
}

function close(): void {
  emit('update:open', false);
}
</script>

<template>
  <BaseSheet :open="open" :title="heading" @update:open="emit('update:open', $event)">
    <div v-if="title" class="quick">
      <div class="quick__head">
        <div class="quick__poster">
          <PosterImage
            :path="title.posterPath"
            :title="title.title"
            :release-year="title.releaseYear"
            :width="92"
          />
        </div>

        <div class="quick__facts">
          <p class="quick__meta">
            <TypeChip :media-type="title.mediaType" />
            <span v-if="metaText">{{ metaText }}</span>
          </p>

          <p v-if="score" class="quick__score">
            <IconStar filled />
            <b>{{ score }}</b>
            <span class="quick__score-src">TMDB</span>
          </p>

          <!-- The viewer's own progress, so it earns the accent here too. -->
          <p v-if="progress" class="quick__progress">{{ progress }} watched</p>
        </div>
      </div>

      <!-- The reason the dialog exists: rate it and get back to the list. -->
      <StarRating
        :model-value="title.myRating"
        :film-title="title.title"
        @update:model-value="onRate"
        @clear="onClearRating"
      />

      <ListToggles :title="title" show-labels />

      <p v-if="detail?.overview" class="quick__synopsis">{{ detail.overview }}</p>
      <p v-else-if="loading" class="quick__synopsis quick__synopsis--wait">Loading details…</p>
    </div>

    <template #actions>
      <!-- A link, not a button: the full screen is a place, with its own URL. -->
      <RouterLink class="quick__open" :to="titlePath(titleKey)" @click="close">
        Full details
      </RouterLink>
    </template>
  </BaseSheet>
</template>

<style scoped>
.quick {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.quick__head {
  display: flex;
  align-items: flex-start;
  gap: var(--space-3);
}

.quick__poster {
  flex: none;
  width: 92px;
}

.quick__facts {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.quick__meta {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.quick__score {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.quick__score :deep(svg) {
  width: 14px;
  height: 14px;
}

.quick__score b {
  color: var(--text);
  font-variant-numeric: tabular-nums;
}

.quick__score-src {
  font-size: 11.5px;
  letter-spacing: 0.08em;
}

.quick__progress {
  font-size: var(--text-xs);
  color: var(--accent);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

/*
 * Clamped, not scrolled: the whole point is a glance. The title screen has the
 * rest, and the footer link is right below this.
 */
.quick__synopsis {
  font-size: var(--text-sm);
  color: var(--text-muted);
  line-height: 1.6;
  display: -webkit-box;
  -webkit-line-clamp: 5;
  line-clamp: 5;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.quick__synopsis--wait {
  opacity: 0.6;
}

/* Shaped like a secondary BaseButton, which is a `<button>` and cannot be this. */
.quick__open {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--tap-min);
  padding: 0 var(--space-4);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  color: var(--text);
  text-decoration: none;
  font-size: var(--text-sm);
  font-weight: 600;
  line-height: 1;
}

.quick__open:hover {
  background: var(--surface-raised);
}
</style>
