<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';

import IconStar from '@/components/icons/IconStar.vue';
import ListToggles from '@/components/ListToggles.vue';
import PosterImage from '@/components/PosterImage.vue';
import StarDisplay from '@/components/StarDisplay.vue';
import TypeChip from '@/components/TypeChip.vue';
import { formatScore } from '@/lib/format';
import { isQuickViewClick } from '@/lib/quickView';
import { ratingText } from '@/lib/stars';
import { titlePath } from '@/lib/titleKey';
import { seasonProgressLabel, titleMeta } from '@/lib/titleMeta';
import type { TitleCard } from '@/api/types';

/**
 * The single most reused component in the app: poster, title, meta, the user's
 * rating when there is one, and the three list toggles. FR-C2, FR-C3 and FR-H4
 * are all satisfied by this one layout — it is not to be redesigned per screen.
 *
 * The layout did not change when series and seasons arrived; what changed is what
 * fills it. A type chip on series and season cards, a meta line that reads per
 * media type, and — on a series with watched seasons — the progress line, which
 * is the signed-in user's own state and therefore the only gold on the card
 * besides their rating.
 *
 * Tapping the poster or the title navigates; the toggles never navigate.
 */
const props = withDefaults(
  defineProps<{
    title: TitleCard;
    /** Rendered poster width in CSS px, for TMDB size selection (FR-H6). */
    posterWidth?: number;
    /**
     * Someone else's rating of this title — a friend's, on their profile (fe-07,
     * task 3). It renders on its own attributed row, never in the `myRating` slot
     * above it: `title.myRating` is always the signed-in user's, whoever's list
     * the card happens to be sitting in.
     */
    theirRating?: number | null;
    /** Whose rating `theirRating` is. Required for it to render at all. */
    theirName?: string | null;
    /**
     * Opt-in, and off everywhere but the lists: a plain tap emits `select`
     * instead of navigating, so the list can open the title in a quick-view
     * dialog. The card is unchanged otherwise — same markup, same href, and a
     * ctrl-click or middle-click still goes to the title screen.
     */
    quickView?: boolean;
  }>(),
  { posterWidth: 138, theirRating: null, theirName: null, quickView: false }
);

const emit = defineEmits<{ select: [key: string] }>();

const score = computed(() => formatScore(props.title.tmdbVoteAverage));
const metaText = computed(() => titleMeta(props.title));
const progress = computed(() => seasonProgressLabel(props.title.seasonProgress));

/** No name, no attribution — an unlabelled second star row is unreadable. */
const showTheirRating = computed(
  () => Boolean(props.theirName) && props.theirRating !== null && props.theirRating !== undefined
);

const theirRatingLabel = computed(() =>
  showTheirRating.value ? `${props.theirName} rated it ${ratingText(props.theirRating)}` : ''
);

/**
 * Capture phase, on the card rather than the link: vue-router's own handler sits
 * on the anchor, so this is the one point at which the click can be claimed
 * before it navigates.
 */
function onCaptureClick(event: MouseEvent): void {
  if (!props.quickView || !isQuickViewClick(event)) return;

  event.preventDefault();
  event.stopPropagation();
  emit('select', props.title.key);
}
</script>

<template>
  <article class="title-card" @click.capture="onCaptureClick">
    <RouterLink class="title-card__link" :to="titlePath(title.key)">
      <PosterImage
        :path="title.posterPath"
        :title="title.title"
        :release-year="title.releaseYear"
        :width="posterWidth"
      />
      <h3 class="title-card__title">{{ title.title }}</h3>
      <p class="title-card__meta">
        <TypeChip :media-type="title.mediaType" />
        <span v-if="metaText">{{ metaText }}</span>
        <span v-if="score" class="title-card__score">
          <span v-if="metaText" aria-hidden="true">·</span>
          <IconStar filled />
          <span>{{ score }}</span>
          <span class="sr-only">TMDB score</span>
        </span>
      </p>
      <!-- The viewer's own progress through a series, so it takes the accent. -->
      <p v-if="progress" class="title-card__progress">{{ progress }} watched</p>
    </RouterLink>

    <!-- The user's own rating, in the accent — the one thing gold ever means. -->
    <p class="title-card__rating">
      <StarDisplay v-if="title.myRating !== null" :value="title.myRating" :size="13" />
    </p>

    <!-- Someone else's rating, always attributed by name so the two never blur. -->
    <p v-if="showTheirRating" class="title-card__theirs">
      <span class="title-card__theirs-name" aria-hidden="true">{{ theirName }}</span>
      <StarDisplay :value="theirRating" :size="12" :label="theirRatingLabel" tone="neutral" />
    </p>

    <ListToggles :title="title" />
  </article>
</template>

<style scoped>
.title-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.title-card__link {
  display: block;
  text-decoration: none;
  color: inherit;
  border-radius: var(--radius-md);
}

.title-card__title {
  /* A display serif is illegible at 13px in a dense grid — card titles are UI. */
  font-family: var(--font-ui);
  font-size: var(--text-sm);
  font-weight: 600;
  line-height: 1.3;
  margin: var(--space-2) 0 1px;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.title-card__meta {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.title-card__score {
  display: inline-flex;
  align-items: center;
  gap: 3px;
}

.title-card__score :deep(svg) {
  width: 11px;
  height: 11px;
}

/* Gold, because how far through a series you are is your own state. */
.title-card__progress {
  font-size: var(--text-xs);
  color: var(--accent);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  margin-top: 2px;
}

.title-card__rating {
  /* Reserved whether or not there is a rating, so cards in a row stay aligned. */
  height: 15px;
  margin: 3px 0 var(--space-2);
  line-height: 0;
}

.title-card__theirs {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  margin: -4px 0 var(--space-2);
  min-width: 0;
}

.title-card__theirs-name {
  font-size: 11px;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
