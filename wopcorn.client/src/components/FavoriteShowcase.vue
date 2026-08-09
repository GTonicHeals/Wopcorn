<script setup lang="ts">
import { computed } from 'vue';

import TitleCard from '@/components/TitleCard.vue';
import { MAX_FAVORITES } from '@/lib/profile';
import { useTitlesStore } from '@/stores/titles';
import type { TitleCard as TitleCardData } from '@/api/types';

/**
 * The favourites showcase: up to six titles the owner chose, in the order they
 * chose them.
 *
 * The card is the ordinary one. A showcase is a different *arrangement* — six
 * across, bigger posters, no scroll — not a different card, so the toggles still
 * act on your lists and a friend's rating still rides on its attributed row.
 *
 * Nothing here is numbered. The first slot is marked by the profile taking its
 * marquee from it, which is the only ranking this screen needs and the only one
 * the design language allows outside the queue.
 *
 * Titles are read back out of the store rather than rendered from the props, so
 * a toggle pressed on a showcase card stays in step with the same title in the
 * activity list below it and everywhere else in the app.
 */
const props = defineProps<{
  titles: TitleCardData[];
  /** Whose showcase this is — used in the empty copy and the rating attribution. */
  ownerName: string;
  isSelf: boolean;
}>();

const store = useTitlesStore();

const cards = computed(() =>
  props.titles.map((title) => store.get(title.key) ?? title)
);

/** Their rating of their own favourites, by key — never written into myRating. */
const theirRatings = computed(() => {
  const map: Record<string, number | null> = {};
  for (const title of props.titles) map[title.key] = title.myRating;
  return map;
});
</script>

<template>
  <div class="showcase">
    <div v-if="cards.length > 0" class="showcase__row">
      <div v-for="card in cards" :key="card.key" class="showcase__cell">
        <TitleCard
          :title="card"
          :poster-width="180"
          :their-rating="isSelf ? null : theirRatings[card.key]"
          :their-name="isSelf ? null : ownerName"
        />
      </div>
    </div>

    <p v-else-if="isSelf" class="showcase__empty">
      Pick up to {{ MAX_FAVORITES }} titles you would hand to someone. The first one
      lights the top of this page.
    </p>

    <p v-else class="showcase__empty">{{ ownerName }} has not picked any favourites yet.</p>
  </div>
</template>

<style scoped>
/*
 * A showcase that wraps is just another grid, so this is one row at every width
 * — but the card underneath is the ordinary one, and its three toggles need
 * about 138px to stay tappable (FR-H4). The column count therefore steps up with
 * the room available rather than staying at six and squeezing: six only fits
 * once the content column is wide enough to give every card its 138px.
 */
.showcase__row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--space-3);
}

.showcase__cell {
  min-width: 0;
}

.showcase__empty {
  font-size: var(--text-sm);
  color: var(--text-muted);
  max-width: 46ch;
  line-height: 1.5;
}

@media (min-width: 520px) {
  .showcase__row {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

/* The sidebar appears here and takes 240px back out of the row. */
@media (min-width: 900px) {
  .showcase__row {
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: var(--space-4);
  }
}

@media (min-width: 1200px) {
  .showcase__row {
    grid-template-columns: repeat(6, minmax(0, 1fr));
  }
}
</style>
