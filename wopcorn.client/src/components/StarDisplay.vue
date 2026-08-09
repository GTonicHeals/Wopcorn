<script setup lang="ts">
import { computed } from 'vue';

import IconStar from '@/components/icons/IconStar.vue';
import { fillPercent, ratingText } from '@/lib/stars';

/**
 * Five stars, filled to a half-star value, drawn as two clipped layers rather
 * than a font glyph — a text star renders at a different width on every platform
 * and cannot be cut in half reliably.
 *
 * Read-only. The interactive control is `StarRating.vue`; this is what a card, a
 * friend row, and the rating average render.
 */
const props = withDefaults(
  defineProps<{
    /** 1–10 half-stars, or null for unrated. Fractions are allowed (averages). */
    value: number | null;
    /** Edge length of one star in px. */
    size?: number;
    /** Omit when a nearby label already says the value. */
    label?: string | null;
    /**
     * The unfilled half. `subtle` (border) is right beside a title; `strong`
     * (muted) is for the interactive control, where the empty stars have to read
     * as a track you can drag along.
     */
    track?: 'subtle' | 'strong';
    /**
     * The filled half. `accent` is the signed-in user's own rating — the one
     * thing gold ever means. Anyone else's rating (a friend's, in the feed or on
     * their profile) renders `neutral`, so gold keeps meaning *you*.
     */
    tone?: 'accent' | 'neutral';
  }>(),
  { size: 13, label: undefined, track: 'subtle', tone: 'accent' }
);

const gap = computed(() => Math.max(1, Math.round(props.size * 0.19)));
const totalWidth = computed(() => props.size * 5 + gap.value * 4);
const filled = computed(() => `${fillPercent(props.value)}%`);
const text = computed(() => props.label ?? ratingText(props.value));
</script>

<template>
  <span
    class="stars"
    :style="{
      width: `${totalWidth}px`,
      height: `${size}px`,
      '--star-size': `${size}px`,
      '--star-gap': `${gap}px`,
      '--star-row': `${totalWidth}px`
    }"
  >
    <span
      class="stars__row stars__row--track"
      :class="{ 'stars__row--track-strong': track === 'strong' }"
      aria-hidden="true"
    >
      <IconStar v-for="index in 5" :key="`t${index}`" />
    </span>
    <span class="stars__clip" :style="{ width: filled }" aria-hidden="true">
      <span
        class="stars__row stars__row--fill"
        :class="{ 'stars__row--fill-neutral': tone === 'neutral' }"
      >
        <IconStar v-for="index in 5" :key="`f${index}`" filled />
      </span>
    </span>
    <span class="sr-only">{{ text }}</span>
  </span>
</template>

<style scoped>
.stars {
  position: relative;
  display: inline-block;
  flex: none;
  line-height: 0;
}

.stars__row {
  display: flex;
  gap: var(--star-gap);
  width: var(--star-row);
}

.stars__row :deep(svg) {
  width: var(--star-size);
  height: var(--star-size);
}

.stars__row--track {
  color: var(--border);
}

.stars__row--track-strong {
  color: var(--text-muted);
}

/* The gold half of the pair: the accent means the user's own rating. */
.stars__row--fill {
  color: var(--accent);
}

/* Someone else's rating: filled, legible, and pointedly not gold. */
.stars__row--fill-neutral {
  color: var(--text-muted);
}

.stars__clip {
  position: absolute;
  inset: 0;
  overflow: hidden;
}
</style>
