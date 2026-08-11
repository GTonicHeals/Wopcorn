<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';

import StarDisplay from '@/components/StarDisplay.vue';
import TitleGrid from '@/components/TitleGrid.vue';
import UserAvatar from '@/components/UserAvatar.vue';
import { absoluteTime, relativeTime } from '@/lib/relativeTime';
import { ratingText } from '@/lib/stars';
import { useTitlesStore } from '@/stores/titles';
import type { ActivityGroup } from '@/lib/feedGroups';
import type { TitleCard as TitleCardData } from '@/api/types';

/**
 * One line of text above the titles it is about (fe-07, task 2) — one title
 * where that is the news, a row of them where one friend did the same thing to
 * several in a sitting. `groupActivity` decides which; see `lib/feedGroups.ts`
 * for why a rating is never one of the several.
 *
 * The cards are the ordinary ones, in the ordinary grid, on purpose: they carry
 * **your** membership and rating, so a friend rating something already in your
 * queue reads without a tap — and the type chip says whether "watched" meant a
 * film, a series or one season of one. The friend's own rating sits on the line
 * above, next to their name, and never in a card's `myRating` slot.
 *
 * Titles are read from the store rather than from `item.title` — the feed
 * upserts every title it receives, and reading them back is what keeps a toggle
 * pressed here in step with the same title on search and list screens.
 */
const props = defineProps<{ group: ActivityGroup }>();

const titles = useTitlesStore();

const cards = computed<TitleCardData[]>(() =>
  props.group.items.map((item) => titles.get(item.title.key) ?? item.title)
);

/** The four kinds read differently; the sentence is the difference. */
const VERBS: Record<ActivityGroup['kind'], string> = {
  rated: 'rated',
  watched: 'watched',
  added_watchlist: 'added to their watchlist',
  added_queue: 'queued'
};

const verb = computed(() => VERBS[props.group.kind]);

/** Only a rating group is ever alone, which is what makes these stars unambiguous. */
const rating = computed(() =>
  props.group.kind === 'rated' ? (props.group.items[0]?.rating ?? null) : null
);

const starsLabel = computed(() => (rating.value === null ? '' : ratingText(rating.value)));

const relative = computed(() => relativeTime(props.group.occurredAt));
const absolute = computed(() => absoluteTime(props.group.occurredAt));
</script>

<template>
  <article class="feed-group">
    <p class="feed-group__line">
      <RouterLink class="feed-group__who" :to="`/u/${group.user.id}`">
        <UserAvatar :user="group.user" :size="26" />
        <span class="feed-group__name">{{ group.user.displayName }}</span>
      </RouterLink>

      <span class="feed-group__verb">{{ verb }}</span>

      <StarDisplay
        v-if="rating !== null"
        :value="rating"
        :size="13"
        :label="starsLabel"
        tone="neutral"
      />

      <!-- Rounded on screen, exact in the markup and in the tooltip. The newest
           event's instant: nothing older than a day is ever inside a group. -->
      <time class="feed-group__time" :datetime="group.occurredAt" :title="absolute">
        {{ relative }}
      </time>
    </p>

    <!-- The same grid the lists and search use, so the gutters line up and a
         lone card stays one column wide instead of stretching to the row. -->
    <TitleGrid :titles="cards" />
  </article>
</template>

<style scoped>
.feed-group__line {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  /* FR-H4: the row that holds the profile link is a real target. */
  min-height: var(--tap-min);
  font-size: var(--text-sm);
  padding: 0 var(--space-4);
  margin-bottom: var(--space-2);
}

.feed-group__who {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--tap-min);
  min-width: 0;
  text-decoration: none;
  color: inherit;
  border-radius: var(--radius-full);
}

.feed-group__name {
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feed-group__verb {
  color: var(--text-muted);
}

.feed-group__time {
  margin-left: auto;
  flex: none;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

@media (min-width: 900px) {
  .feed-group__line {
    padding: 0 var(--space-6);
  }
}
</style>
