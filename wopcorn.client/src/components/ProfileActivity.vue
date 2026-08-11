<script setup lang="ts">
import { RouterLink } from 'vue-router';

import StarDisplay from '@/components/StarDisplay.vue';
import { absoluteTime, relativeTime } from '@/lib/relativeTime';
import { ratingText } from '@/lib/stars';
import { titlePath } from '@/lib/titleKey';
import type { ActivityItem } from '@/api/types';

/**
 * What this person has been up to lately — the profile's counterpart to the
 * feed, which deliberately leaves their own activity out.
 *
 * Deliberately **not** `FeedGroup`. There, the news is who did it, so every
 * entry leads with an avatar and a name and carries full cards. Here the person
 * is the page, so the row leads with the title and the name never appears at
 * all — and nothing is grouped, because one line per event *is* the density.
 */
defineProps<{
  items: ActivityItem[];
  /**
   * `accent` for your own activity, `neutral` for someone else's. Ratings are
   * the one thing gold ever means, so a friend's rating is never gold — the same
   * rule the histogram and the cards follow.
   */
  tone?: 'accent' | 'neutral';
}>();

const VERBS: Record<ActivityItem['kind'], string> = {
  rated: 'Rated',
  watched: 'Watched',
  added_watchlist: 'Added to watchlist',
  added_queue: 'Queued'
};
</script>

<template>
  <ol class="activity">
    <li v-for="item in items" :key="item.id" class="activity__row">
      <span class="activity__verb">{{ VERBS[item.kind] }}</span>

      <RouterLink class="activity__title" :to="titlePath(item.title.key)">
        {{ item.title.title }}
      </RouterLink>

      <StarDisplay
        v-if="item.kind === 'rated' && item.rating !== null"
        :value="item.rating"
        :size="12"
        :label="ratingText(item.rating)"
        :tone="tone === 'accent' ? 'accent' : 'neutral'"
      />

      <time class="activity__time" :datetime="item.occurredAt" :title="absoluteTime(item.occurredAt)">
        {{ relativeTime(item.occurredAt) }}
      </time>
    </li>
  </ol>
</template>

<style scoped>
.activity {
  display: flex;
  flex-direction: column;
}

.activity__row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  /* FR-H4: the row holds a link, so it is a real target. */
  min-height: var(--tap-min);
  font-size: var(--text-sm);
}

.activity__row + .activity__row {
  border-top: 1px solid var(--border);
}

.activity__verb {
  flex: none;
  font-size: var(--text-xs);
  color: var(--text-muted);
}

/*
 * `flex: 1 1 0` rather than a min-width alone. A flex line is broken using each
 * item's *content* size and only then are items shrunk, so a long title on
 * `flex-basis: auto` jumps to its own line before the ellipsis it was given ever
 * gets a chance to apply. A zero basis keeps it on the row and lets it truncate.
 */
.activity__title {
  flex: 1 1 0;
  min-width: 0;
  color: inherit;
  font-weight: 600;
  text-decoration: none;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.activity__title:hover {
  text-decoration: underline;
}

.activity__time {
  flex: none;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

/* Wide enough to line the verbs up as a column; on a phone that is 96px of
   "Queued" the title needs more than the alignment is worth. */
@media (min-width: 560px) {
  .activity__verb {
    min-width: 96px;
  }
}
</style>
