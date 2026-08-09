<script setup lang="ts">
import { computed } from 'vue';

import type { MediaType } from '@/api/types';

/**
 * "Series" or "Season" beside a title, wherever a card or row could otherwise be
 * mistaken for a film.
 *
 * **Films get no chip.** The default needs no label, and a chip on every card is
 * noise that makes the two that matter harder to see. So this renders nothing at
 * all for `movie` — callers place it unconditionally and let it decide.
 *
 * Neutral, not accent: the accent means the signed-in user's own state, and what
 * kind of thing something is is not that.
 */
const props = defineProps<{ mediaType: MediaType }>();

const label = computed(() =>
  props.mediaType === 'series' ? 'Series' : props.mediaType === 'season' ? 'Season' : null
);
</script>

<template>
  <span v-if="label" class="type-chip">{{ label }}</span>
</template>

<style scoped>
.type-chip {
  display: inline-flex;
  align-items: center;
  flex: none;
  padding: 1px 6px;
  border: 1px solid var(--border);
  border-radius: var(--radius-full);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
  line-height: 1.5;
}
</style>
