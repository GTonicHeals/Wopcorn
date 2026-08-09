<script setup lang="ts">
import { computed } from 'vue';

import StarDisplay from '@/components/StarDisplay.vue';
import { titleCount } from '@/lib/format';
import { ratingText, starLabel } from '@/lib/stars';
import type { RatingStats } from '@/api/types';

/**
 * FR-E6. Ten buckets as plain CSS bars — no chart library.
 *
 * A bar chart is not readable by a screen reader, so the same numbers are given
 * as a visually hidden list ("4 stars: 12 titles"). The bars themselves are
 * `aria-hidden`.
 */
const props = withDefaults(
  defineProps<{
    stats: RatingStats;
    /**
     * `accent` for the signed-in user's own spread (the You screen); `neutral`
     * for anyone else's (a friend's profile) — gold only ever means *you*.
     */
    tone?: 'accent' | 'neutral';
  }>(),
  { tone: 'accent' }
);

const largest = computed(() => Math.max(1, ...props.stats.distribution));

/** Highest rating first, so the row order matches the star row above it. */
const rows = computed(() =>
  props.stats.distribution
    .map((count, index) => {
      const value = index + 1;
      return {
        value,
        label: starLabel(value),
        text: ratingText(value),
        count,
        width: `${(count / largest.value) * 100}%`
      };
    })
    .reverse()
);
</script>

<template>
  <div class="histogram">
    <!-- `tone` already says whose spread this is, so it picks the copy too. -->
    <p v-if="stats.count === 0" class="histogram__empty">
      {{
        tone === 'neutral'
          ? 'Nothing rated yet.'
          : 'Your rating spread appears here once you have rated something.'
      }}
    </p>

    <template v-else>
      <div class="histogram__summary">
        <StarDisplay :value="stats.average" :size="16" :tone="tone" />
        <span class="histogram__average">
          {{ stats.average !== null ? (stats.average / 2).toFixed(1) : '—' }} average
        </span>
        <span class="histogram__count">{{ titleCount(stats.count) }} rated</span>
      </div>

      <ul
        class="histogram__rows"
        :class="{ 'histogram__rows--neutral': tone === 'neutral' }"
        aria-hidden="true"
      >
        <li v-for="row in rows" :key="row.value" class="histogram__row">
          <span class="histogram__label">{{ row.label }}</span>
          <span class="histogram__track">
            <span class="histogram__bar" :style="{ width: row.width }" />
          </span>
          <span class="histogram__value">{{ row.count }}</span>
        </li>
      </ul>

      <ul class="sr-only">
        <li v-for="row in rows" :key="`sr-${row.value}`">
          {{ row.text }}: {{ titleCount(row.count) }}
        </li>
      </ul>
    </template>
  </div>
</template>

<style scoped>
.histogram {
  width: 100%;
  /* Ten near-full-width bars would out-shout every poster on a wide screen. */
  max-width: 520px;
}

.histogram__empty {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.histogram__summary {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  margin-bottom: var(--space-3);
  font-size: var(--text-sm);
}

.histogram__average {
  font-variant-numeric: tabular-nums;
}

.histogram__count {
  color: var(--text-muted);
  font-size: var(--text-xs);
}

.histogram__rows {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.histogram__row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.histogram__label {
  width: 30px;
  flex: none;
  text-align: right;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.histogram__track {
  flex: 1;
  min-width: 0;
  height: 10px;
  background: var(--surface-raised);
  border-radius: var(--radius-full);
  overflow: hidden;
}

/* The user's own ratings — the one thing the accent is for. */
.histogram__bar {
  display: block;
  height: 100%;
  background: var(--accent);
  border-radius: var(--radius-full);
}

/* A friend's spread: same shape, none of the gold. */
.histogram__rows--neutral .histogram__bar {
  background: var(--text-muted);
}

.histogram__value {
  width: 26px;
  flex: none;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}
</style>
