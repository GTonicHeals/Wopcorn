<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';

import ErrorState from '@/components/ErrorState.vue';
import SpinnerBlock from '@/components/SpinnerBlock.vue';
import TitleCard from '@/components/TitleCard.vue';
import { ApiError, api } from '@/api/client';
import { useTitlesStore } from '@/stores/titles';
import type { DiscoverFeed, Paged, TitleCard as TitleCardData } from '@/api/types';

/**
 * One discovery rail (FR-B4). The **row** scrolls horizontally; the page never
 * does (FR-H5). Cards are a fixed 138px so the snap points are even.
 */
const props = defineProps<{
  feed: DiscoverFeed;
  title: string;
}>();

const titles = useTitlesStore();

const keys = ref<string[]>([]);
const status = ref<'loading' | 'ready' | 'error'>('loading');
const error = ref<ApiError | null>(null);

const cards = computed(() =>
  keys.value.map((key) => titles.get(key)).filter((t): t is TitleCardData => t !== null)
);

async function load(): Promise<void> {
  status.value = 'loading';
  error.value = null;

  try {
    const page = await api<Paged<TitleCardData>>(`/api/titles/discover/${props.feed}?page=1`);
    titles.upsertMany(page.results);
    keys.value = page.results.map((title) => title.key);
    status.value = 'ready';
  } catch (failure) {
    error.value = failure instanceof ApiError ? failure : null;
    status.value = 'error';
  }
}

onMounted(load);
</script>

<template>
  <section class="rail" :aria-label="title">
    <h2 class="rail__title">{{ title }}</h2>

    <SpinnerBlock v-if="status === 'loading'" :label="`Loading ${title}`" />
    <ErrorState v-else-if="status === 'error'" :error="error" @retry="load" />

    <ul v-else class="rail__track">
      <li v-for="title in cards" :key="title.key" class="rail__item">
        <TitleCard :title="title" :poster-width="138" />
      </li>
    </ul>
  </section>
</template>

<style scoped>
.rail {
  margin-bottom: var(--space-6);
}

.rail__title {
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
  padding: 0 var(--space-4) var(--space-3);
}

.rail__track {
  display: flex;
  gap: var(--space-3);
  overflow-x: auto;
  scroll-snap-type: x mandatory;
  -webkit-overflow-scrolling: touch;
  scrollbar-width: none;
  /* The rail bleeds to the screen edge; the first and last cards keep the page
     gutter so nothing looks clipped. */
  padding: 0 var(--space-4);

  /*
   * Must match the padding above, or `mandatory` eats it. The snapport defaults
   * to the scrollport, so `scroll-snap-align: start` wants the first card's edge
   * flush with the container's *content* edge — and since the padding already
   * holds it 16px in, the browser force-scrolls by exactly that much to satisfy
   * the snap. The gutter survives on screen but the first card starts clipped.
   */
  scroll-padding-inline: var(--space-4);
}

.rail__track::-webkit-scrollbar {
  display: none;
}

.rail__item {
  flex: none;
  width: 138px;
  scroll-snap-align: start;
}

@media (min-width: 900px) {
  .rail__title,
  .rail__track {
    padding-left: var(--space-6);
    padding-right: var(--space-6);
  }

  .rail__track {
    scroll-padding-inline: var(--space-6);
  }
}
</style>
