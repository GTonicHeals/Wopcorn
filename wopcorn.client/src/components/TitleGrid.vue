<script setup lang="ts">
import TitleCard from '@/components/TitleCard.vue';
import type { TitleCard as TitleCardData } from '@/api/types';

/**
 * `repeat(auto-fill, minmax(138px, 1fr))` — two columns at 320px, more as the
 * viewport allows. `content-visibility: auto` on the items means a 200-entry
 * list only lays out what is near the viewport (NFR-2); combined with the
 * reserved poster box and `loading="lazy"`, that is the measurement to beat
 * before any windowing library is considered.
 */
const props = withDefaults(
  defineProps<{
    titles: TitleCardData[];
    /** Reduced opacity while a newer result set is in flight — never a blank grid. */
    stale?: boolean;
    /**
     * Title key → another person's rating, for a friend's list (fe-07, task 3).
     * Rendered on its own attributed row; the cards' toggles still act on your
     * lists, because the server decorates every title for the requester.
     */
    ratings?: Record<string, number | null>;
    /** Whose ratings `ratings` holds. Without it nothing is rendered from it. */
    ratingLabel?: string | null;
    /** Cards emit `select` instead of navigating — the lists' quick view. */
    quickView?: boolean;
  }>(),
  { stale: false, ratings: undefined, ratingLabel: null, quickView: false }
);

const emit = defineEmits<{ select: [key: string] }>();

function ratingFor(key: string): number | null {
  return props.ratings?.[key] ?? null;
}
</script>

<template>
  <div class="title-grid" :class="{ 'title-grid--stale': stale }">
    <div v-for="title in titles" :key="title.key" class="title-grid__cell">
      <TitleCard
        :title="title"
        :their-rating="ratingFor(title.key)"
        :their-name="ratingLabel"
        :quick-view="quickView"
        @select="emit('select', $event)"
      />
    </div>
  </div>
</template>

<style scoped>
.title-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(138px, 1fr));
  gap: var(--space-3);
  padding: 0 var(--space-4) var(--space-6);
  transition: opacity 120ms ease;
}

.title-grid--stale {
  opacity: 0.55;
}

.title-grid__cell {
  min-width: 0;
  content-visibility: auto;
  /* Poster (2:3 of ~138px) + title + meta + toggles. */
  contain-intrinsic-size: auto 320px;
}

@media (min-width: 900px) {
  .title-grid {
    padding: 0 var(--space-6) var(--space-8);
    gap: var(--space-4);
  }
}
</style>
