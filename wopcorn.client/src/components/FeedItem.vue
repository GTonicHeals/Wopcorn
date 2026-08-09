<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';

import StarDisplay from '@/components/StarDisplay.vue';
import TitleCard from '@/components/TitleCard.vue';
import UserAvatar from '@/components/UserAvatar.vue';
import { absoluteTime, relativeTime } from '@/lib/relativeTime';
import { ratingText } from '@/lib/stars';
import { useTitlesStore } from '@/stores/titles';
import type { ActivityItem, TitleCard as TitleCardData } from '@/api/types';

/**
 * One line of text above a `TitleCard` (fe-07, task 2).
 *
 * The card is the ordinary one on purpose: it carries **your** membership and
 * rating, so a friend rating something already in your queue reads without a
 * tap — and its type chip says whether "watched" meant a film, a series or one
 * season of one. The friend's own rating sits on the line above, next to their name, and
 * never in the card's `myRating` slot.
 *
 * The title is read from the store rather than from `item.title` — the feed
 * upserts every title it receives, and reading it back is what keeps a toggle
 * pressed here in step with the same title on search and list screens.
 */
const props = defineProps<{ item: ActivityItem }>();

const titles = useTitlesStore();

const title = computed<TitleCardData>(() => titles.get(props.item.title.key) ?? props.item.title);

/** The four kinds read differently; the sentence is the difference. */
const VERBS: Record<ActivityItem['kind'], string> = {
  rated: 'rated',
  watched: 'watched',
  added_watchlist: 'added to their watchlist',
  added_queue: 'queued'
};

const verb = computed(() => VERBS[props.item.kind]);

const showStars = computed(() => props.item.kind === 'rated' && props.item.rating !== null);

const starsLabel = computed(() =>
  props.item.rating === null ? '' : ratingText(props.item.rating)
);

const relative = computed(() => relativeTime(props.item.occurredAt));
const absolute = computed(() => absoluteTime(props.item.occurredAt));
</script>

<template>
  <article class="feed-item">
    <p class="feed-item__line">
      <RouterLink class="feed-item__who" :to="`/u/${item.user.id}`">
        <UserAvatar :user="item.user" :size="26" />
        <span class="feed-item__name">{{ item.user.displayName }}</span>
      </RouterLink>

      <span class="feed-item__verb">{{ verb }}</span>

      <StarDisplay
        v-if="showStars"
        :value="item.rating"
        :size="13"
        :label="starsLabel"
        tone="neutral"
      />

      <!-- Rounded on screen, exact in the markup and in the tooltip. -->
      <time class="feed-item__time" :datetime="item.occurredAt" :title="absolute">
        {{ relative }}
      </time>
    </p>

    <div class="feed-item__card">
      <TitleCard :title="title" :poster-width="158" />
    </div>
  </article>
</template>

<style scoped>
.feed-item {
  padding: 0 var(--space-4);
}

.feed-item__line {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  /* FR-H4: the row that holds the profile link is a real target. */
  min-height: var(--tap-min);
  font-size: var(--text-sm);
  margin-bottom: var(--space-2);
}

.feed-item__who {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--tap-min);
  min-width: 0;
  text-decoration: none;
  color: inherit;
  border-radius: var(--radius-full);
}

.feed-item__name {
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feed-item__verb {
  color: var(--text-muted);
}

.feed-item__time {
  margin-left: auto;
  flex: none;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

/*
 * The mockup draws the feed card at 158px — the grid unit, not a full-bleed
 * poster. A 2:3 poster at screen width would put the next item a scroll away.
 */
.feed-item__card {
  width: 158px;
  max-width: 100%;
}

@media (min-width: 900px) {
  .feed-item {
    padding: 0 var(--space-6);
  }
}
</style>
