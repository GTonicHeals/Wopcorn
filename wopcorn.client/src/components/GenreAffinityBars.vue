<script setup lang="ts">
import { computed } from 'vue';

import { titleCount } from '@/lib/format';
import { genreShares } from '@/lib/profile';
import type { GenreAffinity } from '@/api/types';

/**
 * What someone actually watches, counted off their watched list.
 *
 * Same construction as the rating histogram, and the same tone rule: `accent`
 * for the signed-in user's own taste, `neutral` for anyone else's. The bars are
 * hidden from screen readers and the numbers repeated as a list, because a bar
 * is not readable and a percentage of a maximum is not a fact worth announcing.
 */
const props = withDefaults(
  defineProps<{
    genres: GenreAffinity[];
    tone?: 'accent' | 'neutral';
  }>(),
  { tone: 'accent' }
);

const rows = computed(() => genreShares(props.genres));
</script>

<template>
  <div class="genres">
    <p v-if="rows.length === 0" class="genres__empty">
      {{
        tone === 'neutral'
          ? 'Nothing watched yet.'
          : 'Your genres appear here once you have marked something watched.'
      }}
    </p>

    <template v-else>
      <ul class="genres__rows" :class="{ 'genres__rows--neutral': tone === 'neutral' }" aria-hidden="true">
        <li v-for="row in rows" :key="row.id" class="genres__row">
          <span class="genres__name">{{ row.name }}</span>
          <span class="genres__track">
            <span class="genres__bar" :style="{ width: row.width }" />
          </span>
          <span class="genres__count">{{ row.count }}</span>
        </li>
      </ul>

      <ul class="sr-only">
        <li v-for="row in rows" :key="`sr-${row.id}`">{{ row.name }}: {{ titleCount(row.count) }}</li>
      </ul>
    </template>
  </div>
</template>

<style scoped>
.genres {
  width: 100%;
  max-width: 520px;
}

.genres__empty {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.genres__rows {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.genres__row {
  display: grid;
  grid-template-columns: 96px 1fr 26px;
  align-items: center;
  gap: var(--space-2);
}

.genres__name {
  font-size: var(--text-xs);
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.genres__track {
  height: 10px;
  background: var(--surface-raised);
  border-radius: var(--radius-full);
  overflow: hidden;
}

/* The signed-in user's own taste — the one thing the accent is for. */
.genres__bar {
  display: block;
  height: 100%;
  background: var(--accent);
  border-radius: var(--radius-full);
}

.genres__rows--neutral .genres__bar {
  background: var(--text-muted);
}

.genres__count {
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
  text-align: right;
}
</style>
